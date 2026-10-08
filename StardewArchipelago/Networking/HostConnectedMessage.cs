namespace StardewArchipelago.Networking
{
    public class HostConnectedMessage
    {
        public string SlotName { get; set; }

        public HostConnectedMessage()
        {
        }

        public HostConnectedMessage(string slotName)
        {
            SlotName = slotName;
        }
    }
}
