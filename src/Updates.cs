using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Continuador
{
    internal static class UpdateService
    {
        private const string DefaultApiUrl = "https://api.github.com/repos/Grandex21/Continuador/releases/latest";
        private const string DefaultReleaseUrl = "https://github.com/Grandex21/Continuador/releases/latest";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static UpdateInfo Check()
        {
            EnableTls12();
            string apiUrl = Environment.GetEnvironmentVariable("CONTINUADOR_UPDATE_API_URL");
            if (string.IsNullOrWhiteSpace(apiUrl)) apiUrl = DefaultApiUrl;

            string payload;
            using (WebClient client = CreateClient())
            {
                payload = client.DownloadString(apiUrl);
            }

            IDictionary root = Json.DeserializeObject(payload) as IDictionary;
            if (root == null)
            {
                throw new InvalidOperationException("GitHub devolvió una respuesta de actualización no válida.");
            }

            string tag = Value(root, "tag_name");
            string latest = NormalizeVersion(tag);
            if (string.IsNullOrWhiteSpace(latest))
            {
                throw new InvalidOperationException("La versión publicada no contiene una etiqueta válida.");
            }

            string installerUrl = null;
            string installerDigest = null;
            IEnumerable assets = root.Contains("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (object rawAsset in assets)
                {
                    IDictionary asset = rawAsset as IDictionary;
                    if (asset == null || !string.Equals(Value(asset, "name"), "Continuador-Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    installerUrl = Value(asset, "browser_download_url");
                    installerDigest = Value(asset, "digest");
                    break;
                }
            }

            string releaseUrl = Value(root, "html_url");
            if (string.IsNullOrWhiteSpace(releaseUrl)) releaseUrl = DefaultReleaseUrl;
            return new UpdateInfo
            {
                Version = latest,
                Tag = tag,
                ReleaseUrl = releaseUrl,
                InstallerUrl = installerUrl,
                InstallerDigest = installerDigest,
                IsNewer = IsNewer(latest, AppConstants.Version)
            };
        }

        public static string DownloadInstaller(UpdateInfo update)
        {
            if (update == null || string.IsNullOrWhiteSpace(update.InstallerUrl))
            {
                throw new InvalidOperationException("La versión publicada no incluye Continuador-Setup.exe.");
            }
            EnableTls12();
            string safeVersion = string.IsNullOrWhiteSpace(update.Version) ? "latest" : update.Version.Replace('.', '-');
            string path = Path.Combine(Path.GetTempPath(), "Continuador-Setup-" + safeVersion + ".exe");
            if (File.Exists(path)) File.Delete(path);
            using (WebClient client = CreateClient())
            {
                client.DownloadFile(update.InstallerUrl, path);
            }
            VerifyDigest(path, update.InstallerDigest);
            return path;
        }

        internal static bool IsNewer(string candidate, string current)
        {
            Version candidateVersion;
            Version currentVersion;
            if (!Version.TryParse(NormalizeVersion(candidate), out candidateVersion)) return false;
            if (!Version.TryParse(NormalizeVersion(current), out currentVersion)) return false;
            return candidateVersion > currentVersion;
        }

        public static bool ShouldNotify(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            return StateStore.Transaction<bool>(delegate(AppState state)
            {
                if (string.Equals(state.Settings.LastUpdateNotificationVersion, version, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                state.Settings.LastUpdateNotificationVersion = version;
                return true;
            }, true);
        }

        private static WebClient CreateClient()
        {
            WebClient client = new WebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "Continuador/" + AppConstants.Version;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static void VerifyDigest(string path, string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) return;
            string expected = digest.Trim();
            int separator = expected.IndexOf(':');
            if (separator >= 0)
            {
                if (!string.Equals(expected.Substring(0, separator), "sha256", StringComparison.OrdinalIgnoreCase)) return;
                expected = expected.Substring(separator + 1);
            }
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    throw new InvalidOperationException("La descarga no coincide con el SHA-256 publicado por GitHub.");
                }
            }
        }

        private static string NormalizeVersion(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
            int suffix = clean.IndexOf('-');
            if (suffix >= 0) clean = clean.Substring(0, suffix);
            int metadata = clean.IndexOf('+');
            if (metadata >= 0) clean = clean.Substring(0, metadata);
            return clean;
        }

        private static string Value(IDictionary dictionary, string key)
        {
            return dictionary != null && dictionary.Contains(key) && dictionary[key] != null
                ? Convert.ToString(dictionary[key]) : null;
        }

        private static void EnableTls12()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch { }
        }
    }
}
