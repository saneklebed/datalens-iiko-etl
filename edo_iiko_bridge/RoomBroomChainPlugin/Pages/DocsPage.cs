using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;

using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid;
using RoomBroomChainPlugin.Config;
using RoomBroomChainPlugin.Diadoc;

namespace Pages
{
    public class DocsPage : XtraUserControl
    {
        private ComboBoxEdit _legalEntityCombo;
        private SimpleButton _btnDrafts;
        private SimpleButton _btnCounteragents;
        private SimpleButton _btnIncoming;
        private PanelControl _filterPanel;
        private DateEdit _dateFrom;
        private DateEdit _dateTo;
        private SimpleButton _btnFetchInvoices;
        private GridControl _grid;
        private GridView _gridView;
        private DiadocApiClient _client;
        private List<DiadocOrg> _orgs = new List<DiadocOrg>();
        private List<CounteragentRow> _counteragents;
        private const string ModeDrafts = "Черновики";
        private const string ModeCounteragents = "Контрагенты";
        private const string ModeIncoming = "Входящие";

        public DocsPage()
        {
            Dock = DockStyle.Fill;
            BuildUi();
        }

        private void BuildUi()
        {
            var topPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(8, 8, 8, 8)
            };

            var labelOrg = new LabelControl { Text = "Выбор юр. лица", AutoSizeMode = LabelAutoSizeMode.None };
            _legalEntityCombo = new ComboBoxEdit
            {
                Width = 320,
                Properties = { TextEditStyle = TextEditStyles.Standard }
            };
            _legalEntityCombo.SelectedIndexChanged += (s, e) => OnSelectionChanged();

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0)
            };
            flow.Controls.Add(labelOrg);
            flow.Controls.Add(_legalEntityCombo);
            topPanel.Controls.Add(flow);
            labelOrg.Location = new Point(8, 12);
            _legalEntityCombo.Location = new Point(120, 8);

            var buttonsPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 4, 8, 4)
            };
            _btnDrafts = new SimpleButton { Text = ModeDrafts, Width = 120 };
            _btnCounteragents = new SimpleButton { Text = ModeCounteragents, Width = 120 };
            _btnIncoming = new SimpleButton { Text = ModeIncoming, Width = 120 };
            _btnDrafts.Click += (s, e) => SetMode(ModeDrafts);
            _btnCounteragents.Click += (s, e) => SetMode(ModeCounteragents);
            _btnIncoming.Click += (s, e) => SetMode(ModeIncoming);

            var flowBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            flowBtns.Controls.Add(_btnDrafts);
            flowBtns.Controls.Add(_btnCounteragents);
            flowBtns.Controls.Add(_btnIncoming);
            buttonsPanel.Controls.Add(flowBtns);
            _btnDrafts.Location = new Point(8, 8);
            _btnCounteragents.Location = new Point(136, 8);
            _btnIncoming.Location = new Point(264, 8);

            var now = DateTime.Now;
            _filterPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 4, 8, 4),
                Visible = false
            };
            var labelFrom = new LabelControl { Text = "С", Size = new Size(20, 16) };
            _dateFrom = new DateEdit { Size = new Size(194, 22) };
            _dateFrom.DateTime = new DateTime(now.Year, now.Month, 1);
            var labelTo = new LabelControl { Text = "По", Size = new Size(22, 16) };
            _dateTo = new DateEdit { Size = new Size(194, 22) };
            _dateTo.DateTime = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
            _btnFetchInvoices = new SimpleButton { Text = "Получить накладные", Width = 165 };
            _btnFetchInvoices.Click += (s, e) => RefreshIncomingDocumentsAsync();
            labelFrom.Location = new Point(22, 15);
            _dateFrom.Location = new Point(48, 11);
            labelTo.Location = new Point(249, 15);
            _dateTo.Location = new Point(281, 11);
            _btnFetchInvoices.Location = new Point(509, 9);
            _filterPanel.Controls.AddRange(new Control[] { _btnFetchInvoices, _dateTo, labelTo, _dateFrom, labelFrom });
            _dateFrom.BringToFront();
            _dateTo.BringToFront();

            _grid = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_grid);
            _grid.MainView = _gridView;
            _gridView.Columns.AddField("Organization").Caption = "Организация";
            _gridView.Columns.AddField("Inn").Caption = "ИНН";
            _gridView.Columns.AddField("Kpp").Caption = "КПП";
            _gridView.OptionsBehavior.Editable = false;
            _gridView.OptionsView.ShowGroupPanel = false;

            Controls.Add(_grid);
            Controls.Add(_filterPanel);
            Controls.Add(buttonsPanel);
            Controls.Add(topPanel);

            _currentMode = ModeCounteragents;
            RefreshOrgListAsync();
        }

        private string _currentMode;

        private void SetMode(string mode)
        {
            _currentMode = mode;
            _filterPanel.Visible = (mode == ModeIncoming);
            if (mode == ModeIncoming)
            {
                _grid.DataSource = new List<DiadocDocumentRow>();
                ApplyDocumentsColumns();
            }
            else
                RefreshCurrentViewAsync();
        }

        private void OnSelectionChanged()
        {
            _counteragents = null;
            RefreshCurrentViewAsync();
        }

        private string GetSelectedBoxId()
        {
            var idx = _legalEntityCombo.SelectedIndex;
            if (idx < 0 || idx >= _orgs.Count) return null;
            return _orgs[idx].BoxId;
        }

        private async void RefreshOrgListAsync()
        {
            var cfg = ConfigStore.Load();
            if (string.IsNullOrWhiteSpace(cfg.DiadocApiToken) || string.IsNullOrWhiteSpace(cfg.DiadocLogin))
            {
                _legalEntityCombo.Properties.Items.Clear();
                _legalEntityCombo.Properties.Items.Add("— Укажите в настройках Логин и Api Token Диадока —");
                _legalEntityCombo.SelectedIndex = 0;
                return;
            }
            try
            {
                _client = new DiadocApiClient(cfg);
                var list = await Task.Run(async () => await _client.GetMyOrganizationsAsync().ConfigureAwait(false)).ConfigureAwait(true);
                _orgs = list ?? new List<DiadocOrg>();
                _legalEntityCombo.Properties.Items.Clear();
                foreach (var o in _orgs)
                    _legalEntityCombo.Properties.Items.Add(o.Name ?? "—");
                if (_orgs.Count > 0)
                {
                    _legalEntityCombo.SelectedIndex = 0;
                    RefreshCurrentViewAsync();
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Ошибка Диадока", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyDocumentsColumns()
        {
            _gridView.PopulateColumns();
            var cols = _gridView.Columns;
            if (cols["MessageId"] != null) cols["MessageId"].Visible = false;
            if (cols["EntityId"] != null) cols["EntityId"].Visible = false;
            if (cols["CounterpartyName"] != null) { cols["CounterpartyName"].Caption = "Отправитель"; cols["CounterpartyName"].VisibleIndex = 0; }
            if (cols["CounterpartyInn"] != null) { cols["CounterpartyInn"].Caption = "ИНН"; cols["CounterpartyInn"].VisibleIndex = 1; }
            if (cols["DocumentNumber"] != null) { cols["DocumentNumber"].Caption = "Номер"; cols["DocumentNumber"].VisibleIndex = 2; }
            if (cols["DocumentDate"] != null) { cols["DocumentDate"].Caption = "От"; cols["DocumentDate"].VisibleIndex = 3; }
            if (cols["SentToEdo"] != null) { cols["SentToEdo"].Caption = "Отправлен в ЭДО"; cols["SentToEdo"].VisibleIndex = 4; }
            if (cols["TotalVat"] != null) { cols["TotalVat"].Caption = "Сумма НДС"; cols["TotalVat"].VisibleIndex = 5; }
            if (cols["TotalAmount"] != null) { cols["TotalAmount"].Caption = "Сумма"; cols["TotalAmount"].VisibleIndex = 6; }
            if (cols["StatusText"] != null) { cols["StatusText"].Caption = "Статус"; cols["StatusText"].VisibleIndex = 7; }
            if (cols["SupplierFound"] != null) { cols["SupplierFound"].Caption = "Поставщик"; cols["SupplierFound"].VisibleIndex = 8; }
            if (cols["Supplier"] != null) cols["Supplier"].Visible = false;
            if (cols["IikoInvoice"] != null) { cols["IikoInvoice"].Caption = "Накладная ЭДО"; cols["IikoInvoice"].VisibleIndex = 9; }

            _gridView.CustomDrawCell -= GridView_CustomDrawCell;
            _gridView.CustomDrawCell += GridView_CustomDrawCell;
        }

        private void GridView_CustomDrawCell(object sender, RowCellCustomDrawEventArgs e)
        {
            var view = sender as GridView;
            if (view == null) return;

            if (e.Column.FieldName == "SupplierFound")
            {
                var val = view.GetRowCellValue(e.RowHandle, e.Column);
                if (val is bool b && b)
                    e.Appearance.BackColor = Color.LightGreen;
            }
            else if (e.Column.FieldName == "StatusText")
            {
                var val = view.GetRowCellValue(e.RowHandle, e.Column);
                var text = val?.ToString() ?? "";
                if (text == "Подписан")
                    e.Appearance.BackColor = Color.LightGreen;
                else if (text.Contains("Отказ") || text.Contains("Отклон"))
                    e.Appearance.BackColor = Color.FromArgb(255, 200, 200);
            }
            else if (e.Column.FieldName == "IikoInvoice")
            {
                var val = view.GetRowCellValue(e.RowHandle, e.Column);
                var text = val?.ToString() ?? "";
                if (!string.IsNullOrEmpty(text))
                    e.Appearance.BackColor = Color.LightGreen;
            }
        }

        private async Task EnsureCounteragentsLoadedAsync(string boxId)
        {
            if (_counteragents != null) return;
            _counteragents = await Task.Run(async () =>
                await _client.GetCounteragentsAsync(boxId).ConfigureAwait(false)
            ).ConfigureAwait(true) ?? new List<CounteragentRow>();
        }

        private async void RefreshIncomingDocumentsAsync()
        {
            if (_client == null) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId))
            {
                XtraMessageBox.Show("Выберите юр. лицо.", "Входящие", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var from = _dateFrom.DateTime;
            var to = _dateTo.DateTime;
            try
            {
                await EnsureCounteragentsLoadedAsync(boxId);
                var ca = _counteragents;
                var list = await Task.Run(async () =>
                    await _client.GetDocumentsAsync(boxId, true, from, to, ca).ConfigureAwait(false)
                ).ConfigureAwait(true);
                _grid.DataSource = list ?? new List<DiadocDocumentRow>();
                ApplyDocumentsColumns();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Ошибка Диадока", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void RefreshCurrentViewAsync()
        {
            if (_client == null) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId))
            {
                _grid.DataSource = null;
                return;
            }
            try
            {
                if (_currentMode == ModeCounteragents)
                {
                    _counteragents = await Task.Run(async () => await _client.GetCounteragentsAsync(boxId).ConfigureAwait(false)).ConfigureAwait(true)
                        ?? new List<CounteragentRow>();
                    _grid.DataSource = _counteragents;
                    _gridView.PopulateColumns();
                    if (_gridView.Columns["BoxId"] != null) _gridView.Columns["BoxId"].Visible = false;
                    _gridView.Columns["Organization"].Visible = true;
                    _gridView.Columns["Organization"].Caption = "Организация";
                    _gridView.Columns["Inn"].Visible = true;
                    _gridView.Columns["Inn"].Caption = "ИНН";
                    _gridView.Columns["Kpp"].Visible = true;
                    _gridView.Columns["Kpp"].Caption = "КПП";
                }
                else if (_currentMode == ModeIncoming)
                {
                    var from = _dateFrom.DateTime;
                    var to = _dateTo.DateTime;
                    await EnsureCounteragentsLoadedAsync(boxId);
                    var ca = _counteragents;
                    var list = await Task.Run(async () =>
                        await _client.GetDocumentsAsync(boxId, true, from, to, ca).ConfigureAwait(false)
                    ).ConfigureAwait(true);
                    _grid.DataSource = list ?? new List<DiadocDocumentRow>();
                    ApplyDocumentsColumns();
                }
                else if (_currentMode == ModeDrafts)
                {
                    var list = await Task.Run(async () => await _client.GetDocumentsAsync(boxId, false).ConfigureAwait(false)).ConfigureAwait(true);
                    _grid.DataSource = list ?? new List<DiadocDocumentRow>();
                    ApplyDocumentsColumns();
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Ошибка Диадока", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
