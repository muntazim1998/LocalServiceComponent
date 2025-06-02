namespace LocalServiceStreaming
{
    public class ConstantVariable
    {
        public static int BoundCapacity = 1;
        public static int websocketPort = 9898;
        public static int LocalPort = 8484;
        public static string FFMPegPath { get; set; }

        public static string GetFFMPegPath()
        {
            var dir = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\FFMpeg";
           CreateDirectory(dir);
            return dir;
        }
        public static void CreateDirectory(string folder)
        {
            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
            }
            catch (IOException ex)
            {
            }
        }
    }
}
