using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RoomBroomChainPlugin.Config
{
    public static class ConfigStore
    {
        public static RoomBroomConfig Load()
        {
            try
            {
                var raw = RoomBroomSettings.Default.SettingsMain;
                if (string.IsNullOrWhiteSpace(raw))
                    return new RoomBroomConfig();

                return Deserialize(raw) ?? new RoomBroomConfig();
            }
            catch
            {
                return new RoomBroomConfig();
            }
        }

        public static void Save(RoomBroomConfig cfg)
        {
            var raw = Serialize(cfg);
            RoomBroomSettings.Default.SettingsMain = raw;
            RoomBroomSettings.Default.Save();
        }

        private static string Serialize(RoomBroomConfig cfg)
        {
            var serializer = new DataContractJsonSerializer(typeof(RoomBroomConfig));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, cfg);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static RoomBroomConfig Deserialize(string raw)
        {
            var serializer = new DataContractJsonSerializer(typeof(RoomBroomConfig));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(raw)))
            {
                var obj = serializer.ReadObject(ms);
                return obj as RoomBroomConfig;
            }
        }
    }
}

