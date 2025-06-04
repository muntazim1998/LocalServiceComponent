namespace LocalServiceStreaming.Models
{
    public class StreamModel
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ChannelId { get; set; }
        public string RtspChannel { get; set; }
        public string Resolution { get; set; }
    }
}
