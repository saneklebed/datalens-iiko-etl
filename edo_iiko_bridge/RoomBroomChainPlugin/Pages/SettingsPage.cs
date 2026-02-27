using System.Drawing;
using System.Windows.Forms;

namespace Pages
{
    public class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            Dock = DockStyle.Fill;

            var label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "RoomBroom → Настройки (пустая тестовая страница)",
            };

            Controls.Add(label);
        }
    }
}

