using System.Collections.Generic;

namespace StardewArchipelago.Networking
{
    // Host -> farmhands: the host's full season order, sent whenever it picks the next season.
    public class SeasonsOrderMessage
    {
        public List<string> SeasonsOrder { get; set; }

        public SeasonsOrderMessage()
        {
        }

        public SeasonsOrderMessage(List<string> seasonsOrder)
        {
            SeasonsOrder = seasonsOrder;
        }
    }
}
