using System.Collections.Generic;
using StardewArchipelago.Serialization;

namespace StardewArchipelago.Networking
{
    public class SlotDataSnapshotMessage
    {
        public string SlotName { get; set; }
        public Dictionary<string, object> SlotDataFields { get; set; }
        public ArchipelagoStateDto State { get; set; }
        public Dictionary<string, int> ReceivedItemCounts { get; set; }
        public HashSet<string> LocationNames { get; set; }
        public HashSet<string> CheckedLocationNames { get; set; }

        public Dictionary<string, ScoutedLocationDto> ScoutedLocations { get; set; }

        public SlotDataSnapshotMessage()
        {
        }

        public SlotDataSnapshotMessage(string slotName, Dictionary<string, object> slotDataFields, ArchipelagoStateDto state, Dictionary<string, int> receivedItemCounts, HashSet<string> locationNames, HashSet<string> checkedLocationNames, Dictionary<string, ScoutedLocationDto> scoutedLocations)
        {
            ScoutedLocations = scoutedLocations;
            SlotName = slotName;
            SlotDataFields = slotDataFields;
            State = state;
            ReceivedItemCounts = receivedItemCounts;
            LocationNames = locationNames;
            CheckedLocationNames = checkedLocationNames;
        }
    }
}
