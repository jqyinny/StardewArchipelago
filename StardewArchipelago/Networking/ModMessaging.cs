using System;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

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

        private readonly ILogger _logger;
        private readonly IModHelper _helper;
        private readonly string _modId;

        // SendMessage has no replay/queueing: a peer who joins after this was first broadcast would
        // otherwise never see it. Remembering the slot name lets OnPeerConnected resend it to just
        // that peer, so joining late (the only way local co-op actually works - the host is always
        // already connected before a farmhand can join) still gets the round trip through.
        private string _lastKnownSlotName;

        public ModMessaging(ILogger logger, IModHelper helper, string modId)
        {
            _logger = logger;
            _helper = helper;
            _modId = modId;
            _helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            _helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        public void BroadcastHostConnected(string slotName)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastHostConnected was called by a farmhand; only the host should send this message.");
                return;
            }

            _lastKnownSlotName = slotName;
            _helper.Multiplayer.SendMessage(new HostConnectedMessage(slotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId });
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || _lastKnownSlotName == null)
            {
                return;
            }

            _helper.Multiplayer.SendMessage(new HostConnectedMessage(_lastKnownSlotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId }, playerIDs: new[] { e.Peer.PlayerID });
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
    }
}
