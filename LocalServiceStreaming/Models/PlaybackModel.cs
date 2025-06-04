namespace LocalServiceStreaming.Models
{
    public class PlaybackModel
    {
        public string ip { get; set; }
        public int rtspPort { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string resolution { get; set; }
        public string playbackUrl { get; set; }
    }
}
