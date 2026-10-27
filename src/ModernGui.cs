using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Continuador
{
    internal static class ModernUi
    {
        public static readonly Color Navy = Color.FromArgb(15, 23, 42);
        public static readonly Color NavyLight = Color.FromArgb(30, 41, 59);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color BlueHover = Color.FromArgb(29, 78, 216);
        public static readonly Color Canvas = Color.FromArgb(244, 247, 251);
        public static readonly Color Text = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);

        public static Icon AppIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                return icon ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        public static Button ActionButton(string text, bool primary, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(108, 38);
            button.Padding = new Padding(13, 0, 13, 0);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Cursor = Cursors.Hand;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Segoe UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.BackColor = primary ? Blue : Color.White;
            button.ForeColor = primary ? Color.White : Text;
            button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(203, 213, 225);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = primary ? BlueHover : Color.FromArgb(248, 250, 252);
            button.Click += handler;
            return button;
        }

        public static Button DangerButton(string text, EventHandler handler)
        {
            Button button = ActionButton(text, false, handler);
            button.ForeColor = Red;
            button.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 242, 242);
            return button;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = 42;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(7, 0, 7, 0);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            grid.DefaultCellStyle.Padding = new Padding(7, 4, 7, 4);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 255);
            grid.RowTemplate.Height = 40;
        }

        public static void PrepareDialog(Form form)
        {
            form.Font = new Font("Segoe UI", 9F);
            form.BackColor = Color.White;
            form.Icon = AppIcon();
        }

        public static void StylePrimary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Blue;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.FlatAppearance.BorderColor = Blue;
            button.Cursor = Cursors.Hand;
            button.Height = Math.Max(button.Height, 36);
        }

        public static void StyleSecondary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.White;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            button.Cursor = Cursors.Hand;
            button.Height = Math.Max(button.Height, 36);
        }
    }

    internal sealed class ModernCard : Panel
    {
        public ModernCard()
        {
            BackColor = Color.White;
            Padding = new Padding(1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(ModernUi.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class ModernMainForm : Form
    {
        private readonly DataGridView schedulesGrid;
        private readonly DataGridView conversationGrid;
        private readonly TextBox conversationSearch;
        private readonly ComboBox conversationFilter;
        private readonly Label footerStatus;
        private readonly Label pageTitle;
        private readonly Label pageSubtitle;
        private readonly Label pendingMetric;
        private readonly Label deliveredMetric;
        private readonly Label totalMetric;
        private readonly Label conversationMetric;
        private readonly Panel schedulesPage;
        private readonly Panel conversationsPage;
        private readonly Button schedulesNav;
        private readonly Button conversationsNav;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly System.Windows.Forms.Timer updateTimer;
        private List<ConversationRecord> conversations;
        private int activePage;
        private int conversationLoading;
        private int updateChecking;
        private int updateInstalling;

        public ModernMainForm()
        {
            Text = "Continuador " + AppConstants.Version;
            Icon = ModernUi.AppIcon();
            Font = new Font("Segoe UI", 9F);
            BackColor = ModernUi.Canvas;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 640);
            Size = new Size(1180, 720);
            DoubleBuffered = true;

            Panel sidebar = BuildSidebar(out schedulesNav, out conversationsNav);
            Panel workspace = new Panel { Dock = DockStyle.Fill, BackColor = ModernUi.Canvas };
            Panel header = BuildHeader(out pageTitle, out pageSubtitle);
            footerStatus = BuildFooter();

            Panel pageHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 22), BackColor = ModernUi.Canvas };
            schedulesGrid = BuildSchedulesGrid();
            conversationGrid = BuildConversationGrid();
            schedulesPage = BuildSchedulesPage(out pendingMetric, out deliveredMetric, out totalMetric);
            conversationsPage = BuildConversationsPage(out conversationSearch, out conversationFilter, out conversationMetric);
            pageHost.Controls.Add(conversationsPage);
            pageHost.Controls.Add(schedulesPage);

            workspace.Controls.Add(pageHost);
            workspace.Controls.Add(footerStatus);
            workspace.Controls.Add(header);
            Controls.Add(workspace);
            Controls.Add(sidebar);

            conversations = new List<ConversationRecord>();
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 5000;
            refreshTimer.Tick += delegate
            {
                if (activePage == 0) RefreshGrid();
            };
            refreshTimer.Start();
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 60 * 60 * 1000;
            updateTimer.Tick += delegate { CheckForUpdates(false); };
            updateTimer.Start();
            Shown += delegate
            {
                SetPage(0);
                RefreshConversations();
                CheckForUpdates(false);
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                updateTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel BuildSidebar(out Button schedulesButton, out Button conversationsButton)
        {
            Panel panel = new Panel { Dock = DockStyle.Left, Width = 224, BackColor = ModernUi.Navy };

            PictureBox logo = new PictureBox();
            logo.Image = ModernUi.AppIcon().ToBitmap();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(22, 25, 42, 42);

            Label brand = new Label();
            brand.Text = "Continuador";
            brand.ForeColor = Color.White;
            brand.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            brand.SetBounds(76, 24, 130, 27);

            Label version = new Label();
            version.Text = "AUTOMATIZACIÓN  ·  v" + AppConstants.Version;
            version.ForeColor = Color.FromArgb(148, 163, 184);
            version.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            version.SetBounds(77, 53, 138, 22);

            Label menu = new Label();
            menu.Text = "NAVEGACIÓN";
            menu.ForeColor = Color.FromArgb(100, 116, 139);
            menu.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            menu.SetBounds(22, 107, 170, 20);

            schedulesButton = NavButton("Programaciones", delegate { SetPage(0); });
            schedulesButton.SetBounds(12, 134, 200, 46);
            conversationsButton = NavButton("Conversaciones", delegate { SetPage(1); });
            conversationsButton.SetBounds(12, 184, 200, 46);

            Label tools = new Label();
            tools.Text = "HERRAMIENTAS";
            tools.ForeColor = Color.FromArgb(100, 116, 139);
            tools.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            tools.SetBounds(22, 265, 170, 20);

            Button doctor = NavButton("Diagnóstico", delegate { ShowDoctor(); });
            doctor.SetBounds(12, 292, 200, 42);
            Button logs = NavButton("Abrir registros", delegate { OpenLogs(); });
            logs.SetBounds(12, 338, 200, 42);
            Button updates = NavButton("Buscar actualizaciones", delegate { CheckForUpdates(true); });
            updates.SetBounds(12, 384, 200, 42);

            Panel privacyHost = new Panel();
            privacyHost.Dock = DockStyle.Bottom;
            privacyHost.Height = 110;
            privacyHost.Padding = new Padding(16, 10, 16, 14);
            privacyHost.BackColor = ModernUi.Navy;
            Panel privacy = new Panel();
            privacy.Dock = DockStyle.Fill;
            privacy.BackColor = ModernUi.NavyLight;
            Label privacyTitle = new Label();
            privacyTitle.Text = "●  SERVICIO ACTIVO";
            privacyTitle.ForeColor = Color.FromArgb(74, 222, 128);
            privacyTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            privacyTitle.SetBounds(14, 13, 164, 20);
            Label privacyText = new Label();
            privacyText.Text = "Tus horarios y sesiones\r\nse guardan solo en este PC.";
            privacyText.ForeColor = Color.FromArgb(203, 213, 225);
            privacyText.Font = new Font("Segoe UI", 8F);
            privacyText.SetBounds(14, 38, 165, 38);
            privacy.Controls.Add(privacyTitle);
            privacy.Controls.Add(privacyText);
            privacyHost.Controls.Add(privacy);

            panel.Controls.Add(logo);
            panel.Controls.Add(brand);
            panel.Controls.Add(version);
            panel.Controls.Add(menu);
            panel.Controls.Add(schedulesButton);
            panel.Controls.Add(conversationsButton);
            panel.Controls.Add(tools);
            panel.Controls.Add(doctor);
            panel.Controls.Add(logs);
            panel.Controls.Add(updates);
            panel.Controls.Add(privacyHost);
            return panel;
        }

        private Button NavButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = "   " + text;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ModernUi.NavyLight;
            button.BackColor = ModernUi.Navy;
            button.ForeColor = Color.FromArgb(203, 213, 225);
            button.Font = new Font("Segoe UI", 9.5F);
            button.Cursor = Cursors.Hand;
            button.Click += handler;
            return button;
        }

        private Panel BuildHeader(out Label title, out Label subtitle)
        {
            Panel panel = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.White };
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(ModernUi.Border)) e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            };
            title = new Label();
            title.Text = "Programaciones";
            title.ForeColor = ModernUi.Text;
            title.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            title.SetBounds(25, 17, 500, 34);
            subtitle = new Label();
            subtitle.Text = "Administra las continuaciones pendientes y su estado.";
            subtitle.ForeColor = ModernUi.Muted;
            subtitle.Font = new Font("Segoe UI", 9F);
            subtitle.SetBounds(27, 55, 560, 23);

            Panel activeHost = new Panel();
            activeHost.Dock = DockStyle.Right;
            activeHost.Width = 260;
            activeHost.BackColor = Color.White;
            Label active = new Label();
            active.Text = "●  ACTIVO EN SEGUNDO PLANO";
            active.TextAlign = ContentAlignment.MiddleCenter;
            active.ForeColor = Color.FromArgb(21, 128, 61);
            active.BackColor = Color.FromArgb(240, 253, 244);
            active.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            active.SetBounds(10, 29, 228, 32);
            activeHost.Controls.Add(active);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(activeHost);
            return panel;
        }

        private Label BuildFooter()
        {
            Label label = new Label();
            label.Dock = DockStyle.Bottom;
            label.Height = 34;
            label.Padding = new Padding(25, 8, 0, 0);
            label.BackColor = Color.White;
            label.ForeColor = ModernUi.Muted;
            label.Font = new Font("Segoe UI", 8F);
            label.Text = "Listo.";
            return label;
        }

        private DataGridView BuildSchedulesGrid()
        {
            DataGridView grid = BaseGrid();
            grid.Columns.Add("Id", "ID");
            grid.Columns.Add("Client", "CLIENTE");
            grid.Columns.Add("Due", "FECHA Y HORA LOCAL");
            grid.Columns.Add("Status", "ESTADO");
            grid.Columns.Add("Attempts", "INTENTOS");
            grid.Columns.Add("Session", "SESIÓN");
            grid.Columns.Add("Error", "ÚLTIMO DETALLE");
            grid.Columns[0].FillWeight = 75;
            grid.Columns[1].FillWeight = 55;
            grid.Columns[2].FillWeight = 105;
            grid.Columns[3].FillWeight = 65;
            grid.Columns[4].FillWeight = 45;
            grid.Columns[5].FillWeight = 85;
            grid.Columns[6].FillWeight = 160;
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex != 3 || e.Value == null) return;
                string value = Convert.ToString(e.Value);
                if (value == ScheduleStatuses.Delivered) e.CellStyle.ForeColor = ModernUi.Green;
                else if (value == ScheduleStatuses.Failed || value == ScheduleStatuses.Cancelled) e.CellStyle.ForeColor = ModernUi.Red;
                else e.CellStyle.ForeColor = Color.FromArgb(180, 83, 9);
                e.CellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            };
            return grid;
        }

        private DataGridView BuildConversationGrid()
        {
            DataGridView grid = BaseGrid();
            grid.Columns.Add("Favorite", "★");
            grid.Columns.Add("Client", "CLIENTE");
            grid.Columns.Add("Title", "NOMBRE DEL CHAT");
            grid.Columns.Add("Last", "ÚLTIMA ACTIVIDAD");
            grid.Columns.Add("Active", "ESTADO");
            grid.Columns.Add("Cwd", "CARPETA");
            grid.Columns.Add("Session", "ID DE SESIÓN");
            grid.Columns[0].FillWeight = 25;
            grid.Columns[1].FillWeight = 48;
            grid.Columns[2].FillWeight = 175;
            grid.Columns[3].FillWeight = 90;
            grid.Columns[4].FillWeight = 55;
            grid.Columns[5].FillWeight = 125;
            grid.Columns[6].FillWeight = 105;
            grid.CellDoubleClick += delegate { ScheduleSelectedConversation(); };
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex == 0 && e.Value != null && Convert.ToString(e.Value).Length > 0)
                {
                    e.CellStyle.ForeColor = Color.FromArgb(234, 179, 8);
                    e.CellStyle.Font = new Font("Segoe UI Symbol", 11F);
                }
                if (e.ColumnIndex == 4 && e.Value != null && Convert.ToString(e.Value) == "Abierta")
                {
                    e.CellStyle.ForeColor = ModernUi.Green;
                    e.CellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                }
            };
            return grid;
        }

        private DataGridView BaseGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersVisible = false;
            ModernUi.StyleGrid(grid);
            return grid;
        }

        private Panel BuildSchedulesPage(out Label pending, out Label delivered, out Label total)
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = ModernUi.Canvas };
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard actions = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(14, 12, 14, 8) };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.White };
            buttons.Controls.Add(ModernUi.ActionButton("+  Nueva", true, delegate { NewSchedule(); }));
            buttons.Controls.Add(ModernUi.ActionButton("Cambiar hora", false, delegate { ChangeTime(); }));
            buttons.Controls.Add(ModernUi.ActionButton("Ejecutar ahora", false, delegate { RunSelectedNow(); }));
            buttons.Controls.Add(ModernUi.DangerButton("Cancelar", delegate { CancelSelected(); }));
            buttons.Controls.Add(ModernUi.ActionButton("Actualizar", false, delegate { RefreshGrid(); }));
            actions.Controls.Add(buttons);

            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            metrics.Controls.Add(MetricCard("PENDIENTES", Color.FromArgb(37, 99, 235), out pending), 0, 0);
            metrics.Controls.Add(MetricCard("ENTREGADAS", Color.FromArgb(22, 163, 74), out delivered), 1, 0);
            metrics.Controls.Add(MetricCard("TOTAL REGISTRADAS", Color.FromArgb(124, 58, 237), out total), 2, 0);

            ModernCard tableCard = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(1) };
            Panel caption = GridCaption("Historial de continuaciones", "Selecciona una fila para cambiarla, ejecutarla o cancelarla.");
            tableCard.Controls.Add(schedulesGrid);
            tableCard.Controls.Add(caption);

            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(metrics, 0, 1);
            layout.Controls.Add(tableCard, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        private Panel BuildConversationsPage(out TextBox search, out ComboBox filter, out Label count)
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = ModernUi.Canvas };
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ModernCard controls = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(14, 10, 14, 8) };
            FlowLayoutPanel actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false, BackColor = Color.White };
            actionRow.Controls.Add(ModernUi.ActionButton("+  Programar", true, delegate { ScheduleSelectedConversation(); }));
            actionRow.Controls.Add(ModernUi.ActionButton("Probar envío", false, delegate { TestSelectedConversation(); }));
            actionRow.Controls.Add(ModernUi.ActionButton("★  Favorito", false, delegate { ToggleSelectedFavorite(); }));
            actionRow.Controls.Add(ModernUi.ActionButton("Renombrar", false, delegate { RenameSelectedConversation(); }));
            actionRow.Controls.Add(ModernUi.ActionButton("Actualizar", false, delegate { RefreshConversations(); }));

            FlowLayoutPanel filterRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, WrapContents = false, BackColor = Color.White, Padding = new Padding(0, 7, 0, 0) };
            Label searchLabel = FilterLabel("Buscar");
            search = new TextBox { Width = 260, Height = 28, Font = new Font("Segoe UI", 9F), Margin = new Padding(8, 0, 24, 0) };
            search.TextChanged += delegate { RenderConversations(); };
            Label filterLabel = FilterLabel("Mostrar");
            filter = new ComboBox { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F), Margin = new Padding(8, 0, 0, 0) };
            filter.Items.AddRange(new object[] { "Todas", "Codex", "Claude", "Abiertas", "Favoritas" });
            filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += delegate { RenderConversations(); };
            filterRow.Controls.Add(searchLabel);
            filterRow.Controls.Add(search);
            filterRow.Controls.Add(filterLabel);
            filterRow.Controls.Add(filter);
            controls.Controls.Add(filterRow);
            controls.Controls.Add(actionRow);

            ModernCard tableCard = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(1) };
            Panel caption = GridCaption("Conversaciones detectadas", "Se muestra el mismo nombre del chat que aparece en la barra lateral de Codex o Claude Code.");
            count = new Label();
            count.Text = "0 conversaciones";
            count.Dock = DockStyle.Right;
            count.Width = 230;
            count.Padding = new Padding(0, 0, 16, 0);
            count.TextAlign = ContentAlignment.MiddleRight;
            count.ForeColor = ModernUi.Muted;
            count.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            caption.Controls.Add(count);
            tableCard.Controls.Add(conversationGrid);
            tableCard.Controls.Add(caption);

            layout.Controls.Add(controls, 0, 0);
            layout.Controls.Add(tableCard, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private Panel MetricCard(string labelText, Color accent, out Label value)
        {
            ModernCard card = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(16, 10, 12, 8) };
            Panel marker = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
            Label label = new Label { Text = labelText, ForeColor = ModernUi.Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold), AutoSize = false };
            label.SetBounds(22, 14, 190, 18);
            value = new Label { Text = "0", ForeColor = ModernUi.Text, Font = new Font("Segoe UI", 18F, FontStyle.Bold), AutoSize = false };
            value.SetBounds(21, 33, 190, 37);
            card.Controls.Add(value);
            card.Controls.Add(label);
            card.Controls.Add(marker);
            return card;
        }

        private Panel GridCaption(string title, string subtitle)
        {
            Panel panel = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.White };
            Label titleLabel = new Label { Text = title, ForeColor = ModernUi.Text, Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true, Location = new Point(16, 10) };
            Label subtitleLabel = new Label { Text = subtitle, ForeColor = ModernUi.Muted, Font = new Font("Segoe UI", 8F), AutoSize = true, Location = new Point(16, 32) };
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(subtitleLabel);
            return panel;
        }

        private Label FilterLabel(string text)
        {
            return new Label { Text = text, ForeColor = ModernUi.Muted, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), AutoSize = false, Width = 55, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
        }

        private void SetPage(int page)
        {
            activePage = page;
            bool schedules = page == 0;
            schedulesPage.Visible = schedules;
            conversationsPage.Visible = !schedules;
            if (schedules) schedulesPage.BringToFront(); else conversationsPage.BringToFront();
            SetNavActive(schedulesNav, schedules);
            SetNavActive(conversationsNav, !schedules);
            pageTitle.Text = schedules ? "Programaciones" : "Conversaciones";
            pageSubtitle.Text = schedules
                ? "Administra las continuaciones pendientes y revisa cada entrega."
                : "Elige exactamente el chat de Codex o Claude Code que quieres reanudar.";
            if (schedules) RefreshGrid(); else RefreshConversations();
        }

        private void SetNavActive(Button button, bool active)
        {
            button.BackColor = active ? ModernUi.Blue : ModernUi.Navy;
            button.ForeColor = active ? Color.White : Color.FromArgb(203, 213, 225);
            button.Font = new Font("Segoe UI", 9.5F, active ? FontStyle.Bold : FontStyle.Regular);
        }

        private void RefreshGrid()
        {
            try
            {
                List<ScheduleRecord> items = ScheduleManager.List();
                string selected = SelectedId();
                schedulesGrid.Rows.Clear();
                foreach (ScheduleRecord item in items)
                {
                    int index = schedulesGrid.Rows.Add(
                        item.Id.Substring(0, Math.Min(10, item.Id.Length)),
                        item.Client == "claude" ? "Claude" : "Codex",
                        TimeUtil.LocalDisplay(item.Due), item.Status, item.Attempts.ToString(),
                        Short(item.SessionId, 16), Short(item.LastError, 90));
                    schedulesGrid.Rows[index].Tag = item.Id;
                    if (item.Id == selected) schedulesGrid.Rows[index].Selected = true;
                }
                int pending = items.Count(delegate(ScheduleRecord item) { return item.Status == ScheduleStatuses.Pending || item.Status == ScheduleStatuses.Running; });
                int delivered = items.Count(delegate(ScheduleRecord item) { return item.Status == ScheduleStatuses.Delivered; });
                pendingMetric.Text = pending.ToString();
                deliveredMetric.Text = delivered.ToString();
                totalMetric.Text = items.Count.ToString();
                if (activePage == 0) footerStatus.Text = "Actualizado ahora  •  " + items.Count + " programación(es)  •  Datos locales: " + StateStore.DataDirectory;
            }
            catch (Exception ex)
            {
                footerStatus.Text = "No se pudo actualizar: " + ex.Message;
            }
        }

        private void NewSchedule()
        {
            using (ScheduleDialog dialog = new ScheduleDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshGrid();
                    footerStatus.Text = "Programación creada correctamente.";
                }
            }
        }

        private void RefreshConversations()
        {
            if (Interlocked.CompareExchange(ref conversationLoading, 1, 0) != 0)
            {
                if (activePage == 1) footerStatus.Text = "Ya se están cargando las conversaciones...";
                return;
            }
            conversationMetric.Text = "Cargando...";
            conversationGrid.Enabled = false;
            if (activePage == 1) footerStatus.Text = "Leyendo conversaciones en segundo plano...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ConversationRecord> loaded = null;
                Exception failure = null;
                try { loaded = ConversationCatalog.ListAll(); }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            conversationGrid.Enabled = true;
                            if (failure != null)
                            {
                                conversationMetric.Text = "Error al cargar";
                                footerStatus.Text = "No se pudieron leer las conversaciones: " + failure.Message;
                                return;
                            }
                            conversations = loaded ?? new List<ConversationRecord>();
                            RenderConversations();
                        }
                        finally { Interlocked.Exchange(ref conversationLoading, 0); }
                    });
                }
                catch { Interlocked.Exchange(ref conversationLoading, 0); }
            });
        }

        private void RenderConversations()
        {
            if (conversationGrid == null || conversationFilter == null || conversationSearch == null) return;
            ConversationRecord selected = SelectedConversation();
            string selectedKey = selected == null ? null : selected.Client + "|" + selected.SessionId;
            IEnumerable<ConversationRecord> filtered = conversations ?? new List<ConversationRecord>();
            string filter = Convert.ToString(conversationFilter.SelectedItem) ?? "Todas";
            if (filter == "Codex") filtered = filtered.Where(delegate(ConversationRecord item) { return item.Client == "codex"; });
            else if (filter == "Claude") filtered = filtered.Where(delegate(ConversationRecord item) { return item.Client == "claude"; });
            else if (filter == "Abiertas") filtered = filtered.Where(delegate(ConversationRecord item) { return item.IsActive; });
            else if (filter == "Favoritas") filtered = filtered.Where(delegate(ConversationRecord item) { return item.Favorite; });
            string query = conversationSearch.Text.Trim();
            if (query.Length > 0)
            {
                filtered = filtered.Where(delegate(ConversationRecord item)
                {
                    return Contains(item.DisplayTitle, query) || Contains(item.WorkingDirectory, query) || Contains(item.SessionId, query) || Contains(item.Client, query);
                });
            }
            List<ConversationRecord> visible = filtered.OrderByDescending(delegate(ConversationRecord item) { return item.Favorite; })
                .ThenByDescending(delegate(ConversationRecord item) { return item.LastActivityUtc; }).ToList();
            List<ConversationRecord> displayed = visible.Take(1000).ToList();
            conversationGrid.SuspendLayout();
            conversationGrid.Rows.Clear();
            foreach (ConversationRecord item in displayed)
            {
                int index = conversationGrid.Rows.Add(item.Favorite ? "★" : string.Empty,
                    item.Client == "claude" ? "Claude" : "Codex", item.DisplayTitle,
                    item.LastActivityUtc.ToLocalTime().ToString("dd/MM/yyyy  HH:mm"), item.IsActive ? "Abierta" : "Guardada",
                    Short(item.WorkingDirectory, 70), Short(item.SessionId, 28));
                conversationGrid.Rows[index].Tag = item;
                if (selectedKey == item.Client + "|" + item.SessionId) conversationGrid.Rows[index].Selected = true;
            }
            conversationGrid.ResumeLayout();
            int codex = conversations.Count(delegate(ConversationRecord item) { return item.Client == "codex"; });
            int claude = conversations.Count(delegate(ConversationRecord item) { return item.Client == "claude"; });
            conversationMetric.Text = visible.Count > displayed.Count
                ? displayed.Count + " mostradas de " + visible.Count + " filtradas"
                : visible.Count + " visibles de " + conversations.Count;
            if (activePage == 1) footerStatus.Text = "Codex: " + codex + "  •  Claude Code: " + claude + "  •  Doble clic para programar";
        }

        private ConversationRecord SelectedConversation()
        {
            if (conversationGrid.SelectedRows.Count == 0) return null;
            return conversationGrid.SelectedRows[0].Tag as ConversationRecord;
        }

        private void ScheduleSelectedConversation()
        {
            ConversationRecord selected = SelectedConversation();
            if (selected == null)
            {
                MessageBox.Show(this, "Selecciona una conversación de Codex o Claude Code.", "Continuador", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (ScheduleDialog dialog = new ScheduleDialog(selected))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetPage(0);
                    footerStatus.Text = "Programada en " + selected.Client + ": " + selected.DisplayTitle;
                }
            }
        }

        private void ToggleSelectedFavorite()
        {
            ConversationRecord selected = SelectedConversation();
            if (selected == null) return;
            ConversationPreferences.ToggleFavorite(selected);
            RenderConversations();
        }

        private void RenameSelectedConversation()
        {
            ConversationRecord selected = SelectedConversation();
            if (selected == null) return;
            using (TextPromptDialog dialog = new TextPromptDialog("Renombrar conversación", "Nombre personalizado (vacío restaura el original):", selected.CustomName ?? selected.Title))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string name = string.Equals(dialog.Value.Trim(), selected.Title, StringComparison.Ordinal) ? null : dialog.Value;
                    ConversationPreferences.SetCustomName(selected, name);
                    RenderConversations();
                }
            }
        }

        private void TestSelectedConversation()
        {
            ConversationRecord selected = SelectedConversation();
            if (selected == null)
            {
                MessageBox.Show(this, "Selecciona una conversación.", "Continuador", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Se enviará un mensaje de prueba visible a:\r\n\r\n" + selected.DisplayTitle + "\r\n\r\n¿Continuar?", "Probar envío", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ScheduleRecord test = ScheduleManager.Create(selected.Client, DateTimeOffset.Now,
                "Prueba de Continuador: responde solamente CONEXIÓN OK.", selected.SessionId, selected.WorkingDirectory,
                selected.Client == "claude" ? NativeMethods.FindClaudeWindow(selected.WorkingDirectory) : new WindowSnapshot());
            footerStatus.Text = "Probando envío en segundo plano...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                ScheduleManager.ProcessDue(null);
                ScheduleRecord result = ScheduleManager.List().FirstOrDefault(delegate(ScheduleRecord item) { return item.Id == test.Id; });
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        RefreshGrid();
                        string detail = result == null ? "Sin resultado." : result.Status + (string.IsNullOrWhiteSpace(result.LastError) ? string.Empty : "\r\n" + result.LastError);
                        MessageBox.Show(this, detail, "Resultado de la prueba", MessageBoxButtons.OK,
                            result != null && result.Status == ScheduleStatuses.Delivered ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    });
                }
                catch { }
            });
        }

        private void ChangeTime()
        {
            string id = SelectedId();
            if (id == null)
            {
                MessageBox.Show(this, "Selecciona una programación.", "Continuador", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (TimeDialog dialog = new TimeDialog("Cambiar hora"))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try { ScheduleManager.Update(id, dialog.SelectedTime); RefreshGrid(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }

        private void CancelSelected()
        {
            string id = SelectedId();
            if (id == null) return;
            if (MessageBox.Show(this, "¿Cancelar esta continuación?", "Continuador", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { ScheduleManager.Cancel(id); RefreshGrid(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RunSelectedNow()
        {
            string id = SelectedId();
            if (id == null) return;
            try { ScheduleManager.RunNow(id); footerStatus.Text = "Se ejecutará en el próximo ciclo."; RefreshGrid(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ShowDoctor()
        {
            MessageBox.Show(this, DoctorService.BuildText(), "Diagnóstico de Continuador", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenLogs()
        {
            try
            {
                Directory.CreateDirectory(StateStore.DataDirectory);
                if (!File.Exists(StateStore.TextLogPath)) File.WriteAllText(StateStore.TextLogPath, "Continuador " + AppConstants.Version + Environment.NewLine);
                Process.Start("notepad.exe", ProcessRunner.QuoteArgument(StateStore.TextLogPath));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void CheckForUpdates(bool manual)
        {
            if (Interlocked.CompareExchange(ref updateChecking, 1, 0) != 0)
            {
                if (manual) footerStatus.Text = "Ya se está buscando una actualización...";
                return;
            }
            if (manual) footerStatus.Text = "Buscando actualizaciones en GitHub...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo update = null;
                Exception failure = null;
                try { update = UpdateService.Check(); }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Interlocked.Exchange(ref updateChecking, 0);
                        if (failure != null)
                        {
                            footerStatus.Text = "No se pudo comprobar la actualización.";
                            if (manual) MessageBox.Show(this, failure.Message, "Actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        if (update == null || !update.IsNewer)
                        {
                            footerStatus.Text = "Continuador " + AppConstants.Version + " está actualizado.";
                            if (manual) MessageBox.Show(this, "Ya tienes la versión más reciente: " + AppConstants.Version + ".", "Actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        footerStatus.Text = "Nueva versión disponible: " + update.Version;
                        if (!manual && !UpdateService.ShouldNotify(update.Version)) return;
                        if (MessageBox.Show(this,
                            "Hay una nueva versión de Continuador: " + update.Version + ".\r\n\r\n¿Quieres descargarla e instalarla ahora?",
                            "Actualización disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        {
                            DownloadAndInstallUpdate(update);
                        }
                    });
                }
                catch { Interlocked.Exchange(ref updateChecking, 0); }
            });
        }

        private void DownloadAndInstallUpdate(UpdateInfo update)
        {
            if (Interlocked.CompareExchange(ref updateInstalling, 1, 0) != 0) return;
            footerStatus.Text = "Descargando Continuador " + update.Version + "...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string installer = null;
                Exception failure = null;
                try { installer = UpdateService.DownloadInstaller(update); }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Interlocked.Exchange(ref updateInstalling, 0);
                        if (failure != null)
                        {
                            footerStatus.Text = "No se pudo descargar la actualización.";
                            MessageBox.Show(this, failure.Message, "Actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        footerStatus.Text = "Instalando la actualización...";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = installer,
                            Arguments = "/install-silent",
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(installer)
                        });
                    });
                }
                catch { Interlocked.Exchange(ref updateInstalling, 0); }
            });
        }

        private string SelectedId()
        {
            if (schedulesGrid.SelectedRows.Count == 0) return null;
            return schedulesGrid.SelectedRows[0].Tag as string;
        }

        private static string Short(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= length ? value : value.Substring(0, length) + "...";
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
}
