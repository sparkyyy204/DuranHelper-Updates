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
                string hex = ConfigBaseView.GetServerColorHex(Server);
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
        }
        [JsonIgnore]
        public SolidColorBrush BadgeBgBrush
        {
            get
            {
                string hex = ConfigBaseView.GetServerColorHex(Server);
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = 0x26;
                return new SolidColorBrush(c);
            }
        }
    }

    public partial class ConfigBaseView : UserControl
    {
        private List<CloudItem> _fullCatalog = new List<CloudItem>();
        private CancellationTokenSource _loadCts;
        private string _lastCloudProfile = null;

        public ConfigBaseView()
        {
            InitializeComponent();
            this.Loaded += ConfigBaseView_Loaded;
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
            cmbCategory.Items.Add("Калькулятор");
            cmbCategory.SelectedIndex = 0;

            cmbServer.SelectionChanged += Filter_Changed;
            cmbCategory.SelectionChanged += Filter_Changed;
        }

        public async Task LoadCatalogAsync()
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
            
            txtLoadingTitle.Text = "ПОДКЛЮЧЕНИЕ К БАЗЕ";
            txtLoadingDesc.Text = "Синхронизация локального каталога...";
            LoadingOverlay.Visibility = Visibility.Visible;
            ErrorView.Visibility = Visibility.Collapsed;
            icCloudList.Visibility = Visibility.Collapsed;

            try
            {
                string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                string catalogPath = System.IO.Path.Combine(appDir, "ConfigBase", "catalog.json");
                
                if (System.IO.File.Exists(catalogPath))
                {
                    string json = await System.IO.File.ReadAllTextAsync(catalogPath, ct);
                    _fullCatalog = await Task.Run(() => JsonConvert.DeserializeObject<List<CloudItem>>(json)) ?? new List<CloudItem>();
                }
                else
                {
                    throw new Exception("Файл ConfigBase/catalog.json не найден.");
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
                    txtErrorMsg.Text = "Не удалось загрузить базу законов. Проверьте папку ConfigBase.";
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
            _ = LoadCatalogAsync();
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
                txtLoadingDesc.Text = "Загрузка файлов из локальной базы...";
                LoadingOverlay.Visibility = Visibility.Visible;
                txtErrorMsg.Text = "Загрузка конфигурации...";
                ErrorView.Visibility = Visibility.Collapsed;

                try
                {
                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                    string fileName = System.IO.Path.GetFileName(item.FileUrl);
                    string filePath = System.IO.Path.Combine(appDir, "ConfigBase", fileName);

                    if (!System.IO.File.Exists(filePath))
                    {
                        throw new Exception($"Файл {fileName} не найден в базе законов.");
                    }

                    string json = await System.IO.File.ReadAllTextAsync(filePath);

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
                        var jObj = Newtonsoft.Json.Linq.JToken.Parse(json);
                        bool isUpdated = false;
                        if (string.IsNullOrEmpty(main.CurrentProfile) || !main.MasterData.ContainsKey(main.CurrentProfile)) throw new Exception("Нет активного профиля для установки калькулятора");
                        var p = main.MasterData[main.CurrentProfile];

                        if (jObj is Newtonsoft.Json.Linq.JArray) {
                            var finesList = JsonConvert.DeserializeObject<List<FineArticle>>(json);
                            if (finesList != null) { 
                                if (p.Fines == null) p.Fines = new List<FineArticle>();
                                p.Fines.AddRange(finesList); 
                                isUpdated = true; 
                            }
                        } else {
                            if (jObj["fines"] != null) {
                                var finesList = jObj["fines"].ToObject<List<FineArticle>>();
                                if (finesList != null) { 
                                    if (p.Fines == null) p.Fines = new List<FineArticle>();
                                    p.Fines.AddRange(finesList); 
                                    isUpdated = true; 
                                }
                            }
                            if (jObj["wanted"] != null) {
                                var wantedList = jObj["wanted"].ToObject<List<WantedArticle>>();
                                if (wantedList != null) { 
                                    if (p.Wanted == null) p.Wanted = new List<WantedArticle>();
                                    p.Wanted.AddRange(wantedList); 
                                    isUpdated = true; 
                                }
                            }
                        }

                        if (isUpdated)
                        {
                            shouldSave = true;
                            shouldRefreshUi = true;
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

    
        private void ConfigBaseView_Loaded(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this) as MainWindow;
            if (mw != null && mw.AcceptedConfigWarning)
            {
                WarningOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void cbAcceptWarning_Checked(object sender, RoutedEventArgs e)
        {
            btnAcceptWarning.IsEnabled = cbAcceptWarning.IsChecked == true;
        }

        private void btnAcceptWarning_Click(object sender, RoutedEventArgs e)
        {
            WarningOverlay.Visibility = Visibility.Collapsed;
            var mw = Window.GetWindow(this) as MainWindow;
            if (mw != null)
            {
                mw.AcceptedConfigWarning = true;
                mw.SaveData();
            }
        }
}
}
