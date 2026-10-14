using System.Collections.Generic;
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
        private readonly ILogger _logger;
        private readonly StardewArchipelagoClient _archipelago;
        private readonly ModMessaging _modMessaging;

        public CoopLocationChecker(ILogger logger, StardewArchipelagoClient archipelago, List<string> locationsAlreadyChecked, ModMessaging modMessaging) : base(logger, archipelago, locationsAlreadyChecked)
        {
            _logger = logger;
            _archipelago = archipelago;
            _modMessaging = modMessaging;
        }

        public override void AddCheckedLocation(string locationName)
        {
            // TEMP DIAGNOSTIC (see NOTES.md "remote multiplayer" investigation) - remove once the
            // farmhand-checks-not-registering issue is resolved.
            _logger.LogInfo($"[DIAG] CoopLocationChecker.AddCheckedLocation: location={locationName} IsMainPlayer={Context.IsMainPlayer}");

            if (Context.IsMainPlayer)
            {
                base.AddCheckedLocation(locationName);
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
            _logger.LogInfo($"[DIAG] CoopLocationChecker.AddCheckedLocations: locations=[{string.Join(", ", locationNames)}] IsMainPlayer={Context.IsMainPlayer}");

            if (Context.IsMainPlayer)
            {
                base.AddCheckedLocations(locationNames);
                return;
            }

            foreach (var locationName in locationNames)
            {
                _archipelago.RecordFarmhandCheckedLocation(locationName);
                _modMessaging.BroadcastLocationChecked(locationName);
            }
        }
    }
}
