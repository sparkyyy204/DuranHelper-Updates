using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace FSB_helper_C__
{
    public class CloudItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("server")] public string Server { get; set; }
        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("fileUrl")] public string FileUrl { get; set; }
        [JsonIgnore] public bool IsInstalled { get; set; }
        [JsonIgnore]
        public SolidColorBrush BadgeTextBrush
        {
            get
            {
                string hex = CloudView.GetServerColorHex(Server);
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
        }
        [JsonIgnore]
        public SolidColorBrush BadgeBgBrush
        {
            get
            {
                string hex = CloudView.GetServerColorHex(Server);
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = 0x26;
                return new SolidColorBrush(c);
            }
        }
    }

    public partial class CloudView : UserControl
    {
        private List<CloudItem> _fullCatalog = new List<CloudItem>();
        private CancellationTokenSource _loadCts;
        private string _lastCloudProfile = null;

        public CloudView()
        {
            InitializeComponent();
            SetupFilters();
        }

        private void SetupFilters()
        {
            cmbServer.Items.Add("🌍 Все серверы");
            for (int i = 1; i <= 21; i++) cmbServer.Items.Add($"СЕРВЕР {i:D2}");
            cmbServer.SelectedIndex = 0;

            cmbCategory.Items.Add("📁 Все категории");
            cmbCategory.Items.Add("Профили");
            cmbCategory.Items.Add("Законы");
            cmbCategory.Items.Add("Бинды");
            cmbCategory.Items.Add("Штрафы");
            cmbCategory.SelectedIndex = 0;

            cmbServer.SelectionChanged += Filter_Changed;
            cmbCategory.SelectionChanged += Filter_Changed;
        }

        public async void LoadCatalog()
        {
            if (_fullCatalog != null && _fullCatalog.Count > 0) 
            {
                var main = Application.Current.MainWindow as MainWindow;
                if (main != null && _lastCloudProfile != main.CurrentProfile) {
                    RefreshState();
                }
                return; // Уже загружено
            }
            // Cancel any previous in-flight load
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;
            
            txtLoadingTitle.Text = "ПОДКЛЮЧЕНИЕ К ОБЛАКУ";
            txtLoadingDesc.Text = "Синхронизация каталога с сервером...";
            LoadingOverlay.Visibility = Visibility.Visible;
            ErrorView.Visibility = Visibility.Collapsed;
            icCloudList.Visibility = Visibility.Collapsed;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "DuranHelper/2.0");

                    string url = "https://cdn.jsdelivr.net/gh/sparkyyy204/Duran-Web-Catalog@main/catalog.json";
                    // Cache-busting для jsDelivr
                    url += "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    
                    string json = await client.GetStringAsync(url, ct);
                    
                    ct.ThrowIfCancellationRequested();
                    
                    _fullCatalog = JsonConvert.DeserializeObject<List<CloudItem>>(json);
                    
                    if (_fullCatalog == null) _fullCatalog = new List<CloudItem>();
                }

                RefreshState();
                icCloudList.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                // Loading was cancelled by tab switch — silently stop
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    txtErrorMsg.Text = "Сбой соединения. Проверьте подключение или зайдите позже.";
                    ErrorView.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        public void CancelLoading()
        {
            _loadCts?.Cancel();
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            LoadCatalog();
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtSearchHint.Visibility = string.IsNullOrWhiteSpace(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshState();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            RefreshState();
        }

        public void RefreshState()
        {
            if (_fullCatalog == null) return;
            
            // Reset all IsInstalled flags first
            foreach (var item in _fullCatalog)
                item.IsInstalled = false;
            
            var query = _fullCatalog.AsEnumerable();

            if (cmbServer.SelectedIndex > 0)
            {
                string srv = cmbServer.SelectedItem.ToString();
                query = query.Where(i => i.Server == srv || i.Server == "Все серверы");
            }

            if (cmbCategory.SelectedIndex > 0)
            {
                string cat = "";
                switch (cmbCategory.SelectedIndex)
                {
                    case 1: cat = "profile"; break;
                    case 2: cat = "laws"; break;
                    case 3: cat = "binds"; break;
                    case 4: cat = "fines"; break;
                }
                if (!string.IsNullOrEmpty(cat)) query = query.Where(i => i.Type == cat);
            }

            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                string term = txtSearch.Text.ToLower();
                query = query.Where(i => i.Title.ToLower().Contains(term) || i.Author.ToLower().Contains(term));
            }

            var main = Application.Current.MainWindow as MainWindow;
            if (main != null)
            {
                _lastCloudProfile = main.CurrentProfile;
                foreach (var item in _fullCatalog)
                {
                    if (item.Type == "profile")
                    {
                        item.IsInstalled = main.MasterData.ContainsKey(item.Title);
                    }
                    else if (!string.IsNullOrEmpty(main.CurrentProfile) && main.MasterData.ContainsKey(main.CurrentProfile))
                    {
                        var p = main.MasterData[main.CurrentProfile];
                        if (p.InstalledCloudIds == null) p.InstalledCloudIds = new List<string>();
                        
                        if (p.InstalledCloudIds.Contains(item.Id))
                        {
                            // Validate that actual data still exists in profile
                            bool dataExists = true;
                            if (item.Type == "laws" && (p.Laws == null || p.Laws.Count == 0))
                                dataExists = false;
                            else if (item.Type == "fines" && (p.Fines == null || p.Fines.Count == 0))
                                dataExists = false;
                            else if (item.Type == "binds" && (p.Binds == null || p.Binds.Count == 0))
                                dataExists = false;
                            
                            if (!dataExists)
                            {
                                p.InstalledCloudIds.Remove(item.Id);
                                item.IsInstalled = false;
                            }
                            else
                            {
                                item.IsInstalled = true;
                            }
                        }
                        else
                        {
                            item.IsInstalled = false;
                        }
                    }
                }
            }

            icCloudList.ItemsSource = null;
            icCloudList.ItemsSource = query.ToList();
        }

        public List<string> GetBindTypeCloudIds() {
            if (_fullCatalog == null) return new List<string>();
            return _fullCatalog.Where(i => i.Type == "binds").Select(i => i.Id).ToList();
        }

        public static string GetServerColorHex(string server)
        {
            if (string.IsNullOrEmpty(server)) return "#8b949e";
            string s = server.ToUpper().Trim();
            if (s.Contains("ВСЕ")) return "#2ea043";
            // Extract server number
            var match = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)");
            if (!match.Success) return "#8b949e";
            int num = int.Parse(match.Groups[1].Value);
            switch (num)
            {
                case 1:  return "#58a6ff";
                case 2:  return "#8957e5";
                case 3:  return "#f78166";
                case 4:  return "#ff7b72";
                case 5:  return "#3fb950";
                case 6:  return "#79c0ff";
                case 7:  return "#d29922";
                case 8:  return "#f0883e";
                case 9:  return "#d2a65e";
                case 10: return "#bc8cff";
                case 11: return "#39d353";
                case 12: return "#e3b341";
                case 13: return "#db6d28";
                case 14: return "#f85149";
                case 15: return "#a371f7";
                case 16: return "#56d364";
                case 17: return "#e6b800";
                case 18: return "#ff9bce";
                case 19: return "#76e4f7";
                case 20: return "#ffa657";
                case 21: return "#7ee787";
                default: return "#8b949e";
            }
        }

        private async void BtnInstall_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border btn && btn.Tag is CloudItem item)
            {
                if (string.IsNullOrWhiteSpace(item.FileUrl) || item.IsInstalled) return;
                
                var main = Application.Current.MainWindow as MainWindow;
                if (main == null) return;
                bool shouldSave = false;
                bool shouldRefreshUi = false;
                
                txtLoadingTitle.Text = "УСТАНОВКА КОНФИГУРАЦИИ";
                txtLoadingDesc.Text = "Идет скачивание файлов с облака...";
                LoadingOverlay.Visibility = Visibility.Visible;
                txtErrorMsg.Text = "Скачивание конфигурации...";
                ErrorView.Visibility = Visibility.Collapsed;

                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(15);
                        client.DefaultRequestHeaders.Add("User-Agent", "DuranHelper/2.0");
                        
                        string originalUrl = item.FileUrl;
                        string cdnUrl = ConvertToJsDelivr(originalUrl);
                        string finalUrl = cdnUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        
                        string json;
                        try {
                            json = await client.GetStringAsync(finalUrl);
                        } catch {
                            // Fallback to original GitHub URL
                            finalUrl = originalUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            json = await client.GetStringAsync(finalUrl);
                        }

                        if (item.Type == "profile")
                        {
                            var pf = JsonConvert.DeserializeObject<ProfileData>(json);
                            if (pf != null)
                            {
                                int idx = 1;
                                string pName = item.Title;
                                while (main.MasterData.ContainsKey(pName))
                                {
                                    idx++;
                                    pName = item.Title + $" ({idx})";
                                }
                                main.MasterData[pName] = pf;
                                main.CurrentProfile = pName;
                                shouldSave = true;
                                shouldRefreshUi = true;
                                // Notifications disabled by request
                            }
                        }
                        else if (item.Type == "laws")
                        {
                            var lawsMap = JsonConvert.DeserializeObject<Dictionary<string, LawSection>>(json);
                            if (lawsMap != null)
                            {
                                if (string.IsNullOrEmpty(main.CurrentProfile) || !main.MasterData.ContainsKey(main.CurrentProfile)) throw new Exception("Нет активного профиля для установки законов");
                                foreach (var kvp in lawsMap)
                                {
                                    string secName = kvp.Key;
                                    int idx = 1;
                                    while (main.MasterData[main.CurrentProfile].Laws.ContainsKey(secName))
                                    {
                                        idx++;
                                        secName = kvp.Key + $" ({idx})";
                                    }
                                    main.MasterData[main.CurrentProfile].Laws[secName] = kvp.Value;
                                }
                                shouldSave = true;
                                shouldRefreshUi = true;
                                // Notifications disabled by request
                            }
                        }
                        else if (item.Type == "fines")
                        {
                            var finesList = JsonConvert.DeserializeObject<List<FineArticle>>(json);
                            if (finesList != null)
                            {
                                if (string.IsNullOrEmpty(main.CurrentProfile) || !main.MasterData.ContainsKey(main.CurrentProfile)) throw new Exception("Нет активного профиля для установки штрафов");
                                if (main.MasterData[main.CurrentProfile].Fines == null) main.MasterData[main.CurrentProfile].Fines = new List<FineArticle>();
                                
                                main.MasterData[main.CurrentProfile].Fines.AddRange(finesList);
                                shouldSave = true;
                                shouldRefreshUi = true;
                                // Notifications disabled by request
                            }
                        }
                        else if (item.Type == "binds")
                        {
                            var exportData = JsonConvert.DeserializeObject<ExportBindsData>(json);
                            if (exportData != null)
                            {
                                if (string.IsNullOrEmpty(main.CurrentProfile) || !main.MasterData.ContainsKey(main.CurrentProfile)) throw new Exception("Нет активного профиля для установки биндов");
                                
                                var p = main.MasterData[main.CurrentProfile];
                                int conflictsResolved = 0;
                                var idMap = new Dictionary<string, string>(); // oldId -> newId
                                if (exportData.Binds != null)
                                {
                                    foreach (var b in exportData.Binds)
                                    {
                                        if (b.Value.active && !string.IsNullOrEmpty(b.Value.key))
                                        {
                                            var conflicts = p.Binds.Values.Where(existing => existing.key == b.Value.key && existing.active).ToList();
                                            if (conflicts.Count > 0)
                                            {
                                                foreach (var c in conflicts) c.active = false;
                                                conflictsResolved++;
                                            }
                                        }

                                        string oldId = b.Key;
                                        string newId = Guid.NewGuid().ToString().Substring(0, 6);
                                        idMap[oldId] = newId;
                                        b.Value.id = newId;
                                        p.Binds[newId] = b.Value;
                                    }
                                }
                                if (exportData.Groups != null)
                                {
                                    foreach (var g in exportData.Groups)
                                    {
                                        if (!p.Groups.Contains(g)) p.Groups.Add(g);
                                    }
                                }
                                if (exportData.Variables != null)
                                {
                                    foreach (var v in exportData.Variables)
                                    {
                                        p.Variables[v.Key] = v.Value;
                                    }
                                }
                                if (exportData.RadialMenu != null)
                                {
                                    p.RadialMenu = exportData.RadialMenu;
                                    // Remap all sector BindIds from old to new
                                    if (p.RadialMenu.Sectors != null)
                                    {
                                        foreach (var sec in p.RadialMenu.Sectors)
                                        {
                                            if (!string.IsNullOrEmpty(sec.BindId) && idMap.ContainsKey(sec.BindId))
                                                sec.BindId = idMap[sec.BindId];
                                        }
                                    }
                                    if (p.RadialMenu.Groups != null)
                                    {
                                        foreach (var grp in p.RadialMenu.Groups)
                                        {
                                            if (grp.Sectors != null)
                                            {
                                                foreach (var sec in grp.Sectors)
                                                {
                                                    if (!string.IsNullOrEmpty(sec.BindId) && idMap.ContainsKey(sec.BindId))
                                                        sec.BindId = idMap[sec.BindId];
                                                }
                                            }
                                        }
                                    }
                                }
                                shouldSave = true;
                                main.UpdateBindGroups();
                                main.UpdateBindsList();
                                // Notifications disabled by request
                            }
                        }
                    }

                    // Save installed ID to current profile and refresh UI
                    if (item.Type != "profile")
                    {
                        if (!string.IsNullOrEmpty(main.CurrentProfile) && main.MasterData.ContainsKey(main.CurrentProfile))
                        {
                            var p = main.MasterData[main.CurrentProfile];
                            if (p.InstalledCloudIds == null) p.InstalledCloudIds = new List<string>();
                            if (!p.InstalledCloudIds.Contains(item.Id))
                                p.InstalledCloudIds.Add(item.Id);
                            shouldSave = true;
                        }
                    }
                    else
                    {
                        shouldSave = true;
                    }

                    if (shouldSave) main.SaveData();
                    if (shouldRefreshUi) main.RefreshUI();

                    // Update "УСТАНОВЛЕНО" immediately before hiding overlay.
                    await Dispatcher.InvokeAsync(() => RefreshState(), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (HttpRequestException)
                {
                    main.OpenInfo("ОШИБКА СЕТИ", "Не удалось подключиться к GitHub.\nВозможно, отсутствует интернет или провайдер блокирует доступ.");
                }
                catch (TaskCanceledException)
                {
                    main.OpenInfo("ОШИБКА СЕТИ", "Превышено время ожидания ответа от сервера.\nПопробуйте позже.");
                }
                catch (Exception ex)
                {
                    main.OpenInfo("ОШИБКА УСТАНОВКИ", "Произошла непредвиденная ошибка: " + ex.Message);
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Converts raw.githubusercontent.com URLs to jsDelivr CDN format.
        /// Example: https://raw.githubusercontent.com/user/repo/branch/path/file.json
        ///       -> https://cdn.jsdelivr.net/gh/user/repo@branch/path/file.json
        /// </summary>
        private static string ConvertToJsDelivr(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            
            const string rawPrefix = "https://raw.githubusercontent.com/";
            if (!url.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase))
                return url; // Not a raw GitHub URL, return as-is
            
            string path = url.Substring(rawPrefix.Length); // "user/repo/branch/path/file.json"
            string[] parts = path.Split('/', 4); // [user, repo, branch, rest]
            if (parts.Length < 4) return url;
            
            return $"https://cdn.jsdelivr.net/gh/{parts[0]}/{parts[1]}@{parts[2]}/{parts[3]}";
        }
    }
}
