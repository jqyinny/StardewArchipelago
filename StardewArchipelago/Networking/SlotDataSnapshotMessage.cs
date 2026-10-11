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

        public SlotDataSnapshotMessage()
        {
        }

        public SlotDataSnapshotMessage(string slotName, Dictionary<string, object> slotDataFields, ArchipelagoStateDto state, Dictionary<string, int> receivedItemCounts)
        {
            SlotName = slotName;
            SlotDataFields = slotDataFields;
            State = state;
            ReceivedItemCounts = receivedItemCounts;
        }
    }
}
