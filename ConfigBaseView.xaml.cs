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
            cmbCategory.Items.Add("Доклады");
            cmbCategory.SelectedIndex = 0;

            cmbServer.SelectionChanged += Filter_Changed;
            cmbCategory.SelectionChanged += Filter_Changed;
        }

        public async Task LoadCatalogAsync()
        {
            if (_fullCatalog != null && _fullCatalog.Count > 0) 
            {
                var main = Application.Current.MainWindow as MainWindow;
                if (main != null) {
                    UpdateInstallStates();
                    ApplyFilters();
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
            EmptyStateView.Visibility = Visibility.Collapsed;
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

                ApplyFilters();
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
            ApplyFilters();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        public void RefreshState()
        {
            UpdateInstallStates();
            ApplyFilters();
        }

        public void UpdateInstallStates()
        {
            if (_fullCatalog == null) return;
            
            var main = Application.Current.MainWindow as MainWindow;
            if (main == null) return;

            _lastCloudProfile = main.CurrentProfile;
            
            foreach (var item in _fullCatalog)
                item.IsInstalled = false;

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
                        bool dataExists = true;
                        if (item.Type == "laws" && (p.Laws == null || p.Laws.Count == 0))
                        {
                            dataExists = false;
                        }
                        else if (item.Type == "laws")
                        {
                            try
                            {
                                string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                string fileName = item.Id;
                                if (!fileName.EndsWith(".json")) fileName += ".json";
                                string fp = string.IsNullOrEmpty(srvNum) 
                                    ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                    : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                    
                                if (System.IO.File.Exists(fp))
                                {
                                    string json = System.IO.File.ReadAllText(fp);
                                    var lawsMap = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, LawSection>>(json);
                                    if (lawsMap != null)
                                    {
                                        foreach (var k in lawsMap.Keys)
                                        {
                                            if (!p.Laws.ContainsKey(k)) { dataExists = false; break; }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else if (item.Type == "fines")
                        {
                            if ((p.Fines == null || p.Fines.Count == 0) && (p.Wanted == null || p.Wanted.Count == 0))
                            {
                                dataExists = false;
                            }
                            else
                            {
                                try
                                {
                                    string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                    if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                    string fileName = item.Id;
                                    if (!fileName.EndsWith(".json")) fileName += ".json";
                                    string fp = string.IsNullOrEmpty(srvNum) 
                                        ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                        : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                        
                                    if (System.IO.File.Exists(fp))
                                    {
                                        string jsonFile = System.IO.File.ReadAllText(fp);
                                        var jObj = Newtonsoft.Json.Linq.JToken.Parse(jsonFile);
                                        List<FineArticle> finesToCheck = null;
                                        List<WantedArticle> wantedToCheck = null;
                                        if (jObj is Newtonsoft.Json.Linq.JArray)
                                        {
                                            finesToCheck = JsonConvert.DeserializeObject<List<FineArticle>>(jsonFile);
                                        }
                                        else
                                        {
                                            if (jObj["fines"] != null) finesToCheck = jObj["fines"].ToObject<List<FineArticle>>();
                                            if (jObj["wanted"] != null) wantedToCheck = jObj["wanted"].ToObject<List<WantedArticle>>();
                                        }
                                        
                                        bool anyExists = false;
                                        if (finesToCheck != null && finesToCheck.Count > 0 && p.Fines != null)
                                        {
                                            foreach (var f in finesToCheck)
                                            {
                                                if (p.Fines.Any(existing => existing.id == f.id && existing.name == f.name))
                                                {
                                                    anyExists = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!anyExists && wantedToCheck != null && wantedToCheck.Count > 0 && p.Wanted != null)
                                        {
                                            foreach (var w in wantedToCheck)
                                            {
                                                if (p.Wanted.Any(existing => existing.id == w.id && existing.name == w.name))
                                                {
                                                    anyExists = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!anyExists) dataExists = false;
                                    }
                                }
                                catch { }
                            }
                        }
                        else if (item.Type == "patrols")
                        {
                            if (p.Patrols == null || p.Patrols.Count == 0)
                            {
                                dataExists = false;
                            }
                            else
                            {
                                try
                                {
                                    string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                    if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                    string fileName = item.Id;
                                    if (!fileName.EndsWith(".json")) fileName += ".json";
                                    string fp = string.IsNullOrEmpty(srvNum) 
                                        ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                        : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                        
                                    if (System.IO.File.Exists(fp))
                                    {
                                        string jsonFile = System.IO.File.ReadAllText(fp);
                                        var jObj = Newtonsoft.Json.Linq.JToken.Parse(jsonFile);
                                        List<PatrolBlock> patrolsToCheck = null;
                                        if (jObj is Newtonsoft.Json.Linq.JArray)
                                        {
                                            patrolsToCheck = JsonConvert.DeserializeObject<List<PatrolBlock>>(jsonFile);
                                        }
                                        else if ((jObj["patrols"] ?? jObj["Patrols"]) != null)
                                        {
                                            patrolsToCheck = (jObj["patrols"] ?? jObj["Patrols"]).ToObject<List<PatrolBlock>>();
                                        }
                                        
                                        bool anyExists = false;
                                        if (patrolsToCheck != null && patrolsToCheck.Count > 0 && p.Patrols != null)
                                        {
                                            foreach (var pat in patrolsToCheck)
                                            {
                                                if (p.Patrols.Any(existing => existing.Name == pat.Name))
                                                {
                                                    anyExists = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!anyExists) dataExists = false;
                                    }
                                }
                                catch { }
                            }
                        }
                        else if (item.Type == "binds")
                        {
                            if (p.Binds == null || p.Binds.Count == 0)
                            {
                                dataExists = false;
                            }
                            else
                            {
                                try
                                {
                                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                    string fileName = item.Id;
                                    string fp = System.IO.Path.Combine(appDir, "ConfigBase", fileName);
                                    if (!System.IO.File.Exists(fp) && !fileName.EndsWith(".json")) fp += ".json";
                                    if (System.IO.File.Exists(fp))
                                    {
                                        string jsonFile = System.IO.File.ReadAllText(fp);
                                        var exportData = JsonConvert.DeserializeObject<ExportBindsData>(jsonFile);
                                        if (exportData != null && exportData.Binds != null && exportData.Binds.Count > 0)
                                        {
                                            bool anyBindExists = false;
                                            foreach (var b in exportData.Binds.Values)
                                            {
                                                if (p.Binds.Values.Any(existing => existing.name == b.name))
                                                {
                                                    anyBindExists = true;
                                                    break;
                                                }
                                            }
                                            if (!anyBindExists) dataExists = false;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

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

        public void ApplyFilters()
        {
            if (_fullCatalog == null) return;
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
                    case 5: cat = "patrols"; break;
                }
                if (!string.IsNullOrEmpty(cat)) query = query.Where(i => i.Type == cat);
            }

            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                string term = txtSearch.Text.ToLower();
                query = query.Where(i => i.Title.ToLower().Contains(term) || (i.Author != null && i.Author.ToLower().Contains(term)));
            }

            // Global sorting: put "Все серверы" first
            query = query.OrderByDescending(i => i.Server == "Все серверы").ThenBy(i => i.Server);

            var resultList = query.ToList();
            icCloudList.ItemsSource = resultList;

            if (resultList.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    EmptyStateTitle.Text = "НИЧЕГО НЕ НАЙДЕНО";
                    EmptyStateTitle.Foreground = (System.Windows.Media.Brush)FindResource("GrayBrush");
                    EmptyStateDesc.Text = "По вашему запросу ничего не найдено.";
                    EmptyStateIconSearch.Visibility = Visibility.Collapsed;
                    EmptyStateIconBg.Visibility = Visibility.Collapsed;
                    EmptyStateIconNotFound.Visibility = Visibility.Visible;
                    EmptyStateIconStrike.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyStateTitle.Text = "ДАННАЯ КАТЕГОРИЯ ПУСТА";
                    EmptyStateTitle.Foreground = (System.Windows.Media.Brush)FindResource("GoldBrush");
                    EmptyStateDesc.Text = "В этой категории пока нет конфигураций.";
                    EmptyStateIconSearch.Visibility = Visibility.Visible;
                    EmptyStateIconBg.Visibility = Visibility.Visible;
                    EmptyStateIconNotFound.Visibility = Visibility.Collapsed;
                    EmptyStateIconStrike.Visibility = Visibility.Collapsed;
                }
                EmptyStateView.Visibility = Visibility.Visible;
                icCloudList.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStateView.Visibility = Visibility.Collapsed;
                icCloudList.Visibility = Visibility.Visible;
            }
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
                var main = Application.Current.MainWindow as MainWindow;
                if (main == null) return;

                if (item.IsInstalled)
                {
                    main._dialogHost.ShowAlert(
                        "УДАЛЕНИЕ КОНФИГУРАЦИИ",
                        $"Вы действительно хотите удалить '{item.Title}'?",
                        async () =>
                        {
                            txtLoadingTitle.Text = "УДАЛЕНИЕ КОНФИГУРАЦИИ";
                            txtLoadingDesc.Text = "Удаление файлов конфигурации...";
                            LoadingOverlay.Visibility = Visibility.Visible;
                            
                            try
                            {
                                await Task.Delay(1000);
                                bool shouldSave = false;
                                bool shouldRefreshUi = false;

                                if (item.Type == "profile")
                                {
                                    if (main.MasterData.ContainsKey(item.Title))
                                    {
                                        if (main.CurrentProfile == item.Title)
                                        {
                                            main.MasterData.Remove(item.Title);
                                            main.CurrentProfile = main.MasterData.Count > 0 ? main.MasterData.Keys.First() : "";
                                        }
                                        else
                                        {
                                            main.MasterData.Remove(item.Title);
                                        }
                                        shouldSave = true;
                                        shouldRefreshUi = true;
                                    }
                                }
                                else if (!string.IsNullOrEmpty(main.CurrentProfile) && main.MasterData.ContainsKey(main.CurrentProfile))
                                {
                                    var p = main.MasterData[main.CurrentProfile];
                                    
                                    if (item.Type == "laws")
                                    {
                                        try
                                        {
                                            string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                            if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                            string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                            string fileName = item.Id;
                                            if (!fileName.EndsWith(".json")) fileName += ".json";
                                            string fp = string.IsNullOrEmpty(srvNum) 
                                                ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                                : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                                
                                            if (System.IO.File.Exists(fp))
                                            {
                                                string jsonFile = System.IO.File.ReadAllText(fp);
                                                var lawsMap = JsonConvert.DeserializeObject<Dictionary<string, LawSection>>(jsonFile);
                                                if (lawsMap != null)
                                                {
                                                    foreach (var k in lawsMap.Keys)
                                                    {
                                                        if (p.Laws.ContainsKey(k))
                                                            p.Laws.Remove(k);
                                                        
                                                        var copies = p.Laws.Keys.Where(x => x.StartsWith(k + " (") && x.EndsWith(")")).ToList();
                                                        foreach (var copy in copies)
                                                            p.Laws.Remove(copy);
                                                    }
                                                    shouldSave = true;
                                                    shouldRefreshUi = true;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    else if (item.Type == "fines")
                                    {
                                        try
                                        {
                                            string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                            if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                            string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                            string fileName = item.Id;
                                            if (!fileName.EndsWith(".json")) fileName += ".json";
                                            string fp = string.IsNullOrEmpty(srvNum) 
                                                ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                                : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                                
                                            if (System.IO.File.Exists(fp))
                                            {
                                                string jsonFile = System.IO.File.ReadAllText(fp);
                                                var jObj = Newtonsoft.Json.Linq.JToken.Parse(jsonFile);
                                                
                                                List<FineArticle> finesToRemove = null;
                                                List<WantedArticle> wantedToRemove = null;
                                                
                                                if (jObj is Newtonsoft.Json.Linq.JArray)
                                                {
                                                    finesToRemove = JsonConvert.DeserializeObject<List<FineArticle>>(jsonFile);
                                                }
                                                else
                                                {
                                                    if (jObj["fines"] != null) finesToRemove = jObj["fines"].ToObject<List<FineArticle>>();
                                                    if (jObj["wanted"] != null) wantedToRemove = jObj["wanted"].ToObject<List<WantedArticle>>();
                                                }
                                                
                                                if (finesToRemove != null && p.Fines != null)
                                                {
                                                    foreach (var f in finesToRemove)
                                                    {
                                                        p.Fines.RemoveAll(existing => existing.id == f.id && existing.name == f.name);
                                                    }
                                                }
                                                if (wantedToRemove != null && p.Wanted != null)
                                                {
                                                    foreach (var w in wantedToRemove)
                                                    {
                                                        p.Wanted.RemoveAll(existing => existing.id == w.id && existing.name == w.name);
                                                    }
                                                }
                                                shouldSave = true;
                                                shouldRefreshUi = true;
                                            }
                                        }
                                        catch { }
                                    }
                                    else if (item.Type == "patrols")
                                    {
                                        try
                                        {
                                            string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                                            if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');
                                            string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                            string fileName = item.Id;
                                            if (!fileName.EndsWith(".json")) fileName += ".json";
                                            string fp = string.IsNullOrEmpty(srvNum) 
                                                ? System.IO.Path.Combine(appDir, "ConfigBase", fileName) 
                                                : System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                                                
                                            if (System.IO.File.Exists(fp))
                                            {
                                                string jsonFile = System.IO.File.ReadAllText(fp);
                                                var jObj = Newtonsoft.Json.Linq.JToken.Parse(jsonFile);
                                                List<PatrolBlock> patrolsToRemove = null;
                                                if (jObj is Newtonsoft.Json.Linq.JArray)
                                                {
                                                    patrolsToRemove = JsonConvert.DeserializeObject<List<PatrolBlock>>(jsonFile);
                                                }
                                                else if ((jObj["patrols"] ?? jObj["Patrols"]) != null)
                                                {
                                                    patrolsToRemove = (jObj["patrols"] ?? jObj["Patrols"]).ToObject<List<PatrolBlock>>();
                                                }
                                                
                                                if (patrolsToRemove != null && p.Patrols != null)
                                                {
                                                    foreach (var pat in patrolsToRemove)
                                                    {
                                                        p.Patrols.RemoveAll(existing => existing.Name == pat.Name);
                                                    }
                                                }
                                                shouldSave = true;
                                                shouldRefreshUi = true;
                                            }
                                        }
                                        catch { }
                                    }
                                    else if (item.Type == "binds")
                                    {
                                        try
                                        {
                                            string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                                            string fileName = item.Id;
                                            string fp = System.IO.Path.Combine(appDir, "ConfigBase", fileName);
                                            if (!System.IO.File.Exists(fp) && !fileName.EndsWith(".json")) fp += ".json";
                                            if (System.IO.File.Exists(fp))
                                            {
                                                string jsonFile = System.IO.File.ReadAllText(fp);
                                                var exportData = JsonConvert.DeserializeObject<ExportBindsData>(jsonFile);
                                                if (exportData != null && exportData.Binds != null)
                                                {
                                                    foreach (var eb in exportData.Binds.Values)
                                                    {
                                                        var keysToRemove = p.Binds.Where(kv => kv.Value.name == eb.name).Select(kv => kv.Key).ToList();
                                                        foreach (var k in keysToRemove)
                                                        {
                                                            p.Binds.Remove(k);
                                                        }
                                                    }
                                                    if (exportData.Variables != null)
                                                    {
                                                        foreach (var v in exportData.Variables.Keys)
                                                        {
                                                            p.Variables.Remove(v);
                                                        }
                                                    }
                                                    if (exportData.Groups != null)
                                                    {
                                                        foreach (var g in exportData.Groups)
                                                        {
                                                            p.Groups.Remove(g);
                                                        }
                                                    }
                                                    
                                                    shouldSave = true;
                                                    main.UpdateBindGroups();
                                                    main.UpdateBindsList();
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    if (p.InstalledCloudIds != null && p.InstalledCloudIds.Contains(item.Id))
                                    {
                                        p.InstalledCloudIds.Remove(item.Id);
                                        shouldSave = true;
                                    }
                                }

                                if (shouldSave) main.SaveData();
                                if (shouldRefreshUi) main.RefreshUI();

                                await Dispatcher.InvokeAsync(() => RefreshState(), System.Windows.Threading.DispatcherPriority.Render);
                            }
                            catch (Exception ex)
                            {
                                main.OpenInfo("ОШИБКА УДАЛЕНИЯ", "Произошла ошибка при удалении: " + ex.Message);
                            }
                            finally
                            {
                                LoadingOverlay.Visibility = Visibility.Collapsed;
                            }
                        }
                    );
                    return;
                }
                
                bool shouldSave = false;
                bool shouldRefreshUi = false;
                
                txtLoadingTitle.Text = "УСТАНОВКА КОНФИГУРАЦИИ";
                txtLoadingDesc.Text = "Загрузка файлов из локальной базы...";
                LoadingOverlay.Visibility = Visibility.Visible;
                txtErrorMsg.Text = "Загрузка конфигурации...";
                ErrorView.Visibility = Visibility.Collapsed;

                try
                {
                    await Task.Delay(1000);
                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                    
                    string srvNum = System.Text.RegularExpressions.Regex.Match(item.Server ?? "", @"\d+").Value;
                    if (!string.IsNullOrEmpty(srvNum)) srvNum = srvNum.PadLeft(2, '0');

                    string fileName = item.Id;
                    if (item.Type == "laws" || item.Type == "fines" || item.Type == "patrols") {
                        if (!fileName.EndsWith(".json")) fileName += ".json";
                    }

                    string filePath;
                    if (!string.IsNullOrEmpty(srvNum)) {
                        filePath = System.IO.Path.Combine(appDir, "ConfigBase", srvNum, fileName);
                    } else {
                        filePath = System.IO.Path.Combine(appDir, "ConfigBase", fileName);
                        if (!System.IO.File.Exists(filePath) && !fileName.EndsWith(".json")) filePath += ".json";
                    }

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
                            var sysKeys = new List<string> {
                                main.btnKeyToggle.Content?.ToString(),
                                main.btnKeyToggleScript.Content?.ToString(),
                                main.btnKeyPrev.Content?.ToString(),
                                main.btnKeyNext.Content?.ToString(),
                                main.btnRadialKey.Content?.ToString(),
                                main.btnBinderHintKey.Content?.ToString(),
                                main.btnIssueFineKey.Content?.ToString(),
                                main.btnCancelFineKey.Content?.ToString(),
                                main.btnStopBindKey.Content?.ToString()
                            }.Where(k => !string.IsNullOrEmpty(k) && k != "КЛАВИША: НЕТ" && k.ToUpper() != "НЕТ").ToList();

                            int conflicts = 0;
                            if (pf.Binds != null) {
                                foreach(var b in pf.Binds.Values) {
                                    if (b.active && !string.IsNullOrEmpty(b.key) && sysKeys.Contains(b.key)) {
                                        b.active = false;
                                        conflicts++;
                                    }
                                }
                            }
                            if (pf.Laws != null) {
                                foreach(var l in pf.Laws.Values) {
                                    if (!string.IsNullOrEmpty(l.Hotkey) && sysKeys.Contains(l.Hotkey)) {
                                        l.Hotkey = "";
                                        conflicts++;
                                    }
                                }
                            }

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
                            
                            if (conflicts > 0) {
                                main.OpenInfo("ИМПОРТ ПРОФИЛЯ", $"Профиль импортирован, но {conflicts} биндов/законов конфликтовали с системными клавишами. Конфликтующие бинды были отключены (убрана галочка активности).", true);
                            }
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
                    else if (item.Type == "patrols")
                    {
                        var jObj = Newtonsoft.Json.Linq.JToken.Parse(json);
                        bool isUpdated = false;
                        if (string.IsNullOrEmpty(main.CurrentProfile) || !main.MasterData.ContainsKey(main.CurrentProfile)) throw new Exception("Нет активного профиля для установки докладов");
                        var p = main.MasterData[main.CurrentProfile];

                        List<PatrolBlock> patrolsList = null;
                        if (jObj is Newtonsoft.Json.Linq.JArray) {
                            patrolsList = JsonConvert.DeserializeObject<List<PatrolBlock>>(json);
                        } else if ((jObj["patrols"] ?? jObj["Patrols"]) != null) {
                            patrolsList = (jObj["patrols"] ?? jObj["Patrols"]).ToObject<List<PatrolBlock>>();
                        }

                        if (patrolsList != null) {
                            if (p.Patrols == null) p.Patrols = new List<PatrolBlock>();
                            foreach (var pat in patrolsList) {
                                p.Patrols.RemoveAll(existing => existing.Name == pat.Name);
                                pat.id = Guid.NewGuid().ToString("N");
                                p.Patrols.Add(pat);
                            }
                            isUpdated = true;
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

                            var sysKeys = new List<string> {
                                main.btnKeyToggle.Content?.ToString(),
                                main.btnKeyToggleScript.Content?.ToString(),
                                main.btnKeyPrev.Content?.ToString(),
                                main.btnKeyNext.Content?.ToString(),
                                main.btnRadialKey.Content?.ToString(),
                                main.btnBinderHintKey.Content?.ToString(),
                                main.btnIssueFineKey.Content?.ToString(),
                                main.btnCancelFineKey.Content?.ToString(),
                                main.btnStopBindKey.Content?.ToString()
                            }.Where(k => !string.IsNullOrEmpty(k) && k != "КЛАВИША: НЕТ" && k.ToUpper() != "НЕТ").ToList();

                            if (exportData.Binds != null)
                            {
                                foreach (var b in exportData.Binds)
                                {
                                    if (b.Value.active && !string.IsNullOrEmpty(b.Value.key))
                                    {
                                        if (sysKeys.Contains(b.Value.key)) {
                                            b.Value.active = false;
                                            conflictsResolved++;
                                        }

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
                            
                            if (conflictsResolved > 0) {
                                main.OpenInfo("ИМПОРТ БИНДОВ", $"Бинды установлены, но {conflictsResolved} из них конфликтовали с системными клавишами или другими биндами. Конфликтующие бинды были отключены.", true);
                            }
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
