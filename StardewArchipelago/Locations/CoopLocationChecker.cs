using System.Collections.Generic;
using System.Linq;
using KaitoKid.Utilities.Interfaces;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Networking;
using StardewModdingAPI;

namespace StardewArchipelago.Locations
{
    // Pilot for farmhand-reported location checks (Craftsanity only, for now - see NOTES.md
    // "Design concerns - farmhand location checks"). The ~115 Harmony-patched callers all depend
    // on the abstract LocationChecker base type, so this is a drop-in replacement for
    // StardewLocationChecker: on the host it behaves identically; on a farmhand it forwards the
    // check to the host over ModMessaging instead of talking to the AP server directly, since
    // only the host's StardewArchipelagoClient is ever actually connected.
    public class CoopLocationChecker : StardewLocationChecker
    {
        private readonly StardewArchipelagoClient _archipelago;
        private readonly ModMessaging _modMessaging;

        public CoopLocationChecker(ILogger logger, StardewArchipelagoClient archipelago, List<string> locationsAlreadyChecked, ModMessaging modMessaging) : base(logger, archipelago, locationsAlreadyChecked)
        {
            _archipelago = archipelago;
            _modMessaging = modMessaging;
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
            // what GetAllLocationsNotCheckedSafe/GetAllLocationsAlreadyCheckedSafe (see
            // StardewLocationChecker) read back, so the AP symbol disappears for a farmhand too once
            // they've actually checked something, instead of just never appearing at all.
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

        // Host-side: many patches call AddCheckedLocation repeatedly for the same location (e.g. every
        // friendship change re-sends hearts 1..N), so only broadcast checks this host hasn't recorded yet,
        // and only when there's anyone to tell. GetAllLocationsAlreadyChecked is purely local on the host.
        private bool IsNewCheckToBroadcast(string locationName)
        {
            return Context.IsMultiplayer && !GetAllLocationsAlreadyChecked().Contains(locationName);
        }
    }
}
