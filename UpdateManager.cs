using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FSB_helper_C__
{
    public static class UpdateManager
    {
        public const string APP_VERSION = "3.3.0";
        private const string JSDELIVR_VERSION_URL = "https://cdn.jsdelivr.net/gh/sparkyyy204/DuranHelper-Updates@main/version.json";
        private const string GITHUB_API_URL = "https://api.github.com/repos/sparkyyy204/DuranHelper-Updates/releases/latest";
        private const int CHECK_TIMEOUT_SECONDS = 8;

        public static string LatestVersion { get; private set; } = "";
        public static string DownloadUrl { get; private set; } = "";
        public static string ReleaseNotes { get; private set; } = "";

        public static async Task<int> CheckForUpdateAsync()
        {
            // Try jsDelivr CDN first (faster, no rate limits, no blocking)
            int result = await CheckViaJsDelivrAsync();
            if (result != -1) return result;
            
            // Fallback to GitHub API
            return await CheckViaGitHubApiAsync();
        }

        private static async Task<int> CheckViaJsDelivrAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(CHECK_TIMEOUT_SECONDS);
                client.DefaultRequestHeaders.Add("User-Agent", "DuranHelper");

                string url = JSDELIVR_VERSION_URL + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string json = await client.GetStringAsync(url);
                var versionInfo = JObject.Parse(json);

                string remoteVer = versionInfo["version"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(remoteVer)) return -1;

                if (Version.TryParse(remoteVer, out Version remote) && Version.TryParse(APP_VERSION, out Version local))
                {
                    if (remote > local)
                    {
                        LatestVersion = remoteVer;
                        ReleaseNotes = versionInfo["notes"]?.ToString() ?? "";
                        DownloadUrl = versionInfo["download_url"]?.ToString() ?? "";
                        return !string.IsNullOrEmpty(DownloadUrl) ? 1 : 0;
                    }
                }
                return 0;
            }
            catch
            {
                return -1; // Signal to try fallback
            }
        }

        private static async Task<int> CheckViaGitHubApiAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(CHECK_TIMEOUT_SECONDS);
                client.DefaultRequestHeaders.Add("User-Agent", "DuranHelper");

                string json = await client.GetStringAsync(GITHUB_API_URL);
                var release = JObject.Parse(json);

                string tagName = release["tag_name"]?.ToString() ?? "";
                ReleaseNotes = release["body"]?.ToString() ?? "";
                string remoteVer = tagName.TrimStart('v', 'V');

                if (string.IsNullOrEmpty(remoteVer)) return 0;

                if (Version.TryParse(remoteVer, out Version remote) && Version.TryParse(APP_VERSION, out Version local))
                {
                    if (remote > local)
                    {
                        LatestVersion = remoteVer;

                        var assets = release["assets"] as JArray;
                        if (assets != null)
                        {
                            foreach (var asset in assets)
                            {
                                string name = asset["name"]?.ToString() ?? "";
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    DownloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                                    break;
                                }
                            }
                        }

                        return !string.IsNullOrEmpty(DownloadUrl) ? 1 : 0;
                    }
                }

                return 0;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Downloads ZIP, extracts it, and launches batch to replace files.
        /// </summary>
        public static async Task<bool> DownloadAndApplyUpdateAsync(Action<int> progressCallback = null)
        {
            try
            {
                string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(appPath)) return false;

                string appDir = Path.GetDirectoryName(appPath) ?? "";
                string zipPath = Path.Combine(appDir, "update_package.zip");
                string updateTempDir = Path.Combine(appDir, "_update_temp");
                string batPath = Path.Combine(appDir, "update.bat");

                // 1. Download ZIP
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "DuranHelper");
                    client.Timeout = TimeSpan.FromMinutes(10);

                    string[] zipMirrors = new string[] {
                        DownloadUrl
                    };

                    HttpResponseMessage response = null;
                    foreach (var m in zipMirrors)
                    {
                        try {
                            var r = await client.GetAsync(m, HttpCompletionOption.ResponseHeadersRead);
                            r.EnsureSuccessStatusCode();
                            response = r;
                            break;
                        } catch { continue; }
                    }
                    if (response == null) throw new Exception("All update download mirrors failed");

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long downloadedBytes = 0;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;
                            if (totalBytes > 0)
                            {
                                int percent = (int)((downloadedBytes * 100) / totalBytes);
                                progressCallback?.Invoke(percent);
                            }
                        }
                    }
                    response?.Dispose();
                }

                // 2. Extract ZIP
                if (Directory.Exists(updateTempDir)) Directory.Delete(updateTempDir, true);
                Directory.CreateDirectory(updateTempDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, updateTempDir);

                // 3. Create update batch script
                // We use xcopy /Y /E to replace only changed files from temp to appDir
                string appExeName = Path.GetFileName(appPath);
                string batContent =
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +
                    "echo Ожидание закрытия лаунчера...\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"taskkill /F /IM \"{appExeName}\" >nul 2>&1\r\n" +
                    "timeout /t 1 /nobreak >nul\r\n" +
                    "echo Применение обновления...\r\n" +
                    $"xcopy /Y /E /Q \"{updateTempDir}\\*\" \"{appDir}\\\"\r\n" +
                    "echo Очистка временных файлов...\r\n" +
                    $"rd /S /Q \"{updateTempDir}\"\r\n" +
                    $"del /Q \"{zipPath}\"\r\n" +
                    "echo Запуск новой версии...\r\n" +
                    $"start \"\" \"{appPath}\"\r\n" +
                    "del \"%~f0\"\r\n";

                File.WriteAllText(batPath, batContent, System.Text.Encoding.UTF8);

                // 4. Launch batch
                var psi = new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads the "CheckUpdates" setting from Settings.json. 
        /// Returns true by default if the key is missing.
        /// </summary>
        public static bool IsUpdateCheckEnabled()
        {
            try
            {
                if (File.Exists("Settings.json"))
                {
                    string json = File.ReadAllText("Settings.json");
                    var s = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(json);
                    if (s != null && s.ContainsKey("CheckUpdates"))
                    {
                        return s["CheckUpdates"] != "False";
                    }
                }
            }
            catch { }
            return true; // enabled by default
        }
    }
}
