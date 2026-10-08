using System;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewArchipelago.Multiplayer
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

        public ModMessaging(ILogger logger, IModHelper helper, string modId)
        {
            _logger = logger;
            _helper = helper;
            _modId = modId;
            _helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        public void BroadcastHostConnected(string slotName)
        {
            if (!Context.IsMainPlayer)
            {
                _logger.LogWarning("BroadcastHostConnected was called by a farmhand; only the host should send this message.");
                return;
            }

            _helper.Multiplayer.SendMessage(new HostConnectedMessage(slotName), MESSAGE_TYPE_HOST_CONNECTED, modIDs: new[] { _modId });
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
