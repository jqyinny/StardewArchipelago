using System;
using System.Collections.Generic;
using System.Linq;
using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.ArchipelagoUtilities.Net.Client;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewArchipelago.Archipelago;
using StardewArchipelago.GameModifications.Testing;
using StardewArchipelago.Serialization;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewArchipelago.Networking
{
    // Phase 2 "Core Plumbing" skeleton for host -> farmhand sync. Only the host holds the
    // Archipelago connection (see ModEntry.OnSaveLoaded's Context.IsMainPlayer gate); this
    // is the channel it has to tell farmhands what happened, and the channel farmhands use
    // to report their own actions back (see CoopLocationChecker).
    public class ModMessaging
    {
        private const string MESSAGE_TYPE_HOST_CONNECTED = "HostConnected";
        private const string MESSAGE_TYPE_SLOT_DATA_SNAPSHOT = "SlotDataSnapshot";
        private const string MESSAGE_TYPE_LOCATION_CHECKED = "LocationChecked";
        private const string MESSAGE_TYPE_ITEM_RECEIVED = "ItemReceived";
        private const string MESSAGE_TYPE_CHAT_MESSAGE = "ChatMessage";
        private const string MESSAGE_TYPE_FORCE_SLEEP = "ForceSleep";

        private readonly ILogger _logger;
        private readonly IModHelper _helper;
        private readonly IManifest _manifest;
        private readonly TesterFeatures _testerFeatures;
        private readonly StardewArchipelagoClient _archipelago;
        private readonly string _modId;

        // SendMessage has no replay/queueing: a peer who joins after this was first broadcast would
        // otherwise never see it. Remembering the last snapshot lets OnPeerConnected resend it to just
        // that peer, so joining late (the only way local co-op actually works - the host is always
        // already connected before a farmhand can join) still gets the round trip through.
        private string _lastKnownSlotName;
        private Dictionary<string, object> _lastKnownSlotDataFields;
        private ArchipelagoStateDto _lastKnownState;
        private Dictionary<string, int> _lastKnownReceivedItemCounts;
        private HashSet<string> _lastKnownLocationNames;
        private HashSet<string> _lastKnownCheckedLocationNames;

        // Only meaningful on the host: the real, connected checker that should actually receive a
        // farmhand's reported checks. Set via SetHostLocationChecker once InitializeAfterConnection
        // builds it.
        private LocationChecker _hostLocationChecker;

        // Only meaningful on the host: the live State and a way to read the item manager's current
        // received-item list. State.ItemsReceived itself is only refreshed in ModEntry.OnSaving, and
        // ForFarmhandSync is a shallow clone, so a snapshot taken once at connect time never included
        // anything received afterwards - a farmhand joining later (e.g. after the host received checks
        // and slept while they were offline) never got those items' letters. Rebuilt per snapshot.
        private ArchipelagoStateDto _hostState;
        private Func<List<ReceivedItem>> _hostItemsReceivedProvider;

        // Fires on a farmhand once its own StardewArchipelagoClient.SlotData has been seeded from the
        // host's snapshot (see HandleSlotDataSnapshot) - the earliest point it's safe to build
        // SlotData/State-dependent objects (e.g. CraftingInjections' CompoundNameMapper, TrapExecutor)
        // for the farmhand. Carries the host's State snapshot since farmhand-local objects need a
        // non-null State too (previously only true "by accident" in split-screen - see NOTES.md).
        public event Action<ArchipelagoStateDto> FarmhandSlotDataReceived;

        // Fires on a farmhand when the host has processed an AP-received item. Per design decision
        // (NOTES.md "item distribution"): every received item is applied to every connected player,
        // not just the host - this is the farmhand's half of that, applying the exact same item the
        // host already decided on, to its own local Game1.player.
        public event Action<ReceivedItem> FarmhandItemReceived;

        // Fires on a farmhand when the host is driving a multi-day sleep (see MultiSleepManager) and
        // needs the farmhand to also be "asleep" that night, since day transitions are host-authoritative
        // in real multiplayer and the host's own repeated advance would otherwise be unsafe/out of sync
        // with a farmhand who never interacted with their own bed. The farmhand's handler is expected to
        // perform its own ordinary, vanilla-safe "go to sleep" locally - never to drive the day-advance
        // itself (see NOTES.md "eighth gap").
        public event Action FarmhandForceSleep;

        public ModMessaging(ILogger logger, IModHelper helper, IManifest manifest, TesterFeatures testerFeatures, StardewArchipelagoClient archipelago, string modId)
        {
            _logger = logger;
            _helper = helper;
            _manifest = manifest;
            _testerFeatures = testerFeatures;
            _archipelago = archipelago;
            _modId = modId;
            _helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            _helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        public void SetHostLocationChecker(LocationChecker locationChecker)
        {
            _hostLocationChecker = locationChecker;
        }

        public void SetHostItemsReceivedProvider(Func<List<ReceivedItem>> itemsReceivedProvider)
        {
            _hostItemsReceivedProvider = itemsReceivedProvider;
        }

        private ArchipelagoStateDto BuildFarmhandStateSnapshot()
        {
            var snapshot = _hostState.ForFarmhandSync();
            var currentItems = _hostItemsReceivedProvider?.Invoke();
            if (currentItems != null)
            {
                snapshot.ItemsReceived = currentItems;
            }

            return snapshot;
        }

        public void BroadcastHostConnected(string slotName, Dictionary<string, object> slotDataFields, ArchipelagoStateDto state)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastHostConnected was called by a farmhand; only the host should send this message.");
                return;
            }

            _lastKnownSlotName = slotName;
            _lastKnownSlotDataFields = slotDataFields;
            _hostState = state;
            _lastKnownState = BuildFarmhandStateSnapshot();
            // Full baseline for GetReceivedItemCountSafe/HasReceivedItemSafe (see StardewArchipelagoClient) -
            // without this, a farmhand's local received-item cache would only ever reflect items received
            // after it joined, missing anything the host already had beforehand.
            _lastKnownReceivedItemCounts = _archipelago.GetAllReceivedItemNamesAndCounts();
            // Full baseline for LocationExistsSafe (see StardewArchipelagoClient) - LocationExists
            // itself short-circuits to false with no live connection, which was zeroing out
            // ArchipelagoFriend.ShuffledUpTo (and anything else that calls it) for a farmhand.
            _lastKnownLocationNames = _archipelago.GetAllLocationNamesForSync();
            // Full baseline for the farmhand's own GetFarmhandCheckedLocationsSafe (see
            // StardewArchipelagoClient) - this is what makes GetAllLocationsNotCheckedSafe (behind the
            // crafting-recipe AP symbol, among others) correct for a farmhand instead of just empty.
            _lastKnownCheckedLocationNames = _hostLocationChecker?.GetAllLocationsAlreadyChecked()?.ToHashSet();
            _helper.Multiplayer.SendMessage(new HostConnectedMessage(slotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(slotName, slotDataFields, _lastKnownState, _lastKnownReceivedItemCounts, _lastKnownLocationNames, _lastKnownCheckedLocationNames), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId });
        }

        public void BroadcastLocationChecked(string locationName)
        {
            if (Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastLocationChecked was called by the host; only a farmhand should send this message.");
                return;
            }

            _helper.Multiplayer.SendMessage(new LocationCheckedMessage(locationName), MESSAGE_TYPE_LOCATION_CHECKED, modIDs: new[] { _modId });
        }

        public void BroadcastItemReceived(ReceivedItem receivedItem)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastItemReceived was called by a farmhand; only the host should send this message.");
                return;
            }

            _helper.Multiplayer.SendMessage(new ItemReceivedMessage(receivedItem), MESSAGE_TYPE_ITEM_RECEIVED, modIDs: new[] { _modId });
        }

        // Fans the AP textbox (chat, item-send announcements, hints, goal completion, etc.) out to
        // every connected farmhand - see StardewArchipelagoClient.ShowArchipelagoMessage, the only
        // caller. A farmhand's own client never receives these in the first place (it's never actually
        // connected), so this is the only way a farmhand ever sees them.
        public void BroadcastChatMessage(string message, Color color)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastChatMessage was called by a farmhand; only the host should send this message.");
                return;
            }

            _helper.Multiplayer.SendMessage(new ChatMessageMessage(message, color.PackedValue), MESSAGE_TYPE_CHAT_MESSAGE, modIDs: new[] { _modId });
        }

        // See FarmhandForceSleep. Host-only, like every other Broadcast* here.
        public void BroadcastForceSleep()
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastForceSleep was called by a farmhand; only the host should send this message.");
                return;
            }

            _helper.Multiplayer.SendMessage(new ForceSleepMessage(), MESSAGE_TYPE_FORCE_SLEEP, modIDs: new[] { _modId });
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || _lastKnownSlotName == null)
            {
                return;
            }

            // Refetch rather than reuse the cached copy from BroadcastHostConnected - a second farmhand
            // joining later should see every item received since then too, not a stale snapshot.
            _lastKnownState = BuildFarmhandStateSnapshot();
            _lastKnownReceivedItemCounts = _archipelago.GetAllReceivedItemNamesAndCounts();
            _lastKnownLocationNames = _archipelago.GetAllLocationNamesForSync();
            _lastKnownCheckedLocationNames = _hostLocationChecker?.GetAllLocationsAlreadyChecked()?.ToHashSet();
            _helper.Multiplayer.SendMessage(new HostConnectedMessage(_lastKnownSlotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(_lastKnownSlotName, _lastKnownSlotDataFields, _lastKnownState, _lastKnownReceivedItemCounts, _lastKnownLocationNames, _lastKnownCheckedLocationNames), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != _modId)
            {
                return;
            }

            try
            {
                switch (e.Type)
                {
                    case MESSAGE_TYPE_HOST_CONNECTED:
                        HandleHostConnected(e.ReadAs<HostConnectedMessage>());
                        break;
                    case MESSAGE_TYPE_SLOT_DATA_SNAPSHOT:
                        HandleSlotDataSnapshot(e.ReadAs<SlotDataSnapshotMessage>());
                        break;
                    case MESSAGE_TYPE_LOCATION_CHECKED:
                        HandleLocationChecked(e.ReadAs<LocationCheckedMessage>());
                        break;
                    case MESSAGE_TYPE_ITEM_RECEIVED:
                        HandleItemReceived(e.ReadAs<ItemReceivedMessage>());
                        break;
                    case MESSAGE_TYPE_CHAT_MESSAGE:
                        HandleChatMessage(e.ReadAs<ChatMessageMessage>());
                        break;
                    case MESSAGE_TYPE_FORCE_SLEEP:
                        HandleForceSleep();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to handle multiplayer message of type '{e.Type}'. Error: {ex}");
            }
        }

        private void HandleHostConnected(HostConnectedMessage message)
        {
            Game1.chatBox?.addMessage($"The host connected to Archipelago as {message.SlotName}.", Color.Green);
        }

        private void HandleSlotDataSnapshot(SlotDataSnapshotMessage message)
        {
            // Seed the farmhand's own (never-connected) client through the exact same SlotData
            // constructor the host used, instead of keeping a separate copy - code that only reads
            // .SlotData (not .IsConnected) then works identically on host and farmhand.
            _archipelago.SeedSlotDataFromSnapshot(message.SlotName, message.SlotDataFields);
            if (message.ReceivedItemCounts != null)
            {
                _archipelago.SeedReceivedItemCountsFromSnapshot(message.ReceivedItemCounts);
            }
            if (message.LocationNames != null)
            {
                _archipelago.SeedLocationNamesFromSnapshot(message.LocationNames);
            }
            if (message.CheckedLocationNames != null)
            {
                _archipelago.SeedCheckedLocationsFromSnapshot(message.CheckedLocationNames);
            }
            FarmhandSlotDataReceived?.Invoke(message.State);
        }

        private void HandleLocationChecked(LocationCheckedMessage message)
        {
            if (!Context.IsMainPlayer || _hostLocationChecker == null)
            {
                return;
            }

            _hostLocationChecker.AddCheckedLocation(message.LocationName);
        }

        private void HandleItemReceived(ItemReceivedMessage message)
        {
            FarmhandItemReceived?.Invoke(message.ReceivedItem);
        }

        private void HandleChatMessage(ChatMessageMessage message)
        {
            if (Context.IsMainPlayer)
            {
                return;
            }

            Game1.chatBox?.addMessage(message.Text, new Color { PackedValue = message.PackedColor });
        }

        private void HandleForceSleep()
        {
            if (Context.IsMainPlayer)
            {
                return;
            }

            FarmhandForceSleep?.Invoke();
        }
    }
}
