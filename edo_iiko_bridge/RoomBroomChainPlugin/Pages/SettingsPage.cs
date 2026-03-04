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
        private readonly ToggleSwitch _confirmSignOrReject = new ToggleSwitch();
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
            _createInvoiceWithPosting.Properties.OnText = "Вкл";
            _createInvoiceWithPosting.Properties.ShowText = true;

            _confirmSignOrReject.Properties.OffText = "Выкл";
            _confirmSignOrReject.Properties.OnText = "Вкл";
            _confirmSignOrReject.Properties.ShowText = true;

            _enableReports.Properties.OffText = "Выкл";
            _enableReports.Properties.OnText = "Вкл";

            inner.Controls.Add(operatorLabel);
            inner.Controls.Add(_login);
            inner.Controls.Add(_password);
            inner.Controls.Add(_token);
            inner.Controls.Add(_save);
            inner.Controls.Add(_createInvoiceWithPosting);
            inner.Controls.Add(_confirmSignOrReject);

            var g = inner.Root;
            g.AddItem("", operatorLabel).TextVisible = false;
            g.AddItem("Логин", _login);
            g.AddItem("Пароль", _password);
            g.AddItem("Api Token", _token);

            var postingLabel = g.AddItem("Создавать накладные с проведением", _createInvoiceWithPosting);
            postingLabel.TextVisible = true;

            var confirmLabel = g.AddItem("Доп. подтверждение для подписи/отказа", _confirmSignOrReject);
            confirmLabel.TextVisible = true;
            g.AddItem("", _save).TextVisible = false;

            // Блок «Отчёты» временно отключён — настройка автоотчётов не используется.
            Controls.Add(new Panel { Dock = DockStyle.Fill }); // заполнитель
            Controls.Add(edoGroup);
        }

        private void LoadFromStore()
        {
            var cfg = ConfigStore.Load();
            _login.Text = cfg.DiadocLogin ?? "";
            _password.Text = cfg.DiadocPassword ?? "";
            _token.Text = cfg.DiadocApiToken ?? "";
            _createInvoiceWithPosting.IsOn = cfg.CreateInvoiceWithPosting;
            _confirmSignOrReject.IsOn = cfg.ConfirmSignOrReject;
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
                ConfirmSignOrReject = _confirmSignOrReject.IsOn,
                EnableReports = _enableReports.IsOn,
            };

            ConfigStore.Save(cfg);
            XtraMessageBox.Show("Сохранено", "RoomBroom", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

