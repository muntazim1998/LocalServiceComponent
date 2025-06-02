namespace LocalServiceStreaming.Models
{
    public class PlaybackModel
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int ChannelId { get; set; }
        public string RtspChannel { get; set; }
        public string Resolution { get; set; }
        public string StartTime { get; set; }
    }
}
