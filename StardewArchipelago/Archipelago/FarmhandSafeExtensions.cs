using System.Collections.Generic;
using System.Linq;
using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.ArchipelagoUtilities.Net.Client;
using StardewArchipelago.Locations;

namespace StardewArchipelago.Archipelago
{
    // The farmhand-safe *Safe methods live on StardewArchipelagoClient / StardewLocationChecker, but many
    // call sites hold these as the library base types (ArchipelagoClient / LocationChecker). These
    // extensions make the *Safe names usable everywhere: an instance method of the same name always wins
    // when the declared type is the derived class, and these forward to it otherwise. On the host the
    // *Safe methods behave exactly like the raw ones (they delegate whenever a live session exists), so
    // using them unconditionally only changes behavior on a farmhand, where the raw calls silently return
    // 0/false (no AP session).
    public static class FarmhandSafeExtensions
    {
        public static bool HasReceivedItemSafe(this ArchipelagoClient archipelago, string itemName)
        {
            return archipelago is StardewArchipelagoClient stardewArchipelago
                ? stardewArchipelago.HasReceivedItemSafe(itemName)
                : archipelago.HasReceivedItem(itemName);
        }

        public static int GetReceivedItemCountSafe(this ArchipelagoClient archipelago, string itemName)
        {
            return archipelago is StardewArchipelagoClient stardewArchipelago
                ? stardewArchipelago.GetReceivedItemCountSafe(itemName)
                : archipelago.GetReceivedItemCount(itemName);
        }

        public static bool LocationExistsSafe(this ArchipelagoClient archipelago, string locationName)
        {
            return archipelago is StardewArchipelagoClient stardewArchipelago
                ? stardewArchipelago.LocationExistsSafe(locationName)
                : archipelago.LocationExists(locationName);
        }

        public static bool IsLocationMissingSafe(this LocationChecker locationChecker, string locationName)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.IsLocationMissingSafe(locationName)
                : locationChecker.IsLocationMissing(locationName);
        }

        public static bool LocationExistsSafe(this LocationChecker locationChecker, string locationName)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.LocationExistsSafe(locationName)
                : locationChecker.LocationExists(locationName);
        }

        public static bool IsLocationCheckedSafe(this LocationChecker locationChecker, string locationName)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.IsLocationCheckedSafe(locationName)
                : locationChecker.IsLocationChecked(locationName);
        }

        public static bool IsLocationNotCheckedSafe(this LocationChecker locationChecker, string locationName)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.IsLocationNotCheckedSafe(locationName)
                : locationChecker.IsLocationNotChecked(locationName);
        }

        public static IEnumerable<string> GetAllLocationsSafe(this LocationChecker locationChecker)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.GetAllLocationsSafe()
                : locationChecker.GetAllLocations();
        }

        public static IEnumerable<string> GetAllLocationsNotCheckedSafe(this LocationChecker locationChecker)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.GetAllLocationsNotCheckedSafe()
                : locationChecker.GetAllLocationsNotChecked();
        }

        public static IEnumerable<string> GetAllMissingLocationNamesSafe(this LocationChecker locationChecker)
        {
            return locationChecker is StardewLocationChecker stardewLocationChecker
                ? stardewLocationChecker.GetAllMissingLocationNamesSafe()
                : locationChecker.GetAllMissingLocationNames();
        }

        public static List<ReceivedItem> GetAllReceivedItemsSafe(this ArchipelagoClient archipelago)
        {
            return archipelago is StardewArchipelagoClient stardewArchipelago
                ? stardewArchipelago.GetAllReceivedItemsSafe()
                : archipelago.GetAllReceivedItems().ToList();
        }
    }
}
