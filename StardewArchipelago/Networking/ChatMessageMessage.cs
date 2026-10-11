namespace StardewArchipelago.Networking
{
    public class ChatMessageMessage
    {
        public string Text { get; set; }
        public uint PackedColor { get; set; }

        public ChatMessageMessage()
        {
        }

        public ChatMessageMessage(string text, uint packedColor)
        {
            Text = text;
            PackedColor = packedColor;
        }
    }
}
