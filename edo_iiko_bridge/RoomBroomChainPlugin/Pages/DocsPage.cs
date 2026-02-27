using System.Drawing;
using System.Windows.Forms;

namespace Pages
{
    public class DocsPage : UserControl
    {
        public DocsPage()
        {
            Dock = DockStyle.Fill;

            var label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "RoomBroom → Документы (пустая тестовая страница)",
            };

            Controls.Add(label);
        }
    }
}

