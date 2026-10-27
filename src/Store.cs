using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Continuador
{
    internal static class StateStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string DataDirectory
        {
            get
            {
                string overridden = Environment.GetEnvironmentVariable("CONTINUADOR_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(overridden))
                {
                    return Path.GetFullPath(overridden);
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Continuador");
            }
        }

        public static string StatePath
        {
            get { return Path.Combine(DataDirectory, "schedules.json"); }
        }

        public static string TextLogPath
        {
            get { return Path.Combine(DataDirectory, "continuador.log"); }
        }

        public static T Transaction<T>(Func<AppState, T> action, bool save)
        {
            Directory.CreateDirectory(DataDirectory);
            using (Mutex mutex = new Mutex(false, MutexName()))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (!acquired)
                    {
                        throw new IOException("No se pudo bloquear el archivo de datos de Continuador.");
                    }

                    AppState state = LoadUnlocked();
                    T result = action(state);
                    if (save)
                    {
                        SaveUnlocked(state);
                    }
                    return result;
                }
                finally
                {
                    if (acquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        public static AppState Load()
        {
            return Transaction<AppState>(delegate(AppState state) { return state; }, false);
        }

        public static void AppendTextLog(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(TextLogPath,
                    DateTimeOffset.UtcNow.ToString("o") + " " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Logging must never prevent delivery.
            }
        }

        private static AppState LoadUnlocked()
        {
            if (!File.Exists(StatePath))
            {
                return new AppState();
            }

            try
            {
                string json = File.ReadAllText(StatePath, Encoding.UTF8);
                AppState state = Serializer.Deserialize<AppState>(json);
                if (state == null)
                {
                    state = new AppState();
                }
                if (state.Schedules == null)
                {
                    state.Schedules = new List<ScheduleRecord>();
                }
                if (state.Logs == null)
                {
                    state.Logs = new List<DeliveryLog>();
                }
                if (state.Settings == null)
                {
                    state.Settings = new AppSettings();
                }
                if (state.Settings.Conversations == null)
                {
                    state.Settings.Conversations = new List<ConversationPreference>();
                }
                return state;
            }
            catch (Exception ex)
            {
                string damaged = StatePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    File.Copy(StatePath, damaged, true);
                }
                catch
                {
                }
                AppendTextLog("No se pudo leer schedules.json: " + ex.Message);
                return new AppState();
            }
        }

        private static void SaveUnlocked(AppState state)
        {
            if (state.Logs.Count > 300)
            {
                state.Logs.RemoveRange(0, state.Logs.Count - 300);
            }

            Directory.CreateDirectory(DataDirectory);
            string temp = StatePath + ".tmp-" + Guid.NewGuid().ToString("N");
            string json = Serializer.Serialize(state);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Copy(temp, StatePath, true);
            File.Delete(temp);
        }

        private static string MutexName()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(DataDirectory.ToLowerInvariant());
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return "Local\\ContinuadorData-" + builder.ToString();
            }
        }
    }
}
