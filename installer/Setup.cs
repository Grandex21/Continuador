using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador de Continuador")]
[assembly: AssemblyDescription("Instala Continuador para Codex y Claude Code")]
[assembly: AssemblyCompany("Continuador")]
[assembly: AssemblyProduct("Continuador")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

namespace ContinuadorInstaller
{
    [Serializable]
    public sealed class InstallState
    {
        public string InstallDirectory { get; set; }
        public string CodexSkill { get; set; }
        public string CodexBackup { get; set; }
        public string ClaudeSkill { get; set; }
        public string ClaudeBackup { get; set; }
        public string StartMenuDirectory { get; set; }
        public string TaskName { get; set; }
    }

    internal static class SetupProgram
    {
        private const string ProductName = "Continuador";
        private const string Version = "1.4.0";
        private const string ScheduledTaskName = "Continuador - Procesar pendientes";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        internal static string ProductVersion { get { return Version; } }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "/install-silent", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Install();
                    return 0;
                }
                catch
                {
                    return 2;
                }
            }
            bool silentUninstall = args.Length > 0 &&
                string.Equals(args[0], "/uninstall-silent", StringComparison.OrdinalIgnoreCase);
            if (silentUninstall || (args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                if (!silentUninstall)
                {
                    Application.EnableVisualStyles();
                    DialogResult answer = MessageBox.Show(
                        "Se eliminará Continuador y sus integraciones. Los registros locales se conservarán.",
                        "Desinstalar Continuador", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (answer != DialogResult.OK)
                    {
                        return 1;
                    }
                }
                try
                {
                    Uninstall();
                    if (!silentUninstall)
                    {
                        MessageBox.Show("Continuador se desinstaló correctamente.", "Continuador",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    if (!silentUninstall)
                    {
                        MessageBox.Show("No se pudo desinstalar: " + ex.Message, "Continuador",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return 2;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }

        public static string Install()
        {
            string user = GetUserRoot();
            string installDirectory = Path.Combine(user, "AppData", "Local", "Programs", ProductName);
            string applicationPath = Path.Combine(installDirectory, "Continuador.exe");
            string workerPath = Path.Combine(installDirectory, "ContinuadorWorker.exe");
            string uninstallerPath = Path.Combine(installDirectory, "Desinstalar.exe");
            string statePath = Path.Combine(installDirectory, "install-state.json");

            InstallState previous = null;
            try
            {
                if (File.Exists(statePath))
                {
                    previous = Json.Deserialize<InstallState>(File.ReadAllText(statePath, Encoding.UTF8));
                }
            }
            catch
            {
            }

            StopInstalledApplication(installDirectory);
            Directory.CreateDirectory(installDirectory);
            WriteResourceBytes("Continuador.Payload.exe", applicationPath);
            WriteResourceBytes("Continuador.WorkerPayload.exe", workerPath);
            File.Copy(Assembly.GetExecutingAssembly().Location, uninstallerPath, true);
            WriteResourceText("Continuador.Readme.txt", Path.Combine(installDirectory, "LEEME.txt"));

            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                codexHome = Path.Combine(user, ".codex");
            }
            string codexSkill = Path.Combine(codexHome, "skills", "continuar-tarea");
            string claudeSkill = Path.Combine(user, ".claude", "skills", "continuar-tarea");

            InstallState state = new InstallState();
            state.InstallDirectory = installDirectory;
            state.CodexSkill = codexSkill;
            state.ClaudeSkill = claudeSkill;
            state.CodexBackup = PrepareSkillForInstall(codexSkill, previous == null ? null : previous.CodexBackup);
            state.ClaudeBackup = PrepareSkillForInstall(claudeSkill, previous == null ? null : previous.ClaudeBackup);
            InstallSkill(codexSkill, "Continuador.CodexSkill.md");
            InstallSkill(claudeSkill, "Continuador.ClaudeSkill.md");

            string startMenu = IsTestMode()
                ? Path.Combine(user, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", ProductName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ProductName);
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "Continuador.lnk"), applicationPath, string.Empty, installDirectory);
            CreateShortcut(Path.Combine(startMenu, "Desinstalar Continuador.lnk"), uninstallerPath, "/uninstall", installDirectory);
            state.StartMenuDirectory = startMenu;

            if (!IsTestMode())
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    run.SetValue(ProductName, "\"" + applicationPath + "\" --tray");
                }
                using (RegistryKey uninstall = Registry.CurrentUser.CreateSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Continuador"))
                {
                    uninstall.SetValue("DisplayName", ProductName);
                    uninstall.SetValue("DisplayVersion", Version);
                    uninstall.SetValue("Publisher", "Continuador");
                    uninstall.SetValue("InstallLocation", installDirectory);
                    uninstall.SetValue("DisplayIcon", applicationPath);
                    uninstall.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall");
                    uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                if (RegisterScheduledTask(workerPath, installDirectory))
                {
                    state.TaskName = ScheduledTaskName;
                }
            }

            File.WriteAllText(statePath, Json.Serialize(state), new UTF8Encoding(false));
            if (!IsTestMode())
            {
                StartApplication(applicationPath, "--tray");
                StartApplication(applicationPath, "gui");
            }
            return installDirectory;
        }

        private static void Uninstall()
        {
            string current = Assembly.GetExecutingAssembly().Location;
            string installDirectory = Path.GetDirectoryName(current);
            string statePath = Path.Combine(installDirectory, "install-state.json");
            InstallState state = File.Exists(statePath)
                ? Json.Deserialize<InstallState>(File.ReadAllText(statePath, Encoding.UTF8))
                : new InstallState { InstallDirectory = installDirectory };

            StopInstalledApplication(installDirectory);
            RestoreSkill(state.CodexSkill, state.CodexBackup);
            RestoreSkill(state.ClaudeSkill, state.ClaudeBackup);

            if (!IsTestMode())
            {
                DeleteScheduledTask(string.IsNullOrWhiteSpace(state.TaskName) ? ScheduledTaskName : state.TaskName);
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (run != null) run.DeleteValue(ProductName, false);
                }
                Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Continuador", false);
            }

            if (!string.IsNullOrWhiteSpace(state.StartMenuDirectory) && Directory.Exists(state.StartMenuDirectory))
            {
                Directory.Delete(state.StartMenuDirectory, true);
            }

            string command = "/d /c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + installDirectory + "\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = command,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static void InstallSkill(string destination, string resourceName)
        {
            Directory.CreateDirectory(destination);
            WriteResourceText(resourceName, Path.Combine(destination, "SKILL.md"));
            File.WriteAllText(Path.Combine(destination, ".continuador-installed"), Version, Encoding.ASCII);
        }

        private static string BackupDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return null;
            }
            string backup = path + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.Move(path, backup);
            return backup;
        }

        private static string PrepareSkillForInstall(string path, string originalBackup)
        {
            if (!Directory.Exists(path))
            {
                return originalBackup;
            }
            if (File.Exists(Path.Combine(path, ".continuador-installed")))
            {
                Directory.Delete(path, true);
                return originalBackup;
            }
            return BackupDirectory(path);
        }

        private static void RestoreSkill(string path, string backup)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                string marker = Path.Combine(path, ".continuador-installed");
                if (File.Exists(marker))
                {
                    Directory.Delete(path, true);
                }
            }
            if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup) && !Directory.Exists(path))
            {
                Directory.Move(backup, path);
            }
        }

        private static void StopInstalledApplication(string installDirectory)
        {
            List<Process> processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName("Continuador"));
            processes.AddRange(Process.GetProcessesByName("ContinuadorWorker"));
            foreach (Process process in processes)
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (path.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }
            }
        }

        private static void WriteResourceBytes(string name, string destination)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (input == null) throw new InvalidOperationException("Falta el recurso " + name + ".");
                using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }
        }

        private static void WriteResourceText(string name, string destination)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (input == null) throw new InvalidOperationException("Falta el recurso " + name + ".");
                using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
                {
                    File.WriteAllText(destination, reader.ReadToEnd(), new UTF8Encoding(false));
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.IconLocation = targetPath + ",0";
            shortcut.Save();
        }

        private static void StartApplication(string executable, string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private static bool RegisterScheduledTask(string workerExecutable, string workingDirectory)
        {
            try
            {
                Type schedulerType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedulerType == null) return false;
                dynamic service = Activator.CreateInstance(schedulerType);
                service.Connect();
                dynamic folder = service.GetFolder("\\");
                dynamic task = service.NewTask(0);
                task.RegistrationInfo.Description = "Procesa continuaciones pendientes de Codex y Claude Code cada diez minutos.";
                task.Settings.Enabled = true;
                task.Settings.StartWhenAvailable = true;
                task.Settings.DisallowStartIfOnBatteries = false;
                task.Settings.StopIfGoingOnBatteries = false;
                task.Settings.MultipleInstances = 2;
                task.Settings.ExecutionTimeLimit = "PT6H";

                dynamic trigger = task.Triggers.Create(2);
                trigger.StartBoundary = DateTime.Now.AddMinutes(1).ToString("s");
                trigger.DaysInterval = 1;
                trigger.Repetition.Interval = "PT10M";
                trigger.Repetition.Duration = "P1D";
                trigger.Enabled = true;

                dynamic action = task.Actions.Create(0);
                action.Path = workerExecutable;
                action.Arguments = string.Empty;
                action.WorkingDirectory = workingDirectory;
                folder.RegisterTaskDefinition(ScheduledTaskName, task, 6, null, null, 3, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteScheduledTask(string taskName)
        {
            try
            {
                Type schedulerType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedulerType == null) return;
                dynamic service = Activator.CreateInstance(schedulerType);
                service.Connect();
                dynamic folder = service.GetFolder("\\");
                folder.DeleteTask(taskName, 0);
            }
            catch
            {
            }
        }

        private static bool IsTestMode()
        {
            return string.Equals(Environment.GetEnvironmentVariable("CONTINUADOR_INSTALL_TEST_MODE"), "1",
                StringComparison.Ordinal);
        }

        private static string GetUserRoot()
        {
            string testRoot = Environment.GetEnvironmentVariable("CONTINUADOR_INSTALL_TEST_ROOT");
            if (IsTestMode() && !string.IsNullOrWhiteSpace(testRoot))
            {
                return Path.GetFullPath(testRoot);
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly Button installButton;
        private readonly Label status;
        private static readonly Color Navy = Color.FromArgb(15, 23, 42);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color TextColor = Color.FromArgb(15, 23, 42);
        private static readonly Color Muted = Color.FromArgb(100, 116, 139);

        public SetupForm()
        {
            Text = "Instalar Continuador " + SetupProgram.ProductVersion;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(244, 247, 251);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(680, 456);

            Panel hero = new Panel();
            hero.Dock = DockStyle.Top;
            hero.Height = 128;
            hero.BackColor = Navy;

            PictureBox logo = new PictureBox();
            try { logo.Image = Icon.ToBitmap(); } catch { }
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(34, 31, 58, 58);

            Label eyebrow = new Label();
            eyebrow.Text = "INSTALADOR OFICIAL  ·  VERSIÓN " + SetupProgram.ProductVersion;
            eyebrow.ForeColor = Color.FromArgb(147, 197, 253);
            eyebrow.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            eyebrow.SetBounds(112, 25, 480, 20);

            Label title = new Label();
            title.Text = "Continuador";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 23F, FontStyle.Bold);
            title.SetBounds(108, 43, 350, 45);

            Label tagline = new Label();
            tagline.Text = "Tus tareas continúan a la hora que tú decidas.";
            tagline.ForeColor = Color.FromArgb(203, 213, 225);
            tagline.Font = new Font("Segoe UI", 9.5F);
            tagline.SetBounds(112, 91, 480, 23);

            hero.Controls.Add(logo);
            hero.Controls.Add(eyebrow);
            hero.Controls.Add(title);
            hero.Controls.Add(tagline);

            Panel content = new Panel();
            content.BackColor = Color.White;
            content.SetBounds(26, 151, 628, 214);
            content.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240)))
                    e.Graphics.DrawRectangle(pen, 0, 0, content.Width - 1, content.Height - 1);
            };

            Label heading = new Label();
            heading.Text = "Todo listo para instalar";
            heading.ForeColor = TextColor;
            heading.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            heading.SetBounds(24, 19, 400, 31);

            Label description = new Label();
            description.Text = "Continuador funciona con Codex Desktop, Codex CLI y Claude Code.";
            description.ForeColor = Muted;
            description.SetBounds(26, 54, 560, 24);

            Label checks = new Label();
            checks.Text = "✓  Se instala únicamente para tu usuario\r\n" +
                          "✓  No guarda contraseñas ni credenciales\r\n" +
                          "✓  Se inicia con Windows y recupera tareas vencidas";
            checks.ForeColor = TextColor;
            checks.Font = new Font("Segoe UI", 9.5F);
            checks.SetBounds(26, 88, 555, 78);

            Label location = new Label();
            location.Text = "UBICACIÓN   %LOCALAPPDATA%\\Programs\\Continuador";
            location.ForeColor = Muted;
            location.Font = new Font("Segoe UI", 7.8F, FontStyle.Bold);
            location.SetBounds(26, 177, 560, 20);

            content.Controls.Add(heading);
            content.Controls.Add(description);
            content.Controls.Add(checks);
            content.Controls.Add(location);

            installButton = new Button();
            installButton.Text = "Instalar Continuador";
            installButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            installButton.ForeColor = Color.White;
            installButton.BackColor = Blue;
            installButton.FlatStyle = FlatStyle.Flat;
            installButton.FlatAppearance.BorderColor = Blue;
            installButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
            installButton.Cursor = Cursors.Hand;
            installButton.Size = new Size(178, 42);
            installButton.Location = new Point(476, 382);
            installButton.Click += InstallClick;

            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.ForeColor = TextColor;
            cancel.BackColor = Color.White;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            cancel.Cursor = Cursors.Hand;
            cancel.Size = new Size(104, 42);
            cancel.Location = new Point(360, 382);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Click += delegate { Close(); };

            status = new Label();
            status.Location = new Point(27, 390);
            status.Size = new Size(320, 28);
            status.ForeColor = Muted;
            status.Text = "●  Listo para instalar";

            Controls.Add(hero);
            Controls.Add(content);
            Controls.Add(installButton);
            Controls.Add(cancel);
            Controls.Add(status);
            AcceptButton = installButton;
            CancelButton = cancel;
        }

        private void InstallClick(object sender, EventArgs e)
        {
            installButton.Enabled = false;
            status.Text = "Instalando...";
            Application.DoEvents();
            try
            {
                string location = SetupProgram.Install();
                status.Text = "Instalado en " + location;
                MessageBox.Show(this,
                    "Continuador se instaló correctamente. Reinicia las sesiones abiertas de Codex y Claude Code para cargar la integración.",
                    "Continuador", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "Error durante la instalación.";
                installButton.Enabled = true;
                MessageBox.Show(this, ex.Message, "No se pudo instalar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
