using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.Utilities.Interfaces;
using Microsoft.Xna.Framework;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Archipelago.SlotData.SlotEnums;
using StardewArchipelago.Extensions;
using StardewArchipelago.GameModifications.CodeInjections;
using StardewArchipelago.Locations;
using StardewArchipelago.Locations.CodeInjections.Vanilla;
using StardewArchipelago.Stardew;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using System.Collections.Generic;
using System.Linq;

namespace StardewArchipelago.GameModifications
{
    public class StartingResources
    {
        private const int UNLIMITED_MONEY_AMOUNT = 9999999;
        private const int MINIMUM_UNLIMITED_MONEY = 1000000;
        private readonly ILogger _logger;
        private readonly StardewArchipelagoClient _archipelago;
        private readonly LocationChecker _locationChecker;
        private readonly StardewItemManager _stardewItemManager;

        public StartingResources(ILogger logger, StardewArchipelagoClient archipelago, LocationChecker locationChecker, StardewItemManager stardewItemManager)
        {
            _logger = logger;
            _archipelago = archipelago;
            _locationChecker = locationChecker;
            _stardewItemManager = stardewItemManager;
        }

        public void GivePlayerStartingResources()
        {
            _logger.LogDebug($"Starting {nameof(GivePlayerStartingResources)} (Game1.Date.TotalDays: {Game1.Date.TotalDays})");
            GivePlayerStartingMoney();
            if (Game1.Date.TotalDays == 0 && Context.IsMainPlayer)
            {
                _logger.LogDebug($"It's the first day! Taking care of Quick Start, Tools and Backpacks!");
                GivePlayerQuickStart();
                RemoveStartingTools();
                RemoveStartingBackpack();
            }

            // GivePlayerQuickStart above only ever targets the shared "FarmHouse" by name, so it must
            // stay host-only (above) - a farmhand's own GivePlayerStartingResources call (this whole
            // method also runs on a farmhand's process, see ModEntry.OnFarmhandSlotDataReceived) would
            // otherwise dump a second full starter kit into the host's house. Farmhands still each get
            // their own Iridium Band, placed by the host next to the host's starter kit (see below).
            GiveQuickStartIridiumBandsToFarmhands();

            // These demolish shared Farm buildings, so only the host decides. A farmhand's process also runs
            // this method on join, and with no AP session its HasReceivedItem was always false - so every
            // farmhand joining demolished the shared farmhouse (and bin/bowl) even though the host had
            // received the starting Progressive House / Shipping Bin / Pet Bowl.
            if (Context.IsMainPlayer)
            {
                RemoveHouse();
                RemoveShippingBin();
                RemovePetBowls();
            }
            SendGilTelephoneLetter();
            GrantStartingQuest();
        }

        private void GivePlayerStartingMoney()
        {
            var startingMoney = _archipelago.SlotData.StartingMoney;
            var isUnlimitedMoney = startingMoney.IsUnlimited();
            if (isUnlimitedMoney)
            {
                startingMoney = UNLIMITED_MONEY_AMOUNT;
            }

            if (Game1.Date.TotalDays == 0 || (isUnlimitedMoney && Game1.player.Money < MINIMUM_UNLIMITED_MONEY))
            {
                Game1.player.Money = startingMoney;
            }
        }

        private void GivePlayerQuickStart()
        {
            if (Game1.getLocationFromName("FarmHouse") is not FarmHouse farmhouse)
            {
                return;
            }

            RemoveGiftBoxes(farmhouse);

            var (location, originalTile) = GetGiftboxPlacement(farmhouse);

            var startCrop = _stardewItemManager.GetItemByName(GetStartingCropForThisSeason()).PrepareForGivingToFarmer(15);
            CreateGiftBoxItemInEmptySpot(location, originalTile, startCrop);
            var telephone = _stardewItemManager.GetItemByName("Telephone").PrepareForGivingToFarmer();
            CreateGiftBoxItemInEmptySpot(location, originalTile, telephone);
            var calendar = _stardewItemManager.GetItemByName("Calendar").PrepareForGivingToFarmer();
            CreateGiftBoxItemInEmptySpot(location, originalTile, calendar);

            if (!_archipelago.SlotData.QuickStart)
            {
                var chest = _stardewItemManager.GetItemByName("Chest").PrepareForGivingToFarmer(1);
                CreateGiftBoxItemInEmptySpot(location, originalTile, chest);
                return;
            }

            var chests = _stardewItemManager.GetItemByName("Chest").PrepareForGivingToFarmer(5);
            var iridiumBand = _stardewItemManager.GetItemByName("Iridium Band").PrepareForGivingToFarmer();
            var qualitySprinklers = _stardewItemManager.GetItemByName("Quality Sprinkler").PrepareForGivingToFarmer(4);
            var autoPetters = _stardewItemManager.GetItemByName("Auto-Petter").PrepareForGivingToFarmer(2);
            var autoGrabbers = _stardewItemManager.GetItemByName("Auto-Grabber").PrepareForGivingToFarmer(2);

            CreateGiftBoxItemInEmptySpot(location, originalTile, chests);
            CreateGiftBoxItemInEmptySpot(location, originalTile, iridiumBand);
            CreateGiftBoxItemInEmptySpot(location, originalTile, qualitySprinklers);
            CreateGiftBoxItemInEmptySpot(location, originalTile, autoPetters);
            CreateGiftBoxItemInEmptySpot(location, originalTile, autoGrabbers);

#if TILESANITY
            var paths = _stardewItemManager.GetItemByName("Crystal Path").PrepareForGivingToFarmer(100);
            var seed_maker = _stardewItemManager.GetItemByName("Seed Maker").PrepareForGivingToFarmer(1);
            CreateGiftBoxItemInEmptySpot(location, originalTile, paths);
            CreateGiftBoxItemInEmptySpot(location, originalTile, seed_maker);
#endif
        }

        // Host-side, one flag per farmhand (keyed by their multiplayer ID). Stored on the host's own
        // Farmer so only the host's process ever reads/writes it. Deliberately a different key from the
        // old per-cabin "apQuickStartIridiumBandGiven" flag (set on the farmhand's Farmer).
        private const string QUICK_START_FARMHAND_BAND_MAIL_KEY_PREFIX = "apQuickStartIridiumBandGivenTo_";

        // Host-only: every farmhand who has actually joined gets one Iridium Band in a gift box at the same
        // spot as the host's own quick-start kit (GetGiftboxPlacement - inside the main FarmHouse, or outside
        // its door with StartWithout.House). This used to be placed in the farmhand's own cabin from the
        // farmhand's process, but a cabin isn't reachable early on (e.g. start_without Buildings), and a
        // farmhand writing objects into a shared location isn't guaranteed to sync - the host owns these.
        // Not day-gated, since a farmhand can join on any day; called both here (once per session) and from
        // ModEntry.DoArchipelagoDayStartedProcesses (so a farmhand who joins mid-session gets it next morning).
        public void GiveQuickStartIridiumBandsToFarmhands()
        {
            if (!Context.IsMainPlayer || !_archipelago.SlotData.QuickStart)
            {
                return;
            }

            if (Game1.getLocationFromName("FarmHouse") is not FarmHouse farmhouse)
            {
                return;
            }

            foreach (var farmhand in Game1.getAllFarmhands())
            {
                // Every cabin has a placeholder Farmer until someone claims it; only give to real players.
                if (farmhand == null || !farmhand.isCustomized.Value || string.IsNullOrWhiteSpace(farmhand.Name))
                {
                    continue;
                }

                var mailKey = $"{QUICK_START_FARMHAND_BAND_MAIL_KEY_PREFIX}{farmhand.UniqueMultiplayerID}";
                if (Game1.player.mailReceived.Contains(mailKey))
                {
                    continue;
                }

                var (location, originalTile) = GetGiftboxPlacement(farmhouse);
                var iridiumBand = _stardewItemManager.GetItemByName("Iridium Band").PrepareForGivingToFarmer();
                CreateGiftBoxItemInEmptySpot(location, originalTile, iridiumBand);
                Game1.player.mailReceived.Add(mailKey);
                _logger.LogInfo($"Placed a quick-start Iridium Band for farmhand {farmhand.Name} next to the starter kit.");
            }
        }

        private (GameLocation, Vector2) GetGiftboxPlacement(FarmHouse farmhouse)
        {
            GameLocation location = farmhouse;
            var originalTile = new Vector2(3f, 7f);

            if (_archipelago.SlotData.StartWithout.HasFlag(StartWithout.House))
            {
                var farm = (Farm)Game1.RequireLocation("Farm");
                location = farm;
                var startingTile = farm.GetMainFarmHouseEntry();
                originalTile = new Vector2(startingTile.X - 2, startingTile.Y + 3);
            }
            return (location, originalTile);
        }

        private void RemoveStartingTools()
        {
            if (!_archipelago.SlotData.StartWithout.HasFlag(StartWithout.Tools))
            {
                return;
            }

            Game1.player.Items.Clear();
        }

        private void RemoveStartingBackpack()
        {
            if (!_archipelago.SlotData.StartWithout.HasFlag(StartWithout.Backpack))
            {
                return;
            }

            if (_archipelago.SlotData.BackpackProgression != BackpackProgression.Vanilla)
            {
                Game1.player.MaxItems = 0;
                var farmhouse = Game1.getLocationFromName("FarmHouse") as FarmHouse;
                var (location, originalTile) = GetGiftboxPlacement(farmhouse);
                while (Game1.player.Items.Count > Game1.player.MaxItems)
                {
                    var item = Game1.player.Items[0];
                    if (item != null)
                    {
                        CreateGiftBoxItemInEmptySpot(location, originalTile, item);
                        Game1.player.Items[0] = null;
                    }
                    Game1.player.Items.RemoveAt(0);
                }
            }
        }

        private void RemoveGiftBoxes(FarmHouse farmhouse)
        {
            foreach (var position in farmhouse.Objects.Keys.ToList())
            {
                if (!(farmhouse.Objects[position] is Chest chest) || !chest.giftbox.Value)
                {
                    continue;
                }

                farmhouse.Objects.Remove(position);
            }
        }

        private string GetStartingCropForThisSeason()
        {
            if (_archipelago.SlotData.FarmType.GetWhichFarm() == (int)SupportedFarmType.Meadowlands)
            {
                return "Hay";
            }

            if (_archipelago.SlotData.Cropsanity == Cropsanity.Shuffled)
            {
                return "Mixed Seeds";
            }

            return Game1.currentSeason switch
            {
                "spring" => "Parsnip Seeds",
                "summer" => "Wheat Seeds",
                "fall" => "Bok Choy Seeds",
                "winter" => "Winter Seeds",
                _ => "Mixed Seeds",
            };
        }

        private void CreateGiftBoxItemInEmptySpot(GameLocation map, Vector2 originTile, Item itemToGift)
        {
            var emptySpot = originTile;
            var maxStep = 3;
            while (map.objects.ContainsKey(emptySpot) || map.IsTileBlockedBy(emptySpot) || map.IsTileOccupiedBy(emptySpot))
            {
                emptySpot.X = emptySpot.X + 1;
                if (emptySpot.X > originTile.X + maxStep)
                {
                    emptySpot.X = originTile.X;
                    emptySpot.Y += 1;
                }

                if (emptySpot.Y > originTile.Y + maxStep)
                {
                    emptySpot.Y = originTile.Y - maxStep;
                }
            }

            map.objects.Add(emptySpot, new Chest(new List<Item> { itemToGift }, emptySpot, true, giftboxIsStarterGift: true));
        }

        private void RemoveHouse()
        {
            if (!_archipelago.SlotData.BuildingProgression.HasFlag(BuildingProgression.Progressive) || _archipelago.HasReceivedItemSafe(CarpenterInjections.BUILDING_PROGRESSIVE_HOUSE))
            {
                return;
            }

            var farm = Game1.getFarm();
            var houses = FarmInjections.FindHouses(farm);
            foreach (var house in houses)
            {
                house.BeforeDemolish();
                farm.destroyStructure(house);
            }
        }

        private void RemoveShippingBin()
        {
            if (!_archipelago.SlotData.BuildingProgression.HasFlag(BuildingProgression.Progressive) || _archipelago.HasReceivedItemSafe("Shipping Bin"))
            {
                return;
            }

            var farm = Game1.getFarm();
            var shippingBins = FarmInjections.FindShippingBins(farm);
            foreach (var shippingBin in shippingBins)
            {
                shippingBin.BeforeDemolish();
                farm.destroyStructure(shippingBin);
            }
        }

        private void RemovePetBowls()
        {
            if (!_archipelago.SlotData.BuildingProgression.HasFlag(BuildingProgression.Progressive) || _archipelago.HasReceivedItemSafe("Pet Bowl"))
            {
                return;
            }

            var farm = Game1.getFarm();
            var petBowls = FarmInjections.FindPetBowls(farm);
            foreach (var petBowl in petBowls)
            {
                petBowl.BeforeDemolish();
                farm.destroyStructure(petBowl);
            }
        }

        private void SendGilTelephoneLetter()
        {
            const string mailId = "Gil_Telephone";
            if (Game1.player.hasOrWillReceiveMail(mailId))
            {
                return;
            }
            Game1.player.mailReceived.Add(mailId);
        }

        public void GrantStartingQuest()
        {
            if (!_locationChecker.IsLocationMissingSafe("Quest: Getting Started"))
            {
                return;
            }

            Game1.player.addQuest(Game1.GetFarmTypeID() == "MeadowlandsFarm" ? "132" : "6");
            Game1.dayTimeMoneyBox.PingQuestLog();
        }
    }
}
