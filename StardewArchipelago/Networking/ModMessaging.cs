using System;
using System.Collections.Generic;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewArchipelago.GameModifications.Testing;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using ApSlotData = StardewArchipelago.Archipelago.SlotData.SlotData;

namespace StardewArchipelago.Networking
{
    // Phase 2 "Core Plumbing" skeleton for host -> farmhand sync. Only the host holds the
    // Archipelago connection (see ModEntry.OnSaveLoaded's Context.IsMainPlayer gate); this
    // is the one channel it has to tell farmhands what happened. Later phases will add real
    // messages here (received items, traps, bundle completions) - for now this only proves
    // the round trip works end to end.
    public class ModMessaging
    {
        private const string MESSAGE_TYPE_HOST_CONNECTED = "HostConnected";
        private const string MESSAGE_TYPE_SLOT_DATA_SNAPSHOT = "SlotDataSnapshot";

        private readonly ILogger _logger;
        private readonly IModHelper _helper;
        private readonly IManifest _manifest;
        private readonly TesterFeatures _testerFeatures;
        private readonly string _modId;

        // SendMessage has no replay/queueing: a peer who joins after this was first broadcast would
        // otherwise never see it. Remembering the last snapshot lets OnPeerConnected resend it to just
        // that peer, so joining late (the only way local co-op actually works - the host is always
        // already connected before a farmhand can join) still gets the round trip through.
        private string _lastKnownSlotName;
        private Dictionary<string, object> _lastKnownSlotDataFields;

        // The farmhand's own local reconstruction of SlotData, built from the fields the host sent.
        // Not wired to anything yet - this is the sync half; GameModifications patchers still need to
        // be updated to read from this instead of a live, connected StardewArchipelagoClient.
        public ApSlotData FarmhandSlotData { get; private set; }

        public ModMessaging(ILogger logger, IModHelper helper, IManifest manifest, TesterFeatures testerFeatures, string modId)
        {
            _logger = logger;
            _helper = helper;
            _manifest = manifest;
            _testerFeatures = testerFeatures;
            _modId = modId;
            _helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            _helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
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
            // Rebuild SlotData locally through its own constructor, exactly like the host did when it
            // first connected - SlotData has mostly private setters, so it can't be deserialized directly.
            FarmhandSlotData = new ApSlotData(message.SlotName, message.SlotDataFields, _logger, _manifest, _testerFeatures);
        }
    }
}
