namespace StardewArchipelago.Networking
{
    public class LocationCheckedMessage
    {
        public string LocationName { get; set; }

        public LocationCheckedMessage()
        {
        }

        public LocationCheckedMessage(string locationName)
        {
            LocationName = locationName;
        }
    }
}
