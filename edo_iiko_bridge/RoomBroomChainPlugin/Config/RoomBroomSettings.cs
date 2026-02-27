using System.Configuration;

namespace RoomBroomChainPlugin.Config
{
    internal sealed class RoomBroomSettings : ApplicationSettingsBase
    {
        private static readonly RoomBroomSettings defaultInstance =
            (RoomBroomSettings)Synchronized(new RoomBroomSettings());

        public static RoomBroomSettings Default => defaultInstance;

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string SettingsMain
        {
            get => (string)this[nameof(SettingsMain)];
            set => this[nameof(SettingsMain)] = value;
        }
    }
}

