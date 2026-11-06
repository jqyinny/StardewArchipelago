using System;
using HarmonyLib;
using KaitoKid.ArchipelagoUtilities.Net.Client;
using KaitoKid.ArchipelagoUtilities.Net.Constants;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewArchipelago.Bundles;
using StardewArchipelago.Locations.CodeInjections.Vanilla.Bundles;
using StardewArchipelago.Networking;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace StardewArchipelago.Archipelago
{
    public class DeathManager
    {
        private static ILogger _logger;
        private static IModHelper _modHelper;
        private static Harmony _harmony;
        private static ArchipelagoClient _archipelago;

        private static bool _isCurrentlyReceivingDeathLink = false;
        private static ModMessaging _modMessaging;

        public DeathManager(ILogger logger, IModHelper modHelper, Harmony harmony, ArchipelagoClient archipelago)
        {
            _logger = logger;
            _modHelper = modHelper;
            _harmony = harmony;
            _archipelago = archipelago;
        }

        // Co-op: deaths are shared between the players of the slot. Only the host talks to AP; farmhands
        // report their deaths to the host and get told to die by the host (ModMessaging FarmhandDied/CoopDeath).
        public static void SetModMessaging(ModMessaging modMessaging)
        {
            _modMessaging = modMessaging;
            _modMessaging.HostFarmhandDied += OnHostFarmhandDied;
            _modMessaging.FarmhandCoopDeath += OnFarmhandCoopDeath;
        }

        // Host: the live connection decides (as before). Farmhand: no connection, so use the slot setting.
        private static bool IsDeathLinkEnabled()
        {
            if (_archipelago == null)
            {
                return false;
            }

            if (_archipelago.GetSession() != null)
            {
                return _archipelago.DeathLink;
            }

            return _archipelago is StardewArchipelagoClient stardewArchipelago && stardewArchipelago.SlotData?.DeathLink == true;
        }

        // Incoming AP death link (host only - only the host is connected).
        public static void ReceiveDeathLink()
        {
            if (!IsDeathLinkEnabled())
            {
                return;
            }

            ModEntry.Instance.State.Wallet.DeathLinks++;
            KillLocalPlayerFromDeathLink();
            // Each player's health is owned by their own machine, so the host can't kill a farmhand by writing
            // to their Farmer - tell them instead.
            _modMessaging?.BroadcastCoopDeath(null, "a death link");
        }

        // Kill this machine's own player, and make sure the resulting death isn't reported back out.
        private static void KillLocalPlayerFromDeathLink()
        {
            var player = Game1.player;
            if (player == null || player.health <= 0 || Game1.killScreen)
            {
                return; // already dying - setting the flag here would swallow their next real death
            }

            _isCurrentlyReceivingDeathLink = true;
            player.health = 0;
        }

        // A forced death that never "took" (e.g. it arrived during a cutscene) must not suppress a later real one.
        public static void ResetDeathLinkFlag()
        {
            _isCurrentlyReceivingDeathLink = false;
        }

        private static void SendDeathLink(string cause)
        {
            if (!IsDeathLinkEnabled())
            {
                return;
            }

            if (_isCurrentlyReceivingDeathLink)
            {
                _isCurrentlyReceivingDeathLink = false;
                return;
            }

            ModEntry.Instance.State.Wallet.DeathLinks++;
            if (!Context.IsMainPlayer)
            {
                // Farmhand: the host sends it to AP and kills the other players.
                _modMessaging?.SendFarmhandDied(Game1.player.Name, cause);
                return;
            }

            _archipelago.SendDeathLink(cause);
            _modMessaging?.BroadcastCoopDeath(Game1.player.Name, cause);
        }

        private static void OnHostFarmhandDied(long farmhandId, CoopDeathMessage message)
        {
            try
            {
                if (!IsDeathLinkEnabled())
                {
                    return;
                }

                var cause = $"{message.PlayerName} {message.Cause}";
                _logger?.LogInfo($"Co-op death: {cause}.");
                _archipelago.SendDeathLink(cause);
                Game1.chatBox?.addInfoMessage($"You have been killed because {cause}.");
                KillLocalPlayerFromDeathLink();
                _modMessaging?.BroadcastCoopDeath(message.PlayerName, message.Cause, farmhandId);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed in {nameof(OnHostFarmhandDied)}:\n{ex}");
            }
        }

        private static void OnFarmhandCoopDeath(CoopDeathMessage message)
        {
            try
            {
                if (!IsDeathLinkEnabled())
                {
                    return;
                }

                var reason = message.PlayerName == null ? message.Cause : $"{message.PlayerName} {message.Cause}";
                Game1.chatBox?.addInfoMessage($"You have been killed because of {reason}.");
                KillLocalPlayerFromDeathLink();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed in {nameof(OnFarmhandCoopDeath)}:\n{ex}");
            }
        }

        public void HookIntoDeathlinkEvents()
        {
            HookIntoDeathEvent();
            HookIntoPassOutEvent();
        }

        private void HookIntoDeathEvent()
        {
            _harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.Update)),
                prefix: new HarmonyMethod(typeof(DeathManager), nameof(Update_SendDeathLink_Prefix))
            );
        }

        private void HookIntoPassOutEvent()
        {
            _harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), "performPassOut"),
                prefix: new HarmonyMethod(typeof(DeathManager), nameof(PerformPassOut_SendDeathLink_Prefix))
            );
        }

        public static bool Update_SendDeathLink_Prefix(Farmer __instance, GameTime time, GameLocation location)
        {
            try
            {
                if (__instance.CanMove && __instance.health <= 0 && !Game1.killScreen && Game1.timeOfDay < 2600)
                {
                    SendDeathLink("died in combat");
                    TriggerDeathBundle();
                }

                return MethodPrefix.RUN_ORIGINAL_METHOD;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed in {nameof(Update_SendDeathLink_Prefix)}:\n{ex}");
                return MethodPrefix.RUN_ORIGINAL_METHOD;
            }
        }

        private static void TriggerDeathBundle()
        {
            if (Game1.currentLocation is not CommunityCenter communityCenter)
            {
                return;
            }

            ArchipelagoJunimoNoteMenu.CompleteBundleIfExists(MemeBundleNames.DEATH);
        }

        public static bool PerformPassOut_SendDeathLink_Prefix(Farmer __instance)
        {
            try
            {
                if (__instance.stamina <= -15)
                {
                    SendDeathLink("Passed out");
                }

                return MethodPrefix.RUN_ORIGINAL_METHOD;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed in {nameof(PerformPassOut_SendDeathLink_Prefix)}:\n{ex}");
                return MethodPrefix.RUN_ORIGINAL_METHOD;
            }
        }
    }
}
