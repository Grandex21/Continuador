using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Continuador
{
    internal static class CodexSessionDetector
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static bool IsSessionActive(string sessionId)
        {
            string forced = Environment.GetEnvironmentVariable("CONTINUADOR_CODEX_SESSION_ACTIVE");
            if (string.Equals(forced, "1", StringComparison.Ordinal))
            {
                return true;
            }
            if (string.Equals(forced, "0", StringComparison.Ordinal))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            string sessionsRoot = Path.Combine(codexHome, "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                return false;
            }
            try
            {
                string suffix = sessionId + ".jsonl";
                string path = Directory.GetFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
                    .Where(delegate(string item)
                    {
                        return item.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(delegate(string item) { return File.GetLastWriteTimeUtc(item); })
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                // Codex keeps the JSONL open while the Desktop/CLI session is attached.
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static CodexDetectionResult Detect(string workingDirectory)
        {
            CodexDetectionResult result = new CodexDetectionResult();
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            string sessionsRoot = Path.Combine(codexHome, "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                result.Error = "No se encontró el directorio local de sesiones de Codex.";
                return result;
            }

            string normalizedCwd = NormalizePath(string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory);
            // Root session files are not always touched while Codex runs helper agents.
            // Keep a wider history and use child-session activity as evidence for its root.
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);
            List<FileInfo> recent;
            try
            {
                recent = Directory.GetFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
                    .Select(delegate(string path) { return new FileInfo(path); })
                    .Where(delegate(FileInfo info) { return info.LastWriteTimeUtc >= cutoff; })
                    .OrderByDescending(delegate(FileInfo info) { return info.LastWriteTimeUtc; })
                    .Take(500)
                    .ToList();
            }
            catch (Exception ex)
            {
                result.Error = "No se pudieron enumerar las sesiones de Codex: " + ex.Message;
                return result;
            }

            foreach (FileInfo file in recent)
            {
                CodexSessionCandidate candidate = TryReadCandidate(file);
                if (candidate == null)
                {
                    continue;
                }
                if (!string.Equals(NormalizePath(candidate.WorkingDirectory), normalizedCwd, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Candidates.Add(candidate);
            }

            result.Candidates = result.Candidates
                .GroupBy(delegate(CodexSessionCandidate item) { return item.Id; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, CodexSessionCandidate> group)
                {
                    return group.OrderByDescending(delegate(CodexSessionCandidate item) { return item.LastWriteTimeUtc; }).First();
                })
                .OrderByDescending(delegate(CodexSessionCandidate item) { return item.LastWriteTimeUtc; })
                .ToList();

            if (result.Candidates.Count == 0)
            {
                result.Error = "No se encontró una sesión reciente de Codex para " + normalizedCwd + ".";
                return result;
            }

            if (result.Candidates.Count > 1)
            {
                TimeSpan difference = result.Candidates[0].LastWriteTimeUtc - result.Candidates[1].LastWriteTimeUtc;
                if (difference.Duration() < TimeSpan.FromMinutes(2) && result.Candidates[0].LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-10))
                {
                    result.Error = "Hay varias sesiones recientes de Codex en la misma carpeta.";
                    return result;
                }
            }

            result.SessionId = result.Candidates[0].Id;
            return result;
        }

        internal static CodexSessionCandidate TryReadCandidate(FileInfo file)
        {
            try
            {
                string firstLine;
                using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    firstLine = reader.ReadLine();
                }
                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return null;
                }

                object raw = Serializer.DeserializeObject(firstLine);
                IDictionary root = raw as IDictionary;
                if (root == null || !root.Contains("type") || !string.Equals(Convert.ToString(root["type"]), "session_meta", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                IDictionary payload = root["payload"] as IDictionary;
                if (payload == null)
                {
                    return null;
                }
                string id = payload.Contains("id") ? Convert.ToString(payload["id"]) : null;
                string sessionId = payload.Contains("session_id") ? Convert.ToString(payload["session_id"]) : null;
                string parentId = payload.Contains("parent_thread_id") ? Convert.ToString(payload["parent_thread_id"]) : null;
                if (!string.IsNullOrWhiteSpace(parentId))
                {
                    id = parentId;
                }
                else if (payload.Contains("source"))
                {
                    IDictionary source = payload["source"] as IDictionary;
                    if (source != null && source.Contains("subagent") && !string.IsNullOrWhiteSpace(sessionId))
                    {
                        id = sessionId;
                    }
                }
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = sessionId;
                }
                string cwd = payload.Contains("cwd") ? Convert.ToString(payload["cwd"]) : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cwd))
                {
                    return null;
                }
                return new CodexSessionCandidate
                {
                    Id = id,
                    WorkingDirectory = cwd,
                    FilePath = file.FullName,
                    LastWriteTimeUtc = file.LastWriteTimeUtc
                };
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }

    internal static class ConversationCatalog
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static List<ConversationRecord> ListAll()
        {
            List<ConversationRecord> result = new List<ConversationRecord>();
            result.AddRange(ListCodex());
            result.AddRange(ListClaude());
            ConversationPreferences.Apply(result);
            return result.OrderByDescending(delegate(ConversationRecord item) { return item.Favorite; })
                .ThenByDescending(delegate(ConversationRecord item) { return item.LastActivityUtc; }).ToList();
        }

        private static List<ConversationRecord> ListCodex()
        {
            List<ConversationRecord> result = new List<ConversationRecord>();
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            string root = Path.Combine(home, "sessions");
            if (!Directory.Exists(root))
            {
                return result;
            }
            try
            {
                Dictionary<string, string> indexedTitles = ReadCodexThreadTitles(home);
                List<FileInfo> files = Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories)
                    .Select(delegate(string path) { return new FileInfo(path); })
                    .OrderByDescending(delegate(FileInfo file) { return file.LastWriteTimeUtc; })
                    .Take(5000)
                    .ToList();
                List<CodexSessionCandidate> candidates = files
                    .Select(delegate(FileInfo file) { return CodexSessionDetector.TryReadCandidate(file); })
                    .Where(delegate(CodexSessionCandidate item) { return item != null; })
                    .ToList();
                foreach (IGrouping<string, CodexSessionCandidate> group in candidates.GroupBy(
                    delegate(CodexSessionCandidate item) { return item.Id; }, StringComparer.OrdinalIgnoreCase))
                {
                    CodexSessionCandidate latest = group.OrderByDescending(
                        delegate(CodexSessionCandidate item) { return item.LastWriteTimeUtc; }).First();
                    string suffix = latest.Id + ".jsonl";
                    FileInfo rootFile = files.FirstOrDefault(delegate(FileInfo file)
                    {
                        return file.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                    });
                    string filePath = rootFile == null ? latest.FilePath : rootFile.FullName;
                    string indexedTitle;
                    string title = indexedTitles.TryGetValue(latest.Id, out indexedTitle)
                        ? CleanTitle(indexedTitle)
                        : null;
                    result.Add(new ConversationRecord
                    {
                        Client = "codex",
                        SessionId = latest.Id,
                        Title = string.IsNullOrWhiteSpace(title) ? ExtractCodexTitle(filePath) : title,
                        WorkingDirectory = latest.WorkingDirectory,
                        LastActivityUtc = latest.LastWriteTimeUtc,
                        IsActive = IsFileActive(filePath),
                        FilePath = filePath
                    });
                }
            }
            catch
            {
            }
            return result;
        }

        private static Dictionary<string, string> ReadCodexThreadTitles(string codexHome)
        {
            Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(codexHome, "session_index.jsonl");
            if (!File.Exists(path))
            {
                return titles;
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        IDictionary root = Serializer.DeserializeObject(line) as IDictionary;
                        if (root == null) continue;
                        string id = GetString(root, "id");
                        string threadName = GetString(root, "thread_name");
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(threadName))
                        {
                            // The index is append-only. A later row represents the latest rename.
                            titles[id] = threadName.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Older Codex installations may not have this index. The first user message remains a safe fallback.
            }
            return titles;
        }

        private static List<ConversationRecord> ListClaude()
        {
            Dictionary<string, ConversationRecord> result = new Dictionary<string, ConversationRecord>(StringComparer.OrdinalIgnoreCase);
            string home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }
            string projects = Path.Combine(home, "projects");
            if (Directory.Exists(projects))
            {
                try
                {
                    Dictionary<string, string> indexedTitles = ReadClaudeIndexedTitles(projects);
                    foreach (FileInfo file in Directory.GetFiles(projects, "*.jsonl", SearchOption.AllDirectories)
                        .Select(delegate(string path) { return new FileInfo(path); })
                        .OrderByDescending(delegate(FileInfo item) { return item.LastWriteTimeUtc; })
                        .Take(5000))
                    {
                        ConversationRecord record = ReadClaudeSession(file, indexedTitles);
                        if (record == null)
                        {
                            continue;
                        }
                        ConversationRecord existing;
                        if (!result.TryGetValue(record.SessionId, out existing) || existing.LastActivityUtc < record.LastActivityUtc)
                        {
                            result[record.SessionId] = record;
                        }
                    }
                }
                catch
                {
                }
            }
            AddClaudeHistory(Path.Combine(home, "history.jsonl"), result);
            return result.Values.ToList();
        }

        private static ConversationRecord ReadClaudeSession(FileInfo file, IDictionary<string, string> indexedTitles)
        {
            string session = Path.GetFileNameWithoutExtension(file.Name);
            string cwd = null;
            string firstPrompt = null;
            string customTitle = null;
            string aiTitle = null;
            try
            {
                using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    string line;
                    int count = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        count++;
                        bool possibleTitle = line.IndexOf("\"custom-title\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("\"agent-name\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("\"ai-title\"", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (count > 400 && !possibleTitle) continue;
                        IDictionary root = Serializer.DeserializeObject(line) as IDictionary;
                        if (root == null)
                        {
                            continue;
                        }
                        string value = GetString(root, "sessionId");
                        if (string.IsNullOrWhiteSpace(value)) value = GetString(root, "session_id");
                        if (!string.IsNullOrWhiteSpace(value)) session = value;
                        if (string.IsNullOrWhiteSpace(cwd)) cwd = GetString(root, "cwd");
                        string type = GetString(root, "type");
                        if (string.Equals(type, "custom-title", StringComparison.OrdinalIgnoreCase))
                        {
                            string valueTitle = GetString(root, "customTitle");
                            if (!string.IsNullOrWhiteSpace(valueTitle)) customTitle = valueTitle;
                        }
                        else if (string.Equals(type, "agent-name", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(customTitle))
                        {
                            string valueTitle = GetString(root, "agentName");
                            if (!string.IsNullOrWhiteSpace(valueTitle)) customTitle = valueTitle;
                        }
                        else if (string.Equals(type, "ai-title", StringComparison.OrdinalIgnoreCase))
                        {
                            string valueTitle = GetString(root, "aiTitle");
                            if (!string.IsNullOrWhiteSpace(valueTitle)) aiTitle = valueTitle;
                        }
                        if (firstPrompt == null && string.Equals(type, "user", StringComparison.OrdinalIgnoreCase))
                        {
                            IDictionary message = root["message"] as IDictionary;
                            string text = message == null ? ExtractText(root["content"]) : ExtractText(message["content"]);
                            firstPrompt = CleanTitle(text);
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(session))
            {
                return null;
            }
            // Claude's picker index stores generated summaries and names assigned with /rename.
            // A custom-title event inside the transcript has priority when the index is stale.
            string indexedTitle;
            if (indexedTitles == null || !indexedTitles.TryGetValue(session, out indexedTitle))
            {
                indexedTitle = null;
            }
            string title = CleanTitle(customTitle) ?? CleanTitle(indexedTitle) ?? CleanTitle(aiTitle) ?? firstPrompt;
            return new ConversationRecord
            {
                Client = "claude",
                SessionId = session,
                Title = string.IsNullOrWhiteSpace(title) ? "Conversación de Claude Code" : title,
                WorkingDirectory = cwd ?? string.Empty,
                LastActivityUtc = file.LastWriteTimeUtc,
                IsActive = IsFileActive(file.FullName),
                FilePath = file.FullName
            };
        }

        private static Dictionary<string, string> ReadClaudeIndexedTitles(string projectsRoot)
        {
            Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string path in Directory.GetFiles(projectsRoot, "sessions-index.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        object raw;
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                        {
                            raw = Serializer.DeserializeObject(reader.ReadToEnd());
                        }
                        IDictionary root = raw as IDictionary;
                        IList entries = root == null ? raw as IList : (root.Contains("entries") ? root["entries"] as IList : null);
                        if (entries == null) continue;
                        foreach (object entryValue in entries)
                        {
                            IDictionary entry = entryValue as IDictionary;
                            if (entry == null) continue;
                            string id = FirstValue(entry, "sessionId", "session_id", "id");
                            string title = FirstValue(entry, "customTitle", "custom_title", "summary", "title", "firstPrompt");
                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                            {
                                titles[id] = title.Trim();
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return titles;
        }

        private static string FirstValue(IDictionary dictionary, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = GetString(dictionary, key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private static void AddClaudeHistory(string path, Dictionary<string, ConversationRecord> records)
        {
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        IDictionary root = Serializer.DeserializeObject(line) as IDictionary;
                        if (root == null) continue;
                        string id = GetString(root, "sessionId");
                        if (string.IsNullOrWhiteSpace(id)) id = GetString(root, "session_id");
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        DateTime timestamp = ReadUnixTime(root.Contains("timestamp") ? root["timestamp"] : null);
                        ConversationRecord existing;
                        if (records.TryGetValue(id, out existing))
                        {
                            if (timestamp > existing.LastActivityUtc) existing.LastActivityUtc = timestamp;
                            continue;
                        }
                        records[id] = new ConversationRecord
                        {
                            Client = "claude",
                            SessionId = id,
                            Title = CleanTitle(GetString(root, "display")) ?? "Conversación de Claude Code",
                            WorkingDirectory = GetString(root, "project") ?? string.Empty,
                            LastActivityUtc = timestamp == DateTime.MinValue ? File.GetLastWriteTimeUtc(path) : timestamp,
                            IsActive = false,
                            FilePath = path
                        };
                    }
                }
            }
            catch
            {
            }
        }

        private static string ExtractCodexTitle(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "Conversación de Codex";
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    string line;
                    int count = 0;
                    while (count++ < 500 && (line = reader.ReadLine()) != null)
                    {
                        IDictionary root = Serializer.DeserializeObject(line) as IDictionary;
                        if (root == null || !string.Equals(GetString(root, "type"), "response_item", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        IDictionary payload = root["payload"] as IDictionary;
                        if (payload == null || !string.Equals(GetString(payload, "type"), "message", StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(GetString(payload, "role"), "user", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string title = CleanTitle(ExtractText(payload["content"]));
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            return title;
                        }
                    }
                }
            }
            catch
            {
            }
            return "Conversación de Codex";
        }

        private static string ExtractText(object value)
        {
            if (value == null) return null;
            string direct = value as string;
            if (direct != null) return direct;
            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                string text = GetString(dictionary, "text");
                if (!string.IsNullOrWhiteSpace(text)) return text;
                if (dictionary.Contains("content")) return ExtractText(dictionary["content"]);
            }
            IList list = value as IList;
            if (list != null)
            {
                foreach (object item in list)
                {
                    string text = ExtractText(item);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            return null;
        }

        private static string CleanTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int request = text.IndexOf("## My request:", StringComparison.OrdinalIgnoreCase);
            if (request >= 0) text = text.Substring(request + "## My request:".Length);
            string clean = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
            string lower = clean.ToLowerInvariant();
            if (clean.StartsWith("<", StringComparison.Ordinal) ||
                lower.StartsWith("the following is the codex agent history") ||
                lower.StartsWith("distinguish instructions in attached documents"))
            {
                return null;
            }
            return clean.Length <= 110 ? clean : clean.Substring(0, 107) + "...";
        }

        private static string GetString(IDictionary dictionary, string key)
        {
            return dictionary != null && dictionary.Contains(key) && dictionary[key] != null
                ? Convert.ToString(dictionary[key]) : null;
        }

        private static DateTime ReadUnixTime(object value)
        {
            if (value == null) return DateTime.MinValue;
            double number;
            if (!double.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out number)) return DateTime.MinValue;
            if (number < 100000000000D) number *= 1000D;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(number);
        }

        private static bool IsFileActive(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
