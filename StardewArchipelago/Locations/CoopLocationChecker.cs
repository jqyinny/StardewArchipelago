using System.Collections.Generic;
using System.Linq;
using KaitoKid.Utilities.Interfaces;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Networking;
using StardewModdingAPI;

namespace StardewArchipelago.Locations
{
    // The location checker used on every process in co-op (host and farmhands). All Harmony-patched
    // check callers depend on the LocationChecker base type, so this is a drop-in replacement for
    // StardewLocationChecker:
    // - On the host it sends checks to the AP server exactly like StardewLocationChecker, and also
    //   tells farmhands about each newly recorded check (ModMessaging.BroadcastHostLocationChecked) so
    //   their checked-location cache stays live after the join snapshot.
    // - On a farmhand (no AP connection) it records the check locally and forwards it to the host over
    //   ModMessaging, which sends it to the server.
    public class CoopLocationChecker : StardewLocationChecker
    {
        private readonly ModMessaging _modMessaging;

        // Host-side: every check this checker has recorded or already knew about at construction. Used to
        // broadcast each check to farmhands once. A local set instead of asking the library's
        // GetAllLocationsAlreadyChecked() each time, since some patches call AddCheckedLocation very often
        // (friendsanity re-sends hearts 1..N on every friendship change).
        private readonly HashSet<string> _knownCheckedLocations;

        public CoopLocationChecker(ILogger logger, StardewArchipelagoClient archipelago, List<string> locationsAlreadyChecked, ModMessaging modMessaging) : base(logger, archipelago, locationsAlreadyChecked)
        {
            _modMessaging = modMessaging;
            _knownCheckedLocations = new HashSet<string>(locationsAlreadyChecked ?? new List<string>());
        }

        public override void AddCheckedLocation(string locationName)
        {
            if (Context.IsMainPlayer)
            {
                var isNew = IsNewCheckToBroadcast(locationName);
                base.AddCheckedLocation(locationName);
                if (isNew)
                {
                    // Keep every farmhand's checked-location cache live (see ModMessaging.BroadcastHostLocationChecked).
                    _modMessaging.BroadcastHostLocationChecked(locationName);
                }
                return;
            }

            // base.AddCheckedLocation is skipped here on purpose (it would try to send the check to the
            // AP server, which a farmhand has no live connection to) - but that also means the base
            // class's own local _checkedLocations never gets this entry. Recording it here instead is
            // what the *Safe queries in StardewLocationChecker read back on a farmhand.
            _archipelago.RecordFarmhandCheckedLocation(locationName);
            _modMessaging.BroadcastLocationChecked(locationName);
        }

        public override void AddCheckedLocations(string[] locationNames)
        {
            if (Context.IsMainPlayer)
            {
                var newLocations = locationNames.Where(IsNewCheckToBroadcast).ToArray();
                base.AddCheckedLocations(locationNames);
                foreach (var locationName in newLocations)
                {
                    _modMessaging.BroadcastHostLocationChecked(locationName);
                }
                return;
            }

            foreach (var locationName in locationNames)
            {
                _archipelago.RecordFarmhandCheckedLocation(locationName);
                _modMessaging.BroadcastLocationChecked(locationName);
            }
        }

        // Host-side: true only the first time a location is seen (records it), and only worth a broadcast
        // when there's anyone to tell. Farmhands joining later get the full checked set in their snapshot.
        private bool IsNewCheckToBroadcast(string locationName)
        {
            var isNew = _knownCheckedLocations.Add(locationName);
            return isNew && Context.IsMultiplayer;
        }
    }
}
