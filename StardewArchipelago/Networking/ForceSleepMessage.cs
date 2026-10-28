namespace StardewArchipelago.Networking
{
    // No payload needed - this just tells every connected farmhand "the host is sleeping (for
    // multiple days), go to sleep too" so the host's own day-advance isn't blocked waiting on a
    // farmhand who never interacted with their own bed. See MultiSleepManager/NOTES.md.
    public class ForceSleepMessage
    {
    }
}
