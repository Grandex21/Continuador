using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Continuador
{
    internal static class NativeMethods
    {
        private const int SW_RESTORE = 9;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const ushort VK_RETURN = 0x0D;

        private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        public static void DetachConsole()
        {
            try { FreeConsole(); } catch { }
        }

        public static WindowSnapshot CaptureForeground()
        {
            WindowSnapshot snapshot = new WindowSnapshot();
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                {
                    return snapshot;
                }
                StringBuilder title = new StringBuilder(1024);
                GetWindowText(handle, title, title.Capacity);
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                string processName = string.Empty;
                try
                {
                    processName = Process.GetProcessById((int)processId).ProcessName;
                }
                catch
                {
                }
                snapshot.Handle = handle.ToInt64();
                snapshot.Title = title.ToString();
                snapshot.ProcessName = processName;
            }
            catch
            {
            }
            return snapshot;
        }

        public static WindowSnapshot FindClaudeWindow(string workingDirectory)
        {
            WindowSnapshot best = new WindowSnapshot();
            int bestScore = 0;
            string folderName = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    folderName = new DirectoryInfo(workingDirectory).Name;
                }
            }
            catch { }

            try
            {
                int currentProcessId = Process.GetCurrentProcess().Id;
                EnumWindows(delegate(IntPtr handle, IntPtr parameter)
                {
                    if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return true;
                    StringBuilder titleBuilder = new StringBuilder(1024);
                    GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
                    string title = titleBuilder.ToString().Trim();
                    if (title.Length == 0) return true;

                    uint processId;
                    GetWindowThreadProcessId(handle, out processId);
                    if (processId == currentProcessId) return true;
                    string processName = string.Empty;
                    try { processName = Process.GetProcessById((int)processId).ProcessName; }
                    catch { }

                    int score = 0;
                    if (processName.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0) score += 120;
                    if (title.IndexOf("Claude Code", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
                    else if (title.IndexOf("Claude", StringComparison.OrdinalIgnoreCase) >= 0) score += 80;
                    bool terminal = processName.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(processName, "Code", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(processName, "Cursor", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(processName, "OpenConsole", StringComparison.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(folderName) && title.IndexOf(folderName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += terminal ? 55 : 15;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best.Handle = handle.ToInt64();
                        best.Title = title;
                        best.ProcessName = processName;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return best;
        }

        public static bool SendTextToWindow(long rawHandle, string text, out string error)
        {
            error = null;
            IntPtr handle = new IntPtr(rawHandle);
            if (rawHandle == 0 || !IsWindow(handle))
            {
                error = "La ventana capturada ya no existe.";
                return false;
            }

            try
            {
                ShowWindow(handle, SW_RESTORE);
                if (!SetForegroundWindow(handle))
                {
                    error = "Windows no permitió activar la ventana de destino.";
                    return false;
                }

                Thread.Sleep(500);
                List<INPUT> inputs = new List<INPUT>();
                foreach (char character in text)
                {
                    INPUT down = new INPUT();
                    down.type = INPUT_KEYBOARD;
                    down.U.ki = new KEYBDINPUT();
                    down.U.ki.wScan = character;
                    down.U.ki.dwFlags = KEYEVENTF_UNICODE;
                    inputs.Add(down);

                    INPUT up = down;
                    up.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                    inputs.Add(up);
                }

                INPUT enterDown = new INPUT();
                enterDown.type = INPUT_KEYBOARD;
                enterDown.U.ki = new KEYBDINPUT();
                enterDown.U.ki.wVk = VK_RETURN;
                inputs.Add(enterDown);

                INPUT enterUp = enterDown;
                enterUp.U.ki.dwFlags = KEYEVENTF_KEYUP;
                inputs.Add(enterUp);

                INPUT[] array = inputs.ToArray();
                uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(INPUT)));
                if (sent != array.Length)
                {
                    error = "Windows solo envió " + sent + " de " + array.Length + " eventos de teclado.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal static class ExecutableFinder
    {
        public static string FindCodex()
        {
            string overridden = Environment.GetEnvironmentVariable("CONTINUADOR_CODEX_EXE");
            if (File.Exists(overridden))
            {
                return overridden;
            }

            string found = FindOnPath(new string[] { "codex.exe", "codex.cmd", "codex.bat" });
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            return FindNewestRecursive(root, "codex.exe");
        }

        public static string FindClaude()
        {
            string overridden = Environment.GetEnvironmentVariable("CONTINUADOR_CLAUDE_EXE");
            if (File.Exists(overridden))
            {
                return overridden;
            }

            try
            {
                AppState state = StateStore.Load();
                if (state.Settings != null && File.Exists(state.Settings.ClaudeExecutable))
                {
                    return state.Settings.ClaudeExecutable;
                }
            }
            catch { }

            string found = FindOnPath(new string[] { "claude.exe", "claude.cmd", "claude.bat" });
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }

            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = new string[]
            {
                Path.Combine(user, ".local", "bin", "claude.exe"),
                Path.Combine(user, ".local", "bin", "claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "claude.exe"),
                Path.Combine(user, "scoop", "shims", "claude.exe"),
                Path.Combine(user, "scoop", "shims", "claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "claude.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "claude.cmd")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            string localShare = Path.Combine(user, ".local", "share");
            string recursive = FindNewestRecursive(localShare, "claude.exe");
            return recursive;
        }

        private static string FindOnPath(string[] names)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string clean = directory.Trim().Trim('"');
                if (clean.Length == 0)
                {
                    continue;
                }
                foreach (string name in names)
                {
                    try
                    {
                        string candidate = Path.Combine(clean, name);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        private static string FindNewestRecursive(string root, string fileName)
        {
            if (!Directory.Exists(root))
            {
                return null;
            }
            try
            {
                FileInfo newest = null;
                foreach (string path in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                {
                    FileInfo info = new FileInfo(path);
                    if (newest == null || info.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                    {
                        newest = info;
                    }
                }
                return newest == null ? null : newest.FullName;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class ProcessRunner
    {
        public static CommandResult Run(string executable, IList<string> arguments, string workingDirectory, int timeoutMilliseconds)
        {
            CommandResult result = new CommandResult();
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                result.ExitCode = -1;
                result.StandardError = "No se encontró el ejecutable: " + (executable ?? "(vacío)");
                return result;
            }

            bool isScript = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                            executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            ProcessStartInfo info = new ProcessStartInfo();
            if (isScript)
            {
                info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                info.Arguments = "/d /s /c " + QuoteForCmd(executable, arguments);
            }
            else
            {
                info.FileName = executable;
                info.Arguments = JoinArguments(arguments);
            }
            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
            }
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();
                    string standardOutput = string.Empty;
                    string standardError = string.Empty;
                    Thread outputThread = new Thread(delegate() { standardOutput = process.StandardOutput.ReadToEnd(); });
                    Thread errorThread = new Thread(delegate() { standardError = process.StandardError.ReadToEnd(); });
                    outputThread.Start();
                    errorThread.Start();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        result.TimedOut = true;
                        try { process.Kill(); } catch { }
                    }
                    outputThread.Join(5000);
                    errorThread.Join(5000);
                    result.StandardOutput = standardOutput;
                    result.StandardError = standardError;
                    result.ExitCode = result.TimedOut ? -2 : process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.StandardError = ex.Message;
            }
            return result;
        }

        public static string JoinArguments(IList<string> arguments)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(QuoteArgument(arguments[i]));
            }
            return builder.ToString();
        }

        public static string QuoteArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }
            if (argument.Length > 0 && argument.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                }
                else if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                }
                else
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                    builder.Append(character);
                }
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static string QuoteForCmd(string executable, IList<string> arguments)
        {
            return "\"\"" + executable + "\" " + JoinArguments(arguments) + "\"";
        }
    }
}
