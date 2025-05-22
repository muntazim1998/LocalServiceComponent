using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalServiceComponent
{
    public class UtilityConstants
    {
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
