using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Globalization;
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
        private PanelControl _buttonsPanel;
        private PanelControl _backPanel;
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
        private SimpleButton _btnRefreshMappings;
        private CheckEdit _chkCreateWithPosting;
        private ComboBoxEdit _storeCombo;
        private GridControl _detailsGrid;
        private GridView _detailsView;

        private DiadocApiClient _client;
        private IikoRestoClient _iikoClient;
        private List<DiadocOrg> _orgs = new List<DiadocOrg>();
        private List<CounteragentRow> _counteragents;
        private List<IikoSupplier> _suppliers;
        private List<IikoStore> _stores;
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

            _buttonsPanel = new PanelControl
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
            _buttonsPanel.Controls.Add(flowBtns);
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
            // Включаем панель поиска (лупа в правом верхнем углу грида).
            _gridView.OptionsFind.AlwaysVisible = true;
            _gridView.OptionsFind.ShowCloseButton = true;
            _gridView.OptionsFind.FindNullPrompt = "Введите текст для поиска (номер УПД, сумма и т.д.)";
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
                Text = "← Назад к входящим",
                Width = 120,
                Height = 20,
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
                Appearance =
                {
                    Font = new Font("Segoe UI", 9, FontStyle.Regular),
                    TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center }
                },
                Padding = new Padding(16, 0, 0, 0)
            };

            detailsTop.Controls.Add(_detailsHeader);

            var actionsPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(4)
            };

            _btnSignAndUpload = new SimpleButton { Text = "Подписать и выгрузить в iiko", Width = 190 };
            _btnReject = new SimpleButton { Text = "Отказать", Width = 100 };
            _btnUploadOnly = new SimpleButton { Text = "Выгрузить в iiko", Width = 150 };
            _btnRefreshMappings = new SimpleButton { Text = "Обновить прайс-лист", Width = 150 };

            _btnUploadOnly.Click += async (s, e) => await UploadToIikoAsync(false);
            _btnSignAndUpload.Click += async (s, e) =>
            {
                var cfg = ConfigStore.Load();
                if (cfg != null && cfg.ConfirmSignOrReject)
                {
                    var r = XtraMessageBox.Show("Подписать и выгрузить накладную в iiko?", "Подтверждение",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes)
                        return;
                }
                await UploadToIikoAsync(true);
            };
            _btnReject.Click += (s, e) =>
            {
                var cfg = ConfigStore.Load();
                if (cfg != null && cfg.ConfirmSignOrReject)
                {
                    var r = XtraMessageBox.Show("Отказать контрагенту в подписи документа?", "Подтверждение",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes)
                        return;
                }

                XtraMessageBox.Show("Отказ в подписи будет реализован в следующей версии.", "ЭДО ↔ iiko",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _btnRefreshMappings.Click += async (s, e) => await RefreshMappingsAsync();

            var lblStore = new LabelControl { Text = "Склад iiko:", AutoSizeMode = LabelAutoSizeMode.None, Width = 70 };
            _storeCombo = new ComboBoxEdit { Width = 220 };

            // Галочка «С проведением» (отображается внизу справа страницы документов). По умолчанию берётся из настроек.
            var cfgForCheckbox = ConfigStore.Load();
            _chkCreateWithPosting = new CheckEdit
            {
                Text = "С проведением",
                Checked = cfgForCheckbox != null && cfgForCheckbox.CreateInvoiceWithPosting,
                AutoSizeInLayoutControl = false,
                Size = new Size(130, 22),
                Margin = new Padding(0, 4, 0, 0)
            };
            _chkCreateWithPosting.CheckedChanged += (s, e) =>
            {
                var cfg = ConfigStore.Load() ?? new RoomBroomConfig();
                if (cfg.CreateInvoiceWithPosting != _chkCreateWithPosting.Checked)
                {
                    cfg.CreateInvoiceWithPosting = _chkCreateWithPosting.Checked;
                    ConfigStore.Save(cfg);
                }
            };

            var flowActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            flowActions.Controls.Add(_btnSignAndUpload);
            flowActions.Controls.Add(_btnReject);
            flowActions.Controls.Add(_btnUploadOnly);
            flowActions.Controls.Add(lblStore);
            flowActions.Controls.Add(_storeCombo);
            flowActions.Controls.Add(_btnRefreshMappings);
            actionsPanel.Controls.Add(flowActions);

            _detailsGrid = new GridControl { Dock = DockStyle.Fill };
            _detailsView = new GridView(_detailsGrid);
            _detailsGrid.MainView = _detailsView;
            _detailsView.OptionsBehavior.Editable = false;
            _detailsView.OptionsView.ShowGroupPanel = false;

            // Нижняя панель с галочкой «С проведением» — прижата к правому нижнему углу.
            var bottomPanel = new PanelControl
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Padding = new Padding(4, 4, 40, 4)
            };
            var bottomFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            bottomFlow.Controls.Add(_chkCreateWithPosting);
            bottomPanel.Controls.Add(bottomFlow);

            _detailsPanel.Controls.Add(_detailsGrid);
            _detailsPanel.Controls.Add(bottomPanel);
            _detailsPanel.Controls.Add(actionsPanel);
            _detailsPanel.Controls.Add(detailsTop);

            // Отдельная панель под кнопку «Назад к входящим»
            _backPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(8, 4, 8, 0),
                Visible = false,
                BorderStyle = BorderStyles.NoBorder
            };
            _backPanel.Controls.Add(_btnBack);

            Controls.Add(_detailsPanel);
            Controls.Add(_grid);
            Controls.Add(_backPanel);
            Controls.Add(_filterPanel);
            Controls.Add(_buttonsPanel);
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

            if (_buttonsPanel != null)
                _buttonsPanel.Visible = (mode != ModeIncomingDetails);
            if (_backPanel != null)
                _backPanel.Visible = (mode == ModeIncomingDetails);

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
            _stores = null;
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
            if (cols["DocumentNumber"] != null)
            {
                cols["DocumentNumber"].Caption = "Номер";
                cols["DocumentNumber"].VisibleIndex = 2;
            }
            if (cols["DocumentDate"] != null)
            {
                cols["DocumentDate"].Caption = "От";
                cols["DocumentDate"].VisibleIndex = 3;
                cols["DocumentDate"].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
                cols["DocumentDate"].Width = 80;
            }
            if (cols["SentToEdo"] != null)
            {
                cols["SentToEdo"].Caption = "Отправлен в ЭДО";
                cols["SentToEdo"].VisibleIndex = 4;
                cols["SentToEdo"].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            }
            if (cols["TotalVat"] != null)
            {
                cols["TotalVat"].Caption = "Сумма НДС";
                cols["TotalVat"].VisibleIndex = 5;
                cols["TotalVat"].Width = 100;
            }
            if (cols["TotalAmount"] != null)
            {
                cols["TotalAmount"].Caption = "Сумма";
                cols["TotalAmount"].VisibleIndex = 6;
                cols["TotalAmount"].Width = 110;
            }
            if (cols["StatusText"] != null) { cols["StatusText"].Caption = "Статус ЭДО"; cols["StatusText"].VisibleIndex = 7; }
            if (cols["SupplierFound"] != null)
            {
                cols["SupplierFound"].Caption = "Поставщик";
                cols["SupplierFound"].VisibleIndex = 8;
                cols["SupplierFound"].Width = 80;
            }
            if (cols["Supplier"] != null) cols["Supplier"].Visible = false;
            // Столбик «Накладная iiko» больше не показываем отдельно — номер включён в статус.
            if (cols["IikoInvoice"] != null) cols["IikoInvoice"].Visible = false;
            if (cols["IikoStatus"] != null)
            {
                cols["IikoStatus"].Caption = "Статус накладной";
                cols["IikoStatus"].VisibleIndex = 9;
                cols["IikoStatus"].Width = 220;
            }
            if (cols["IikoSupplierId"] != null) cols["IikoSupplierId"].Visible = false;

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

        private void DetailsView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.RowHandle < 0) return;
            if (e.Column.FieldName != "IikoProductName")
                return;

            var view = sender as GridView;
            var row = view?.GetRow(e.RowHandle) as UtdItemRow;
            if (row == null)
                return;

            if (string.IsNullOrWhiteSpace(row.Product))
            {
                // Нет привязки к товару iiko → подсветить как на примере из iiko (красная ячейка "Выберите").
                e.Appearance.BackColor = Color.FromArgb(255, 200, 200);
                e.Appearance.ForeColor = Color.DarkRed;
            }
        }

        private DiadocDocumentRow _currentDocument;
        private UtdItemRow[] _currentItems;
        private bool _currentAllItemsMapped;

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

                var supplierName = row.CounterpartyName ?? "";
                var supplierInn = row.CounterpartyInn ?? "";
                var docNumber = row.DocumentNumber ?? "";
                var docDate = row.DocumentDate ?? "";
                _detailsHeader.Text =
                    $"Наименование: {supplierName}{Environment.NewLine}" +
                    $"ИНН: {supplierInn}{Environment.NewLine}" +
                    $"Номер и дата документа: {docNumber} от {docDate}";

                _currentDocument = row;
                _currentItems = items ?? Array.Empty<UtdItemRow>();

                // Единицы измерения поставщика делаем строчными
                foreach (var it in _currentItems)
                {
                    if (!string.IsNullOrWhiteSpace(it.UnitName))
                        it.UnitName = it.UnitName.ToLowerInvariant();
                }

                // Подгружаем прайс-лист поставщика и пытаемся заранее проставить привязки «товар поставщика → наш товар».
                await EnsureMappingsForCurrentDocumentAsync().ConfigureAwait(true);

                _detailsGrid.DataSource = _currentItems;
                _detailsView.PopulateColumns();

                if (_detailsView.Columns["Product"] != null) _detailsView.Columns["Product"].Visible = false;
                if (_detailsView.Columns["Gtin"] != null) _detailsView.Columns["Gtin"].Visible = false;
                if (_detailsView.Columns["ItemAdditionalInfo"] != null) _detailsView.Columns["ItemAdditionalInfo"].Visible = false;
                // Артикул дублирует «Код поставщика» — не показываем при провале в накладную
                if (_detailsView.Columns["ItemArticle"] != null) _detailsView.Columns["ItemArticle"].Visible = false;

                int col = 0;
                if (_detailsView.Columns["LineIndex"] != null)
                {
                    _detailsView.Columns["LineIndex"].Caption = "№";
                    _detailsView.Columns["LineIndex"].VisibleIndex = col++;
                    _detailsView.Columns["LineIndex"].Width = 40;
                }
                if (_detailsView.Columns["ItemVendorCode"] != null) { _detailsView.Columns["ItemVendorCode"].Caption = "Код поставщика"; _detailsView.Columns["ItemVendorCode"].VisibleIndex = col++; }
                if (_detailsView.Columns["SupplierProductName"] != null) { _detailsView.Columns["SupplierProductName"].Caption = "Наименование у поставщика"; _detailsView.Columns["SupplierProductName"].VisibleIndex = col++; }
                if (_detailsView.Columns["UnitName"] != null)
                {
                    _detailsView.Columns["UnitName"].Caption = "Ед. изм. у поставщика";
                    _detailsView.Columns["UnitName"].VisibleIndex = col++;
                }
                if (_detailsView.Columns["IikoProductName"] != null) { _detailsView.Columns["IikoProductName"].Caption = "Наименование товара у нас"; _detailsView.Columns["IikoProductName"].VisibleIndex = col++; }
                if (_detailsView.Columns["Unit"] != null) { _detailsView.Columns["Unit"].Caption = "В таре"; _detailsView.Columns["Unit"].VisibleIndex = col++; }
                if (_detailsView.Columns["Quantity"] != null) { _detailsView.Columns["Quantity"].Caption = "Кол-во"; _detailsView.Columns["Quantity"].VisibleIndex = col++; }
                if (_detailsView.Columns["Price"] != null) { _detailsView.Columns["Price"].Caption = "Цена"; _detailsView.Columns["Price"].VisibleIndex = col++; }
                if (_detailsView.Columns["Subtotal"] != null) { _detailsView.Columns["Subtotal"].Caption = "Сумма"; _detailsView.Columns["Subtotal"].VisibleIndex = col++; }
                if (_detailsView.Columns["Vat"] != null) { _detailsView.Columns["Vat"].Caption = "Сумма НДС"; _detailsView.Columns["Vat"].VisibleIndex = col++; }

                // Подсветка незамапленных строк и блокировка кнопок выгрузки.
                _detailsView.RowCellStyle -= DetailsView_RowCellStyle;
                _detailsView.RowCellStyle += DetailsView_RowCellStyle;
                UpdateUploadButtonsEnabled();

                await EnsureStoresLoadedAsync().ConfigureAwait(true);
                PopulateStoresCombo();

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

        private async Task EnsureStoresLoadedAsync()
        {
            if (_stores != null || _iikoClient == null)
                return;

            try
            {
                _stores = await Task.Run(async () =>
                    await _iikoClient.GetStoresAsync().ConfigureAwait(false)
                ).ConfigureAwait(true) ?? new List<IikoStore>();
            }
            catch
            {
                _stores = new List<IikoStore>();
            }
        }

        /// <summary>
        /// Загружает прайс-лист текущего поставщика и заполняет Product/IikoProductName у строк текущей накладной.
        /// </summary>
        private async Task EnsureMappingsForCurrentDocumentAsync()
        {
            _currentAllItemsMapped = false;
            if (_currentDocument == null || _currentItems == null || _currentItems.Length == 0 || _iikoClient == null)
                return;
            if (string.IsNullOrWhiteSpace(_currentDocument.IikoSupplierId))
                return;

            var supplierPricelistKey = _currentDocument.IikoSupplierId;
            var sup = _suppliers?.FirstOrDefault(s => s.Id == _currentDocument.IikoSupplierId);
            if (sup != null && !string.IsNullOrWhiteSpace(sup.Code))
                supplierPricelistKey = sup.Code;

            var pricelist = await _iikoClient.GetSupplierPricelistAsync(supplierPricelistKey).ConfigureAwait(true);
            if (pricelist == null || pricelist.Count == 0)
                return;

            var codeToProduct = new Dictionary<string, SupplierPricelistItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in pricelist)
            {
                if (string.IsNullOrWhiteSpace(row.NativeProduct))
                    continue;
                if (!string.IsNullOrWhiteSpace(row.SupplierProductNum))
                    codeToProduct[row.SupplierProductNum.Trim()] = row;
                if (!string.IsNullOrWhiteSpace(row.SupplierProductCode))
                    codeToProduct[row.SupplierProductCode.Trim()] = row;
            }

            foreach (var it in _currentItems)
            {
                // Сбрасываем предыдущую привязку, чтобы не тянуть старые данные между накладными.
                it.Product = null;
                it.IikoProductName = null;
                it.Unit = null;
                var code = (it.ItemVendorCode ?? "").Trim();
                SupplierPricelistItem mapped = null;
                if (!string.IsNullOrEmpty(code) && codeToProduct.TryGetValue(code, out mapped))
                {
                    it.Product = mapped.NativeProduct;
                    it.IikoProductName = mapped.NativeProductName;
                    if (!string.IsNullOrWhiteSpace(mapped.ContainerName))
                        it.Unit = mapped.ContainerName;
                    continue;
                }

                var article = (it.ItemArticle ?? "").Trim();
                if (!string.IsNullOrEmpty(article) && codeToProduct.TryGetValue(article, out mapped))
                {
                    it.Product = mapped.NativeProduct;
                    it.IikoProductName = mapped.NativeProductName;
                    if (!string.IsNullOrWhiteSpace(mapped.ContainerName))
                        it.Unit = mapped.ContainerName;
                }
            }

            // Для незамапленных строк отображаем плейсхолдер "[выберите]".
            foreach (var it in _currentItems)
            {
                if (string.IsNullOrWhiteSpace(it.Product))
                    it.IikoProductName = "Отсутствует в прайс-листе";
            }

            _currentAllItemsMapped = _currentItems.All(it => !string.IsNullOrWhiteSpace(it.Product));
        }

        private void UpdateUploadButtonsEnabled()
        {
            var enabled = _currentAllItemsMapped && _currentItems != null && _currentItems.Length > 0;
            _btnUploadOnly.Enabled = enabled;
            _btnSignAndUpload.Enabled = enabled;
        }

        /// <summary>
        /// Кнопка «Обновить прайс-лист» — повторно тянет прайс поставщика и обновляет маппинг/подсветку.
        /// Удобно, когда пользователь только что добавил привязки в iiko и хочет обновить текущую накладную.
        /// </summary>
        private async Task RefreshMappingsAsync()
        {
            await EnsureSuppliersLoadedAsync().ConfigureAwait(true);
            await EnsureMappingsForCurrentDocumentAsync().ConfigureAwait(true);
            _detailsGrid.RefreshDataSource();
            _detailsView.RefreshData();
            UpdateUploadButtonsEnabled();
        }

        private void PopulateStoresCombo()
        {
            _storeCombo.Properties.Items.Clear();
            if (_stores == null || _stores.Count == 0)
            {
                _storeCombo.Properties.Items.Add("— нет данных по складам iiko —");
                _storeCombo.SelectedIndex = 0;
                return;
            }

            foreach (var s in _stores)
            {
                var name = string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name;
                _storeCombo.Properties.Items.Add(name);
            }
            if (_stores.Count > 0)
                _storeCombo.SelectedIndex = 0;
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
                {
                    // Нет ИНН → не можем подобрать поставщика в iiko → информация о поставщике не заполнена.
                    if (string.IsNullOrWhiteSpace(d.IikoStatus))
                        d.IikoStatus = "Информация о поставщике не заполнена";
                    continue;
                }

                if (innToSupplier.TryGetValue(inn.Trim(), out var sup))
                {
                    d.SupplierFound = true;
                    d.IikoSupplierId = sup.Id;
                    if (string.IsNullOrWhiteSpace(d.Supplier))
                        d.Supplier = sup.Name ?? "";
                    matched++;
                }
                else
                {
                    // Поставщик по ИНН в iiko не найден → информация о поставщике не заполнена.
                    if (string.IsNullOrWhiteSpace(d.IikoStatus))
                        d.IikoStatus = "Информация о поставщике не заполнена";
                }
            }

            IikoLog.Write("MarkSuppliersAsync: matched=" + matched);
        }

        private static string NormalizeDateForIikoDocument(string date)
        {
            // Для dateIncoming/dueDate iiko на практике ожидает формат dd.MM.yyyy.
            if (string.IsNullOrWhiteSpace(date))
                return "";
            if (DateTime.TryParse(date, out var dt))
                return dt.ToString("dd.MM.yyyy");
            return date;
        }

        /// <param name="supplierCodeToNativeProductGuid">Маппинг «код/артикул поставщика» → guid нашего товара в iiko (из прайс-листа поставщика).</param>
        /// <param name="createWithPosting">Если true, в XML добавляется проведение документа (conducted).</param>
        private string BuildIncomingInvoiceXml(DiadocDocumentRow doc, UtdItemRow[] items, string supplierId, string storeId,
            Dictionary<string, string> supplierCodeToNativeProductGuid = null, bool createWithPosting = false)
        {
            var xDoc = new XDocument();
            var root = new XElement("document");
            xDoc.Add(root);

            // Проведение делаем отдельным вызовом ProcessIncomingInvoiceAsync после импорта (см. UploadToIikoAsync).

            var itemsEl = new XElement("items");
            root.Add(itemsEl);

            foreach (var it in items ?? Array.Empty<UtdItemRow>())
            {
                var itemEl = new XElement("item");
                itemsEl.Add(itemEl);

                itemEl.Add(new XElement("amount", it.Quantity.ToString(CultureInfo.InvariantCulture)));

                string nativeProductGuid = null;
                if (supplierCodeToNativeProductGuid != null)
                {
                    var code = (it.ItemVendorCode ?? "").Trim();
                    if (!string.IsNullOrEmpty(code) && supplierCodeToNativeProductGuid.TryGetValue(code, out var guid))
                        nativeProductGuid = guid;
                    if (string.IsNullOrEmpty(nativeProductGuid))
                    {
                        code = (it.ItemArticle ?? "").Trim();
                        if (!string.IsNullOrEmpty(code) && supplierCodeToNativeProductGuid.TryGetValue(code, out var g))
                            nativeProductGuid = g;
                    }
                }
                if (!string.IsNullOrWhiteSpace(nativeProductGuid))
                    itemEl.Add(new XElement("product", nativeProductGuid));
                if (!string.IsNullOrWhiteSpace(it.ItemArticle))
                    itemEl.Add(new XElement("productArticle", it.ItemArticle));
                if (!string.IsNullOrWhiteSpace(it.ItemVendorCode))
                    itemEl.Add(new XElement("supplierProductArticle", it.ItemVendorCode));

                itemEl.Add(new XElement("num", it.LineIndex));

                if (!string.IsNullOrWhiteSpace(storeId))
                    itemEl.Add(new XElement("store", storeId));

                itemEl.Add(new XElement("price", it.Price.ToString(CultureInfo.InvariantCulture)));
                itemEl.Add(new XElement("sum", it.Subtotal.ToString(CultureInfo.InvariantCulture)));
                itemEl.Add(new XElement("actualAmount", it.Quantity.ToString(CultureInfo.InvariantCulture)));
            }

            var dateIncoming = NormalizeDateForIikoDocument(doc.DocumentDate);

            if (!string.IsNullOrWhiteSpace(dateIncoming))
                root.Add(new XElement("dateIncoming", dateIncoming));
            if (!string.IsNullOrWhiteSpace(doc.DocumentNumber))
                root.Add(new XElement("incomingDocumentNumber", doc.DocumentNumber));
            // Если в настройках включено «создавать с проведением» — сразу помечаем документ как PROCESSED.
            if (createWithPosting)
                root.Add(new XElement("status", "PROCESSED"));
            if (!string.IsNullOrWhiteSpace(storeId))
                root.Add(new XElement("defaultStore", storeId));
            if (!string.IsNullOrWhiteSpace(supplierId))
                root.Add(new XElement("supplier", supplierId));
            // incomingDate оставляем пустым, чтобы iiko подставил его из dateIncoming.
            root.Add(new XElement("useDefaultDocumentTime", "true"));

            return xDoc.ToString(SaveOptions.DisableFormatting);
        }

        private async Task UploadToIikoAsync(bool withSign)
        {
            if (_currentMode != ModeIncomingDetails || _client == null || _iikoClient == null)
                return;

            if (_currentDocument == null || _currentItems == null || _currentItems.Length == 0)
            {
                XtraMessageBox.Show("Нет выбранной накладной или её строк.", "Выгрузка в iiko",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_currentItems.Any(it => string.IsNullOrWhiteSpace(it.Product)))
            {
                XtraMessageBox.Show(
                    "Не все строки накладной сопоставлены с товарами iiko.\r\n" +
                    "Проверьте колонку «Наименование товара у нас» — строки с текстом «Выберите» нужно привязать через прайс-лист поставщика в iiko.",
                    "Выгрузка в iiko",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentDocument.IikoSupplierId))
            {
                XtraMessageBox.Show("Не найден поставщик в iiko по ИНН. Проверьте сопоставление поставщиков.",
                    "Выгрузка в iiko", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string storeId = null;
            if (_stores != null && _stores.Count > 0 &&
                _storeCombo.SelectedIndex >= 0 && _storeCombo.SelectedIndex < _stores.Count)
            {
                storeId = _stores[_storeCombo.SelectedIndex].Id;
            }
            else
            {
                // Фолбэк: позволяем ввести GUID склада вручную.
                storeId = _storeCombo.Text?.Trim();
            }

            if (string.IsNullOrWhiteSpace(storeId))
            {
                XtraMessageBox.Show(
                    "Не выбран склад iiko. Заполните прайс-лист поставщика в iiko и обновите список складов/подразделений, затем выберите склад в выпадающем списке или введите GUID склада вручную.",
                    "Выгрузка в iiko",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var supplierPricelistKey = _currentDocument.IikoSupplierId;
                if (!string.IsNullOrWhiteSpace(supplierPricelistKey))
                {
                    var sup = _suppliers?.FirstOrDefault(s => s.Id == _currentDocument.IikoSupplierId);
                    if (sup != null && !string.IsNullOrWhiteSpace(sup.Code))
                        supplierPricelistKey = sup.Code;
                }
                IikoRestoClient.WriteImportDebugLog("storeId=" + (storeId ?? "<null>") + " supplierId=" + (_currentDocument.IikoSupplierId ?? "<null>"));

                var pricelist = await _iikoClient.GetSupplierPricelistAsync(supplierPricelistKey).ConfigureAwait(true);
                var supplierCodeToNativeGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (pricelist != null)
                {
                    foreach (var row in pricelist)
                    {
                        if (!string.IsNullOrWhiteSpace(row.NativeProduct))
                        {
                            if (!string.IsNullOrWhiteSpace(row.SupplierProductNum))
                                supplierCodeToNativeGuid[row.SupplierProductNum.Trim()] = row.NativeProduct;
                            if (!string.IsNullOrWhiteSpace(row.SupplierProductCode))
                                supplierCodeToNativeGuid[row.SupplierProductCode.Trim()] = row.NativeProduct;
                        }
                    }
                }

                // Используем галочку на форме как источник правды; она синхронизирует значение в ConfigStore.
                var createWithPosting = _chkCreateWithPosting != null && _chkCreateWithPosting.Checked;
                var xml = BuildIncomingInvoiceXml(_currentDocument, _currentItems, _currentDocument.IikoSupplierId, storeId, supplierCodeToNativeGuid, createWithPosting);
                var result = await _iikoClient.ImportIncomingInvoiceAsync(xml).ConfigureAwait(true);

                var valid = result.Valid == true;
                var iikoNumber = result.DocumentNumber ?? _currentDocument.DocumentNumber;
                var msg = valid
                    ? (createWithPosting
                        ? $"Накладная № {iikoNumber} успешно сохранена с проведением."
                        : $"Накладная № {iikoNumber} успешно сохранена без проведения.")
                    : "Ответ iiko получен, но документ не прошёл валидацию. Проверьте детали в журнале.";

                if (!string.IsNullOrEmpty(result.RawXml))
                    IikoLog.Write("ImportIncomingInvoiceAsync result: " + result.RawXml.Replace(Environment.NewLine, " "));

                if (valid)
                {
                    _currentDocument.IikoInvoice = iikoNumber;
                    _currentDocument.IikoStatus = createWithPosting
                        ? $"Внесено в iiko с проведением (№ {iikoNumber})"
                        : $"Внесено в iiko без проведения (№ {iikoNumber})";
                    RefreshCurrentViewAsync();
                }

                XtraMessageBox.Show(msg, "Выгрузка в iiko", MessageBoxButtons.OK,
                    valid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                if (withSign && valid)
                {
                    XtraMessageBox.Show("Подпись и квитанция Диадока будут реализованы в следующей версии.",
                        "Подписать и выгрузить", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (IikoImportException ie)
            {
                string msg;
                var body = ie.ResponseBody ?? "";
                if (ie.StatusCode == 409 && !string.IsNullOrEmpty(body))
                {
                    if (body.IndexOf("Product is not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        msg = "Товар из накладной не найден в номенклатуре iiko.\r\n" +
                              "Добавьте товар в iiko с нужным артикулом или сопоставьте «Код поставщика» из УПД с артикулом существующего товара.\r\n\r\n" +
                              "Ответ iiko: " + body;
                    }
                    else if (body.IndexOf("Native product for supplier product", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        msg = "В прайс-листе поставщика в iiko не найдено сопоставление «товар поставщика → наш товар» для одной из строк.\r\n" +
                              "Откройте в iiko прайс-лист поставщика и заполните колонку «Наш товар» для этой позиции (или удалите лишнюю строку), затем повторите выгрузку.\r\n\r\n" +
                              "Ответ iiko: " + body;
                    }
                    else
                    {
                        msg = "Ошибка iiko (HTTP " + ie.StatusCode + "):\r\n" + body;
                    }
                }
                else
                {
                    msg = "Ошибка iiko (HTTP " + ie.StatusCode + "):\r\n" + (body != "" ? body : ie.Message);
                }
                XtraMessageBox.Show(msg, "Выгрузка в iiko", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Выгрузка в iiko", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

                // Подтягиваем из iiko уже внесённые приходы по входящему номеру (incomingDocumentNumber)
                // и выставляем корректный статус / номер накладной.
                if (_iikoClient != null && list.Count > 0)
                {
                    var docsForExport = list
                        .Where(d => d.SupplierFound && !string.IsNullOrWhiteSpace(d.DocumentNumber))
                        .ToList();
                    var dateCandidates = docsForExport
                        .Select(d =>
                        {
                            if (DateTime.TryParse(d.DocumentDate, out var dt))
                                return (DateTime?)dt.Date;
                            return null;
                        })
                        .Where(dt => dt.HasValue)
                        .Select(dt => dt.Value)
                        .ToList();

                    if (dateCandidates.Count > 0)
                    {
                        var minDate = dateCandidates.Min();
                        var maxDate = dateCandidates.Max();

                        var incomingInvoices = await _iikoClient
                            .GetIncomingInvoicesAsync(minDate, maxDate)
                            .ConfigureAwait(true) ?? new List<IncomingInvoiceInfo>();

                        var byIncomingNumber = new Dictionary<string, IncomingInvoiceInfo>(StringComparer.OrdinalIgnoreCase);
                        foreach (var inv in incomingInvoices)
                        {
                            var incomingNum = (inv.IncomingDocumentNumber ?? "").Trim();
                            if (string.IsNullOrEmpty(incomingNum))
                                continue;
                            if (!byIncomingNumber.ContainsKey(incomingNum))
                                byIncomingNumber[incomingNum] = inv;
                        }

                        foreach (var d in list)
                        {
                            // Если поставщика нет в iiko — статус уже выставлен как "Не требует внесения".
                            if (!d.SupplierFound)
                                continue;

                            var key = (d.DocumentNumber ?? "").Trim();
                            if (!string.IsNullOrEmpty(key) && byIncomingNumber.TryGetValue(key, out var inv))
                            {
                                var num = (inv.DocumentNumber ?? "").Trim();
                                if (!string.IsNullOrEmpty(num))
                                    d.IikoInvoice = num;

                                var status = (inv.Status ?? "").Trim().ToUpperInvariant();
                                if (status == "PROCESSED")
                                {
                                    d.IikoStatus = string.IsNullOrEmpty(num)
                                        ? "Внесено в iiko с проведением"
                                        : $"Внесено в iiko с проведением (№ {num})";
                                }
                                else if (status == "NEW" || string.IsNullOrEmpty(status))
                                {
                                    d.IikoStatus = string.IsNullOrEmpty(num)
                                        ? "Внесено в iiko без проведения"
                                        : $"Внесено в iiko без проведения (№ {num})";
                                }
                                else if (status == "DELETED")
                                {
                                    d.IikoStatus = string.IsNullOrEmpty(num)
                                        ? "Накладная в iiko удалена"
                                        : $"Накладная в iiko удалена (№ {num})";
                                }

                                continue;
                            }

                            // Поставщик найден, но по входящему номеру прихода в iiko нет.
                            if (string.IsNullOrWhiteSpace(d.IikoStatus))
                                d.IikoStatus = "Не внесено в iiko";
                        }
                    }
                }

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
