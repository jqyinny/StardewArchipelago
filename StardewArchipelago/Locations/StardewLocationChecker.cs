using System.Collections.Generic;
using System.Linq;
using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.Utilities.Interfaces;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Goals;

namespace StardewArchipelago.Locations
{

    public class StardewLocationChecker : LocationChecker, ILocationChecker
    {
        public const bool PREVENT_SENDING_CHECKS = false;

        private readonly StardewArchipelagoClient _archipelago;
        private readonly LocationNameMatcher _locationNameMatcher;

        public StardewLocationChecker(ILogger logger, StardewArchipelagoClient archipelago, List<string> locationsAlreadyChecked) : base(logger, archipelago, locationsAlreadyChecked)
        {
            _archipelago = archipelago;
            _locationNameMatcher = new LocationNameMatcher();
        }

        public override void SendAllLocationChecks()
        {
#pragma warning disable CS0162 // Unreachable code detected
            if (PREVENT_SENDING_CHECKS)
            {
                return;
            }
#pragma warning restore CS0162 // Unreachable code detected

            base.SendAllLocationChecks();
            GoalCodeInjection.CheckAllsanityGoalCompletion();
        }

        public override void ClearCache()
        {
            base.ClearCache();
            _locationNameMatcher.ClearCache();
        }

        // GetAllLocationsNotChecked()/GetAllLocations() (inherited, non-virtual) gate on IsConnected and
        // return empty with no live connection - same bug family as GetReceivedItemCount/LocationExists
        // (see StardewArchipelagoClient), confirmed via IL inspection. On a farmhand that made every one
        // of these methods return nothing, so the AP symbol never appeared on an un-checked item (e.g. a
        // craftable recipe) even when it genuinely hadn't been checked yet. These farmhand-safe
        // equivalents fall back to the client's synced location-name snapshot and its own locally
        // tracked checked-location cache (see StardewArchipelagoClient.GetAllLocationNamesSafe/
        // GetFarmhandCheckedLocationsSafe) instead of the live session.
        private IEnumerable<string> GetAllLocationsSafe()
        {
            return _archipelago.GetSession() != null ? GetAllLocations() : _archipelago.GetAllLocationNamesSafe();
        }

        private IEnumerable<string> GetAllLocationsNotCheckedSafe()
        {
            if (_archipelago.GetSession() != null)
            {
                return GetAllLocationsNotChecked();
            }

            var alreadyChecked = _archipelago.GetFarmhandCheckedLocationsSafe();
            return _archipelago.GetAllLocationNamesSafe().Where(x => !alreadyChecked.Contains(x));
        }

        private IEnumerable<string> GetAllLocationsAlreadyCheckedSafe()
        {
            return _archipelago.GetSession() != null ? GetAllLocationsAlreadyChecked() : _archipelago.GetFarmhandCheckedLocationsSafe();
        }

        public IEnumerable<string> GetAllLocationsNotChecked(string filter)
        {
            return _locationNameMatcher.GetAllLocationsMatching(GetAllLocationsNotCheckedSafe(), filter);
        }

        public IEnumerable<string> GetAllLocationsNotCheckedMatchingExactly(string filter)
        {
            return _locationNameMatcher.GetAllLocationsMatchingExactly(GetAllLocationsNotCheckedSafe(), filter);
        }

        public IEnumerable<string> GetAllLocationsNotCheckedStartingWith(string prefix)
        {
            return _locationNameMatcher.GetAllLocationsStartingWith(GetAllLocationsNotCheckedSafe(), prefix);
        }

        public IEnumerable<string> GetAllLocationsStartingWith(string prefix)
        {
            return _locationNameMatcher.GetAllLocationsStartingWith(GetAllLocationsSafe(), prefix);
        }

        public string[] GetAllLocationsNotCheckedContainingWord(string wordFilter)
        {
            return _locationNameMatcher.GetAllLocationsContainingWord(GetAllLocationsNotCheckedSafe(), wordFilter);
        }

        public bool IsAnyLocationNotChecked(string filter)
        {
            return _locationNameMatcher.IsAnyLocationMatching(GetAllLocationsNotCheckedSafe(), filter);
        }

        public bool IsAnyLocationNotCheckedStartingWith(string prefix)
        {
            return _locationNameMatcher.IsAnyLocationStartingWith(GetAllLocationsNotCheckedSafe(), prefix);
        }

        public bool IsAnyLocationNotCheckedEndingWith(string prefix)
        {
            return _locationNameMatcher.IsAnyLocationEndingWith(GetAllLocationsNotCheckedSafe(), prefix);
        }

        public IEnumerable<string> GetLocationsByTag(string locationTag)
        {
            return GetAllLocationsSafe().Where(x => _archipelago.DataPackageCache.GetLocation(x).LocationTags.Contains(locationTag));
        }

        public IEnumerable<string> GetMissingLocationsByTag(string locationTag)
        {
            return GetAllMissingLocationNames().Where(x => _archipelago.DataPackageCache.GetLocation(x).LocationTags.Contains(locationTag));
        }

        public IEnumerable<string> GetCheckedLocationsByTag(string locationTag)
        {
            return GetAllLocationsAlreadyCheckedSafe().Where(x => _archipelago.DataPackageCache.GetLocation(x).LocationTags.Contains(locationTag));
        }
        public double GetPercentLocationsChecked()
        {
            var numChecked = GetAllLocationsAlreadyCheckedSafe().Count();
            var numTotal = GetAllLocationsSafe().Count();
            return (double)numChecked / (double)numTotal;
        }
    }
}
