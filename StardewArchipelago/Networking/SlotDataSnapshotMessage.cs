using System.Collections.Generic;

namespace StardewArchipelago.Networking
{
    public class SlotDataSnapshotMessage
    {
        public string SlotName { get; set; }
        public Dictionary<string, object> SlotDataFields { get; set; }

        public SlotDataSnapshotMessage()
        {
        }

        public SlotDataSnapshotMessage(string slotName, Dictionary<string, object> slotDataFields)
        {
            SlotName = slotName;
            SlotDataFields = slotDataFields;
        }
    }
}
