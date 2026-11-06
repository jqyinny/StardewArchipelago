using Archipelago.MultiClient.Net.Enums;

namespace StardewArchipelago.Networking
{
    // Plain serializable copy of the library's ScoutedLocation, so a farmhand (which has no
    // Archipelago session and so can't scout anything itself) can be handed the host's results.
    public class ScoutedLocationDto
    {
        public string LocationName { get; set; }
        public string ItemName { get; set; }
        public string PlayerName { get; set; }
        public string GameName { get; set; }
        public long LocationId { get; set; }
        public long ItemId { get; set; }
        public long PlayerId { get; set; }
        public ItemFlags ClassificationFlags { get; set; }
    }
}
