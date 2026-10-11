using System;
using System.Collections.Generic;
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

        // Only meaningful on the host: the real, connected checker that should actually receive a
        // farmhand's reported checks. Set via SetHostLocationChecker once InitializeAfterConnection
        // builds it.
        private LocationChecker _hostLocationChecker;

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

        public void BroadcastHostConnected(string slotName, Dictionary<string, object> slotDataFields, ArchipelagoStateDto state)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastHostConnected was called by a farmhand; only the host should send this message.");
                return;
            }

            _lastKnownSlotName = slotName;
            _lastKnownSlotDataFields = slotDataFields;
            _lastKnownState = state.ForFarmhandSync();
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] BroadcastHostConnected: slotName={slotName} slotDataFieldCount={slotDataFields?.Count} IsMainPlayer={Context.IsMainPlayer}");
            _helper.Multiplayer.SendMessage(new HostConnectedMessage(slotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(slotName, slotDataFields, _lastKnownState), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId });
        }

        public void BroadcastLocationChecked(string locationName)
        {
            if (Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastLocationChecked was called by the host; only a farmhand should send this message.");
                return;
            }

            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] BroadcastLocationChecked: location={locationName} IsMainPlayer={Context.IsMainPlayer} Game1.player={Game1.player?.Name}");

            _helper.Multiplayer.SendMessage(new LocationCheckedMessage(locationName), MESSAGE_TYPE_LOCATION_CHECKED, modIDs: new[] { _modId });
        }

        public void BroadcastItemReceived(ReceivedItem receivedItem)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastItemReceived was called by a farmhand; only the host should send this message.");
                return;
            }

            // TEMP DIAGNOSTIC (see NOTES.md "farmhand fan-out" investigation) - remove once the
            // split-screen delivery-timing question is resolved.
            _logger.LogInfo($"[DIAG] BroadcastItemReceived: item={receivedItem?.ItemName} time={DateTime.Now:HH:mm:ss.fff} thread={System.Threading.Thread.CurrentThread.ManagedThreadId} IsMainPlayer={Context.IsMainPlayer} Game1.player={Game1.player?.Name} screenId={Context.ScreenId}");

            _helper.Multiplayer.SendMessage(new ItemReceivedMessage(receivedItem), MESSAGE_TYPE_ITEM_RECEIVED, modIDs: new[] { _modId });
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] OnPeerConnected: peerPlayerID={e.Peer.PlayerID} IsMainPlayer={Context.IsMainPlayer} haveLastKnownSlotName={_lastKnownSlotName != null}");

            if (!Context.IsMainPlayer || _lastKnownSlotName == null)
            {
                return;
            }

            _helper.Multiplayer.SendMessage(new HostConnectedMessage(_lastKnownSlotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(_lastKnownSlotName, _lastKnownSlotDataFields, _lastKnownState), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
            _logger.LogInfo($"[DIAG] OnPeerConnected: resent HostConnected+SlotDataSnapshot to peer {e.Peer.PlayerID}");
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] OnModMessageReceived: type={e.Type} fromModID={e.FromModID} expectedModID={_modId} fromPlayerID={e.FromPlayerID} IsMainPlayer={Context.IsMainPlayer}");

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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to handle multiplayer message of type '{e.Type}'. Error: {ex}");
            }
        }

        private void HandleHostConnected(HostConnectedMessage message)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] HandleHostConnected: slotName={message.SlotName}");

            Game1.chatBox?.addMessage($"The host connected to Archipelago as {message.SlotName}.", Color.Green);
        }

        private void HandleSlotDataSnapshot(SlotDataSnapshotMessage message)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] HandleSlotDataSnapshot: slotName={message.SlotName} slotDataFieldCount={message.SlotDataFields?.Count} stateIsNull={message.State == null}");

            // Seed the farmhand's own (never-connected) client through the exact same SlotData
            // constructor the host used, instead of keeping a separate copy - code that only reads
            // .SlotData (not .IsConnected) then works identically on host and farmhand.
            _archipelago.SeedSlotDataFromSnapshot(message.SlotName, message.SlotDataFields);
            FarmhandSlotDataReceived?.Invoke(message.State);

            _logger.LogInfo("[DIAG] HandleSlotDataSnapshot: FarmhandSlotDataReceived invoked and returned without throwing");
        }

        private void HandleLocationChecked(LocationCheckedMessage message)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once resolved.
            _logger.LogInfo($"[DIAG] HandleLocationChecked: location={message.LocationName} IsMainPlayer={Context.IsMainPlayer} hostLocationCheckerIsNull={_hostLocationChecker == null}");

            if (!Context.IsMainPlayer || _hostLocationChecker == null)
            {
                return;
            }

            _hostLocationChecker.AddCheckedLocation(message.LocationName);
        }

        private void HandleItemReceived(ItemReceivedMessage message)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "farmhand fan-out" investigation) - remove once the
            // split-screen delivery-timing question is resolved.
            _logger.LogInfo($"[DIAG] HandleItemReceived: item={message.ReceivedItem?.ItemName} time={DateTime.Now:HH:mm:ss.fff} thread={System.Threading.Thread.CurrentThread.ManagedThreadId} IsMainPlayer={Context.IsMainPlayer} Game1.player={Game1.player?.Name} screenId={Context.ScreenId}");

            FarmhandItemReceived?.Invoke(message.ReceivedItem);
        }
    }
}
