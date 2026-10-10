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
        private readonly ModMessaging _modMessaging;

        public CoopLocationChecker(ILogger logger, StardewArchipelagoClient archipelago, List<string> locationsAlreadyChecked, ModMessaging modMessaging) : base(logger, archipelago, locationsAlreadyChecked)
        {
            _modMessaging = modMessaging;
        }

        public override void AddCheckedLocation(string locationName)
        {
            if (Context.IsMainPlayer)
            {
                base.AddCheckedLocation(locationName);
                return;
            }

            _modMessaging.BroadcastLocationChecked(locationName);
        }

        public override void AddCheckedLocations(string[] locationNames)
        {
            if (Context.IsMainPlayer)
            {
                base.AddCheckedLocations(locationNames);
                return;
            }

            foreach (var locationName in locationNames)
            {
                _modMessaging.BroadcastLocationChecked(locationName);
            }
        }
    }
}
