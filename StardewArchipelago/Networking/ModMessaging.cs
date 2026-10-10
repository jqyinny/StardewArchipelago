using System;
using System.Collections.Generic;
using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewArchipelago.Archipelago;
using StardewArchipelago.GameModifications.Testing;
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

        // Only meaningful on the host: the real, connected checker that should actually receive a
        // farmhand's reported checks. Set via SetHostLocationChecker once InitializeAfterConnection
        // builds it.
        private LocationChecker _hostLocationChecker;

        // Fires on a farmhand once its own StardewArchipelagoClient.SlotData has been seeded from the
        // host's snapshot (see HandleSlotDataSnapshot) - the earliest point it's safe to build
        // SlotData-dependent objects (e.g. CraftingInjections' CompoundNameMapper) for the farmhand.
        public event Action FarmhandSlotDataReceived;

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

        public void BroadcastHostConnected(string slotName, Dictionary<string, object> slotDataFields)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastHostConnected was called by a farmhand; only the host should send this message.");
                return;
            }

            _lastKnownSlotName = slotName;
            _lastKnownSlotDataFields = slotDataFields;
            _helper.Multiplayer.SendMessage(new HostConnectedMessage(slotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(slotName, slotDataFields), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId });
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

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || _lastKnownSlotName == null)
            {
                return;
            }

            _helper.Multiplayer.SendMessage(new HostConnectedMessage(_lastKnownSlotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
            _helper.Multiplayer.SendMessage(new SlotDataSnapshotMessage(_lastKnownSlotName, _lastKnownSlotDataFields), MESSAGE_TYPE_SLOT_DATA_SNAPSHOT, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to handle multiplayer message of type '{e.Type}'. Error: {ex}");
            }
        }

        private void HandleHostConnected(HostConnectedMessage message)
        {
            Game1.chatBox?.addMessage($"The host connected to Archipelago as {message.SlotName}. Multiplayer item/location sync is not implemented yet.", Color.Green);
        }

        private void HandleSlotDataSnapshot(SlotDataSnapshotMessage message)
        {
            // Seed the farmhand's own (never-connected) client through the exact same SlotData
            // constructor the host used, instead of keeping a separate copy - code that only reads
            // .SlotData (not .IsConnected) then works identically on host and farmhand.
            _archipelago.SeedSlotDataFromSnapshot(message.SlotName, message.SlotDataFields);
            FarmhandSlotDataReceived?.Invoke();
        }

        private void HandleLocationChecked(LocationCheckedMessage message)
        {
            if (!Context.IsMainPlayer || _hostLocationChecker == null)
            {
                return;
            }

            _hostLocationChecker.AddCheckedLocation(message.LocationName);
        }
    }
}
