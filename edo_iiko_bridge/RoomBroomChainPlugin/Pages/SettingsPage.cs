using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using RoomBroomChainPlugin.Config;

namespace Pages
{
    // XtraUserControl унаследован от UserControl, подходит для PluginTabPageBase.
    public class SettingsPage : XtraUserControl
    {
        private readonly TextEdit _login = new TextEdit();
        private readonly TextEdit _password = new TextEdit();
        private readonly TextEdit _token = new TextEdit();
        private readonly SimpleButton _save = new SimpleButton();
        private readonly ToggleSwitch _createInvoiceWithPosting = new ToggleSwitch();
        private readonly ToggleSwitch _enableReports = new ToggleSwitch();

        public SettingsPage()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            LoadFromStore();
        }

        private void BuildUi()
        {
            var edoGroup = new GroupControl
            {
                Text = "Выбор ЭДО",
                Dock = DockStyle.Top,
                Height = 250,
                Padding = new Padding(8),
            };

            var inner = new LayoutControl { Dock = DockStyle.Fill };
            inner.Root = new LayoutControlGroup
            {
                EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.True,
                TextVisible = false,
                GroupBordersVisible = false,
            };
            edoGroup.Controls.Add(inner);

            var operatorLabel = new LabelControl
            {
                Text = "Ваш оператор ЭДО — Диадок",
                Appearance = { Font = new Font("Segoe UI", 9, FontStyle.Regular) },
            };

            _login.Properties.NullValuePrompt = "manager@company.com";
            _password.Properties.PasswordChar = '●';
            _token.Properties.NullValuePrompt = "API Token";

            _save.Text = "Сохранить";
            _save.Click += OnSaveClick;

            _createInvoiceWithPosting.Properties.OffText = "Выкл";
            _createInvoiceWithPosting.Properties.OnText = "Создавать накладные с проведением";
            _createInvoiceWithPosting.Properties.ShowText = true;

            _enableReports.Properties.OffText = "Выкл";
            _enableReports.Properties.OnText = "Вкл";

            inner.Controls.Add(operatorLabel);
            inner.Controls.Add(_login);
            inner.Controls.Add(_password);
            inner.Controls.Add(_token);
            inner.Controls.Add(_save);
            inner.Controls.Add(_createInvoiceWithPosting);

            var g = inner.Root;
            g.AddItem("", operatorLabel).TextVisible = false;
            g.AddItem("Логин", _login);
            g.AddItem("Пароль", _password);
            g.AddItem("Api Token", _token);
            g.AddItem("", _createInvoiceWithPosting).TextVisible = false;
            g.AddItem("", _save).TextVisible = false;

            var reportsGroup = new GroupControl
            {
                Text = "Отчёты",
                Dock = DockStyle.Top,
                Height = 110,
                Padding = new Padding(8),
            };

            var reportsLayout = new LayoutControl { Dock = DockStyle.Fill };
            reportsLayout.Root = new LayoutControlGroup
            {
                EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.True,
                TextVisible = false,
                GroupBordersVisible = false,
            };
            reportsGroup.Controls.Add(reportsLayout);

            var reportsHint = new LabelControl
            {
                Text = "Автоматически формировать отчёт после получения/подписания документов",
                AutoSizeMode = LabelAutoSizeMode.Vertical,
            };

            reportsLayout.Controls.Add(reportsHint);
            reportsLayout.Controls.Add(_enableReports);
            var rg = reportsLayout.Root;
            rg.AddItem("", reportsHint).TextVisible = false;
            rg.AddItem("", _enableReports).TextVisible = false;

            var spacer = new Panel { Dock = DockStyle.Top, Height = 8 };

            Controls.Add(new Panel { Dock = DockStyle.Fill }); // заполнитель
            Controls.Add(reportsGroup);
            Controls.Add(spacer);
            Controls.Add(edoGroup);
        }

        private void LoadFromStore()
        {
            var cfg = ConfigStore.Load();
            _login.Text = cfg.DiadocLogin ?? "";
            _password.Text = cfg.DiadocPassword ?? "";
            _token.Text = cfg.DiadocApiToken ?? "";
            _createInvoiceWithPosting.IsOn = cfg.CreateInvoiceWithPosting;
            _enableReports.IsOn = cfg.EnableReports;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            var cfg = new RoomBroomConfig
            {
                DiadocLogin = _login.Text?.Trim() ?? "",
                DiadocPassword = _password.Text ?? "",
                DiadocApiToken = _token.Text?.Trim() ?? "",
                CreateInvoiceWithPosting = _createInvoiceWithPosting.IsOn,
                EnableReports = _enableReports.IsOn,
            };

            ConfigStore.Save(cfg);
            XtraMessageBox.Show("Сохранено", "RoomBroom", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

