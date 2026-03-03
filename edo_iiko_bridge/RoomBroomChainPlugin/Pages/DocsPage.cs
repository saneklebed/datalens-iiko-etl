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
using RoomBroomChainPlugin.Iiko;

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

        private PanelControl _detailsPanel;
        private LabelControl _detailsHeader;
        private SimpleButton _btnBack;
        private SimpleButton _btnSignAndUpload;
        private SimpleButton _btnUploadOnly;
        private SimpleButton _btnReject;
        private ComboBoxEdit _storeCombo;
        private GridControl _detailsGrid;
        private GridView _detailsView;

        private DiadocApiClient _client;
        private IikoRestoClient _iikoClient;
        private List<DiadocOrg> _orgs = new List<DiadocOrg>();
        private List<CounteragentRow> _counteragents;
        private List<IikoSupplier> _suppliers;
        private const string ModeDrafts = "Черновики";
        private const string ModeCounteragents = "Контрагенты";
        private const string ModeIncoming = "Входящие";
        private const string ModeIncomingDetails = "Входящие_Детали";

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
            _gridView.DoubleClick += GridView_DoubleClick;

            // Панель деталей входящей накладной (второй «экран» внутри документов).
            _detailsPanel = new PanelControl
            {
                Dock = DockStyle.Fill,
                Visible = false,
                Padding = new Padding(8)
            };

            var detailsTop = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 70,
                Padding = new Padding(4)
            };

            _btnBack = new SimpleButton
            {
                Text = "< Назад к входящим",
                Width = 160,
                Dock = DockStyle.Left
            };
            _btnBack.Click += (s, e) =>
            {
                SetMode(ModeIncoming);
                RefreshIncomingDocumentsAsync();
            };

            _detailsHeader = new LabelControl
            {
                Dock = DockStyle.Fill,
                AutoSizeMode = LabelAutoSizeMode.Vertical,
                Appearance = { Font = new Font("Segoe UI", 9, FontStyle.Regular) }
            };

            detailsTop.Controls.Add(_detailsHeader);
            detailsTop.Controls.Add(_btnBack);

            var actionsPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(4)
            };

            _btnSignAndUpload = new SimpleButton { Text = "Подписать и выгрузить в iiko", Width = 190 };
            _btnUploadOnly = new SimpleButton { Text = "Выгрузить в iiko", Width = 150 };
            _btnReject = new SimpleButton { Text = "Отказать", Width = 100 };

            var lblStore = new LabelControl { Text = "Склад iiko:", AutoSizeMode = LabelAutoSizeMode.None, Width = 70 };
            _storeCombo = new ComboBoxEdit { Width = 220 };

            var flowActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            flowActions.Controls.Add(_btnSignAndUpload);
            flowActions.Controls.Add(_btnUploadOnly);
            flowActions.Controls.Add(_btnReject);
            flowActions.Controls.Add(lblStore);
            flowActions.Controls.Add(_storeCombo);
            actionsPanel.Controls.Add(flowActions);

            _detailsGrid = new GridControl { Dock = DockStyle.Fill };
            _detailsView = new GridView(_detailsGrid);
            _detailsGrid.MainView = _detailsView;
            _detailsView.OptionsBehavior.Editable = false;
            _detailsView.OptionsView.ShowGroupPanel = false;

            _detailsPanel.Controls.Add(_detailsGrid);
            _detailsPanel.Controls.Add(actionsPanel);
            _detailsPanel.Controls.Add(detailsTop);

            Controls.Add(_detailsPanel);
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
            _detailsPanel.Visible = (mode == ModeIncomingDetails);
            _grid.Visible = (mode != ModeIncomingDetails);

            if (mode == ModeIncoming)
            {
                _grid.DataSource = new List<DiadocDocumentRow>();
                ApplyDocumentsColumns();
            }
            else if (mode != ModeIncomingDetails)
            {
                RefreshCurrentViewAsync();
            }
        }

        private void OnSelectionChanged()
        {
            _counteragents = null;
            _suppliers = null;
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
                _iikoClient = new IikoRestoClient();
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

        private async void GridView_DoubleClick(object sender, EventArgs e)
        {
            if (_currentMode != ModeIncoming)
                return;

            var view = sender as GridView;
            if (view == null) return;
            var hit = view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition));
            if (!hit.InRow || hit.RowHandle < 0)
                return;

            var row = view.GetRow(hit.RowHandle) as DiadocDocumentRow;
            if (row == null)
                return;

            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) || string.IsNullOrEmpty(row.MessageId) || string.IsNullOrEmpty(row.EntityId))
                return;

            try
            {
                var items = await _client.GetUtdItemsAsync(boxId, row.MessageId, row.EntityId).ConfigureAwait(true);

                _detailsHeader.Text =
                    $"Поставщик: {row.CounterpartyName} (ИНН {row.CounterpartyInn}){Environment.NewLine}" +
                    $"Документ: {row.DocumentNumber} от {row.DocumentDate}";

                _detailsGrid.DataSource = items ?? Array.Empty<UtdItemRow>();
                _detailsView.PopulateColumns();

                if (_detailsView.Columns["Product"] != null) _detailsView.Columns["Product"].Visible = false;
                if (_detailsView.Columns["Gtin"] != null) _detailsView.Columns["Gtin"].Visible = false;
                if (_detailsView.Columns["ItemAdditionalInfo"] != null) _detailsView.Columns["ItemAdditionalInfo"].Visible = false;
                if (_detailsView.Columns["Unit"] != null) _detailsView.Columns["Unit"].Visible = false;

                int col = 0;
                if (_detailsView.Columns["LineIndex"] != null) { _detailsView.Columns["LineIndex"].Caption = "№"; _detailsView.Columns["LineIndex"].VisibleIndex = col++; }
                if (_detailsView.Columns["UnitName"] != null) { _detailsView.Columns["UnitName"].Caption = "Ед."; _detailsView.Columns["UnitName"].VisibleIndex = col++; }
                if (_detailsView.Columns["Quantity"] != null) { _detailsView.Columns["Quantity"].Caption = "Кол-во"; _detailsView.Columns["Quantity"].VisibleIndex = col++; }
                if (_detailsView.Columns["Price"] != null) { _detailsView.Columns["Price"].Caption = "Цена"; _detailsView.Columns["Price"].VisibleIndex = col++; }
                if (_detailsView.Columns["Subtotal"] != null) { _detailsView.Columns["Subtotal"].Caption = "Сумма"; _detailsView.Columns["Subtotal"].VisibleIndex = col++; }
                if (_detailsView.Columns["Vat"] != null) { _detailsView.Columns["Vat"].Caption = "Сумма НДС"; _detailsView.Columns["Vat"].VisibleIndex = col++; }
                if (_detailsView.Columns["ItemVendorCode"] != null) { _detailsView.Columns["ItemVendorCode"].Caption = "Код поставщика"; _detailsView.Columns["ItemVendorCode"].VisibleIndex = col++; }
                if (_detailsView.Columns["ItemArticle"] != null) { _detailsView.Columns["ItemArticle"].Caption = "Артикул"; _detailsView.Columns["ItemArticle"].VisibleIndex = col++; }

                SetMode(ModeIncomingDetails);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Детали накладной", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task EnsureCounteragentsLoadedAsync(string boxId)
        {
            if (_counteragents != null) return;
            _counteragents = await Task.Run(async () =>
                await _client.GetCounteragentsAsync(boxId).ConfigureAwait(false)
            ).ConfigureAwait(true) ?? new List<CounteragentRow>();
        }

        private async Task EnsureSuppliersLoadedAsync()
        {
            if (_suppliers != null || _iikoClient == null)
                return;

            try
            {
                _suppliers = await Task.Run(async () =>
                    await _iikoClient.GetSuppliersAsync().ConfigureAwait(false)
                ).ConfigureAwait(true) ?? new List<IikoSupplier>();
            }
            catch
            {
                // Если по какой-то причине не удалось получить поставщиков (нет ключа, нет доступа и т.п.),
                // просто работаем без отметки «Поставщик», без всплывающих ошибок.
                _suppliers = new List<IikoSupplier>();
            }
        }

        private async Task MarkSuppliersAsync(List<DiadocDocumentRow> docs)
        {
            if (docs == null || docs.Count == 0)
                return;

            await EnsureSuppliersLoadedAsync().ConfigureAwait(true);
            if (_suppliers == null || _suppliers.Count == 0)
            {
                IikoLog.Write("MarkSuppliersAsync: no suppliers loaded");
                return;
            }

            IikoLog.Write("MarkSuppliersAsync: docs=" + docs.Count + " suppliers=" + _suppliers.Count);

            var innToSupplier = _suppliers
                .Where(s => !string.IsNullOrWhiteSpace(s.Inn))
                .GroupBy(s => s.Inn.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int matched = 0;
            foreach (var d in docs)
            {
                var inn = d.CounterpartyInn;
                if (string.IsNullOrWhiteSpace(inn))
                    continue;

                if (innToSupplier.TryGetValue(inn.Trim(), out var sup))
                {
                    d.SupplierFound = true;
                    if (string.IsNullOrWhiteSpace(d.Supplier))
                        d.Supplier = sup.Name ?? "";
                    matched++;
                }
            }

            IikoLog.Write("MarkSuppliersAsync: matched=" + matched);
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
                ).ConfigureAwait(true) ?? new List<DiadocDocumentRow>();
                await MarkSuppliersAsync(list).ConfigureAwait(true);
                _grid.DataSource = list;
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
                    ).ConfigureAwait(true) ?? new List<DiadocDocumentRow>();
                    await MarkSuppliersAsync(list).ConfigureAwait(true);
                    _grid.DataSource = list;
                    ApplyDocumentsColumns();
                }
                else if (_currentMode == ModeDrafts)
                {
                    var list = await Task.Run(async () =>
                        await _client.GetDocumentsAsync(boxId, false).ConfigureAwait(false)
                    ).ConfigureAwait(true) ?? new List<DiadocDocumentRow>();
                    await MarkSuppliersAsync(list).ConfigureAwait(true);
                    _grid.DataSource = list;
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
