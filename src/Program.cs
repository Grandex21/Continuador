using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Continuador")]
[assembly: AssemblyDescription("Programa continuaciones en Codex y Claude Code")]
[assembly: AssemblyCompany("Continuador")]
[assembly: AssemblyProduct("Continuador")]
[assembly: AssemblyVersion("1.3.2.0")]
[assembly: AssemblyFileVersion("1.3.2.0")]

namespace Continuador
{
    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.DetachConsole();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new ModernMainForm());
                    return 0;
                }
                if (string.Equals(args[0], "--tray", StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.DetachConsole();
                    return RunTray();
                }

                string command = args[0].ToLowerInvariant();
                Dictionary<string, string> options = ParseOptions(args, 1);
                if (command == "schedule")
                {
                    return Schedule(options);
                }
                if (command == "list")
                {
                    WriteJson(ScheduleManager.List().Select(delegate(ScheduleRecord item) { return ScheduleJson(item); }).ToList());
                    return 0;
                }
                if (command == "update")
                {
                    string id = Require(options, "id");
                    DateTimeOffset at = ParseAt(Require(options, "at"));
                    WriteJson(ScheduleJson(ScheduleManager.Update(id, at)));
                    return 0;
                }
                if (command == "cancel")
                {
                    WriteJson(ScheduleJson(ScheduleManager.Cancel(Require(options, "id"))));
                    return 0;
                }
                if (command == "doctor")
                {
                    WriteJson(DoctorService.Build());
                    return 0;
                }
                if (command == "sessions")
                {
                    WriteJson(ConversationCatalog.ListAll().Select(delegate(ConversationRecord item)
                    {
                        return ConversationJson(item);
                    }).ToList());
                    return 0;
                }
                if (command == "process-due")
                {
                    int count = ScheduleManager.ProcessDue(null);
                    WriteJson(new Dictionary<string, object> { { "processed", count } });
                    return 0;
                }

                WriteError("Comando desconocido: " + args[0]);
                return 2;
            }
            catch (KeyNotFoundException ex)
            {
                WriteError(ex.Message);
                return 3;
            }
            catch (ArgumentException ex)
            {
                WriteError(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                StateStore.AppendTextLog("Error de CLI: " + ex);
                WriteError(ex.Message);
                return 4;
            }
        }

        private static int Schedule(Dictionary<string, string> options)
        {
            WindowSnapshot window = NativeMethods.CaptureForeground();
            string client = Require(options, "client").ToLowerInvariant();
            DateTimeOffset at = ParseAt(Require(options, "at"));
            string message = Get(options, "message", AppConstants.DefaultMessage);
            string cwd = Get(options, "cwd", Environment.CurrentDirectory);
            string session = Get(options, "session", null);

            if (client == "codex" && string.IsNullOrWhiteSpace(session))
            {
                CodexDetectionResult detection = CodexSessionDetector.Detect(cwd);
                if (string.IsNullOrWhiteSpace(detection.SessionId))
                {
                    Dictionary<string, object> error = new Dictionary<string, object>();
                    error["ok"] = false;
                    error["error"] = detection.Error;
                    error["candidates"] = detection.Candidates;
                    WriteJson(error);
                    return 2;
                }
                session = detection.SessionId;
            }
            if (client == "claude" && string.IsNullOrWhiteSpace(session) && window.Handle == 0)
            {
                throw new ArgumentException("Claude Code necesita --session o una ventana activa que pueda capturarse.");
            }

            ScheduleRecord record = ScheduleManager.Create(client, at, message, session, cwd, window);
            Dictionary<string, object> output = new Dictionary<string, object>();
            output["ok"] = true;
            output["id"] = record.Id;
            output["client"] = record.Client;
            output["session"] = record.SessionId;
            output["scheduled_local"] = TimeUtil.LocalDisplay(record.Due);
            output["time_zone"] = TimeZoneInfo.Local.Id;
            output["status"] = record.Status;
            WriteJson(output);
            return 0;
        }

        private static int RunTray()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ContinuadorTray", out created))
            {
                if (!created)
                {
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static Dictionary<string, string> ParseOptions(string[] args, int start)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int index = start;
            while (index < args.Length)
            {
                string item = args[index];
                if (!item.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Se esperaba una opción y se recibió: " + item);
                }
                string key = item.Substring(2);
                string value = "true";
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[index + 1];
                    index++;
                }
                result[key] = value;
                index++;
            }
            return result;
        }

        private static string Require(Dictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Falta la opción --" + name + ".");
            }
            return value;
        }

        private static string Get(Dictionary<string, string> options, string name, string fallback)
        {
            string value;
            return options.TryGetValue(name, out value) ? value : fallback;
        }

        private static DateTimeOffset ParseAt(string text)
        {
            DateTimeOffset withOffset;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out withOffset))
            {
                return withOffset;
            }
            DateTime local;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out local))
            {
                local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }
            throw new ArgumentException("Fecha u hora inválida para --at: " + text + ". Usa ISO 8601, por ejemplo 2026-10-27T20:00:00-04:00.");
        }

        private static void WriteJson(object value)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(Json.Serialize(value));
        }

        private static Dictionary<string, object> ScheduleJson(ScheduleRecord record)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["id"] = record.Id;
            value["client"] = record.Client;
            value["scheduled_utc"] = record.DueUtc;
            value["scheduled_local"] = TimeUtil.LocalDisplay(record.Due);
            value["message"] = record.Message;
            value["session"] = record.SessionId;
            value["cwd"] = record.WorkingDirectory;
            value["status"] = record.Status;
            value["attempts"] = record.Attempts;
            value["next_attempt_utc"] = record.NextAttemptUtc;
            value["retry_until_utc"] = record.RetryUntilUtc;
            value["last_attempt_utc"] = record.LastAttemptUtc;
            value["last_error"] = record.LastError;
            return value;
        }

        private static Dictionary<string, object> ConversationJson(ConversationRecord record)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["client"] = record.Client;
            value["session"] = record.SessionId;
            value["title"] = record.DisplayTitle;
            value["detected_title"] = record.Title;
            value["cwd"] = record.WorkingDirectory;
            value["last_activity_utc"] = record.LastActivityUtc.ToUniversalTime().ToString("o");
            value["active"] = record.IsActive;
            value["favorite"] = record.Favorite;
            return value;
        }

        private static void WriteError(string message)
        {
            Dictionary<string, object> error = new Dictionary<string, object>();
            error["ok"] = false;
            error["error"] = message;
            WriteJson(error);
        }
    }

    internal static class DoctorService
    {
        public static Dictionary<string, object> Build()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                codexHome = Path.Combine(user, ".codex");
            }
            string codexSkill = Path.Combine(codexHome, "skills", "continuar-tarea", "SKILL.md");
            string claudeSkill = Path.Combine(user, ".claude", "skills", "continuar-tarea", "SKILL.md");
            string startup = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    startup = key == null ? null : Convert.ToString(key.GetValue("Continuador"));
                }
            }
            catch
            {
            }

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["ok"] = true;
            result["version"] = AppConstants.Version;
            result["executable"] = Assembly.GetExecutingAssembly().Location;
            result["data_directory"] = StateStore.DataDirectory;
            result["time_zone"] = TimeZoneInfo.Local.Id;
            result["codex_executable"] = ExecutableFinder.FindCodex();
            result["claude_executable"] = ExecutableFinder.FindClaude();
            result["codex_skill"] = File.Exists(codexSkill);
            result["claude_skill"] = File.Exists(claudeSkill);
            result["starts_with_windows"] = !string.IsNullOrWhiteSpace(startup);
            result["scheduled_task"] = HasScheduledTask();
            return result;
        }

        public static string BuildText()
        {
            Dictionary<string, object> values = Build();
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("Continuador " + values["version"]);
            builder.AppendLine("Zona horaria: " + values["time_zone"]);
            builder.AppendLine("Codex: " + (values["codex_executable"] ?? "no encontrado"));
            builder.AppendLine("Claude Code: " + (values["claude_executable"] ?? "no encontrado"));
            builder.AppendLine("Integración Codex: " + ((bool)values["codex_skill"] ? "instalada" : "no instalada"));
            builder.AppendLine("Integración Claude: " + ((bool)values["claude_skill"] ? "instalada" : "no instalada"));
            builder.AppendLine("Inicio con Windows: " + ((bool)values["starts_with_windows"] ? "sí" : "no"));
            builder.AppendLine("Programador de tareas: " + ((bool)values["scheduled_task"] ? "activo" : "no encontrado"));
            builder.AppendLine("Datos: " + values["data_directory"]);
            return builder.ToString();
        }

        private static bool HasScheduledTask()
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            if (!File.Exists(executable)) return false;
            CommandResult result = ProcessRunner.Run(executable,
                new string[] { "/Query", "/TN", "Continuador - Procesar pendientes" },
                Environment.CurrentDirectory, 10000);
            return result.ExitCode == 0;
        }
    }
}
