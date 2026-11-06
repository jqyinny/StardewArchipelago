namespace StardewArchipelago.Networking
{
    // Shared co-op death (death link between the players of one slot):
    // farmhand -> host: "I died" (Cause is filled, e.g. "died in combat");
    // host -> farmhands: "die now" (Cause says why, for the chat message).
    public class CoopDeathMessage
    {
        public string PlayerName { get; set; }
        public string Cause { get; set; }

        public CoopDeathMessage()
        {
        }

        public CoopDeathMessage(string playerName, string cause)
        {
            PlayerName = playerName;
            Cause = cause;
        }
    }
}
