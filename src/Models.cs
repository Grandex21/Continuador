using System;
using System.Collections.Generic;

namespace Continuador
{
    internal static class AppConstants
    {
        public const string ProductName = "Continuador";
        public const string Version = "1.4.0";
        public const string DefaultMessage = "Continúa la tarea pendiente desde donde te quedaste.";
        public const int RetryMinutes = 10;
        public const int RetryHours = 6;
    }

    [Serializable]
    public sealed class AppState
    {
        public AppState()
        {
            Schedules = new List<ScheduleRecord>();
            Logs = new List<DeliveryLog>();
            Settings = new AppSettings();
        }

        public List<ScheduleRecord> Schedules { get; set; }
        public List<DeliveryLog> Logs { get; set; }
        public AppSettings Settings { get; set; }
    }

    [Serializable]
    public sealed class AppSettings
    {
        public AppSettings()
        {
            Conversations = new List<ConversationPreference>();
        }

        public string CodexExecutable { get; set; }
        public string ClaudeExecutable { get; set; }
        public List<ConversationPreference> Conversations { get; set; }
        public string LastUpdateNotificationVersion { get; set; }
    }

    [Serializable]
    public sealed class ConversationPreference
    {
        public string Key { get; set; }
        public bool Favorite { get; set; }
        public string CustomName { get; set; }
    }

    [Serializable]
    public sealed class ScheduleRecord
    {
        public string Id { get; set; }
        public string Client { get; set; }
        public string DueUtc { get; set; }
        public string OriginalLocalTime { get; set; }
        public string Message { get; set; }
        public string SessionId { get; set; }
        public string WorkingDirectory { get; set; }
        public string Status { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public string NextAttemptUtc { get; set; }
        public string RetryUntilUtc { get; set; }
        public string LastAttemptUtc { get; set; }
        public int Attempts { get; set; }
        public string LastError { get; set; }
        public long WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public string WindowProcess { get; set; }

        public DateTimeOffset Due
        {
            get { return TimeUtil.ParseUtc(DueUtc); }
        }

        public DateTimeOffset NextAttempt
        {
            get { return TimeUtil.ParseUtc(NextAttemptUtc); }
        }

        public DateTimeOffset RetryUntil
        {
            get { return TimeUtil.ParseUtc(RetryUntilUtc); }
        }
    }

    [Serializable]
    public sealed class DeliveryLog
    {
        public string TimestampUtc { get; set; }
        public string ScheduleId { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    internal static class ScheduleStatuses
    {
        public const string Pending = "Pendiente";
        public const string Running = "Ejecutando";
        public const string Delivered = "Entregada";
        public const string Cancelled = "Cancelada";
        public const string Failed = "Fallida";
    }

    internal sealed class CommandResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool TimedOut { get; set; }

        public string CombinedOutput
        {
            get
            {
                return ((StandardOutput ?? string.Empty) + Environment.NewLine + (StandardError ?? string.Empty)).Trim();
            }
        }
    }

    internal sealed class DeliveryResult
    {
        public bool Success { get; set; }
        public bool Retryable { get; set; }
        public string Details { get; set; }
    }

    internal sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Tag { get; set; }
        public string ReleaseUrl { get; set; }
        public string InstallerUrl { get; set; }
        public string InstallerDigest { get; set; }
        public bool IsNewer { get; set; }
    }

    internal sealed class WindowSnapshot
    {
        public long Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
    }

    internal sealed class CodexSessionCandidate
    {
        public string Id { get; set; }
        public string WorkingDirectory { get; set; }
        public string FilePath { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
    }

    internal sealed class CodexDetectionResult
    {
        public CodexDetectionResult()
        {
            Candidates = new List<CodexSessionCandidate>();
        }

        public string SessionId { get; set; }
        public string Error { get; set; }
        public List<CodexSessionCandidate> Candidates { get; set; }
    }

    internal sealed class ConversationRecord
    {
        public string Client { get; set; }
        public string SessionId { get; set; }
        public string Title { get; set; }
        public string WorkingDirectory { get; set; }
        public DateTime LastActivityUtc { get; set; }
        public bool IsActive { get; set; }
        public string FilePath { get; set; }
        public bool Favorite { get; set; }
        public string CustomName { get; set; }

        public string DisplayTitle
        {
            get { return string.IsNullOrWhiteSpace(CustomName) ? Title : CustomName; }
        }
    }

    internal static class TimeUtil
    {
        public static string ToUtcString(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("o");
        }

        public static DateTimeOffset ParseUtc(string value)
        {
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
            {
                return DateTimeOffset.MinValue;
            }
            return parsed.ToUniversalTime();
        }

        public static string LocalDisplay(DateTimeOffset value)
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
    }
}
