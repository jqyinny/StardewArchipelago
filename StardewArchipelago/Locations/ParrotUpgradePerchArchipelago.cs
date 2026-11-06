using System;
using KaitoKid.ArchipelagoUtilities.Net.Client;
using Microsoft.Xna.Framework;
using Netcode;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Extensions;
using StardewValley;
using StardewValley.BellsAndWhistles;
using xTile.Dimensions;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace StardewArchipelago.Locations
{
    // Lives in IslandLocation.parrotUpgradePerches, a network-synced list: the host builds these
    // (GingerIslandPatcher, host-only), and a farmhand entering the location gets them from the host's
    // location data. Netcode recreates each element on the farmhand by type through the parameterless
    // constructor below, so anything the farmhand needs must be a synced NetField (ApLocationName).
    // The apply/completion delegates only exist on the host's instances - vanilla applies parrot
    // upgrades on the host, which is also where the AP check is sent (the *Injections Purchase* methods).
    public class ParrotUpgradePerchArchipelago : ParrotUpgradePerch
    {
        // Set by GingerIslandPatcher on every process, so netcode-created instances can scout too.
        private static StardewArchipelagoClient _archipelago;

        private readonly NetString _apLocationName = new();
        private string _scoutedItemName;

        public string ApLocationName => _apLocationName.Value;

        public static void Initialize(StardewArchipelagoClient archipelago)
        {
            _archipelago = archipelago;
        }

        // Required by Netcode on a farmhand (see class comment).
        public ParrotUpgradePerchArchipelago() : base()
        {
            RegisterArchipelagoNetFields();
        }

        public ParrotUpgradePerchArchipelago(string apLocationName, StardewArchipelagoClient archipelago,
            GameLocation location, Point tile_position, Rectangle upgrade_rectangle,
            int required_nuts, Action apply_upgrade, Func<bool> update_completion_status, string upgrade_name = "", string required_mail = "")
            : base(location, tile_position, upgrade_rectangle, required_nuts, apply_upgrade, update_completion_status, upgrade_name, required_mail)
        {
            RegisterArchipelagoNetFields();
            _archipelago ??= archipelago;
            _apLocationName.Value = apLocationName;
        }

        private void RegisterArchipelagoNetFields()
        {
            NetFields.AddField(_apLocationName, "apLocationName");
        }

        // Scouted lazily: a netcode-created instance only gets ApLocationName after its fields are read.
        private string GetScoutedItemName()
        {
            if (_scoutedItemName != null)
            {
                return _scoutedItemName;
            }

            var scoutedLocation = string.IsNullOrEmpty(ApLocationName) ? null : _archipelago?.ScoutStardewLocation(ApLocationName);
            _scoutedItemName = scoutedLocation == null ? ScoutedLocation.GenericItemName() : scoutedLocation.GetItemName(StringExtensions.TurnHeartsIntoStardewHearts);
            return _scoutedItemName;
        }

        public bool CheckActionArchipelago(Location tileLocation)
        {
            if (!IsAtTile(tileLocation.X, tileLocation.Y) || !IsAvailable())
            {
                return false;
            }

            var upgradeMessageFormat = Game1.content.LoadStringReturnNullIfNotFound("Strings\\UI:UpgradePerch_" + upgradeName.Value);
            var gameLocation = locationRef.Value;
            if (upgradeMessageFormat != null && gameLocation != null)
            {
                var scoutedMessageFormat = ReplaceUnlockWithScoutedItem(upgradeMessageFormat);
                var upgradeMessageFormatted = string.Format(scoutedMessageFormat, requiredNuts.Value);
                costShakeTime = 0.5f;
                squawkTime = 0.5f;
                shakeTime = 0.5f;
                if (locationRef.Value == Game1.currentLocation)
                {
                    Game1.playSound("parrot_squawk");
                }

                if (Game1.netWorldState.Value.GoldenWalnuts >= requiredNuts.Value)
                {
                    gameLocation.createQuestionDialogue(upgradeMessageFormatted, gameLocation.createYesNoResponses(), "UpgradePerch_" + upgradeName.Value);
                }
                else
                {
                    Game1.drawDialogueNoTyping(upgradeMessageFormatted);
                }
            }
            else if (Game1.netWorldState.Value.GoldenWalnuts >= requiredNuts.Value)
            {
                AttemptConstruction();
            }
            else
            {
                ShowInsufficientNuts();
            }

            return true;
        }

        private string ReplaceUnlockWithScoutedItem(string upgradeMessageFormat)
        {
            const string ellipsis = "... ";
            var lastEllipsis = upgradeMessageFormat.LastIndexOf(ellipsis, StringComparison.InvariantCultureIgnoreCase) + ellipsis.Length;
            var messageWithoutUnlock = upgradeMessageFormat[..lastEllipsis];
            var messageWithScout = $"{messageWithoutUnlock}{GetScoutedItemName()}!";
            return messageWithScout;
        }
    }
}
