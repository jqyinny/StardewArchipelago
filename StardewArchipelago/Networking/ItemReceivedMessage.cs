using KaitoKid.ArchipelagoUtilities.Net.Client;

namespace StardewArchipelago.Networking
{
    public class ItemReceivedMessage
    {
        public ReceivedItem ReceivedItem { get; set; }

        public ItemReceivedMessage()
        {
        }

        public ItemReceivedMessage(ReceivedItem receivedItem)
        {
            ReceivedItem = receivedItem;
        }
    }
}
