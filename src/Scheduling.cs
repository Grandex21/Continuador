using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Continuador
{
    internal static class ScheduleManager
    {
        public static ScheduleRecord Create(string client, DateTimeOffset due, string message, string sessionId,
            string workingDirectory, WindowSnapshot window)
        {
            client = (client ?? string.Empty).Trim().ToLowerInvariant();
            if (client != "codex" && client != "claude")
            {
                throw new ArgumentException("El cliente debe ser codex o claude.");
            }
            if (due < DateTimeOffset.Now.AddMinutes(-5))
            {
                throw new ArgumentException("La fecha programada ya pasó.");
            }
            if (string.IsNullOrWhiteSpace(message))
            {
                message = AppConstants.DefaultMessage;
            }
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Environment.CurrentDirectory;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ScheduleRecord record = new ScheduleRecord
            {
                Id = "con-" + Guid.NewGuid().ToString("N"),
                Client = client,
                DueUtc = TimeUtil.ToUtcString(due),
                OriginalLocalTime = due.ToString("o"),
                Message = message,
                SessionId = sessionId,
                WorkingDirectory = workingDirectory,
                Status = ScheduleStatuses.Pending,
                CreatedUtc = TimeUtil.ToUtcString(now),
                UpdatedUtc = TimeUtil.ToUtcString(now),
                NextAttemptUtc = TimeUtil.ToUtcString(due),
                RetryUntilUtc = TimeUtil.ToUtcString(due.AddHours(AppConstants.RetryHours)),
                LastAttemptUtc = null,
                Attempts = 0,
                LastError = null,
                WindowHandle = window == null ? 0 : window.Handle,
                WindowTitle = window == null ? null : window.Title,
                WindowProcess = window == null ? null : window.ProcessName
            };

            StateStore.Transaction<object>(delegate(AppState state)
            {
                state.Schedules.Add(record);
                AddLog(state, record.Id, "info", "Programada para " + TimeUtil.LocalDisplay(due) + " en " + client + ".");
                return null;
            }, true);

            EnsureTrayRunning();
            return record;
        }

        public static List<ScheduleRecord> List()
        {
            AppState state = StateStore.Load();
            return state.Schedules.OrderBy(delegate(ScheduleRecord item) { return item.Due; }).ToList();
        }

        public static ScheduleRecord Update(string id, DateTimeOffset due)
        {
            return StateStore.Transaction<ScheduleRecord>(delegate(AppState state)
            {
                ScheduleRecord record = Find(state, id);
                if (record == null)
                {
                    throw new KeyNotFoundException("No existe la programación " + id + ".");
                }
                record.DueUtc = TimeUtil.ToUtcString(due);
                record.OriginalLocalTime = due.ToString("o");
                record.NextAttemptUtc = TimeUtil.ToUtcString(due);
                record.RetryUntilUtc = TimeUtil.ToUtcString(due.AddHours(AppConstants.RetryHours));
                record.Status = ScheduleStatuses.Pending;
                record.Attempts = 0;
                record.LastError = null;
                record.UpdatedUtc = TimeUtil.ToUtcString(DateTimeOffset.UtcNow);
                AddLog(state, record.Id, "info", "Hora cambiada a " + TimeUtil.LocalDisplay(due) + ".");
                return record;
            }, true);
        }

        public static ScheduleRecord Cancel(string id)
        {
            return StateStore.Transaction<ScheduleRecord>(delegate(AppState state)
            {
                ScheduleRecord record = Find(state, id);
                if (record == null)
                {
                    throw new KeyNotFoundException("No existe la programación " + id + ".");
                }
                record.Status = ScheduleStatuses.Cancelled;
                record.UpdatedUtc = TimeUtil.ToUtcString(DateTimeOffset.UtcNow);
                AddLog(state, record.Id, "info", "Programación cancelada.");
                return record;
            }, true);
        }

        public static ScheduleRecord RunNow(string id)
        {
            return Update(id, DateTimeOffset.Now);
        }

        public static int ProcessDue(Action<string, string> notification)
        {
            int processed = 0;
            while (processed < 5)
            {
                ScheduleRecord claimed = ClaimNextDue(DateTimeOffset.UtcNow);
                if (claimed == null)
                {
                    break;
                }
                processed++;
                DeliveryResult result = DeliveryEngine.Deliver(claimed);
                FinalizeAttempt(claimed.Id, result);
                if (notification != null)
                {
                    notification(result.Success ? "Continuación enviada" : "Continuación pendiente",
                        result.Success ? (claimed.Client + ": " + claimed.Message) : result.Details);
                }
            }
            return processed;
        }

        private static ScheduleRecord ClaimNextDue(DateTimeOffset now)
        {
            return StateStore.Transaction<ScheduleRecord>(delegate(AppState state)
            {
                foreach (ScheduleRecord stuck in state.Schedules.Where(delegate(ScheduleRecord item)
                {
                    if (item.Status != ScheduleStatuses.Running || string.IsNullOrEmpty(item.LastAttemptUtc))
                    {
                        return false;
                    }
                    return TimeUtil.ParseUtc(item.LastAttemptUtc) < now.AddMinutes(-30);
                }))
                {
                    stuck.Status = ScheduleStatuses.Pending;
                    stuck.LastError = "Se recuperó una ejecución interrumpida.";
                    stuck.NextAttemptUtc = TimeUtil.ToUtcString(now);
                }

                List<ScheduleRecord> candidates = state.Schedules
                    .Where(delegate(ScheduleRecord item)
                    {
                        return item.Status == ScheduleStatuses.Pending && item.NextAttempt <= now;
                    })
                    .OrderBy(delegate(ScheduleRecord item) { return item.NextAttempt; })
                    .ToList();
                ScheduleRecord candidate = null;
                foreach (ScheduleRecord due in candidates)
                {
                    if (due.RetryUntil < now)
                    {
                        due.Status = ScheduleStatuses.Failed;
                        due.LastError = "Terminó la ventana de reintentos de seis horas.";
                        due.UpdatedUtc = TimeUtil.ToUtcString(now);
                        AddLog(state, due.Id, "error", due.LastError);
                        continue;
                    }
                    candidate = due;
                    break;
                }
                if (candidate == null)
                {
                    return null;
                }
                candidate.Status = ScheduleStatuses.Running;
                candidate.Attempts++;
                candidate.LastAttemptUtc = TimeUtil.ToUtcString(now);
                candidate.UpdatedUtc = TimeUtil.ToUtcString(now);
                return candidate;
            }, true);
        }

        private static void FinalizeAttempt(string id, DeliveryResult result)
        {
            StateStore.Transaction<object>(delegate(AppState state)
            {
                ScheduleRecord record = Find(state, id);
                if (record == null)
                {
                    return null;
                }
                DateTimeOffset now = DateTimeOffset.UtcNow;
                record.UpdatedUtc = TimeUtil.ToUtcString(now);
                record.LastError = result.Success ? null : result.Details;
                if (result.Success)
                {
                    record.Status = ScheduleStatuses.Delivered;
                    AddLog(state, record.Id, "info", "Entregada correctamente en el intento " + record.Attempts + ".");
                }
                else if (result.Retryable && now.AddMinutes(AppConstants.RetryMinutes) <= record.RetryUntil)
                {
                    record.Status = ScheduleStatuses.Pending;
                    record.NextAttemptUtc = TimeUtil.ToUtcString(now.AddMinutes(AppConstants.RetryMinutes));
                    AddLog(state, record.Id, "warning", result.Details + " Reintento en diez minutos.");
                }
                else
                {
                    record.Status = ScheduleStatuses.Failed;
                    AddLog(state, record.Id, "error", result.Details);
                }
                return null;
            }, true);
        }

        private static ScheduleRecord Find(AppState state, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }
            ScheduleRecord exact = state.Schedules.FirstOrDefault(delegate(ScheduleRecord item)
            {
                return string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase);
            });
            if (exact != null)
            {
                return exact;
            }
            List<ScheduleRecord> matches = state.Schedules.Where(delegate(ScheduleRecord item)
            {
                return item.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (matches.Count > 1)
            {
                throw new ArgumentException("El identificador abreviado " + id + " coincide con varias programaciones.");
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        private static void AddLog(AppState state, string id, string level, string message)
        {
            state.Logs.Add(new DeliveryLog
            {
                TimestampUtc = TimeUtil.ToUtcString(DateTimeOffset.UtcNow),
                ScheduleId = id,
                Level = level,
                Message = message
            });
            StateStore.AppendTextLog("[" + level + "] " + id + " " + message);
        }

        private static void EnsureTrayRunning()
        {
            if (Environment.GetEnvironmentVariable("CONTINUADOR_NO_DAEMON") == "1")
            {
                return;
            }
            try
            {
                string executable = Assembly.GetExecutingAssembly().Location;
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--tray",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                });
            }
            catch (Exception ex)
            {
                StateStore.AppendTextLog("No se pudo iniciar la bandeja: " + ex.Message);
            }
        }
    }

    internal static class DeliveryEngine
    {
        public static DeliveryResult Deliver(ScheduleRecord record)
        {
            try
            {
                if (record.Client == "codex")
                {
                    return DeliverCodex(record);
                }
                if (record.Client == "claude")
                {
                    return DeliverClaude(record);
                }
                return Failure("Cliente no compatible: " + record.Client, false);
            }
            catch (Exception ex)
            {
                return Failure("Error inesperado: " + ex.Message, true);
            }
        }

        private static DeliveryResult DeliverCodex(ScheduleRecord record)
        {
            string executable = ExecutableFinder.FindCodex();
            if (string.IsNullOrEmpty(executable))
            {
                return Failure("No se encontró codex.exe.", true);
            }
            if (string.IsNullOrWhiteSpace(record.SessionId))
            {
                return Failure("La programación no contiene un identificador de sesión de Codex.", false);
            }

            bool active = CodexSessionDetector.IsSessionActive(record.SessionId);
            string queueError = null;
            if (active)
            {
                CommandResult queued = ProcessRunner.Run(executable,
                    new string[] { "queue", "--thread", record.SessionId, "--message", record.Message },
                    record.WorkingDirectory, 120000);
                if (queued.ExitCode == 0)
                {
                    return Success("Codex aceptó el mensaje en la sesión activa " + record.SessionId + ".");
                }
                queueError = queued.CombinedOutput;
                if (IsRetryable(queueError))
                {
                    return Failure("codex queue: " + TrimOutput(queueError), true);
                }
            }

            CommandResult resumed = ProcessRunner.Run(executable,
                new string[] { "exec", "resume", "--skip-git-repo-check", record.SessionId, record.Message },
                record.WorkingDirectory, 7200000);
            if (resumed.ExitCode == 0)
            {
                return Success("Codex reanudó la sesión mediante exec resume.");
            }

            string combined = (queueError == null ? string.Empty : "codex queue: " + TrimOutput(queueError) + " | ") +
                              "codex exec resume: " + TrimOutput(resumed.CombinedOutput);
            return Failure(combined, IsRetryable(combined) || resumed.ExitCode == -1);
        }

        private static DeliveryResult DeliverClaude(ScheduleRecord record)
        {
            string executable = ExecutableFinder.FindClaude();
            if (!string.IsNullOrEmpty(executable) && !string.IsNullOrWhiteSpace(record.SessionId))
            {
                CommandResult resumed = ProcessRunner.Run(executable,
                    new string[] { "-p", "--resume", record.SessionId, record.Message },
                    record.WorkingDirectory, 7200000);
                if (resumed.ExitCode == 0)
                {
                    return Success("Claude Code reanudó la sesión " + record.SessionId + ".");
                }

                string output = resumed.CombinedOutput;
                if (LooksLikeActiveSessionConflict(output) && record.WindowHandle != 0)
                {
                    string keyboardError;
                    if (NativeMethods.SendTextToWindow(record.WindowHandle, record.Message, out keyboardError))
                    {
                        return Success("El mensaje se envió a la terminal activa de Claude Code.");
                    }
                    output = output + " | Respaldo de teclado: " + keyboardError;
                }
                return Failure("claude --resume: " + TrimOutput(output), IsRetryable(output));
            }

            if (record.WindowHandle != 0)
            {
                string error;
                if (NativeMethods.SendTextToWindow(record.WindowHandle, record.Message, out error))
                {
                    return Success("El mensaje se envió a la ventana capturada de Claude Code.");
                }
                return Failure("No se encontró Claude Code y falló el respaldo de teclado: " + error, true);
            }

            return Failure("No se encontró Claude Code ni una ventana de respaldo.", true);
        }

        private static bool IsRetryable(string output)
        {
            string lower = (output ?? string.Empty).ToLowerInvariant();
            string[] markers = new string[]
            {
                "rate limit", "usage limit", "credit", "crédito", "429", "overloaded",
                "temporarily unavailable", "temporalmente", "connection", "conexión",
                "not connected", "timeout", "timed out", "try again", "intenta de nuevo"
            };
            return markers.Any(delegate(string marker) { return lower.Contains(marker); });
        }

        private static bool LooksLikeActiveSessionConflict(string output)
        {
            string lower = (output ?? string.Empty).ToLowerInvariant();
            return lower.Contains("already active") || lower.Contains("already in use") ||
                   lower.Contains("session is active") || lower.Contains("attach");
        }

        private static string TrimOutput(string output)
        {
            string clean = string.IsNullOrWhiteSpace(output) ? "sin detalles" : output.Trim();
            if (clean.Length > 1200)
            {
                clean = clean.Substring(0, 1200) + "...";
            }
            return clean.Replace(Environment.NewLine, " ");
        }

        private static DeliveryResult Success(string details)
        {
            return new DeliveryResult { Success = true, Retryable = false, Details = details };
        }

        private static DeliveryResult Failure(string details, bool retryable)
        {
            return new DeliveryResult { Success = false, Retryable = retryable, Details = details };
        }
    }
}
