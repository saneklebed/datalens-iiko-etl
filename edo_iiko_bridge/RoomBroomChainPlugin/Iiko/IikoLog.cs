using System;
using System.IO;
using System.Text;

namespace RoomBroomChainPlugin.Iiko
{
    internal static class IikoLog
    {
        private static string ResolveLogPath()
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.GetTempPath();
                var folder = Path.Combine(dir, "RoomBroomChainPlugin");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "RoomBroom.iiko.log.txt");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "RoomBroom.iiko.log.txt");
            }
        }

        public static void Write(string message)
        {
            try
            {
                var path = ResolveLogPath();
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // Игнорируем ошибки логирования
            }
        }
    }
}

