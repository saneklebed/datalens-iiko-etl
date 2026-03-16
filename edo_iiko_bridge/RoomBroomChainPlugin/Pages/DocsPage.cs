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
        // Инкремент для фонового пересчёта статуса привязок (чтобы отменять старые прогоны).
        private int _mappingStatusRunId;
        private ComboBoxEdit _legalEntityCombo;
        private PanelControl _buttonsPanel;
        private PanelControl _backPanel;
        private SimpleButton _btnCounteragents;
        private SimpleButton _btnIncoming;
        private PanelControl _filterPanel;
        private DateEdit _dateFrom;
        private DateEdit _dateTo;
        private SimpleButton _btnFetchInvoices;
        private GridControl _grid;
        private GridView _gridView;
        private PanelControl _batchActionsPanel;
        private SimpleButton _btnBatchSignAndUpload;
        private SimpleButton _btnBatchUpload;
        private SimpleButton _btnBatchReject;
        private LabelControl _lblBatchSignIcon;
        private LabelControl _lblBatchUploadIcon;
        private LabelControl _lblBatchRejectIcon;
        private LabelControl _lblBatchHint;
        private ComboBoxEdit _batchStoreCombo;
        private LabelControl _lblBatchStore;
        private PanelControl _busyOverlay;
        private PanelControl _busyCard;
        private LabelControl _busyLabel;
        private MarqueeProgressBarControl _busyProgress;
        private int _busyOverlayDepth;

        private PanelControl _detailsPanel;
        private LabelControl _detailsHeader;
        private SimpleButton _btnBack;
        private SimpleButton _btnSignAndUpload;
        private SimpleButton _btnUploadOnly;
        private SimpleButton _btnReject;
        private LabelControl _lblSignIcon;
        private LabelControl _lblRejectIcon;
        private LabelControl _lblUploadIcon;
        private SimpleButton _btnRefreshMappings;
        private CheckEdit _chkCreateWithPosting;
        private LabelControl _lblInvoiceTotal;
        private LabelControl _lblInvoiceTotalVat;
        private ComboBoxEdit _storeCombo;
        private DateEdit _incomingDateEdit;
        private GridControl _detailsGrid;
        private GridView _detailsView;

        private DiadocApiClient _client;
        private IikoRestoClient _iikoClient;
        private List<DiadocOrg> _orgs = new List<DiadocOrg>();
        private List<CounteragentRow> _counteragents;
        private List<DiadocDocumentRow> _incomingDocuments;
        private List<IikoSupplier> _suppliers;
        private List<IikoStore> _stores;
        private const string ModeCounteragents = "Поставщики";
        private const string ModeIncoming = "Накладные";
        private const string ModeIncomingDetails = "Накладные_Детали";

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
            _btnCounteragents = new SimpleButton { Text = ModeCounteragents, Width = 120 };
            _btnIncoming = new SimpleButton { Text = ModeIncoming, Width = 120 };
            _btnCounteragents.Click += (s, e) => SetMode(ModeCounteragents);
            _btnIncoming.Click += (s, e) => SetMode(ModeIncoming);

            var flowBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            flowBtns.Controls.Add(_btnCounteragents);
            flowBtns.Controls.Add(_btnIncoming);
            _buttonsPanel.Controls.Add(flowBtns);
            _btnCounteragents.Location = new Point(8, 8);
            _btnIncoming.Location = new Point(136, 8);

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
            _gridView.OptionsBehavior.Editable = true;
            _gridView.OptionsView.ShowGroupPanel = false;
            // Включаем панель поиска (лупа в правом верхнем углу грида).
            _gridView.OptionsFind.AlwaysVisible = true;
            _gridView.OptionsFind.ShowCloseButton = true;
            _gridView.OptionsFind.FindNullPrompt = "Введите текст для поиска (номер УПД, сумма и т.д.)";
            _gridView.DoubleClick += GridView_DoubleClick;
            _gridView.CellValueChanged += GridView_CellValueChanged;

            // Панель массовых действий по выбранным накладным (появляется при выборе в списке «Входящие»)
            _batchActionsPanel = new PanelControl
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(8, 4, 8, 4),
                Visible = false,
                BorderStyle = BorderStyles.NoBorder
            };
            _btnBatchSignAndUpload = new SimpleButton
            {
                Text = "   Подписать и выгрузить",
                Width = 172,
                Height = 26
            };
            _btnBatchSignAndUpload.Appearance.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnBatchSignAndUpload.Appearance.ForeColor = Color.ForestGreen;
            _btnBatchSignAndUpload.AppearanceDisabled.BackColor = Color.LightGray;
            _btnBatchSignAndUpload.AppearanceDisabled.ForeColor = Color.Gray;
            _lblBatchSignIcon = new LabelControl
            {
                Text = "✓",
                Parent = _btnBatchSignAndUpload,
                Location = new Point(4, 3),
                Size = new Size(18, 20),
                MinimumSize = new Size(18, 20),
                MaximumSize = new Size(18, 20),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold),
                ForeColor = Color.ForestGreen,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblBatchSignIcon.Click += (s, e) => _btnBatchSignAndUpload.PerformClick();
            _lblBatchSignIcon.BringToFront();

            _btnBatchUpload = new SimpleButton
            {
                Text = "   Выгрузить",
                Width = 112,
                Height = 26
            };
            _btnBatchUpload.Appearance.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnBatchUpload.Appearance.ForeColor = Color.ForestGreen;
            _btnBatchUpload.AppearanceDisabled.BackColor = Color.LightGray;
            _btnBatchUpload.AppearanceDisabled.ForeColor = Color.Gray;
            _lblBatchUploadIcon = new LabelControl
            {
                Text = "✓",
                Parent = _btnBatchUpload,
                Location = new Point(4, 3),
                Size = new Size(18, 20),
                MinimumSize = new Size(18, 20),
                MaximumSize = new Size(18, 20),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold),
                ForeColor = Color.ForestGreen,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblBatchUploadIcon.Click += (s, e) => _btnBatchUpload.PerformClick();
            _lblBatchUploadIcon.BringToFront();

            _btnBatchReject = new SimpleButton
            {
                Text = "   Отказать",
                Width = 92,
                Height = 26
            };
            _btnBatchReject.Appearance.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnBatchReject.Appearance.ForeColor = Color.DarkRed;
            _btnBatchReject.AppearanceDisabled.BackColor = Color.LightGray;
            _btnBatchReject.AppearanceDisabled.ForeColor = Color.Gray;
            _lblBatchRejectIcon = new LabelControl
            {
                Text = "✗",
                Parent = _btnBatchReject,
                Location = new Point(4, 3),
                Size = new Size(18, 20),
                MinimumSize = new Size(18, 20),
                MaximumSize = new Size(18, 20),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold),
                ForeColor = Color.DarkRed,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblBatchRejectIcon.Click += (s, e) => _btnBatchReject.PerformClick();
            _lblBatchRejectIcon.BringToFront();

            _lblBatchHint = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Padding = new Padding(8, 0, 0, 0),
                Appearance = { ForeColor = Color.DarkOrange },
                Text = "",
                MaximumSize = new Size(600, 0)
            };
            _lblBatchStore = new LabelControl { Text = "Склад:", AutoSizeMode = LabelAutoSizeMode.None, Width = 40 };
            _batchStoreCombo = new ComboBoxEdit { Width = 220 };
            var batchFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            batchFlow.Controls.Add(_btnBatchSignAndUpload);
            // Порядок: сначала «Подписать и выгрузить», затем «Отказать», потом «Выгрузить».
            batchFlow.Controls.Add(_btnBatchReject);
            batchFlow.Controls.Add(_btnBatchUpload);
            batchFlow.Controls.Add(_lblBatchStore);
            batchFlow.Controls.Add(_batchStoreCombo);
            batchFlow.Controls.Add(_lblBatchHint);
            _batchActionsPanel.Controls.Add(batchFlow);
            _btnBatchSignAndUpload.Click += async (s, e) => await BatchSignAndUploadAsync();
            _btnBatchUpload.Click += async (s, e) => await BatchUploadAsync();
            _btnBatchReject.Click += async (s, e) => await BatchRejectAsync();

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
                Text = "  ← Назад к накладным",
                Width = 145,
                Height = 28,
                Dock = DockStyle.Left
            };
            _btnBack.Appearance.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
            _btnBack.Appearance.ForeColor = Color.SteelBlue;
            _btnBack.Appearance.BackColor = Color.AliceBlue;
            _btnBack.Click += async (s, e) => await ReturnToIncomingDocumentsAsync();

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

            _btnSignAndUpload = new SimpleButton
            {
                Text = "   Подписать и выгрузить в iiko",
                Width = 235,
                Height = 32
            };
            _btnSignAndUpload.Appearance.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnSignAndUpload.Appearance.ForeColor = Color.ForestGreen;
            _btnSignAndUpload.AppearanceDisabled.BackColor = Color.LightGray;
            _btnSignAndUpload.AppearanceDisabled.ForeColor = Color.Gray;
            _lblSignIcon = new LabelControl
            {
                Text = "✓",
                Parent = _btnSignAndUpload,
                Location = new Point(6, 4),
                Size = new Size(24, 24),
                MinimumSize = new Size(24, 24),
                MaximumSize = new Size(24, 24),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold),
                ForeColor = Color.ForestGreen,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblSignIcon.Click += (s, e) => _btnSignAndUpload.PerformClick();
            _lblSignIcon.BringToFront();

            _btnReject = new SimpleButton
            {
                Text = "  Отказать",
                Width = 118,
                Height = 32
            };
            _btnReject.Appearance.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnReject.Appearance.ForeColor = Color.DarkRed;
            _btnReject.AppearanceDisabled.BackColor = Color.LightGray;
            _btnReject.AppearanceDisabled.ForeColor = Color.Gray;
            _lblRejectIcon = new LabelControl
            {
                Text = "✗",
                Parent = _btnReject,
                Location = new Point(6, 4),
                Size = new Size(24, 24),
                MinimumSize = new Size(24, 24),
                MaximumSize = new Size(24, 24),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold),
                ForeColor = Color.DarkRed,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblRejectIcon.Click += (s, e) => _btnReject.PerformClick();
            _lblRejectIcon.BringToFront();

            _btnUploadOnly = new SimpleButton
            {
                Text = "  Выгрузить в iiko",
                Width = 178,
                Height = 32
            };
            _btnUploadOnly.Appearance.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnUploadOnly.Appearance.ForeColor = Color.ForestGreen;
            _btnUploadOnly.AppearanceDisabled.BackColor = Color.LightGray;
            _btnUploadOnly.AppearanceDisabled.ForeColor = Color.Gray;
            _lblUploadIcon = new LabelControl
            {
                Text = "✓",
                Parent = _btnUploadOnly,
                Location = new Point(6, 4),
                Size = new Size(24, 24),
                MinimumSize = new Size(24, 24),
                MaximumSize = new Size(24, 24),
                Padding = new Padding(0),
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold),
                ForeColor = Color.ForestGreen,
                Cursor = Cursors.Hand,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
            };
            _lblUploadIcon.Click += (s, e) => _btnUploadOnly.PerformClick();
            _lblUploadIcon.BringToFront();
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
                await SignAndUploadAsync();
            };
            _btnReject.Click += async (s, e) =>
            {
                var cfg = ConfigStore.Load();
                if (cfg != null && cfg.ConfirmSignOrReject)
                {
                    var r = XtraMessageBox.Show("Отказать контрагенту в подписи документа?", "Подтверждение",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes)
                        return;
                }

                await RejectInDiadocAsync();
            };
            _btnRefreshMappings.Click += async (s, e) => await RefreshMappingsAsync();

            var lblStore = new LabelControl { Text = "Склад iiko:", AutoSizeMode = LabelAutoSizeMode.None, Width = 70 };
            _storeCombo = new ComboBoxEdit { Width = 180 };

            var lblIncomingDate = new LabelControl { Text = "Дата прихода в iiko:", AutoSizeMode = LabelAutoSizeMode.None, Width = 130 };
            _incomingDateEdit = new DateEdit { Width = 110 };

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
            flowActions.Controls.Add(lblIncomingDate);
            flowActions.Controls.Add(_incomingDateEdit);
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
                // Правый отступ оставляем небольшим: суммы смещаем правее, но так, чтобы обе подписи полностью влезали.
                Padding = new Padding(4, 4, 40, 4)
            };
            // Нижний блок делим на две явные панели:
            // - слева чекбокс «С проведением»;
            // - справа суммы под колонками «Сумма» и «Сумма НДС».
            // Явные размеры и координаты надёжнее auto-layout и помогают избежать «серых квадратиков».
            var bottomLeftPanel = new PanelControl
            {
                Dock = DockStyle.Left,
                Width = 150,
                BorderStyle = BorderStyles.NoBorder
            };
            _chkCreateWithPosting.Location = new Point(0, 6);
            _chkCreateWithPosting.Margin = new Padding(0);
            bottomLeftPanel.Controls.Add(_chkCreateWithPosting);

            var bottomRightPanel = new PanelControl
            {
                Dock = DockStyle.Right,
                Width = 340,
                BorderStyle = BorderStyles.NoBorder
            };
            _lblInvoiceTotal = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(170, 22),
                Location = new Point(0, 6),
                Margin = new Padding(0),
                Text = string.Empty,
                Appearance =
                {
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = SystemColors.Control,
                    Options = { UseBackColor = true },
                    TextOptions =
                    {
                        HAlignment = DevExpress.Utils.HorzAlignment.Near,
                        VAlignment = DevExpress.Utils.VertAlignment.Center
                    }
                }
            };
            _lblInvoiceTotalVat = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(150, 22),
                Location = new Point(180, 6),
                Margin = new Padding(0),
                Text = string.Empty,
                Appearance =
                {
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = SystemColors.Control,
                    Options = { UseBackColor = true },
                    TextOptions =
                    {
                        HAlignment = DevExpress.Utils.HorzAlignment.Near,
                        VAlignment = DevExpress.Utils.VertAlignment.Center
                    }
                }
            };
            bottomRightPanel.Controls.Add(_lblInvoiceTotal);
            bottomRightPanel.Controls.Add(_lblInvoiceTotalVat);
            _lblInvoiceTotal.BringToFront();
            _lblInvoiceTotalVat.BringToFront();
            bottomPanel.Controls.Add(bottomRightPanel);
            bottomPanel.Controls.Add(bottomLeftPanel);

            _detailsPanel.Controls.Add(_detailsGrid);
            _detailsPanel.Controls.Add(bottomPanel);
            _detailsPanel.Controls.Add(actionsPanel);
            _detailsPanel.Controls.Add(detailsTop);

            // Отдельная панель под кнопку «Назад к накладным»
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
            Controls.Add(_batchActionsPanel);
            Controls.Add(_backPanel);
            Controls.Add(_filterPanel);
            Controls.Add(_buttonsPanel);
            Controls.Add(topPanel);

            _busyOverlay = new PanelControl
            {
                Dock = DockStyle.Fill,
                Visible = false,
                BorderStyle = BorderStyles.NoBorder,
                BackColor = Color.FromArgb(245, 245, 245)
            };
            _busyCard = new PanelControl
            {
                Size = new Size(360, 108),
                BorderStyle = BorderStyles.Simple,
                BackColor = Color.White
            };
            _busyLabel = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.Vertical,
                Text = "Загрузка...",
                Location = new Point(24, 18),
                Size = new Size(312, 38),
                Appearance =
                {
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                    TextOptions = { HAlignment = HorzAlignment.Center, VAlignment = VertAlignment.Center }
                }
            };
            _busyProgress = new MarqueeProgressBarControl
            {
                Size = new Size(312, 18),
                Location = new Point(24, 68),
                EditValue = 0
            };
            _busyProgress.Properties.MarqueeAnimationSpeed = 30;
            _busyCard.Controls.Add(_busyLabel);
            _busyCard.Controls.Add(_busyProgress);
            _busyOverlay.Controls.Add(_busyCard);
            _busyOverlay.Resize += (s, e) => CenterBusyCard();
            Controls.Add(_busyOverlay);
            CenterBusyCard();

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
            if (_batchActionsPanel != null && mode == ModeIncomingDetails)
                _batchActionsPanel.Visible = false;

            if (mode == ModeIncoming)
            {
                _grid.DataSource = _incomingDocuments ?? new List<DiadocDocumentRow>();
                ApplyDocumentsColumns();
            }
            else if (mode != ModeIncomingDetails)
            {
                RefreshCurrentViewAsync();
            }
        }

        private void CenterBusyCard()
        {
            if (_busyOverlay == null || _busyCard == null)
                return;

            var x = Math.Max(0, (_busyOverlay.ClientSize.Width - _busyCard.Width) / 2);
            var y = Math.Max(0, (_busyOverlay.ClientSize.Height - _busyCard.Height) / 2);
            _busyCard.Location = new Point(x, y);
        }

        private void ShowBusyOverlay(string message)
        {
            _busyOverlayDepth++;
            if (_busyOverlay == null)
                return;

            _busyLabel.Text = string.IsNullOrWhiteSpace(message) ? "Загрузка..." : message;
            CenterBusyCard();
            _busyOverlay.Visible = true;
            _busyOverlay.BringToFront();
            UseWaitCursor = true;

            // Принудительно даём WinForms отрисовать overlay до тяжёлой перерисовки экрана,
            // иначе пользователь успевает увидеть промежуточный "пустой" кадр.
            _busyOverlay.Refresh();
            _busyCard.Refresh();
            Refresh();
            Application.DoEvents();
        }

        private void UpdateBusyOverlay(string message)
        {
            if (_busyOverlay == null || !_busyOverlay.Visible || string.IsNullOrWhiteSpace(message))
                return;

            _busyLabel.Text = message;
            CenterBusyCard();
        }

        private void HideBusyOverlay()
        {
            if (_busyOverlayDepth > 0)
                _busyOverlayDepth--;

            if (_busyOverlayDepth > 0 || _busyOverlay == null)
                return;

            _busyOverlay.Visible = false;
            UseWaitCursor = false;
        }

        private async Task RunWithBusyOverlayAsync(string message, Func<Task> action)
        {
            ShowBusyOverlay(message);
            await Task.Yield();
            try
            {
                await action().ConfigureAwait(true);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private void OnSelectionChanged()
        {
            _counteragents = null;
            _incomingDocuments = null;
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

            // Самый левый столбец — выбор накладных для массовых действий (чекбокс)
            if (cols["Selected"] != null)
            {
                cols["Selected"].Caption = "Выбрать накладную";
                cols["Selected"].VisibleIndex = 0;
                cols["Selected"].Width = 70;
                var repoCheck = new DevExpress.XtraEditors.Repository.RepositoryItemCheckEdit();
                repoCheck.EditValueChanged += (s, e) =>
                {
                    // Сразу фиксируем изменение и пересчитываем состояние панели массовых действий,
                    // чтобы кнопки появлялись/блокировались уже после первого клика.
                    _gridView.PostEditor();
                    _gridView.UpdateCurrentRow();
                    RefreshBatchPanelState();
                };
                _gridView.GridControl.RepositoryItems.Add(repoCheck);
                cols["Selected"].ColumnEdit = repoCheck;
                cols["Selected"].OptionsColumn.AllowEdit = true;
            }
            // Столбец «Все привязки сделаны?»: только символы (✓/✗/пусто), по центру, крупнее
            if (cols["RequiresBinding"] != null)
            {
                cols["RequiresBinding"].Caption = "Все привязки сделаны?";
                cols["RequiresBinding"].VisibleIndex = 1;
                cols["RequiresBinding"].Width = 90;
                cols["RequiresBinding"].OptionsColumn.AllowEdit = false;
                cols["RequiresBinding"].DisplayFormat.FormatType = DevExpress.Utils.FormatType.None;
                var repoText = new DevExpress.XtraEditors.Repository.RepositoryItemTextEdit();
                _gridView.GridControl.RepositoryItems.Add(repoText);
                cols["RequiresBinding"].ColumnEdit = repoText;
            }

            if (cols["CounterpartyName"] != null) { cols["CounterpartyName"].Caption = "Отправитель"; cols["CounterpartyName"].VisibleIndex = 2; }
            if (cols["CounterpartyInn"] != null)
            {
                cols["CounterpartyInn"].Caption = "ИНН";
                cols["CounterpartyInn"].VisibleIndex = 3;
                cols["CounterpartyInn"].Width = 90;
            }
            if (cols["DocumentNumber"] != null)
            {
                cols["DocumentNumber"].Caption = "Номер";
                cols["DocumentNumber"].VisibleIndex = 4;
            }
            if (cols["DocumentDate"] != null)
            {
                cols["DocumentDate"].Caption = "От";
                cols["DocumentDate"].VisibleIndex = 5;
                cols["DocumentDate"].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
                cols["DocumentDate"].Width = 80;
            }
            if (cols["SentToEdo"] != null)
            {
                cols["SentToEdo"].Caption = "Отправлен в ЭДО";
                cols["SentToEdo"].VisibleIndex = 6;
                cols["SentToEdo"].AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
                cols["SentToEdo"].Width = 110;
            }
            if (cols["TotalVat"] != null)
            {
                cols["TotalVat"].Caption = "Сумма НДС";
                cols["TotalVat"].VisibleIndex = 7;
                cols["TotalVat"].Width = 63;
            }
            if (cols["TotalAmount"] != null)
            {
                cols["TotalAmount"].Caption = "Сумма";
                cols["TotalAmount"].VisibleIndex = 8;
                cols["TotalAmount"].Width = 63;
            }
            if (cols["StatusText"] != null) { cols["StatusText"].Caption = "Статус ЭДО"; cols["StatusText"].VisibleIndex = 10; }
            if (cols["SupplierFound"] != null)
            {
                cols["SupplierFound"].Caption = "Поставщик";
                cols["SupplierFound"].VisibleIndex = 9;
                cols["SupplierFound"].Width = 80;
                cols["SupplierFound"].OptionsColumn.AllowEdit = false;
            }
            // Внутренняя тех. колонка — в гриде не показываем.
            if (cols["AllItemsMapped"] != null)
            {
                cols["AllItemsMapped"].Visible = false;
            }
            if (cols["Supplier"] != null) cols["Supplier"].Visible = false;
            if (cols["IikoInvoice"] != null) cols["IikoInvoice"].Visible = false;
            if (cols["IikoStatus"] != null)
            {
                cols["IikoStatus"].Caption = "Статус накладной";
                cols["IikoStatus"].VisibleIndex = 11;
                cols["IikoStatus"].Width = 220;
                cols["IikoStatus"].OptionsColumn.AllowEdit = false;
            }
            if (cols["IikoSupplierId"] != null) cols["IikoSupplierId"].Visible = false;

            // Заголовки всех видимых столбцов — по центру.
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in cols)
            {
                if (c.Visible)
                    c.AppearanceHeader.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            }

            // Все остальные столбцы только для чтения (кроме Selected)
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in cols)
            {
                if (c.FieldName != "Selected")
                    c.OptionsColumn.AllowEdit = false;
            }

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
            else if (e.Column.FieldName == "RequiresBinding")
            {
                var row = view.GetRow(e.RowHandle) as DiadocDocumentRow;
                if (row == null) { e.DisplayText = ""; return; }
                // Поставщик не найден в iiko — пусто (к внесению не относится)
                if (!row.SupplierFound)
                {
                    e.DisplayText = "";
                    return;
                }
                // Поставщик найден: зелёная галочка если все привязки в рамках накладной сделаны, иначе красный крестик
                e.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
                e.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;
                e.Appearance.Font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold);
                if (row.AllItemsMapped == true)
                {
                    e.Appearance.ForeColor = Color.Green;
                    e.DisplayText = "✓";
                }
                else if (row.AllItemsMapped == false)
                {
                    e.Appearance.ForeColor = Color.Red;
                    e.DisplayText = "✗";
                }
                else
                {
                    e.Appearance.ForeColor = Color.Gray;
                    e.DisplayText = "…";
                }
            }
        }

        private void GridView_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (e.Column?.FieldName == "Selected")
            {
                _gridView.PostEditor();
                _gridView.UpdateCurrentRow();
                RefreshBatchPanelState();
            }
        }

        private void RefreshBatchPanelState()
        {
            if (_currentMode != ModeIncoming || _grid == null)
            {
                if (_batchActionsPanel != null)
                    _batchActionsPanel.Visible = false;
                return;
            }
            var list = _grid.DataSource as List<DiadocDocumentRow>;
            if (list == null)
            {
                _batchActionsPanel.Visible = false;
                return;
            }
            var selected = list.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                _batchActionsPanel.Visible = false;
                return;
            }
            _batchActionsPanel.Visible = true;
            _ = EnsureBatchStoreComboPopulated();
            // Уже внесённые в iiko (не удалённые) — блокируем выгрузку, чтобы не дублировать приход.
            var anyAlreadyInIiko = selected.Any(r =>
            {
                var s = (r.IikoStatus ?? "").Trim().ToLowerInvariant();
                return s.StartsWith("внесено в iiko");
            });
            // Блокируем кнопки, если среди выбранных:
            // 1) есть накладные, уже внесённые в iiko (не дублируем приход), или
            // 2) есть накладные без поставщика в iiko (требуется привязка), или
            // 3) есть накладные, для которых точно не все строки привязаны (AllItemsMapped == false; null = неизвестно, не блокируем).
            var anyRequiresBinding = selected.Any(r => r.RequiresBinding);
            var anyNotFullyMapped = selected.Any(r => r.AllItemsMapped == false);
            if (anyAlreadyInIiko)
            {
                _btnBatchSignAndUpload.Enabled = false;
                _btnBatchUpload.Enabled = false;
                _btnBatchReject.Enabled = false;
                _lblBatchHint.Text = "Среди выбранных есть накладные, уже внесённые в iiko. Снимите их с выбора, чтобы не дублировать приход.";
            }
            else if (anyRequiresBinding || anyNotFullyMapped)
            {
                _btnBatchSignAndUpload.Enabled = false;
                _btnBatchUpload.Enabled = false;
                _btnBatchReject.Enabled = false;
                _lblBatchHint.Text = "Сначала сделай привязки для всех строк выбранных накладных (зелёная галочка в колонке «Все ли привязки сделаны?»), либо сними галочки с проблемных накладных.";
            }
            else
            {
                _btnBatchSignAndUpload.Enabled = true;
                _btnBatchUpload.Enabled = true;
                _btnBatchReject.Enabled = true;
                _lblBatchHint.Text = "";
            }
            SyncBatchIconLabelsAppearance();
        }

        private void SyncBatchIconLabelsAppearance()
        {
            if (_lblBatchSignIcon != null)
            {
                _lblBatchSignIcon.Enabled = _btnBatchSignAndUpload.Enabled;
                _lblBatchSignIcon.BackColor = _btnBatchSignAndUpload.Enabled ? SystemColors.Control : Color.LightGray;
                _lblBatchSignIcon.ForeColor = _btnBatchSignAndUpload.Enabled ? Color.ForestGreen : Color.Gray;
            }
            if (_lblBatchRejectIcon != null)
            {
                _lblBatchRejectIcon.Enabled = _btnBatchReject.Enabled;
                _lblBatchRejectIcon.BackColor = _btnBatchReject.Enabled ? SystemColors.Control : Color.LightGray;
                _lblBatchRejectIcon.ForeColor = _btnBatchReject.Enabled ? Color.DarkRed : Color.Gray;
            }
            if (_lblBatchUploadIcon != null)
            {
                _lblBatchUploadIcon.Enabled = _btnBatchUpload.Enabled;
                _lblBatchUploadIcon.BackColor = _btnBatchUpload.Enabled ? SystemColors.Control : Color.LightGray;
                _lblBatchUploadIcon.ForeColor = _btnBatchUpload.Enabled ? Color.ForestGreen : Color.Gray;
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
                if (_lblInvoiceTotal != null || _lblInvoiceTotalVat != null)
                {
                    var total = (row.TotalAmount ?? string.Empty).Trim();
                    var vat = (row.TotalVat ?? string.Empty).Trim();
                    if (_lblInvoiceTotal != null)
                        _lblInvoiceTotal.Text = string.IsNullOrEmpty(total) ? string.Empty : $"Итого: {total}";
                    if (_lblInvoiceTotalVat != null)
                        _lblInvoiceTotalVat.Text = string.IsNullOrEmpty(vat) ? string.Empty : $"НДС: {vat}";
                }
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
                if (_detailsView.Columns["IikoProductArticle"] != null) _detailsView.Columns["IikoProductArticle"].Visible = false;
                if (_detailsView.Columns["ContainerId"] != null) _detailsView.Columns["ContainerId"].Visible = false;
                if (_detailsView.Columns["AmountUnitId"] != null) _detailsView.Columns["AmountUnitId"].Visible = false;
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
                if (_detailsView.Columns["VatPercent"] != null)
                {
                    _detailsView.Columns["VatPercent"].Caption = "Ставка, %";
                    _detailsView.Columns["VatPercent"].VisibleIndex = col++;
                    _detailsView.Columns["VatPercent"].Width = 58;
                }

                // Подсветка незамапленных строк и блокировка кнопок выгрузки.
                _detailsView.RowCellStyle -= DetailsView_RowCellStyle;
                _detailsView.RowCellStyle += DetailsView_RowCellStyle;
                UpdateUploadButtonsEnabled();

                await EnsureStoresLoadedAsync().ConfigureAwait(true);
                PopulateStoresCombo();

                // По умолчанию дата прихода в iiko = дата документа из Диадока.
                if (_incomingDateEdit != null)
                {
                    if (DateTime.TryParse(row.DocumentDate, out var dtDoc))
                        _incomingDateEdit.DateTime = dtDoc.Date;
                    else
                        _incomingDateEdit.EditValue = null;
                }

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
        /// Одновременно пишет подробный лог по тому, как именно нашлась (или не нашлась) каждая строка.
        /// </summary>
        private async Task EnsureMappingsForCurrentDocumentAsync()
        {
            _currentAllItemsMapped = false;
            if (_currentDocument == null || _currentItems == null || _currentItems.Length == 0 || _iikoClient == null)
                return;
            if (string.IsNullOrWhiteSpace(_currentDocument.IikoSupplierId))
                return;

            var docNumber = _currentDocument.DocumentNumber ?? "<no-number>";

            var supplierPricelistKey = _currentDocument.IikoSupplierId;
            var sup = _suppliers?.FirstOrDefault(s => s.Id == _currentDocument.IikoSupplierId);
            if (sup != null && !string.IsNullOrWhiteSpace(sup.Code))
                supplierPricelistKey = sup.Code;

            var pricelistDate = NormalizeDateForIikoDocument(_currentDocument.DocumentDate);
            var pricelist = await _iikoClient.GetSupplierPricelistAsync(supplierPricelistKey, pricelistDate).ConfigureAwait(true);
            if (pricelist == null || pricelist.Count == 0)
            {
                IikoRestoClient.WriteImportDebugLog("EnsureMappings: doc=" + docNumber + " supplierKey=" + supplierPricelistKey + " -> Прайс-лист пустой или не получен.");
                return;
            }

            var codeToProduct = BuildPricelistCodeMap(pricelist, out var ambiguousCodes);

            IikoRestoClient.WriteImportDebugLog("EnsureMappings: doc=" + docNumber + " items=" + _currentItems.Length
                                               + " pricelistByCode=" + codeToProduct.Count
                                               + " ambiguousCodes=" + ambiguousCodes.Count);

            foreach (var it in _currentItems)
            {
                // Сбрасываем предыдущую привязку, чтобы не тянуть старые данные между накладными.
                it.Product = null;
                it.IikoProductName = null;
                it.IikoProductArticle = null;
                it.Unit = null;
                it.ContainerId = null;
                it.ContainerCount = null;
                it.AmountUnitId = null;

                var code = (it.ItemVendorCode ?? "").Trim();
                var article = (it.ItemArticle ?? "").Trim();
                var supplierName = (it.SupplierProductName ?? "").Trim();

                SupplierPricelistItem mapped = null;
                string mappingSource = "none";

                // Поиск привязки жёстко ограничиваем только артикулом у поставщика.
                // Это позволяет использовать его как уникальный ключ и избегать
                // неожиданных совпадений по наименованию.
                if (!string.IsNullOrEmpty(code) && ambiguousCodes.Contains(code))
                {
                    mappingSource = "duplicateVendorCode";
                }
                else if (!string.IsNullOrEmpty(code) && codeToProduct.TryGetValue(code, out mapped))
                {
                    mappingSource = "vendorCode";
                }

                if (mapped != null)
                {
                    it.Product = mapped.NativeProduct;
                    it.IikoProductArticle = mapped.NativeProductNum;
                    it.IikoProductName = mapped.NativeProductName;
                    if (!string.IsNullOrWhiteSpace(mapped.ContainerName))
                        it.Unit = mapped.ContainerName;
                    if (!string.IsNullOrWhiteSpace(mapped.ContainerId))
                        it.ContainerId = mapped.ContainerId;
                    if (mapped.ContainerCount.HasValue)
                        it.ContainerCount = mapped.ContainerCount.Value;
                    if (!string.IsNullOrWhiteSpace(mapped.AmountUnitId))
                        it.AmountUnitId = mapped.AmountUnitId;
                }

                var logLine = "EnsureMappings.Item: doc=" + docNumber
                              + " line=" + it.LineIndex
                              + " supplierName=\"" + supplierName + "\""
                              + " code=\"" + code + "\""
                              + " article=\"" + article + "\""
                              + " mappingSource=" + mappingSource
                              + " mappedProduct=" + (it.Product ?? "<null>")
                              + " mappedArticle=\"" + (it.IikoProductArticle ?? "<null>") + "\""
                              + " mappedName=\"" + (it.IikoProductName ?? "<null>") + "\""
                              + " containerId=" + (it.ContainerId ?? "<null>")
                              + " containerCount=" + (it.ContainerCount.HasValue ? it.ContainerCount.Value.ToString(CultureInfo.InvariantCulture) : "<null>")
                              + " amountUnitId=" + (it.AmountUnitId ?? "<null>")
                              + " containerName=\"" + (it.Unit ?? "<null>") + "\"";
                IikoRestoClient.WriteImportDebugLog(logLine);
            }

            // Для незамапленных строк отображаем плейсхолдер "[выберите]".
            foreach (var it in _currentItems)
            {
                var code = (it.ItemVendorCode ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(code) && ambiguousCodes.Contains(code))
                    it.IikoProductName = "Дубль кода в прайс-листе поставщика";
                else if (string.IsNullOrWhiteSpace(it.Product))
                    it.IikoProductName = "Отсутствует в прайс-листе";
            }

            _currentAllItemsMapped = _currentItems.All(it => !string.IsNullOrWhiteSpace(it.Product));
            IikoRestoClient.WriteImportDebugLog("EnsureMappings: doc=" + docNumber + " allMapped=" + _currentAllItemsMapped);
        }

        private void UpdateUploadButtonsEnabled()
        {
            var docFinalized = IsDocumentFinalized();
            var hasMappedItems = _currentAllItemsMapped && _currentItems != null && _currentItems.Length > 0;
            var alreadyInIiko = IsCurrentDocumentAlreadyInIiko();
            // Подписанный документ можно повторно выгрузить в iiko, если накладная была удалена из iiko.
            _btnUploadOnly.Enabled = hasMappedItems && !alreadyInIiko;
            _btnSignAndUpload.Enabled = !docFinalized && hasMappedItems && !alreadyInIiko;
            _btnReject.Enabled = !docFinalized;
            SyncIconLabelsAppearance();
        }

        private void SyncIconLabelsAppearance()
        {
            if (_lblSignIcon != null)
            {
                _lblSignIcon.Enabled = _btnSignAndUpload.Enabled;
                _lblSignIcon.BackColor = _btnSignAndUpload.Enabled ? SystemColors.Control : Color.LightGray;
                _lblSignIcon.ForeColor = _btnSignAndUpload.Enabled ? Color.ForestGreen : Color.Gray;
            }
            if (_lblRejectIcon != null)
            {
                _lblRejectIcon.Enabled = _btnReject.Enabled;
                _lblRejectIcon.BackColor = _btnReject.Enabled ? SystemColors.Control : Color.LightGray;
                _lblRejectIcon.ForeColor = _btnReject.Enabled ? Color.DarkRed : Color.Gray;
            }
            if (_lblUploadIcon != null)
            {
                _lblUploadIcon.Enabled = _btnUploadOnly.Enabled;
                _lblUploadIcon.BackColor = _btnUploadOnly.Enabled ? SystemColors.Control : Color.LightGray;
                _lblUploadIcon.ForeColor = _btnUploadOnly.Enabled ? Color.ForestGreen : Color.Gray;
            }
        }

        private bool IsDocumentFinalized()
        {
            if (_currentDocument == null) return false;
            var s = (_currentDocument.StatusText ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return false;
            // Диадок возвращает, например: "Подписан", "Подписан получателем", "Отказ в подписи", "Отклонён" и т.п.
            if (s.IndexOf("подписан", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("отказ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("отклон", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool IsCurrentDocumentAlreadyInIiko()
        {
            if (_currentDocument == null)
                return false;
            var s = (_currentDocument.IikoStatus ?? "").Trim().ToLowerInvariant();
            return s.StartsWith("внесено в iiko");
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

        private static Dictionary<string, SupplierPricelistItem> BuildPricelistCodeMap(
            IEnumerable<SupplierPricelistItem> pricelist,
            out HashSet<string> ambiguousCodes)
        {
            var codeToProduct = new Dictionary<string, SupplierPricelistItem>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCode(string rawCode, SupplierPricelistItem row)
            {
                var code = (rawCode ?? "").Trim();
                if (string.IsNullOrWhiteSpace(code) || ambiguous.Contains(code))
                    return;

                if (codeToProduct.TryGetValue(code, out var existing))
                {
                    var sameNativeProduct =
                        string.Equals(existing.NativeProduct ?? "", row.NativeProduct ?? "", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.NativeProductNum ?? "", row.NativeProductNum ?? "", StringComparison.OrdinalIgnoreCase);

                    if (!sameNativeProduct)
                    {
                        codeToProduct.Remove(code);
                        ambiguous.Add(code);
                    }
                    return;
                }

                codeToProduct[code] = row;
            }

            foreach (var row in pricelist ?? Enumerable.Empty<SupplierPricelistItem>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.NativeProduct))
                    continue;
                AddCode(row.SupplierProductNum, row);
                AddCode(row.SupplierProductCode, row);
            }

            ambiguousCodes = ambiguous;
            return codeToProduct;
        }

        private static decimal? TryCalculateVatPercent(UtdItemRow item)
        {
            if (item == null)
                return null;
            if (item.VatPercent.HasValue)
                return item.VatPercent.Value;
            if (item.Subtotal <= 0m || item.Vat < 0m)
                return null;
            if (item.Vat == 0m)
                return 0m;

            var subtotalWithoutVat = item.Subtotal - item.Vat;
            if (subtotalWithoutVat <= 0m)
                return null;

            return decimal.Round(item.Vat * 100m / subtotalWithoutVat, 3, MidpointRounding.AwayFromZero);
        }

        private static bool ShouldUseContainerQuantity(UtdItemRow item)
        {
            if (item == null || !item.ContainerCount.HasValue || item.ContainerCount.Value <= 0m)
                return false;

            var unitName = (item.UnitName ?? "").Trim().ToLowerInvariant();
            if (unitName == "шт" || unitName == "штука" || unitName == "штук" ||
                unitName.StartsWith("уп") || unitName.StartsWith("кор") ||
                unitName.StartsWith("ящ") || unitName.StartsWith("бут") ||
                unitName.StartsWith("бан") || unitName.StartsWith("вед") ||
                unitName.StartsWith("кан"))
                return true;

            var containerName = (item.Unit ?? "").Trim().ToLowerInvariant();
            if (containerName.StartsWith("шт по") || containerName.StartsWith("уп ") ||
                containerName.StartsWith("уп.") || containerName.Contains(" по "))
                return true;

            return false;
        }

        /// <summary>
        /// Строит XML приходной накладной для iiko. Использует только уже вычисленные привязки (it.Product),
        /// без повторного поиска по прайс-листу — источник истины: EnsureMappingsForCurrentDocumentAsync.
        /// </summary>
        /// <param name="createWithPosting">Если true, в XML добавляется проведение документа (conducted).</param>
        private string BuildIncomingInvoiceXml(DiadocDocumentRow doc, UtdItemRow[] items, string supplierId, string storeId,
            bool createWithPosting = false,
            string customDateIncoming = null)
        {
            var xDoc = new XDocument();
            var root = new XElement("document");
            xDoc.Add(root);

            // Проведение делаем отдельным вызовом ProcessIncomingInvoiceAsync после импорта (см. UploadToIikoAsync).

            var itemsEl = new XElement("items");
            root.Add(itemsEl);

            var itemsArr = items ?? Array.Empty<UtdItemRow>();
            IikoRestoClient.WriteImportDebugLog("BuildIncomingInvoiceXml: doc=" + (doc.DocumentNumber ?? "<no-number>")
                + " supplierId=" + (supplierId ?? "<null>")
                + " storeId=" + (storeId ?? "<null>")
                + " itemsCount=" + itemsArr.Length
                + " createWithPosting=" + createWithPosting);

            foreach (var it in itemsArr)
            {
                var itemEl = new XElement("item");
                itemsEl.Add(itemEl);

                var useContainerQuantity = ShouldUseContainerQuantity(it);
                var actualAmount = it.Quantity;
                if (useContainerQuantity)
                    actualAmount = decimal.Round(it.Quantity * it.ContainerCount.Value, 3, MidpointRounding.AwayFromZero);
                var amountInContainer = useContainerQuantity ? actualAmount : it.Quantity;

                itemEl.Add(new XElement("amount", amountInContainer.ToString(CultureInfo.InvariantCulture)));

                var vendorCode = (it.ItemVendorCode ?? "").Trim();
                var article = (it.ItemArticle ?? "").Trim();
                var supplierName = (it.SupplierProductName ?? "").Trim();

                // Единственный источник productGuid — уже вычисленная привязка в EnsureMappingsForCurrentDocumentAsync.
                string nativeProductGuid = string.IsNullOrWhiteSpace(it.Product) ? null : it.Product.Trim();
                if (!string.IsNullOrWhiteSpace(nativeProductGuid))
                    itemEl.Add(new XElement("product", nativeProductGuid));

                // Наш артикул в iiko безопасно отправлять всегда: он описывает уже найденный native product.
                if (!string.IsNullOrWhiteSpace(it.IikoProductArticle))
                    itemEl.Add(new XElement("productArticle", it.IikoProductArticle));

                // Код/артикул поставщика отправляем только когда не удалось однозначно проставить product GUID.
                // Иначе iiko может начать повторно искать позицию по supplier article и наткнуться на дубли
                // в чужих/старых прайс-листах, хотя нужный товар уже найден по GUID.
                var shouldSendSupplierArticle = string.IsNullOrWhiteSpace(nativeProductGuid);
                if (shouldSendSupplierArticle && !string.IsNullOrWhiteSpace(vendorCode))
                {
                    itemEl.Add(new XElement("supplierProductArticle", vendorCode));
                    itemEl.Add(new XElement("code", vendorCode));
                }

                itemEl.Add(new XElement("num", it.LineIndex));

                if (!string.IsNullOrWhiteSpace(it.ContainerId))
                    itemEl.Add(new XElement("containerId", it.ContainerId));
                // Для фасовок вида "шт по 4,1 кг" amount должно оставаться в штуках.
                // Если отправить amountUnit, iiko начинает трактовать amount как базовую единицу
                // (кг/л) и в колонке "В таре" появляются дроби вроде 1,463 вместо 6.
                if (!useContainerQuantity && !string.IsNullOrWhiteSpace(it.AmountUnitId))
                    itemEl.Add(new XElement("amountUnit", it.AmountUnitId));

                if (!string.IsNullOrWhiteSpace(storeId))
                    itemEl.Add(new XElement("store", storeId));

                itemEl.Add(new XElement("price", it.Price.ToString(CultureInfo.InvariantCulture)));
                itemEl.Add(new XElement("sum", it.Subtotal.ToString(CultureInfo.InvariantCulture)));
                var vatPercent = TryCalculateVatPercent(it);
                if (vatPercent.HasValue)
                    itemEl.Add(new XElement("vatPercent", vatPercent.Value.ToString(CultureInfo.InvariantCulture)));
                itemEl.Add(new XElement("vatSum", it.Vat.ToString(CultureInfo.InvariantCulture)));
                itemEl.Add(new XElement("actualAmount", actualAmount.ToString(CultureInfo.InvariantCulture)));

                var logLine = "BuildIncomingInvoiceXml.Item: doc=" + (doc.DocumentNumber ?? "<no-number>")
                              + " line=" + it.LineIndex
                              + " supplierName=\"" + supplierName + "\""
                              + " code=\"" + vendorCode + "\""
                              + " article=\"" + article + "\""
                              + " productGuid=" + (nativeProductGuid ?? "<null>")
                              + " productArticle=\"" + (it.IikoProductArticle ?? "<null>") + "\""
                              + " sendSupplierArticle=" + shouldSendSupplierArticle
                              + " containerId=" + (it.ContainerId ?? "<null>")
                              + " containerCount=" + (it.ContainerCount.HasValue ? it.ContainerCount.Value.ToString(CultureInfo.InvariantCulture) : "<null>")
                              + " amountUnitId=" + (!useContainerQuantity ? (it.AmountUnitId ?? "<null>") : "<skipped>")
                              + " amount=" + amountInContainer.ToString(CultureInfo.InvariantCulture)
                              + " actualAmount=" + actualAmount.ToString(CultureInfo.InvariantCulture)
                              + " price=" + it.Price.ToString(CultureInfo.InvariantCulture)
                              + " sum=" + it.Subtotal.ToString(CultureInfo.InvariantCulture)
                              + " vat=" + it.Vat.ToString(CultureInfo.InvariantCulture)
                              + " vatPercent=" + (vatPercent.HasValue ? vatPercent.Value.ToString(CultureInfo.InvariantCulture) : "<null>");
                IikoRestoClient.WriteImportDebugLog(logLine);
            }

            var dateIncoming = !string.IsNullOrWhiteSpace(customDateIncoming)
                ? NormalizeDateForIikoDocument(customDateIncoming)
                : NormalizeDateForIikoDocument(doc.DocumentDate);

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

            // На всякий случай перед выгрузкой ещё раз подтягиваем прайс-лист и применяем все правила маппинга
            // (по коду, артикулу и наименованию), чтобы Product был проставлен для всех строк.
            await EnsureMappingsForCurrentDocumentAsync().ConfigureAwait(true);

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
                IikoRestoClient.WriteImportDebugLog("storeId=" + (storeId ?? "<null>") + " supplierId=" + (_currentDocument.IikoSupplierId ?? "<null>"));

                // Используем галочку на форме как источник правды; она синхронизирует значение в ConfigStore.
                var createWithPosting = _chkCreateWithPosting != null && _chkCreateWithPosting.Checked;
                string customDate = null;
                if (_incomingDateEdit != null && _incomingDateEdit.EditValue is DateTime dtIncoming)
                    customDate = dtIncoming.ToString("dd.MM.yyyy");
                var xml = BuildIncomingInvoiceXml(_currentDocument, _currentItems, _currentDocument.IikoSupplierId, storeId, createWithPosting, customDate);
                IikoRestoClient.SaveOutgoingXmlForDebug(_currentDocument.DocumentNumber ?? "unknown", xml);
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
                        msg = "iiko не смог однозначно сопоставить одну или несколько строк с прайс-листом текущего поставщика.\r\n" +
                              "Обычно это означает, что в прайс-листе есть незаполненная привязка «товар поставщика → наш товар» или дубли по коду поставщика.\r\n" +
                              "Проверьте прайс-лист именно этого поставщика в iiko и повторите выгрузку.\r\n\r\n" +
                              "Ответ iiko: " + body;
                    }
                    else if (body.IndexOf("One entity expected for article=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        msg = "iiko нашёл несколько сущностей по одному и тому же артикулу поставщика.\r\n" +
                              "Плагин уже передаёт supplier документа и product GUID, но в прайс-листе поставщика всё ещё могут быть дубли одной позиции.\r\n" +
                              "Проверьте у этого поставщика строки с одинаковым артикулом и удалите/объедините дубликаты, затем повторите выгрузку.\r\n\r\n" +
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

        /// <summary>
        /// Комбинированная кнопка: сначала выгрузка в iiko, затем подпись входящего документа в Диадоке.
        /// </summary>
        private async Task SignAndUploadAsync()
        {
            if (_currentMode != ModeIncomingDetails || _client == null || _iikoClient == null)
                return;

            // Сначала стандартная выгрузка в iiko с проведением (withSign = true учитывает галочку "С проведением").
            await UploadToIikoAsync(true).ConfigureAwait(true);

            if (_currentDocument == null)
                return;

            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) ||
                string.IsNullOrEmpty(_currentDocument.MessageId) ||
                string.IsNullOrEmpty(_currentDocument.EntityId))
                return;

            try
            {
                await _client.SignIncomingDocumentAsync(boxId, _currentDocument.MessageId, _currentDocument.EntityId)
                    .ConfigureAwait(true);

                _currentDocument.StatusText = "Подписан";
                UpdateUploadButtonsEnabled();

                XtraMessageBox.Show("Документ в Диадоке подписан.", "ЭДО ↔ iiko",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show("Ошибка при подписи документа в Диадоке:\r\n" + ex.Message,
                    "ЭДО ↔ iiko", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Кнопка "Отказать" — отправка в Диадок отказа в подписи для текущего УПД.
        /// </summary>
        private async Task RejectInDiadocAsync()
        {
            if (_currentMode != ModeIncomingDetails || _client == null)
                return;
            if (_currentDocument == null)
                return;

            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) ||
                string.IsNullOrEmpty(_currentDocument.MessageId) ||
                string.IsNullOrEmpty(_currentDocument.EntityId))
                return;

            string reason = "Отказ в подписании документа.";
            try
            {
                var input = XtraInputBox.Show(
                    "Укажи причину отказа в подписи (она уйдёт в Диадок).",
                    "Причина отказа",
                    "Не согласен с содержимым документа.");
                if (input == null)
                    return;
                if (!string.IsNullOrWhiteSpace(input))
                    reason = input.Trim();
            }
            catch
            {
                // Если по какой-то причине диалог не открылся, используем дефолтную причину.
            }

            try
            {
                await _client.RejectIncomingDocumentAsync(boxId, _currentDocument.MessageId, _currentDocument.EntityId, reason)
                    .ConfigureAwait(true);

                _currentDocument.StatusText = "Отказ в подписи";
                UpdateUploadButtonsEnabled();

                XtraMessageBox.Show("Отказ в подписи отправлен в Диадок.", "ЭДО ↔ iiko",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show("Ошибка при отправке отказа в Диадок:\r\n" + ex.Message,
                    "ЭДО ↔ iiko", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private List<DiadocDocumentRow> GetSelectedDocumentsForBatch()
        {
            if (_currentMode != ModeIncoming || _grid?.DataSource == null)
                return new List<DiadocDocumentRow>();
            var list = _grid.DataSource as List<DiadocDocumentRow>;
            if (list == null) return new List<DiadocDocumentRow>();
            return list.Where(r => r.Selected && r.SupplierFound && r.AllItemsMapped != false).ToList();
        }

        private async Task EnsureBatchStoreComboPopulated()
        {
            if (_batchStoreCombo == null) return;
            if (_stores != null && _stores.Count > 0 && _batchStoreCombo.Properties.Items.Count > 0)
                return;
            await EnsureStoresLoadedAsync().ConfigureAwait(true);
            if (_stores == null || _stores.Count == 0)
            {
                _batchStoreCombo.Properties.Items.Clear();
                _batchStoreCombo.Properties.Items.Add("— нет данных по складам iiko —");
                if (_batchStoreCombo.Properties.Items.Count > 0)
                    _batchStoreCombo.SelectedIndex = 0;
                return;
            }
            _batchStoreCombo.Properties.Items.Clear();
            foreach (var s in _stores)
            {
                var name = string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name;
                _batchStoreCombo.Properties.Items.Add(name);
            }
            if (_stores.Count > 0)
                _batchStoreCombo.SelectedIndex = 0;
        }

        private string GetBatchStoreId()
        {
            if (_stores == null || _stores.Count == 0 || _batchStoreCombo == null) return null;
            if (_batchStoreCombo.SelectedIndex >= 0 && _batchStoreCombo.SelectedIndex < _stores.Count)
                return _stores[_batchStoreCombo.SelectedIndex].Id;
            return _batchStoreCombo.Text?.Trim();
        }

        private async Task BatchUploadAsync()
        {
            var selected = GetSelectedDocumentsForBatch();
            if (selected.Count == 0) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) || _client == null || _iikoClient == null) return;
            await EnsureBatchStoreComboPopulated().ConfigureAwait(true);
            var storeId = GetBatchStoreId();
            if (string.IsNullOrWhiteSpace(storeId))
            {
                XtraMessageBox.Show("Выберите склад iiko в панели массовой выгрузки.", "Массовая выгрузка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var createWithPosting = _chkCreateWithPosting != null && _chkCreateWithPosting.Checked;
            int ok = 0, fail = 0;
            foreach (var doc in selected)
            {
                try
                {
                    var items = await _client.GetUtdItemsAsync(boxId, doc.MessageId, doc.EntityId).ConfigureAwait(true);
                    _currentDocument = doc;
                    _currentItems = items ?? Array.Empty<UtdItemRow>();
                    await EnsureMappingsForCurrentDocumentAsync().ConfigureAwait(true);
                    if (!_currentAllItemsMapped) { fail++; continue; }
                    var success = await UploadOneDocumentToIikoAsync(doc, _currentItems, storeId, createWithPosting).ConfigureAwait(true);
                    if (success) ok++; else fail++;
                }
                catch { fail++; }
            }
            _gridView.RefreshData();
            RefreshBatchPanelState();
            XtraMessageBox.Show($"Выгружено: {ok}, ошибок: {fail}.", "Массовая выгрузка", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task BatchSignAndUploadAsync()
        {
            var selected = GetSelectedDocumentsForBatch();
            if (selected.Count == 0) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) || _client == null || _iikoClient == null) return;
            await EnsureBatchStoreComboPopulated().ConfigureAwait(true);
            var storeId = GetBatchStoreId();
            if (string.IsNullOrWhiteSpace(storeId))
            {
                XtraMessageBox.Show("Выберите склад iiko в панели массовой выгрузки.", "Массовая выгрузка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var createWithPosting = _chkCreateWithPosting != null && _chkCreateWithPosting.Checked;
            int ok = 0, fail = 0;
            foreach (var doc in selected)
            {
                try
                {
                    var items = await _client.GetUtdItemsAsync(boxId, doc.MessageId, doc.EntityId).ConfigureAwait(true);
                    _currentDocument = doc;
                    _currentItems = items ?? Array.Empty<UtdItemRow>();
                    await EnsureMappingsForCurrentDocumentAsync().ConfigureAwait(true);
                    if (!_currentAllItemsMapped) { fail++; continue; }
                    var uploaded = await UploadOneDocumentToIikoAsync(doc, _currentItems, storeId, createWithPosting).ConfigureAwait(true);
                    if (uploaded)
                    {
                        await _client.SignIncomingDocumentAsync(boxId, doc.MessageId, doc.EntityId).ConfigureAwait(true);
                        doc.StatusText = "Подписан";
                        ok++;
                    }
                    else fail++;
                }
                catch { fail++; }
            }
            _gridView.RefreshData();
            RefreshBatchPanelState();
            XtraMessageBox.Show($"Подписано и выгружено: {ok}, ошибок: {fail}.", "Массовая выгрузка", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task BatchRejectAsync()
        {
            if (_currentMode != ModeIncoming || _grid?.DataSource == null) return;
            var list = _grid.DataSource as List<DiadocDocumentRow>;
            var selected = list?.Where(r => r.Selected).ToList() ?? new List<DiadocDocumentRow>();
            if (selected.Count == 0) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId) || _client == null) return;
            var reason = XtraInputBox.Show("Причина отказа (будет отправлена в Диадок для всех выбранных):", "Массовый отказ", "Отказ в подписании документа.");
            if (reason == null) return;
            if (string.IsNullOrWhiteSpace(reason)) reason = "Отказ в подписании документа.";
            int ok = 0, fail = 0;
            foreach (var doc in selected)
            {
                try
                {
                    await _client.RejectIncomingDocumentAsync(boxId, doc.MessageId, doc.EntityId, reason).ConfigureAwait(true);
                    doc.StatusText = "Отказ в подписи";
                    ok++;
                }
                catch { fail++; }
            }
            _gridView.RefreshData();
            RefreshBatchPanelState();
            XtraMessageBox.Show($"Отказ отправлен: {ok}, ошибок: {fail}.", "Массовый отказ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Выгрузка одной накладной в iiko (для массовых операций). Возвращает true при успехе.</summary>
        private async Task<bool> UploadOneDocumentToIikoAsync(DiadocDocumentRow doc, UtdItemRow[] items, string storeId, bool createWithPosting)
        {
            if (_iikoClient == null || doc == null || items == null || items.Length == 0 ||
                items.Any(it => string.IsNullOrWhiteSpace(it.Product)) ||
                string.IsNullOrWhiteSpace(doc.IikoSupplierId))
                return false;
            try
            {
                var xml = BuildIncomingInvoiceXml(doc, items, doc.IikoSupplierId, storeId, createWithPosting);
                IikoRestoClient.SaveOutgoingXmlForDebug(doc.DocumentNumber ?? "unknown", xml);
                var result = await _iikoClient.ImportIncomingInvoiceAsync(xml).ConfigureAwait(true);
                if (result.Valid == true)
                {
                    doc.IikoInvoice = result.DocumentNumber ?? doc.DocumentNumber;
                    doc.IikoStatus = createWithPosting
                        ? $"Внесено в iiko с проведением (№ {doc.IikoInvoice})"
                        : $"Внесено в iiko без проведения (№ {doc.IikoInvoice})";
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void RestoreIncomingDocumentsView()
        {
            if (_grid == null)
            {
                RefreshIncomingDocumentsAsync();
                return;
            }

            if (_incomingDocuments == null)
            {
                RefreshIncomingDocumentsAsync();
                return;
            }

            _grid.DataSource = _incomingDocuments;
            if (_gridView != null)
                _gridView.BeginDataUpdate();
            try
            {
                if (_gridView != null)
                    _gridView.RefreshData();
                ApplyDocumentsColumns();
                RefreshBatchPanelState();
            }
            finally
            {
                if (_gridView != null)
                    _gridView.EndDataUpdate();
            }
        }

        private async Task ReturnToIncomingDocumentsAsync()
        {
            await RunWithBusyOverlayAsync("Возвращаемся к накладным...", async () =>
            {
                if (_currentDocument != null)
                    _currentDocument.AllItemsMapped = _currentAllItemsMapped;

                SuspendLayout();
                try
                {
                    SetMode(ModeIncoming);
                    RestoreIncomingDocumentsView();
                }
                finally
                {
                    ResumeLayout(true);
                }

                await Task.CompletedTask;
            }).ConfigureAwait(true);
        }

        private async void RefreshIncomingDocumentsAsync()
        {
            await RunWithBusyOverlayAsync("Получаем накладные...", RefreshIncomingDocumentsCoreAsync).ConfigureAwait(true);
        }

        private async Task RefreshIncomingDocumentsCoreAsync()
        {
            if (_client == null) return;
            var boxId = GetSelectedBoxId();
            if (string.IsNullOrEmpty(boxId))
            {
                XtraMessageBox.Show("Выберите юр. лицо.", "Накладные", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

                        // Для каждого входящего номера выбираем статус с максимальным приоритетом:
                        // PROCESSED > NEW/пусто > DELETED. Так "внесено" важнее "удалено".
                        var byIncomingNumber = new Dictionary<string, IncomingInvoiceInfo>(StringComparer.OrdinalIgnoreCase);
                        foreach (var inv in incomingInvoices)
                        {
                            var incomingNum = (inv.IncomingDocumentNumber ?? "").Trim();
                            if (string.IsNullOrEmpty(incomingNum))
                                continue;

                            var status = (inv.Status ?? "").Trim().ToUpperInvariant();
                            int prio = status == "PROCESSED" ? 3 : (status == "DELETED" ? 1 : 2);

                            if (!byIncomingNumber.TryGetValue(incomingNum, out var existing))
                            {
                                byIncomingNumber[incomingNum] = inv;
                                continue;
                            }

                            var existingStatus = (existing.Status ?? "").Trim().ToUpperInvariant();
                            int existingPrio = existingStatus == "PROCESSED" ? 3 : (existingStatus == "DELETED" ? 1 : 2);
                            if (prio > existingPrio)
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

                // Мгновенно выставляем AllItemsMapped по эвристике, без долгого похода за строками УПД:
                // - если поставщика нет → индикатор пустой (null);
                // - если статус накладной говорит, что она уже внесена/удалена в iiko → считаем, что все строки были привязаны (true);
                // - «Не внесено в iiko» → неизвестно (null), чтобы не блокировать массовые кнопки; при выгрузке привязки проверятся.
                foreach (var d in list)
                {
                    if (!d.SupplierFound)
                    {
                        d.AllItemsMapped = null;
                        continue;
                    }

                    var status = (d.IikoStatus ?? "").Trim().ToLowerInvariant();
                    if (status.StartsWith("внесено в iiko") || status.StartsWith("накладная в iiko"))
                    {
                        d.AllItemsMapped = true;
                    }
                    else
                    {
                        d.AllItemsMapped = null;
                    }
                }

                UpdateBusyOverlay("Проверяем привязки и проставляем галочки...");

                // Пересчёт привязок по прайс-листу до отображения грида — галочки и крестики сразу.
                await UpdateAllItemsMappedStatusAsync(boxId, list).ConfigureAwait(true);

                _incomingDocuments = list;
                _grid.DataSource = list;
                ApplyDocumentsColumns();
                RefreshBatchPanelState();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(ex.Message, "Ошибка Диадока", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task UpdateAllItemsMappedStatusAsync(string boxId, List<DiadocDocumentRow> list)
        {
            if (string.IsNullOrWhiteSpace(boxId) || list == null || list.Count == 0)
                return;
            if (_client == null || _iikoClient == null)
                return;

            var runId = ++_mappingStatusRunId;

            await EnsureSuppliersLoadedAsync().ConfigureAwait(true);

            var pricelistCache = new Dictionary<string, List<SupplierPricelistItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var doc in list)
            {
                if (runId != _mappingStatusRunId)
                    return; // запущен новый пересчёт

                if (doc == null)
                    continue;

                // Поставщик не найден — индикатор не нужен.
                if (!doc.SupplierFound)
                {
                    doc.AllItemsMapped = null;
                    continue;
                }

                // Уже посчитано (например, после открытия деталей).
                if (doc.AllItemsMapped.HasValue)
                    continue;

                if (string.IsNullOrWhiteSpace(doc.IikoSupplierId))
                {
                    doc.AllItemsMapped = null;
                    continue;
                }

                try
                {
                    var items = await _client.GetUtdItemsAsync(boxId, doc.MessageId, doc.EntityId).ConfigureAwait(true);
                    if (items == null || items.Length == 0)
                    {
                        doc.AllItemsMapped = false;
                        continue;
                    }

                    var supplierPricelistKey = doc.IikoSupplierId;
                    var sup = _suppliers?.FirstOrDefault(s => s.Id == doc.IikoSupplierId);
                    if (sup != null && !string.IsNullOrWhiteSpace(sup.Code))
                        supplierPricelistKey = sup.Code;

                    var docDate = NormalizeDateForIikoDocument(doc.DocumentDate);
                    var cacheKey = (supplierPricelistKey ?? "") + "|" + (docDate ?? "");
                    if (!pricelistCache.TryGetValue(cacheKey, out var pricelist))
                    {
                        pricelist = await _iikoClient.GetSupplierPricelistAsync(supplierPricelistKey, docDate).ConfigureAwait(true);
                        pricelistCache[cacheKey] = pricelist ?? new List<SupplierPricelistItem>();
                    }
                    if (pricelist == null || pricelist.Count == 0)
                    {
                        doc.AllItemsMapped = false;
                        continue;
                    }

                    var codeToProduct = BuildPricelistCodeMap(pricelist, out var ambiguousCodes);

                    // Те же правила, что и в EnsureMappingsForCurrentDocumentAsync: только по vendorCode.
                    var allMapped = true;
                    foreach (var it in items)
                    {
                        var code = (it.ItemVendorCode ?? "").Trim();
                        if (string.IsNullOrEmpty(code) || ambiguousCodes.Contains(code) || !codeToProduct.TryGetValue(code, out _))
                        {
                            allMapped = false;
                            break;
                        }
                    }

                    doc.AllItemsMapped = allMapped;
                }
                catch
                {
                    // Если не удалось посчитать — оставим "неизвестно", чтобы не вводить в заблуждение крестиком.
                    doc.AllItemsMapped = null;
                }
            }
            if (_gridView != null && _grid != null)
            {
                void RefreshGridAndBatch()
                {
                    _gridView.RefreshData();
                    RefreshBatchPanelState();
                }
                if (_grid.InvokeRequired)
                    _grid.BeginInvoke(new Action(RefreshGridAndBatch));
                else
                    RefreshGridAndBatch();
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
                    if (_gridView.Columns["Status"] != null)
                    {
                        _gridView.Columns["Status"].Visible = true;
                        _gridView.Columns["Status"].Caption = "Статус";
                        _gridView.Columns["Status"].VisibleIndex = 3;
                    }
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
                    _incomingDocuments = list;
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
