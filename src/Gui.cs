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
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon notifyIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Control dispatcher;
        private int processing;

        public TrayContext()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Abrir Continuador", null, delegate { OpenWindow(); });
            menu.Items.Add("Procesar ahora", null, delegate { ProcessNow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, delegate { ExitThread(); });

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = ModernUi.AppIcon();
            notifyIcon.Text = "Continuador";
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { OpenWindow(); };

            dispatcher = new Control();
            dispatcher.CreateControl();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15000;
            timer.Tick += delegate { ProcessNow(); };
            timer.Start();
            ProcessNow();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            dispatcher.Dispose();
            base.ExitThreadCore();
        }

        private void OpenWindow()
        {
            ModernMainForm form = new ModernMainForm();
            form.Show();
            form.Activate();
        }

        private void ProcessNow()
        {
            if (Interlocked.CompareExchange(ref processing, 1, 0) != 0)
            {
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ScheduleManager.ProcessDue(delegate(string title, string message)
                    {
                        try
                        {
                            dispatcher.BeginInvoke((MethodInvoker)delegate
                            {
                                notifyIcon.BalloonTipTitle = title;
                                notifyIcon.BalloonTipText = message.Length > 240 ? message.Substring(0, 240) : message;
                                notifyIcon.ShowBalloonTip(5000);
                            });
                        }
                        catch
                        {
                        }
                    });
                }
                finally
                {
                    Interlocked.Exchange(ref processing, 0);
                }
            });
        }
    }

    internal sealed class LegacyMainForm : Form
    {
        private readonly DataGridView grid;
        private readonly DataGridView conversationGrid;
        private readonly TabControl tabs;
        private readonly ToolStripTextBox conversationSearch;
        private readonly ToolStripComboBox conversationFilter;
        private readonly Label status;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private List<ConversationRecord> conversations;

        public LegacyMainForm()
        {
            Text = "Continuador " + AppConstants.Version;
            Icon = SystemIcons.Application;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 520);
            Size = new Size(1000, 600);

            ToolStrip tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.Items.Add(Button("Nueva", delegate { NewSchedule(); }));
            tools.Items.Add(Button("Cambiar hora", delegate { ChangeTime(); }));
            tools.Items.Add(Button("Cancelar", delegate { CancelSelected(); }));
            tools.Items.Add(Button("Ejecutar ahora", delegate { RunSelectedNow(); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("Actualizar", delegate { RefreshGrid(); }));
            tools.Items.Add(Button("Diagnóstico", delegate { ShowDoctor(); }));
            tools.Items.Add(Button("Abrir registros", delegate { OpenLogs(); }));
            tools.Dock = DockStyle.Top;

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersVisible = false;
            grid.Columns.Add("Id", "ID");
            grid.Columns.Add("Client", "Cliente");
            grid.Columns.Add("Due", "Fecha y hora local");
            grid.Columns.Add("Status", "Estado");
            grid.Columns.Add("Attempts", "Intentos");
            grid.Columns.Add("Session", "Sesión");
            grid.Columns.Add("Error", "Último detalle");
            grid.Columns[0].FillWeight = 90;
            grid.Columns[6].FillWeight = 180;

            conversationGrid = new DataGridView();
            conversationGrid.Dock = DockStyle.Fill;
            conversationGrid.ReadOnly = true;
            conversationGrid.AllowUserToAddRows = false;
            conversationGrid.AllowUserToDeleteRows = false;
            conversationGrid.MultiSelect = false;
            conversationGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            conversationGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            conversationGrid.RowHeadersVisible = false;
            conversationGrid.Columns.Add("Favorite", "★");
            conversationGrid.Columns.Add("Client", "Cliente");
            conversationGrid.Columns.Add("Title", "Nombre del chat");
            conversationGrid.Columns.Add("Last", "Última actividad");
            conversationGrid.Columns.Add("Active", "Estado");
            conversationGrid.Columns.Add("Cwd", "Carpeta");
            conversationGrid.Columns.Add("Session", "ID de sesión");
            conversationGrid.Columns[0].FillWeight = 25;
            conversationGrid.Columns[1].FillWeight = 55;
            conversationGrid.Columns[2].FillWeight = 175;
            conversationGrid.Columns[3].FillWeight = 90;
            conversationGrid.Columns[4].FillWeight = 55;
            conversationGrid.Columns[5].FillWeight = 125;
            conversationGrid.Columns[6].FillWeight = 105;
            conversationGrid.CellDoubleClick += delegate { ScheduleSelectedConversation(); };

            ToolStrip conversationTools = new ToolStrip();
            conversationTools.GripStyle = ToolStripGripStyle.Hidden;
            conversationTools.Items.Add(Button("Programar en seleccionada", delegate { ScheduleSelectedConversation(); }));
            conversationTools.Items.Add(Button("Probar envío", delegate { TestSelectedConversation(); }));
            conversationTools.Items.Add(Button("★ Favorito", delegate { ToggleSelectedFavorite(); }));
            conversationTools.Items.Add(Button("Renombrar", delegate { RenameSelectedConversation(); }));
            conversationTools.Items.Add(Button("Actualizar conversaciones", delegate { RefreshConversations(); }));
            conversationTools.Items.Add(new ToolStripSeparator());
            conversationTools.Items.Add(new ToolStripLabel("Filtro:"));
            conversationFilter = new ToolStripComboBox();
            conversationFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            conversationFilter.Items.AddRange(new object[] { "Todas", "Codex", "Claude", "Abiertas", "Favoritas" });
            conversationFilter.SelectedIndex = 0;
            conversationFilter.SelectedIndexChanged += delegate { RenderConversations(); };
            conversationTools.Items.Add(conversationFilter);
            conversationTools.Items.Add(new ToolStripLabel("Buscar:"));
            conversationSearch = new ToolStripTextBox();
            conversationSearch.AutoSize = false;
            conversationSearch.Width = 180;
            conversationSearch.TextChanged += delegate { RenderConversations(); };
            conversationTools.Items.Add(conversationSearch);
            conversationTools.Dock = DockStyle.Top;
            conversations = new List<ConversationRecord>();

            TabPage schedulesPage = new TabPage("Programaciones");
            schedulesPage.Controls.Add(grid);
            TabPage conversationsPage = new TabPage("Conversaciones");
            conversationsPage.Controls.Add(conversationGrid);
            conversationsPage.Controls.Add(conversationTools);
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(schedulesPage);
            tabs.TabPages.Add(conversationsPage);
            tabs.SelectedIndexChanged += delegate
            {
                if (tabs.SelectedIndex == 1) RefreshConversations();
                else RefreshGrid();
            };

            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = 28;
            status.Padding = new Padding(8, 6, 0, 0);
            status.Text = "Listo.";

            Controls.Add(tabs);
            Controls.Add(status);
            Controls.Add(tools);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 5000;
            refreshTimer.Tick += delegate { RefreshGrid(); };
            refreshTimer.Start();
            Shown += delegate
            {
                RefreshGrid();
                RefreshConversations();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private ToolStripButton Button(string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += handler;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            return button;
        }

        private void RefreshGrid()
        {
            try
            {
                List<ScheduleRecord> items = ScheduleManager.List();
                string selected = SelectedId();
                grid.Rows.Clear();
                foreach (ScheduleRecord item in items)
                {
                    int index = grid.Rows.Add(
                        item.Id.Substring(0, Math.Min(12, item.Id.Length)),
                        item.Client,
                        TimeUtil.LocalDisplay(item.Due),
                        item.Status,
                        item.Attempts.ToString(),
                        Short(item.SessionId, 16),
                        Short(item.LastError, 80));
                    grid.Rows[index].Tag = item.Id;
                    if (item.Id == selected)
                    {
                        grid.Rows[index].Selected = true;
                    }
                }
                if (tabs.SelectedIndex == 0)
                {
                    status.Text = items.Count + " programación(es). Datos: " + StateStore.DataDirectory;
                }
            }
            catch (Exception ex)
            {
                status.Text = "Error: " + ex.Message;
            }
        }

        private void NewSchedule()
        {
            using (ScheduleDialog dialog = new ScheduleDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshGrid();
                    status.Text = "Programada: " + dialog.CreatedRecord.Id;
                }
            }
        }

        private void RefreshConversations()
        {
            try
            {
                conversations = ConversationCatalog.ListAll();
                RenderConversations();
            }
            catch (Exception ex)
            {
                status.Text = "No se pudieron leer las conversaciones: " + ex.Message;
            }
        }

        private void RenderConversations()
        {
            if (conversationGrid == null || conversationFilter == null || conversationSearch == null)
            {
                return;
            }
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
                    return Contains(item.DisplayTitle, query) || Contains(item.WorkingDirectory, query) ||
                           Contains(item.SessionId, query) || Contains(item.Client, query);
                });
            }
            List<ConversationRecord> visible = filtered
                .OrderByDescending(delegate(ConversationRecord item) { return item.Favorite; })
                .ThenByDescending(delegate(ConversationRecord item) { return item.LastActivityUtc; }).ToList();
            conversationGrid.Rows.Clear();
            foreach (ConversationRecord item in visible)
            {
                int index = conversationGrid.Rows.Add(
                    item.Favorite ? "★" : string.Empty,
                    item.Client,
                    item.DisplayTitle,
                    item.LastActivityUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    item.IsActive ? "Abierta" : "Guardada",
                    Short(item.WorkingDirectory, 70),
                    Short(item.SessionId, 28));
                conversationGrid.Rows[index].Tag = item;
                if (selectedKey == item.Client + "|" + item.SessionId)
                {
                    conversationGrid.Rows[index].Selected = true;
                }
            }
            if (tabs != null && tabs.SelectedIndex == 1)
            {
                int codex = conversations.Count(delegate(ConversationRecord item) { return item.Client == "codex"; });
                int claude = conversations.Count(delegate(ConversationRecord item) { return item.Client == "claude"; });
                status.Text = visible.Count + " visible(s) de " + conversations.Count + ". Codex: " + codex + ", Claude: " + claude + ".";
            }
        }

        private ConversationRecord SelectedConversation()
        {
            if (conversationGrid.SelectedRows.Count == 0)
            {
                return null;
            }
            return conversationGrid.SelectedRows[0].Tag as ConversationRecord;
        }

        private void ScheduleSelectedConversation()
        {
            ConversationRecord selected = SelectedConversation();
            if (selected == null)
            {
                MessageBox.Show(this, "Selecciona una conversación de Codex o Claude Code.", "Continuador",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (ScheduleDialog dialog = new ScheduleDialog(selected))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    tabs.SelectedIndex = 0;
                    RefreshGrid();
                    status.Text = "Programada en " + selected.Client + ": " + selected.DisplayTitle;
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
            using (TextPromptDialog dialog = new TextPromptDialog("Renombrar conversación",
                "Nombre personalizado (vacío restaura el original):", selected.CustomName ?? selected.Title))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string name = string.Equals(dialog.Value.Trim(), selected.Title, StringComparison.Ordinal)
                        ? null : dialog.Value;
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
            if (MessageBox.Show(this,
                "Se enviará un mensaje de prueba visible a:\r\n\r\n" + selected.DisplayTitle +
                "\r\n\r\n¿Continuar?", "Probar envío", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            ScheduleRecord test = ScheduleManager.Create(selected.Client, DateTimeOffset.Now,
                "Prueba de Continuador: responde solamente CONEXIÓN OK.", selected.SessionId,
                selected.WorkingDirectory, new WindowSnapshot());
            status.Text = "Probando envío en segundo plano...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                ScheduleManager.ProcessDue(null);
                ScheduleRecord result = ScheduleManager.List().FirstOrDefault(delegate(ScheduleRecord item) { return item.Id == test.Id; });
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        RefreshGrid();
                        string detail = result == null ? "Sin resultado." : result.Status +
                            (string.IsNullOrWhiteSpace(result.LastError) ? string.Empty : "\r\n" + result.LastError);
                        MessageBox.Show(this, detail, "Resultado de la prueba", MessageBoxButtons.OK,
                            result != null && result.Status == ScheduleStatuses.Delivered ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    });
                }
                catch
                {
                }
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
                    try
                    {
                        ScheduleManager.Update(id, dialog.SelectedTime);
                        RefreshGrid();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void CancelSelected()
        {
            string id = SelectedId();
            if (id == null)
            {
                return;
            }
            if (MessageBox.Show(this, "¿Cancelar esta continuación?", "Continuador", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                ScheduleManager.Cancel(id);
                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RunSelectedNow()
        {
            string id = SelectedId();
            if (id == null)
            {
                return;
            }
            try
            {
                ScheduleManager.RunNow(id);
                status.Text = "Se ejecutará en el próximo ciclo.";
                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowDoctor()
        {
            MessageBox.Show(this, DoctorService.BuildText(), "Diagnóstico", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenLogs()
        {
            try
            {
                Directory.CreateDirectory(StateStore.DataDirectory);
                if (!File.Exists(StateStore.TextLogPath))
                {
                    File.WriteAllText(StateStore.TextLogPath, "Continuador " + AppConstants.Version + Environment.NewLine);
                }
                Process.Start("notepad.exe", ProcessRunner.QuoteArgument(StateStore.TextLogPath));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string SelectedId()
        {
            if (grid.SelectedRows.Count == 0)
            {
                return null;
            }
            return grid.SelectedRows[0].Tag as string;
        }

        private static string Short(string value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Length <= length ? value : value.Substring(0, length) + "...";
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }

    internal sealed class TextPromptDialog : Form
    {
        private readonly TextBox input;

        public string Value { get { return input.Text; } }

        public TextPromptDialog(string title, string label, string value)
        {
            Text = title;
            ModernUi.PrepareDialog(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 145);
            Label prompt = new Label { Text = label, Left = 15, Top = 15, Width = 485, Height = 22 };
            input = new TextBox { Left = 15, Top = 42, Width = 485, Text = value ?? string.Empty };
            Button ok = new Button { Text = "Aceptar", Left = 330, Top = 90, Width = 80, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Cancelar", Left = 420, Top = 90, Width = 80, DialogResult = DialogResult.Cancel };
            ModernUi.StylePrimary(ok);
            ModernUi.StyleSecondary(cancel);
            Controls.Add(prompt);
            Controls.Add(input);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal sealed class ScheduleDialog : Form
    {
        private readonly ComboBox client;
        private readonly DateTimePicker date;
        private readonly DateTimePicker time;
        private readonly TextBox message;
        private readonly TextBox session;
        private readonly TextBox cwd;

        public ScheduleRecord CreatedRecord { get; private set; }

        public ScheduleDialog() : this(null)
        {
        }

        public ScheduleDialog(ConversationRecord selectedConversation)
        {
            Text = "Nueva continuación";
            ModernUi.PrepareDialog(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 340);

            client = new ComboBox();
            client.DropDownStyle = ComboBoxStyle.DropDownList;
            client.Items.AddRange(new object[] { "codex", "claude" });
            client.SelectedIndex = 0;

            date = new DateTimePicker();
            date.Format = DateTimePickerFormat.Short;
            date.Value = DateTime.Today;

            time = new DateTimePicker();
            time.Format = DateTimePickerFormat.Time;
            time.ShowUpDown = true;
            time.Value = DateTime.Now.AddHours(1);

            message = new TextBox();
            message.Text = AppConstants.DefaultMessage;
            session = new TextBox();
            cwd = new TextBox();
            cwd.Text = Environment.CurrentDirectory;

            if (selectedConversation != null)
            {
                client.SelectedItem = selectedConversation.Client;
                client.Enabled = false;
                session.Text = selectedConversation.SessionId;
                session.ReadOnly = true;
                cwd.Text = selectedConversation.WorkingDirectory;
                Text = "Programar en: " + selectedConversation.Title;
            }

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12);
            layout.ColumnCount = 2;
            layout.RowCount = 7;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(layout, 0, "Cliente", client);
            AddRow(layout, 1, "Fecha", date);
            AddRow(layout, 2, "Hora", time);
            AddRow(layout, 3, "Mensaje", message);
            AddRow(layout, 4, "ID de sesión (opcional)", session);
            AddRow(layout, 5, "Carpeta de trabajo", cwd);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;
            Button ok = new Button();
            ok.Text = "Programar";
            ok.AutoSize = true;
            ok.Click += delegate { Save(); };
            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.AutoSize = true;
            cancel.DialogResult = DialogResult.Cancel;
            ModernUi.StylePrimary(ok);
            ModernUi.StyleSecondary(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 1, 6);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Save()
        {
            try
            {
                DateTime local = date.Value.Date.Add(time.Value.TimeOfDay);
                DateTimeOffset due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                string sessionId = session.Text.Trim();
                if (client.Text == "codex" && sessionId.Length == 0)
                {
                    CodexDetectionResult detection = CodexSessionDetector.Detect(cwd.Text);
                    if (string.IsNullOrEmpty(detection.SessionId))
                    {
                        string detail = detection.Error;
                        if (detection.Candidates.Count > 0)
                        {
                            detail += Environment.NewLine + string.Join(Environment.NewLine,
                                detection.Candidates.Select(delegate(CodexSessionCandidate item) { return item.Id; }).ToArray());
                        }
                        throw new InvalidOperationException(detail);
                    }
                    sessionId = detection.SessionId;
                }
                if (client.Text == "claude" && sessionId.Length == 0)
                {
                    throw new InvalidOperationException("Para programar manualmente Claude Code, escribe el ID de la sesión. Desde el chat se completa automáticamente.");
                }
                CreatedRecord = ScheduleManager.Create(client.Text, due, message.Text, sessionId, cwd.Text, new WindowSnapshot());
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "No se pudo programar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Label text = new Label();
            text.Text = label;
            text.Dock = DockStyle.Fill;
            text.TextAlign = ContentAlignment.MiddleLeft;
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(text, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }

    internal sealed class TimeDialog : Form
    {
        private readonly DateTimePicker date;
        private readonly DateTimePicker time;

        public DateTimeOffset SelectedTime
        {
            get
            {
                DateTime local = date.Value.Date.Add(time.Value.TimeOfDay);
                return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }
        }

        public TimeDialog(string title)
        {
            Text = title;
            ModernUi.PrepareDialog(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(360, 145);
            MaximizeBox = false;
            MinimizeBox = false;

            date = new DateTimePicker();
            date.Format = DateTimePickerFormat.Short;
            date.Value = DateTime.Today;
            date.Location = new Point(20, 20);
            date.Width = 145;
            time = new DateTimePicker();
            time.Format = DateTimePickerFormat.Time;
            time.ShowUpDown = true;
            time.Value = DateTime.Now.AddHours(1);
            time.Location = new Point(185, 20);
            time.Width = 145;

            Button ok = new Button();
            ok.Text = "Aceptar";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(174, 83);
            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(255, 83);
            ModernUi.StylePrimary(ok);
            ModernUi.StyleSecondary(cancel);
            Controls.Add(date);
            Controls.Add(time);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
