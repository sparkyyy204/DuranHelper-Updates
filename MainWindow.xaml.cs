using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Security.Principal;
using System.Text;


namespace FSB_helper_C__
{
    public partial class MainWindow : Window
    {
        public Dictionary<string, ProfileData> MasterData = new Dictionary<string, ProfileData>();
        public Dictionary<string, Dictionary<string, string>> CustomThemes = new Dictionary<string, Dictionary<string, string>>();
        public string CurrentProfile = "";
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> AppLogs = new System.Collections.ObjectModel.ObservableCollection<LogEntry>();
        
        private string _currentBindGroup = "ВСЕ";
        private string _lastLawSection = "";
        private System.Threading.CancellationTokenSource _renderLawsCts;
        private Task _lawsRenderTask;
        private BindItem _tempBind;

        private LawItem _tempLaw;
        private string _capturedKey = "";
        private string _captureTarget = "";

        // WPF Key.ToString() → AHK hotkey name mapping (по документации AHK)
        private static readonly Dictionary<string, string> WpfToAhkKey = new(StringComparer.OrdinalIgnoreCase) {
            // === OEM / Punctuation keys ===
            {"Oem5", "\\"}, {"OemPipe", "\\"}, 
            {"Oem3", "``"}, {"OemTilde", "``"},              // Ё / `~
            {"OemPlus", "="}, {"OemMinus", "-"},              // +/= и -/_  (основная)
            {"OemQuestion", "/"}, {"Oem2", "/"},              // ?/
            {"OemComma", ","}, {"OemPeriod", "."},             // , .
            {"OemSemicolon", ";"}, {"Oem1", ";"},             // ;:
            {"OemQuotes", "'"}, {"Oem7", "'"},                // '"
            {"OemOpenBrackets", "["}, {"Oem4", "["},          // [{
            {"OemCloseBrackets", "]"}, {"Oem6", "]"},         // ]}
            
            // === Numpad (NumLock ON) ===
            {"NumPad0", "Numpad0"}, {"NumPad1", "Numpad1"}, {"NumPad2", "Numpad2"},
            {"NumPad3", "Numpad3"}, {"NumPad4", "Numpad4"}, {"NumPad5", "Numpad5"},
            {"NumPad6", "Numpad6"}, {"NumPad7", "Numpad7"}, {"NumPad8", "Numpad8"},
            {"NumPad9", "Numpad9"},
            {"Multiply", "NumpadMult"}, {"Divide", "NumpadDiv"},
            {"Add", "NumpadAdd"}, {"Subtract", "NumpadSub"},
            {"Decimal", "NumpadDot"},
            
            // === Lock keys ===
            {"Capital", "CapsLock"}, {"Scroll", "ScrollLock"}, {"NumLock", "NumLock"},
            
            // === Navigation ===
            {"Prior", "PgUp"}, {"Next", "PgDn"},
            {"Return", "Enter"},
            
            // === Special keys ===
            {"Back", "Backspace"}, {"Snapshot", "PrintScreen"},
            {"Cancel", "CtrlBreak"}, {"Pause", "Pause"},
            {"Apps", "AppsKey"}, {"Sleep", "Sleep"},
            {"Help", "Help"},
            
            // === Windows keys ===
            {"LWin", "LWin"}, {"RWin", "RWin"},

            // === Browser/Multimedia ===
            {"BrowserBack", "Browser_Back"}, {"BrowserForward", "Browser_Forward"},
            {"BrowserRefresh", "Browser_Refresh"}, {"BrowserStop", "Browser_Stop"},
            {"BrowserSearch", "Browser_Search"}, {"BrowserFavorites", "Browser_Favorites"},
            {"BrowserHome", "Browser_Home"},
            {"VolumeMute", "Volume_Mute"}, {"VolumeDown", "Volume_Down"}, {"VolumeUp", "Volume_Up"},
            {"MediaNextTrack", "Media_Next"}, {"MediaPreviousTrack", "Media_Prev"},
            {"MediaStop", "Media_Stop"}, {"MediaPlayPause", "Media_Play_Pause"},
            {"LaunchMail", "Launch_Mail"}, {"SelectMedia", "Launch_Media"},
            {"LaunchApplication1", "Launch_App1"}, {"LaunchApplication2", "Launch_App2"},
        };

        // Display names for the UI (friendly labels)
        private static readonly Dictionary<string, string> WpfToDisplayKey = new(StringComparer.OrdinalIgnoreCase) {
            {"Oem5", "\\"}, {"OemPipe", "\\"},
            {"Oem3", "Ё"}, {"OemTilde", "Ё"},
            {"OemPlus", "="}, {"OemMinus", "-"},
            {"OemQuestion", "/"}, {"Oem2", "/"},
            {"OemComma", ","}, {"OemPeriod", "."},
            {"OemSemicolon", ";"}, {"Oem1", ";"},
            {"OemQuotes", "'"}, {"Oem7", "'"},
            {"OemOpenBrackets", "["}, {"Oem4", "["},
            {"OemCloseBrackets", "]"}, {"Oem6", "]"},
            {"Multiply", "Num *"}, {"Divide", "Num /"},
            {"Add", "Num +"}, {"Subtract", "Num -"},
            {"Decimal", "Num ."}, {"Capital", "CapsLock"},
            {"Prior", "PgUp"}, {"Next", "PgDn"},
            {"Return", "Enter"}, {"Back", "Backspace"},
            {"Snapshot", "PrintScreen"}, {"Apps", "Menu"},
        };
        private string _tempRenameTarget = "";
        private bool _isLoadingRtf = false; 
        private bool _startMinimized = false;        
        private DragAdorner _dragAdorner;
        private Point _dragStartPoint;
        private string _reorderMode = "";
        private List<string> _reorderList = new List<string>();
        private string _importTargetContext = "";
        private List<ImportItem> _tempImportList = new List<ImportItem>();
        private string _capturingSection = "";


        private bool _ignoreEvents = false;
        private bool _isLoaded = false;
        private string _lastAppliedLauncherTheme = null;
        private bool _isStartupStatsAnimating = false;
        private bool _isEngineRunning = false;
        private bool _isClosing = false;
        private TaskCompletionSource<bool> _updateDecision;

        private int _devClickCount = 0;
        private DateTime _lastDevClick = DateTime.MinValue;

        private System.Windows.Forms.NotifyIcon _trayIcon;
        private System.Windows.Threading.DispatcherTimer _opacityDebounceTimer;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public Int32 X; public Int32 Y; }

        private bool _isUpgrade = false;



        public MainWindow()
        {
            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return; 
            }

            try {
                string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DURAN HELPER");
                if (!Directory.Exists(docPath)) Directory.CreateDirectory(docPath);
                
                string[] filesToMigrate = { "Profiles.json", "Settings.json", "laws.json", "fines.json" };
                foreach (var f in filesToMigrate) {
                    string src = Path.Combine(Environment.CurrentDirectory, f);
                    string dst = Path.Combine(docPath, f);
                    if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                    
                    if (File.Exists(src)) {
                        if (!File.Exists(dst)) {
                            try { File.Move(src, dst); _isUpgrade = true; } catch { }
                        } else {
                            try { File.Delete(src); } catch { }
                        }
                    }
                }
                Environment.CurrentDirectory = docPath;
            } catch { }

            InitializeComponent();
            _dialogHost.OnInstructionClick = () => { OpenDialog(WinSectionGuide); };
            BinderEditorControl.Init(this);
            SetupTrayIcon();
            
            _startMinimized = Environment.GetCommandLineArgs().Contains("-autorun");
            
            icLogs.ItemsSource = AppLogs;
            
            LoadData();
            CheckAutorun();
            _isLoaded = true;
            RefreshUI();
            
            AddLog("Приложение DURAN HELPER запущено", "#2ea043");
        }

        public void AddLog(string message, string colorHex = "#e6edf3")
        {
            Dispatcher.Invoke(() =>
            {
                AppLogs.Insert(0, new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    ColorBrush = (Brush)new BrushConverter().ConvertFrom(colorHex)
                });
                if (AppLogs.Count > 100) AppLogs.RemoveAt(AppLogs.Count - 1);
                
                // Keep the ScrollViewer at the top since we insert at 0. 
                // Ah! AppLogs.Insert(0, ...) means newest log is at the TOP. 
                // So the user doesn't need to scroll down! But if there's an issue with logs not updating visually:
                // We should make sure it scrolls to TOP.
                if (svLogs != null) svLogs.ScrollToTop();
            });
        }

        private bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdmin()
        {
            ProcessStartInfo proc = new ProcessStartInfo { 
                UseShellExecute = true, 
                WorkingDirectory = Environment.CurrentDirectory, 
                FileName = Environment.ProcessPath, 
                Verb = "runas" 
            };
            try { Process.Start(proc); } catch { }

            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            if (_startMinimized) 
            {
                SplashContent.Visibility = Visibility.Collapsed;
                SplashBg.Visibility = Visibility.Collapsed;
                MainBackgroundBorder.Visibility = Visibility.Visible;
                MainBackgroundBorder.Opacity = 1;
                SplashUI.HorizontalAlignment = HorizontalAlignment.Stretch;
                SplashUI.VerticalAlignment = VerticalAlignment.Stretch;
                SplashUI.Width = double.NaN;
                SplashUI.Height = double.NaN;
                SplashUI.ClipToBounds = false;
                this.WindowState = WindowState.Minimized;
                this.Hide();
                return;
            }

            // === SVG-matched timings (loading.svg 10s cycle → one-shot) ===
            // Bar fills: 0.5s→2.2s = 1.7s duration
            // Color shift: 2.1s→2.4s = 0.3s duration  
            // Status init visible: 0.8s→1.8s, fades out 1.8→2.1s
            // Status ready fades in: 2.1s→2.4s
            // Splash card fades out: 2.5s→2.8s = 0.3s
            // Main UI expand: 2.6s→3.2s = 0.6s

            // === Dash 1: Update Check ===
            var colorEaseUpd = new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 3 };
            Dash1.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { EasingFunction = colorEaseUpd });
            await Task.Delay(200);

            // === Update check (before main loading) ===
            if (UpdateManager.IsUpdateCheckEnabled())
            {
                // Show "ПРОВЕРКА ОБНОВЛЕНИЙ..." text
                SplashStatusInit.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                await Task.Delay(200);
                SplashStatusUpdate.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });

                int updateStatus = await UpdateManager.CheckForUpdateAsync();
                bool anyError = (updateStatus == -1);

                if (updateStatus == 1)
                {
                    // Hide progress bar, show separator
                    SplashBar.Visibility = Visibility.Collapsed;
                    SplashBarTrack.Visibility = Visibility.Collapsed;


                    // Expand splash
                    SplashUI.BeginAnimation(HeightProperty, new DoubleAnimation(200, 340, TimeSpan.FromSeconds(0.4)) { EasingFunction = colorEaseUpd });

                    // Set header
                    SplashStatusUpdate.Opacity = 1;
                    SplashStatusUpdate.Text = "ДОСТУПНО ОБНОВЛЕНИЕ ЛАУНЧЕРА";
                    lblUpdateVersion.Text = $"ВЕРСИЯ {UpdateManager.LatestVersion}";

                    // Set changelog
                    lblUpdateChangelog.Text = string.IsNullOrEmpty(UpdateManager.ReleaseNotes) ? "Описание обновления недоступно." : UpdateManager.ReleaseNotes;

                    // Show panel with fade-in
                    Canvas.SetTop(SplashUpdatePanel, 128);
                    SplashUpdatePanel.Visibility = Visibility.Visible;
                    UpdateButtonsPanel.Visibility = Visibility.Visible;
                    SplashUpdatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)) { EasingFunction = colorEaseUpd });

                    // Wait for user
                    _updateDecision = new TaskCompletionSource<bool>();
                    bool wantsUpdate = await _updateDecision.Task;

                    if (wantsUpdate)
                    {
                        // Smooth collapse: fade out changelog panel
                        SplashUpdatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = colorEaseUpd });
                        await Task.Delay(450);
                        SplashUpdatePanel.Visibility = Visibility.Collapsed;

                        // Smooth shrink window
                        SplashUI.BeginAnimation(HeightProperty, new DoubleAnimation(340, 200, TimeSpan.FromSeconds(0.5)) { EasingFunction = colorEaseUpd });
                        await Task.Delay(550);
                        SplashUI.BeginAnimation(HeightProperty, null);
                        SplashUI.Height = 200;

                        // Update header
                        SplashStatusUpdate.Text = "ЗАГРУЗКА... 0%";

                        // Brief pause before bar appears
                        await Task.Delay(200);

                        // Smooth simultaneous fade-in of track + bar filling
                        SplashBarTrack.Width = 300;
                        SplashBarTrack.Opacity = 0;
                        SplashBarTrack.Visibility = Visibility.Visible;
                        SplashBarTrack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8)) { EasingFunction = colorEaseUpd });
                        SplashBar.Width = 0;
                        SplashBar.Opacity = 1;
                        SplashBar.Visibility = Visibility.Visible;

                        bool success = await UpdateManager.DownloadAndApplyUpdateAsync(percent =>
                        {
                            Dispatcher.Invoke(() => 
                            {
                                SplashStatusUpdate.Text = $"ЗАГРУЗКА... {percent}%";
                                SplashBar.Width = 3.0 * percent;
                            });
                        });

                        if (success)
                        {
                            SplashStatusUpdate.Text = "ПЕРЕЗАПУСК...";
                            await Task.Delay(500);
                            Application.Current.Shutdown();
                            return;
                        }
                        else
                        {
                            SplashStatusUpdate.Text = "ОШИБКА ЗАГРУЗКИ";
                            await Task.Delay(1500);
                        }
                    }
                    else
                    {
                        // User clicked ПОЗЖЕ — collapse panel
                        SplashUpdatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                        await Task.Delay(200);
                        SplashUpdatePanel.Visibility = Visibility.Collapsed;
                    }

                    // Contract splash back
                    var contractAnim = new DoubleAnimation(SplashUI.ActualHeight, 200, TimeSpan.FromSeconds(0.3)) { EasingFunction = colorEaseUpd };
                    SplashUI.BeginAnimation(HeightProperty, contractAnim);
                    await Task.Delay(300);
                    SplashUI.BeginAnimation(HeightProperty, null);
                    SplashUI.Height = 200;
                    SplashBarTrack.Visibility = Visibility.Visible;
                    SplashBar.Visibility = Visibility.Visible;
                }

                // === ASI Local Check (Silent) ===
                string gamePath = txtGamePath?.Text ?? "";
                if (!string.IsNullOrEmpty(gamePath) && System.IO.Directory.Exists(gamePath)) {
                    try {
                        string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                        string localAsi = System.IO.Path.Combine(appDir, "DuranOverlay.asi");
                        string targetAsi = System.IO.Path.Combine(gamePath, "DuranOverlay.asi");
                        
                        bool needsAsiUpdate = false;
                        if (System.IO.File.Exists(localAsi) && System.IO.File.Exists(targetAsi)) {
                            long localSize = new System.IO.FileInfo(localAsi).Length;
                            long gameSize = new System.IO.FileInfo(targetAsi).Length;
                            if (localSize != gameSize) needsAsiUpdate = true;
                        } else if (System.IO.File.Exists(localAsi) && !System.IO.File.Exists(targetAsi)) {
                            needsAsiUpdate = true; // Auto-install if missing but path is set
                        }

                        if (needsAsiUpdate) {
                            if (IsFileLockedForWrite(targetAsi)) {
                                OpenInfo("ВНИМАНИЕ", "Файл DuranOverlay.asi занят процессом игры.\nСначала полностью закройте игру (gta_sa.exe), чтобы лаунчер мог обновить скрипт.");
                            } else {
                                System.IO.File.Copy(localAsi, targetAsi, true);
                            }
                        }
                    } catch {
                    } 
                }

                if (anyError)
                {
                }

                // Fade out update status text
                SplashStatusUpdate.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                await Task.Delay(200);

                // Restore init text for normal loading sequence
                SplashStatusInit.Text = "ИНИЦИАЛИЗАЦИЯ ЯДРА...";
                SplashStatusInit.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                await Task.Delay(200);
            }
            else
            {
                // Update checks are disabled - quickly transition UI to init state
                SplashStatusInit.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                await Task.Delay(200);

                SplashStatusInit.Text = "ИНИЦИАЛИЗАЦИЯ ЯДРА...";
                SplashStatusInit.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEaseUpd });
                // No need to light up Dash2 here, it will be lit up below for Init

            }

            // ===================================================================
            // LOADING + EXPANSION SEQUENCE
            // ===================================================================
            // Standard TabControl: only selected tab is rendered. 
            // No pre-warming needed — Dashboard is the only active tab during expansion.

            // === Dash 2: Local Init ===
            Dash2.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { EasingFunction = colorEaseUpd });
            await Task.Delay(300);

            // Set default gold colors for the dashes
            var goldCol2 = ((SolidColorBrush)Application.Current.Resources["GoldBrush"]).Color;
            Dash1Brush.Color = goldCol2; Dash1Glow.Color = goldCol2;
            Dash2Brush.Color = goldCol2; Dash2Glow.Color = goldCol2;

            // Pre-warm: make main UI visible at opacity 0 (behind splash) to create visual trees
            MainBackgroundBorder.Visibility = Visibility.Visible;
            MainBackgroundBorder.Opacity = 0;
            await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Render);

            // Cycle tabs to pre-warm visual trees
            int originalTab = Tabs.SelectedIndex;
            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                if (Tabs.Items[i] is TabItem warmTab) warmTab.Visibility = Visibility.Visible;
                await Task.Delay(200);
                Tabs.SelectedIndex = i;
                await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Render);

                // Laws tab is the heaviest on first open: force data/layout warm-up now.
                if (i == 1 && _hasProfile && !string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile))
                {
                    UpdateLawsList();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    // Prewarm first dropdown open animation in Laws tab.
                    if (cbSections != null) {
                        cbSections.ApplyTemplate();
                        cbSections.UpdateLayout();
                        bool wasOpen = cbSections.IsDropDownOpen;
                        cbSections.IsDropDownOpen = true;
                        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                        cbSections.IsDropDownOpen = wasOpen;
                        cbSections.UpdateLayout();
                    }
                }
            }
            Tabs.SelectedIndex = originalTab;
            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                if (i == originalTab) continue;
                if (Tabs.Items[i] is TabItem warmedTab) warmedTab.Visibility = Visibility.Hidden;
            }
            // Ensure final tab is Visible after pre-warm (pre-warm sets tabs to Hidden)
            if (originalTab >= 0 && originalTab < Tabs.Items.Count && Tabs.Items[originalTab] is TabItem finalTab)
                finalTab.Visibility = Visibility.Visible;
            await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Render);

            // Zero out stats before UI fades in to prevent blinking destination values
            if (lblStatBinds != null) {
                lblStatBinds.Text = "0";
                lblStatSections.Text = "0";
                lblStatLaws.Text = "0";
            }

            // --- Color shift gold → green (0.3s) ---
            Color green = ((SolidColorBrush)Application.Current.Resources["GreenBrush"]).Color;
            var colorDur = TimeSpan.FromSeconds(0.3);
            var colorEase = new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 3 };

            Dash1Brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(green, colorDur) { EasingFunction = colorEase });
            Dash1Glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(green, colorDur) { EasingFunction = colorEase });
            Dash2Brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(green, colorDur) { EasingFunction = colorEase });
            Dash2Glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(green, colorDur) { EasingFunction = colorEase });

            splashDBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(green, colorDur) { EasingFunction = colorEase });

            // Status swap
            SplashStatusInit.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEase });
            await Task.Delay(200);
            SplashStatusReady.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)) { EasingFunction = colorEase });
            
            // MainBackgroundBorder is already Visible+Opacity=0 from XAML (layout done at startup, zero cost here).
            // Pre-set theme flag so UpdateDashboardState skips ApplyTheme (which would flicker splash brushes).
            if (string.IsNullOrEmpty(_lastAppliedLauncherTheme)) {
                _lastAppliedLauncherTheme = "Default (Dark Blue)";
                if (rbThemeBlack?.IsChecked == true) _lastAppliedLauncherTheme = "Black (AMOLED)";
                else if (rbThemeGrey?.IsChecked == true) _lastAppliedLauncherTheme = "Grey (Sport red)";
            }
            MainBackgroundBorder.Clip = new RectangleGeometry(new Rect(270, 225, 460, 200), 16, 16);
            _isStartupStatsAnimating = !string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile);
            UpdateDashboardState();
            
            // Force shield hidden (UpdateDashboardState may have faded it in)
            canvasShieldBg.BeginAnimation(OpacityProperty, null);
            canvasShieldBg.Opacity = 0;
            
            await Task.Delay(250);

            // --- SIMULTANEOUS: fade out splash card + expand window (cross-fade, no gap) ---
            var expandDur = TimeSpan.FromSeconds(0.6);
            var svgSpline = new KeySpline(0.77, 0, 0.175, 1);
            var fadeEase = new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 2 };

            DoubleAnimationUsingKeyFrames MakeSpline(double from, double to) {
                var anim = new DoubleAnimationUsingKeyFrames();
                anim.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(expandDur), svgSpline));
                return anim;
            }

            SplashUI.ClipToBounds = false;
            
            // Keep splash card stretched with SplashUI during expansion.
            // Fixed-size lock made it look like the background window expands instead of the splash itself.
            SplashBg.BeginAnimation(WidthProperty, null);
            SplashBg.BeginAnimation(HeightProperty, null);
            SplashCard.BeginAnimation(WidthProperty, null);
            SplashCard.BeginAnimation(HeightProperty, null);
            SplashBg.Width = double.NaN;
            SplashBg.Height = double.NaN;
            SplashCard.Width = double.NaN;
            SplashCard.Height = double.NaN;
            SplashBg.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashBg.VerticalAlignment = VerticalAlignment.Stretch;
            SplashCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashCard.VerticalAlignment = VerticalAlignment.Stretch;

            // Freeze splash content at base card size and center only for final cross-fade,
            // so it does not drift to top-left while SplashUI expands.
            SplashContent.BeginAnimation(WidthProperty, null);
            SplashContent.BeginAnimation(HeightProperty, null);
            SplashContent.Width = SplashContent.ActualWidth;
            SplashContent.Height = SplashContent.ActualHeight;
            SplashContent.HorizontalAlignment = HorizontalAlignment.Center;
            SplashContent.VerticalAlignment = VerticalAlignment.Center;
            // Pre-layout before starting expansion to avoid first-frame hitch.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            // Cross-fade: splash card fades out (0.3s) WHILE expansion runs (0.6s)
            SplashContent.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = fadeEase });
            SplashBg.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = fadeEase });
            SplashCard.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = fadeEase });
            
            // Show main UI and expand simultaneously
            MainBackgroundBorder.Opacity = 1;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            var rectAnim = new RectAnimationUsingKeyFrames();
            rectAnim.KeyFrames.Add(new SplineRectKeyFrame(new Rect(0, 0, 1000, 650), KeyTime.FromTimeSpan(expandDur), svgSpline));
            
            // Re-instantiate the geometry to ensure radii are kept just in case
            if (MainBackgroundBorder.Clip is RectangleGeometry rg) {
                rg.RadiusX = 16;
                rg.RadiusY = 16;
            } else {
                MainBackgroundBorder.Clip = new RectangleGeometry(new Rect(270, 225, 460, 200), 16, 16);
            }
            MainBackgroundBorder.Clip.BeginAnimation(RectangleGeometry.RectProperty, rectAnim);
            
            // Pre-hide shield and sidebar if no profiles
            if (MasterData.Count == 0 || string.IsNullOrEmpty(CurrentProfile)) {
                canvasShieldBg.Visibility = Visibility.Collapsed;
                navLaws.Visibility = Visibility.Collapsed;
                navBinds.Visibility = Visibility.Collapsed;
            }
            
            // Fade and slide in Sidebar and Content UI
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (SidebarPanel != null && SidebarPanel_TT != null) {
                SidebarPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(0.2), EasingFunction = ease });
                SidebarPanel_TT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-30, 0, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(0.2), EasingFunction = ease });
            }
            if (Tabs != null && Tabs_TT != null) {
                Tabs.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(0.3), EasingFunction = ease });
                Tabs_TT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(0.3), EasingFunction = ease });
            }
            
            SplashUI.BeginAnimation(WidthProperty, MakeSpline(460, 1000));
            SplashUI.BeginAnimation(HeightProperty, MakeSpline(200, 650));
            
            await Task.Delay(600); // Wait for full expansion

            // Safely collapse splash elements
            SplashContent.Visibility = Visibility.Collapsed;
            SplashBg.Visibility = Visibility.Collapsed;
            SplashCard.Visibility = Visibility.Collapsed;

            // Finalize
            SplashUI.BeginAnimation(WidthProperty, null);
            SplashUI.BeginAnimation(HeightProperty, null);
            SplashUI.Width = double.NaN;
            SplashUI.Height = double.NaN;
            SplashUI.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashUI.VerticalAlignment = VerticalAlignment.Stretch;
            SplashUI.IsHitTestVisible = false;
            SplashContent.BeginAnimation(WidthProperty, null);
            SplashContent.BeginAnimation(HeightProperty, null);
            SplashContent.Width = double.NaN;
            SplashContent.Height = double.NaN;
            SplashContent.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashContent.VerticalAlignment = VerticalAlignment.Stretch;
            
            if (MainBackgroundBorder.Clip != null) {
                MainBackgroundBorder.Clip.BeginAnimation(RectangleGeometry.RectProperty, null);
                MainBackgroundBorder.Clip = null;
            }
            
            // Draw the shield and start stats animation
            AnimateShieldDraw();
            _ = AnimateDashboardStatsAsync();
        }

        private TrayPopupWindow _trayPopup;

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            _trayIcon.Visible = true;
            _trayIcon.Text = "DURAN HELPER";
            
            _trayPopup = new TrayPopupWindow();
            _trayPopup.RestoreRequested += () => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); };
            _trayPopup.CloseRequested += () => { App_Close(null, null); };

            _trayIcon.MouseClick += (s, e) => { 
                if (e.Button == System.Windows.Forms.MouseButtons.Left) {
                    if (_isClosing || !this.IsLoaded) return;
                    try {
                        this.Show(); 
                        this.WindowState = WindowState.Normal; 
                        this.Activate(); 
                    } catch {}
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right) {
                    if (_isClosing || _trayPopup == null) return;
                    var pos = System.Windows.Forms.Cursor.Position;
                    _trayPopup.ShowAtPosition(pos.X, pos.Y);
                }
            };
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing) {
                e.Cancel = true;
                App_Close(null, null);
            }
        }

        private void App_Minimize(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private async void HideLauncher()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            this.BeginAnimation(OpacityProperty, fadeOut);
            await Task.Delay(150);
            this.Hide(); 
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 1;
        }



        private async void App_Close(object sender, RoutedEventArgs e) 
        { 
            if (_isClosing) return;
            _isClosing = true;

            // Cancel any in-flight cloud loading
            if (ViewCloud != null) ViewCloud.CancelLoading();

            // Fade out the window smoothly (0.2s)
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) 
                { FillBehavior = FillBehavior.HoldEnd };
            this.BeginAnimation(OpacityProperty, fadeOut);
            await Task.Delay(220);

            // Window is now invisible — hide it so user sees it as "closed"
            this.Hide();

            // Save data while hidden (user doesn't see the delay)
            SaveData();

            if (_trayIcon != null) { 
                _trayIcon.Visible = false; 
                _trayIcon.Dispose(); 
            }

            Application.Current.Shutdown(); 
            Environment.Exit(0);
        }

        private void CheckAutorun() 
        { 
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) {
                    if (key != null) {
                        chkAutoRun.IsChecked = (key.GetValue("DuranHelper") != null);
                    }
                }
            } catch { }
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\DuranSettings", false)) {
                    if (key != null) {
                        string val = key.GetValue("BindsListView")?.ToString();
                        if (val == "1") { _isListView = true; }
                        else if (val == "0") { _isListView = false; }
                    }
                }
                SetBindsViewMode();
            } catch { }
        }


        private void Logo_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if ((DateTime.Now - _lastDevClick).TotalSeconds > 1.5) _devClickCount = 0;
            _devClickCount++;
            _lastDevClick = DateTime.Now;

            if (_devClickCount >= 5)
            {
                _devClickCount = 0;
                PopulateDevMenu();
                OpenDialog(WinDevMenu);
            }
        }

        private void PopulateDevMenu()
        {
            try {
                int profileCount = MasterData.Count;
                int totalBinds = MasterData.Values.Sum(p => p.Binds?.Count ?? 0);
                int activeBinds = MasterData.Values.Sum(p => p.Binds?.Values.Count(b => b.active) ?? 0);
                int totalSections = MasterData.Values.Sum(p => p.Laws?.Count ?? 0);
                int totalArticles = MasterData.Values.Sum(p => p.Laws == null ? 0 : CountRealArticles(p.Laws));
                int totalFines = MasterData.Values.Sum(p => p.Fines?.Count ?? 0);
                string gamePath = txtGamePath?.Text ?? "";
                bool gamePathOk = !string.IsNullOrWhiteSpace(gamePath) && File.Exists(Path.Combine(gamePath, "gta_sa.exe"));
                string profilesFile = Path.Combine(Environment.CurrentDirectory, "Profiles.json");
                string settingsFile = Path.Combine(Environment.CurrentDirectory, "Settings.json");
                string radialFile = Path.Combine(Environment.CurrentDirectory, "radial.json");

                txtDevSystemInfo.Text =
                    $"Launcher build: {GetLauncherVersionString()}\n" +
                    $"OS: {Environment.OSVersion}\n" +
                    $".NET: {Environment.Version}\n" +
                    $"Machine/User: {Environment.MachineName} / {Environment.UserName}\n" +
                    $"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}   Admin: {(IsAdministrator() ? "yes" : "no")}\n" +
                    $"Work dir: {Environment.CurrentDirectory}\n" +
                    $"Game path: {(string.IsNullOrWhiteSpace(gamePath) ? "not set" : gamePath)}\n" +
                    $"gta_sa.exe: {(gamePathOk ? "found" : "missing")}";

                txtDevRuntimeInfo.Text =
                    $"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\.hh\\:mm\\:ss}\n" +
                    $"Current profile: {(string.IsNullOrEmpty(CurrentProfile) ? "none" : CurrentProfile)}\n" +
                    $"Profiles: {profileCount}\n" +
                    $"Binds: {activeBinds} active / {totalBinds} total\n" +
                    $"Law sections: {totalSections}   Articles: {totalArticles}\n" +
                    $"Fines: {totalFines}\n" +
                    $"ASI engine state: {(_isEngineRunning ? "running" : "stopped")}\n" +
                    $"Debug logs mode: {(App.IsDebugMode ? "enabled" : "disabled")}\n" +
                    $"Profiles.json: {(File.Exists(profilesFile) ? new FileInfo(profilesFile).Length + " bytes" : "missing")}\n" +
                    $"Settings.json: {(File.Exists(settingsFile) ? new FileInfo(settingsFile).Length + " bytes" : "missing")}\n" +
                    $"radial.json: {(File.Exists(radialFile) ? new FileInfo(radialFile).Length + " bytes" : "missing")}";

                var recent = AppLogs.OrderByDescending(l => l.Timestamp).Take(12).ToList();
                txtDevRecentEvents.Text = recent.Count == 0
                    ? "No runtime events yet."
                    : string.Join("\n", recent.Select(l => $"{l.Timestamp:HH:mm:ss}  {l.Message}"));

                txtDevDumpPreview.Text =
                    "Dump sections:\n" +
                    "- meta, runtime, paths, files, profiles, recent_logs, ui_state\n\n" +
                    $"profiles_count: {profileCount}\n" +
                    $"recent_logs_count: {recent.Count}\n" +
                    "contains file snapshots: Settings.json, Profiles.json, radial.json\n" +
                    $"generated_at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            } catch { }
        }

        private void Dev_ClearBinds_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile)) return;
            MasterData[CurrentProfile].Binds.Clear();
            UpdateBindGroups();
            UpdateBindsList();
            UpdateBindStatsUI();
            SaveData(); 
            ShowCustomToast("Все бинды удалены!"); 
            Dialog_Close(null, null);
        }

        private void Dev_ClearLaws_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile)) return;
            MasterData[CurrentProfile].Laws.Clear();
            UpdateLawsList(); 
            SaveData(); 
            ShowCustomToast("Все законы удалены!"); 
            Dialog_Close(null, null);
        }

        private void Dev_ReorderProfiles_Click(object sender, RoutedEventArgs e) {
            Dialog_Close(null, null);
            _reorderMode = "Profiles";
            lblReorderTitle.Text = "УПРАВЛЕНИЕ ГРУППАМИ";
            _reorderList = MasterData.Keys.ToList();
            icReorderList.ItemsSource = null; 
            icReorderList.ItemsSource = _reorderList;
            OpenOverlay(WinReorder);
        }

        private void Dev_ReorderSections_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile)) return;
            Dialog_Close(null, null);
            _reorderMode = "Sections";
            lblReorderTitle.Text = "УПРАВЛЕНИЕ ПРОФИЛЯМИ";
            _reorderList = MasterData[CurrentProfile].Laws.Keys.ToList();
            icReorderList.ItemsSource = null; 
            icReorderList.ItemsSource = _reorderList;
            OpenOverlay(WinReorder);
        }



        private void Reorder_Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(sender as IInputElement);
        
        private void Reorder_Item_MouseMove(object sender, MouseEventArgs e) { 
            if (e.LeftButton == MouseButtonState.Pressed) { 
                var b = sender as Border; 
                if(b != null) { 
                    var layer = AdornerLayer.GetAdornerLayer(WinReorder); 
                    if (layer != null) { 
                        _dragAdorner = new DragAdorner(b, e.GetPosition(b)); 
                        layer.Add(_dragAdorner); 
                        DragDrop.DoDragDrop(b, b.DataContext, DragDropEffects.Move); 
                        layer.Remove(_dragAdorner); 
                        _dragAdorner = null; 
                    } 
                } 
            } 
        }

        private void Reorder_Item_Drop(object sender, DragEventArgs e) { 
            if (e.Data.GetDataPresent(typeof(string))) { 
                string src = e.Data.GetData(typeof(string)) as string; 
                string trg = (sender as Border)?.DataContext as string; 
                if (src != null && trg != null && src != trg) { 
                    int i1 = _reorderList.IndexOf(src); 
                    int i2 = _reorderList.IndexOf(trg); 
                    if (i1 > -1 && i2 > -1) { 
                        _reorderList.RemoveAt(i1); 
                        _reorderList.Insert(i2, src); 
                        icReorderList.ItemsSource = null; 
                        icReorderList.ItemsSource = _reorderList;
                    } 
                } 
            } 
        }

        private void Reorder_Save_Click(object sender, RoutedEventArgs e) {
            if (_reorderMode == "Profiles") {
                var newDict = new Dictionary<string, ProfileData>();
                foreach(var k in _reorderList) newDict[k] = MasterData[k];
                MasterData = newDict;
                RefreshUI();
            } else if (_reorderMode == "Sections") {
                var newDict = new Dictionary<string, LawSection>();
                foreach(var k in _reorderList) newDict[k] = MasterData[CurrentProfile].Laws[k];
                MasterData[CurrentProfile].Laws = newDict;
                UpdateLawsList();
            }
            SaveData(); 
            Overlay_Close(null, null); 
            Dialog_Close(null, null); 
            ShowCustomToast("Порядок сохранён!");
        }




        private void Dev_OpenFolder_Click(object sender, RoutedEventArgs e) {
            try { Process.Start("explorer.exe", Environment.CurrentDirectory); } catch { }
        }

        private void Dev_Dump_Click(object sender, RoutedEventArgs e) {
            try {
                SaveFileDialog sfd = new SaveFileDialog { 
                    FileName = $"DuranHelper_Dump_{DateTime.Now:yyyyMMdd_HHmmss}.json", 
                    Filter = "JSON файлы (*.json)|*.json" 
                };
                if (sfd.ShowDialog() == true) {
                    var dump = BuildDiagnosticDump();
                    File.WriteAllText(sfd.FileName, JsonConvert.SerializeObject(dump, Formatting.Indented));
                    Dialog_Close(null, null);
                    OpenInfo("ДАМП СОЗДАН", "Диагностический дамп успешно сохранён.", true);
                }
            } catch { OpenInfo("ОШИБКА", "Не удалось создать копию!"); }
        }

        private object BuildDiagnosticDump() {
            string profilesPath = Path.Combine(Environment.CurrentDirectory, "Profiles.json");
            string settingsPath = Path.Combine(Environment.CurrentDirectory, "Settings.json");
            string radialPath = Path.Combine(Environment.CurrentDirectory, "radial.json");
            string gamePath = txtGamePath?.Text ?? "";
            bool hasGta = !string.IsNullOrWhiteSpace(gamePath) && File.Exists(Path.Combine(gamePath, "gta_sa.exe"));

            var profilesSummary = MasterData.Select(kvp => new {
                profile = kvp.Key,
                binds_total = kvp.Value.Binds?.Count ?? 0,
                binds_active = kvp.Value.Binds?.Values.Count(b => b.active) ?? 0,
                laws_sections = kvp.Value.Laws?.Count ?? 0,
                laws_articles = kvp.Value.Laws == null ? 0 : CountRealArticles(kvp.Value.Laws),
                fines_total = kvp.Value.Fines?.Count ?? 0,
                radial_sectors = kvp.Value.RadialMenu?.SectorCount ?? 0,
                radial_enabled = kvp.Value.RadialMenu?.Enabled ?? false
            }).ToList();

            var recentLogs = AppLogs.OrderByDescending(l => l.Timestamp).Take(250).Select(l => new {
                time = l.Timestamp,
                message = l.Message
            }).ToList();

            string ReadSafe(string filePath) {
                try {
                    return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
                } catch (Exception ex) {
                    return $"<read_error: {ex.Message}>";
                }
            }

            object FileMeta(string filePath) {
                if (!File.Exists(filePath)) return new { exists = false };
                var fi = new FileInfo(filePath);
                return new {
                    exists = true,
                    size_bytes = fi.Length,
                    modified_utc = fi.LastWriteTimeUtc
                };
            }

            return new {
                meta = new {
                    generated_at_local = DateTime.Now,
                    generated_at_utc = DateTime.UtcNow,
                    launcher_version = GetLauncherVersionString()
                },
                runtime = new {
                    os = Environment.OSVersion.ToString(),
                    dotnet = Environment.Version.ToString(),
                    machine = Environment.MachineName,
                    user = Environment.UserName,
                    is_admin = IsAdministrator(),
                    is_64bit_process = Environment.Is64BitProcess,
                    process_uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
                    debug_mode = App.IsDebugMode,
                    asi_engine_running = _isEngineRunning
                },
                paths = new {
                    working_directory = Environment.CurrentDirectory,
                    game_path = gamePath,
                    game_exe_found = hasGta
                },
                files = new {
                    settings_json = FileMeta(settingsPath),
                    profiles_json = FileMeta(profilesPath),
                    radial_json = FileMeta(radialPath),
                    settings_content = ReadSafe(settingsPath),
                    profiles_content = ReadSafe(profilesPath),
                    radial_content = ReadSafe(radialPath)
                },
                profiles_summary = profilesSummary,
                ui_state = new {
                    current_profile = CurrentProfile,
                    selected_tab_index = Tabs?.SelectedIndex ?? -1,
                    dashboard_state = _dashboardState,
                    profile_count = MasterData.Count
                },
                recent_logs = recentLogs
            };
        }

        private string GetLauncherVersionString() {
            try {
                var fvi = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion)) return fvi.FileVersion;
            } catch { }
            try {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            } catch {
                return "unknown";
            }
        }

        private void Dev_Reset_Click(object sender, RoutedEventArgs e) {
            _dialogHost.ShowAlert("СБРОС НАСТРОЕК", "Внимание! Все данные будут удалены!\nПродолжить?", () => {
                try {
                    if (File.Exists("Profiles.json")) File.Delete("Profiles.json");
                    if (File.Exists("Settings.json")) File.Delete("Settings.json");
                    ProcessStartInfo proc = new ProcessStartInfo { UseShellExecute = true, WorkingDirectory = Environment.CurrentDirectory, FileName = Environment.ProcessPath };
                    Process.Start(proc);
                    Application.Current.Shutdown();
                    Environment.Exit(0);
                } catch { }
            });
        }

        private async void DevConsole_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key != Key.Enter) return;
            var devConsole = sender as TextBox;
            string cmd = (devConsole?.Text ?? "").Trim().ToLower();
            if (devConsole != null) devConsole.Text = "";
            Dialog_Close(null, null);

            switch (cmd)
            {
                case "update":
                    // Show test update panel on splash
                    MainBackgroundBorder.Visibility = Visibility.Collapsed;
                    SplashUI.Visibility = Visibility.Visible;
                    SplashContent.Visibility = Visibility.Visible;
                    SplashBg.Visibility = Visibility.Visible;

                    // Clear any previous animations that might be locking properties
                    SplashContent.BeginAnimation(OpacityProperty, null);
                    SplashBg.BeginAnimation(OpacityProperty, null);
                    SplashUI.BeginAnimation(OpacityProperty, null);
                    SplashUI.BeginAnimation(HeightProperty, null);
                    Dash1.BeginAnimation(OpacityProperty, null);
                    Dash2.BeginAnimation(OpacityProperty, null);
                    Dash1.Opacity = 0; Dash2.Opacity = 0;
                    SplashD.BeginAnimation(OpacityProperty, null);
                    SplashTitle.BeginAnimation(OpacityProperty, null);
                    SplashStatusInit.BeginAnimation(OpacityProperty, null);
                    SplashStatusReady.BeginAnimation(OpacityProperty, null);
                    SplashStatusUpdate.BeginAnimation(OpacityProperty, null);
                    SplashUpdatePanel.BeginAnimation(OpacityProperty, null);

                    SplashContent.Opacity = 1;
                    SplashBg.Opacity = 1;
                    SplashUI.Opacity = 1;
                    
                    // Reset elements to "Gold" state (since they might be green/invisible)
                    var goldResource = Application.Current.Resources["GoldBrushColor"];
                    Color gold = goldResource is Color c ? c : (Color)ColorConverter.ConvertFromString(goldResource.ToString());
                    Dash1Brush.BeginAnimation(SolidColorBrush.ColorProperty, null); Dash1Brush.Color = gold;
                    Dash2Brush.BeginAnimation(SolidColorBrush.ColorProperty, null); Dash2Brush.Color = gold;
                    Dash1Glow.BeginAnimation(DropShadowEffect.ColorProperty, null); Dash1Glow.Color = gold;
                    Dash2Glow.BeginAnimation(DropShadowEffect.ColorProperty, null); Dash2Glow.Color = gold;

                    splashDBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    splashDBrush.Color = gold;
                    splashBarBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    splashBarBrush.Color = gold;


                    SplashD.Opacity = 1;
                    SplashTitle.Opacity = 1;
                    
                    SplashUI.Width = 460;
                    SplashUI.Height = 200;
                    SplashUI.HorizontalAlignment = HorizontalAlignment.Center;
                    SplashUI.VerticalAlignment = VerticalAlignment.Center;
                    SplashStatusInit.Opacity = 0;
                    SplashStatusReady.Opacity = 0;
                    SplashStatusUpdate.Opacity = 0;
                    SplashBar.Visibility = Visibility.Collapsed;

                    // Expand
                    var ease = new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 3 };
                    SplashUI.BeginAnimation(HeightProperty, new DoubleAnimation(200, 340, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease });
                    
                    SplashStatusUpdate.Opacity = 1;
                    SplashStatusUpdate.Text = "ДОСТУПНО ОБНОВЛЕНИЕ v1.2.0 (ТЕСТ)";
                    Canvas.SetTop(SplashUpdatePanel, 128);
                    SplashUpdatePanel.Visibility = Visibility.Visible;
                    SplashUpdatePanel.Opacity = 1;
                    UpdateButtonsPanel.Visibility = Visibility.Visible;

                    _updateDecision = new TaskCompletionSource<bool>();
                    bool testWantsUpdate = await _updateDecision.Task;

                    if (testWantsUpdate)
                    {
                        // Smooth collapse panel
                        SplashUpdatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease });
                        await Task.Delay(450);
                        SplashUpdatePanel.Visibility = Visibility.Collapsed;

                        // Smooth shrink
                        SplashUI.BeginAnimation(HeightProperty, new DoubleAnimation(340, 200, TimeSpan.FromSeconds(0.5)) { EasingFunction = ease });
                        await Task.Delay(550);
                        SplashUI.BeginAnimation(HeightProperty, null);
                        SplashUI.Height = 200;

                        SplashStatusUpdate.Text = "ЗАГРУЗКА... 0%";
                        await Task.Delay(200);

                        // Smooth simultaneous: track fades in + bar visible
                        SplashBarTrack.Width = 300;
                        SplashBarTrack.Opacity = 0;
                        SplashBarTrack.Visibility = Visibility.Visible;
                        SplashBarTrack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8)) { EasingFunction = ease });
                        SplashBar.Width = 0;
                        SplashBar.Opacity = 1;
                        SplashBar.Visibility = Visibility.Visible;

                        for (int i = 0; i <= 100; i += 2)
                        {
                            SplashStatusUpdate.Text = $"ЗАГРУЗКА... {i}%";
                            SplashBar.Width = 3.0 * i;
                            await Task.Delay(30);
                        }

                        SplashStatusUpdate.Text = "ПЕРЕЗАПУСК...";
                        await Task.Delay(1000);
                    }
                    else
                    {
                        SplashUpdatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2)) { EasingFunction = ease });
                        await Task.Delay(200);
                        SplashUpdatePanel.Visibility = Visibility.Collapsed;
                    }

                    // Close test
                    SplashUI.BeginAnimation(HeightProperty, new DoubleAnimation(SplashUI.ActualHeight, 200, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease });
                    await Task.Delay(300);
                    SplashUI.BeginAnimation(HeightProperty, null);
                    SplashUI.Height = 200;
                    SplashUI.Visibility = Visibility.Collapsed;
                    SplashBar.Visibility = Visibility.Visible;
                    SplashContent.Visibility = Visibility.Collapsed;
                    SplashBg.Visibility = Visibility.Collapsed;
                    
                    // Show EVERYTHING back (restore opacity and visibility)
                    MainBackgroundBorder.Visibility = Visibility.Visible;
                    MainBackgroundBorder.Opacity = 1;

                    SidebarPanel.Visibility = Visibility.Visible;
                    SidebarPanel.Opacity = 1;
                    SidebarPanel_TT.X = 0; // Reset any animation shift

                    Tabs.Visibility = Visibility.Visible;
                    Tabs.Opacity = 1;
                    Tabs_TT.Y = 0; // Reset any animation shift

                    OverlayContainer.Visibility = Visibility.Visible;
                    OverlayContainer.Opacity = 1;
                    
                    AddLog("[DEV] Тест окна обновления завершён", "#ff7b72");
                    break;

                case "alreadyrunning":
                    var arw = new AlreadyRunningWindow();
                    arw.Show();
                    AddLog("[DEV] AlreadyRunningWindow открыто", "#ff7b72");
                    break;

                default:
                    ShowCustomToast($"Неизвестная команда: {cmd}", "red");
                    break;
            }
        }

        private void Dev_RestartAHK_Click(object sender, RoutedEventArgs e) {
            try {
                SaveData();
                ShowCustomToast("Конфиги ASI обновлены", "green");
                PopulateDevMenu();
            } catch { ShowCustomToast("Ошибка обновления конфигов", "red"); }
        }

        private void Dev_ToggleDebug_Click(object sender, RoutedEventArgs e) {
            App.IsDebugMode = !App.IsDebugMode;
            ShowCustomToast(App.IsDebugMode ? "Debug режим включён" : "Debug режим выключен", App.IsDebugMode ? "green" : "red");
            PopulateDevMenu();
        }

        private void Groups_Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            ScrollViewer scv = (ScrollViewer)sender;
            scv.ScrollToVerticalOffset(scv.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void Laws_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            e.Handled = true;
            var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
            while (parent != null && !(parent is ScrollViewer)) parent = VisualTreeHelper.GetParent(parent);
            if (parent is ScrollViewer sv) sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        }

        private void OverlayName_TextChanged(object sender, TextChangedEventArgs e) {
            if (txtOverlayName == null) return;
            Setting_Changed(sender, e);
        }

        internal void TxtStepVal_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is TextBox txt && txt.Parent is Grid grid)
            {
                var svSyntax = grid.Children.OfType<ScrollViewer>().FirstOrDefault(x => x.Name == "svSyntax");
                if (svSyntax != null) svSyntax.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
        }

        public void StepVal_SelectionChanged(object sender, RoutedEventArgs e) {
            if (sender is TextBox txt && txt.DataContext is BindStep step) {
                if ((step.action == "CHAT" || step.action == "TEXT") && txt.Text != null) {
                    int caret = txt.CaretIndex;
                    if (caret <= txt.Text.Length && caret >= 0) {
                        step.CursorOffset = txt.Text.Length - caret;
                    }
                }
            }
        }

        internal void StepVal_Loaded(object sender, RoutedEventArgs e) { FormatSyntax(sender as TextBox); }
        internal void StepVal_TextChanged(object sender, TextChangedEventArgs e) { FormatSyntax(sender as TextBox); }
        
        private void FormatSyntax(TextBox txt)
        {
            if (txt == null || string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            if (txt.Parent is Grid grid)
            {
                var svSyntax = grid.Children.OfType<ScrollViewer>().FirstOrDefault(x => x.Name == "svSyntax");
                if (svSyntax == null) return;
                
                var tbSyntax = svSyntax.Content as TextBlock;
                if (tbSyntax != null)
                {
                    tbSyntax.Inlines.Clear();
                    var parts = System.Text.RegularExpressions.Regex.Split(txt.Text ?? "", @"(\*[^\*]+\*)");
                    var gold = (SolidColorBrush)Application.Current.Resources["GoldBrush"];
                    var white = (SolidColorBrush)Application.Current.Resources["TextBrush"];
                    var vars = MasterData[CurrentProfile].Variables;

                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part)) continue;
                        if ((part.StartsWith("*") && part.EndsWith("*") && vars != null && vars.ContainsKey(part)) || part == "*ВРЕМЯ*")
                            tbSyntax.Inlines.Add(new Run(part) { Foreground = gold, FontWeight = FontWeights.Bold });
                        else
                            tbSyntax.Inlines.Add(new Run(part) { Foreground = white });
                    }
                }
            }
        }



        private static List<Window> _activeToasts = new List<Window>();

        internal void ShowCustomToast(string message, string variant = "gold")
        {
            ShowCustomToast("DURAN HELPER", message, variant, null);
        }

        internal async void ShowCustomToast(string title, string message, string variant, string subtitle2)
        {
            // Check if notifications are enabled
            if (chkNotifications != null && chkNotifications.IsChecked == false) return;

            Color accentColor = variant == "green" 
                ? (Color)ColorConverter.ConvertFromString("#2ea043") 
                : variant == "red"
                ? (Color)ColorConverter.ConvertFromString("#ff5555")
                : (Color)ColorConverter.ConvertFromString("#d2a65e");
            var accentBrush = new SolidColorBrush(accentColor);

            double baseTop = SystemParameters.PrimaryScreenHeight - 155;
            double topOffset = baseTop - (_activeToasts.Count * 130);

            Window toast = new Window { 
                Width = 380, Height = 120, WindowStyle = WindowStyle.None, AllowsTransparency = true, 
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, Opacity = 0, 
                Left = SystemParameters.PrimaryScreenWidth - 400, 
                Top = topOffset 
            };

            _activeToasts.Add(toast);

            EventHandler closedHandler = null;
            closedHandler = (s, e) => {
                toast.Closed -= closedHandler;
                _activeToasts.Remove(toast);
                for (int i = 0; i < _activeToasts.Count; i++) {
                    var t = _activeToasts[i];
                    double newTop = baseTop - (i * 130);
                    var move = new DoubleAnimation(t.Top, newTop, TimeSpan.FromSeconds(0.3)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
                    t.BeginAnimation(Window.TopProperty, move);
                }
            };
            toast.Closed += closedHandler;

            // Main container
            var mainGrid = new Grid();
            
            // Background card with border
            var cardBorder = new Border { 
                Background = (SolidColorBrush)Application.Current.Resources["BgBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["LineBrush"],
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12, 12, 0, 12),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 15, Opacity = 0.8 }
            };
            mainGrid.Children.Add(cardBorder);

            // Content grid
            var contentGrid = new Grid { Margin = new Thickness(0) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });    // accent bar
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });   // icon
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // close
            mainGrid.Children.Add(contentGrid);

            // Left accent bar (3px, glowing)
            var accentBar = new Border { 
                Background = accentBrush, CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 25, 0, 25), VerticalAlignment = VerticalAlignment.Stretch,
                Effect = new DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
            };
            Grid.SetColumn(accentBar, 0);
            contentGrid.Children.Add(accentBar);

            // Icon box (50x50 rounded)
            var iconBorder = new Border { 
                Width = 50, Height = 50, CornerRadius = new CornerRadius(10),
                Background = (SolidColorBrush)Application.Current.Resources["SideBrush"],
                BorderBrush = accentBrush, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var iconCanvas = new Canvas { Width = 50, Height = 50 };
            // Glow circle behind icon
            var glowCircle = new System.Windows.Shapes.Ellipse { 
                Width = 32, Height = 32, Fill = accentBrush, Opacity = variant == "green" ? 0.2 : 0.1
            };
            Canvas.SetLeft(glowCircle, 9); Canvas.SetTop(glowCircle, 9);
            iconCanvas.Children.Add(glowCircle);

            if (variant == "green") {
                // Checkmark icon
                var check = new System.Windows.Shapes.Path {
                    Data = System.Windows.Media.Geometry.Parse("M 18,25 L 23,30 L 33,19"),
                    Stroke = accentBrush, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Effect = new DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
                };
                iconCanvas.Children.Add(check);
            } else if (variant == "red") {
                // X cross icon
                var cross = new System.Windows.Shapes.Path {
                    Data = System.Windows.Media.Geometry.Parse("M 18,18 L 32,32 M 32,18 L 18,32"),
                    Stroke = accentBrush, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Effect = new DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
                };
                iconCanvas.Children.Add(cross);
            } else {
                // "D" letter
                var dText = new TextBlock { 
                    Text = "D", FontFamily = new FontFamily("Arial"), FontSize = 22, FontWeight = FontWeights.Black,
                    Foreground = accentBrush, HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center, Width = 50
                };
                Canvas.SetTop(dText, 10);
                iconCanvas.Children.Add(dText);
            }
            iconBorder.Child = iconCanvas;
            Grid.SetColumn(iconBorder, 1);
            contentGrid.Children.Add(iconBorder);

            // Text area
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            textPanel.Children.Add(new TextBlock { 
                Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, 
                FontSize = 14, FontFamily = new FontFamily("Segoe UI")
            });
            textPanel.Children.Add(new TextBlock { 
                Text = message, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b949e")),
                FontSize = 12, FontFamily = new FontFamily("Segoe UI"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            if (!string.IsNullOrEmpty(subtitle2)) {
                textPanel.Children.Add(new TextBlock { 
                    Text = subtitle2, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370")),
                    FontSize = 12, FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(0, 1, 0, 0)
                });
            }
            Grid.SetColumn(textPanel, 2);
            contentGrid.Children.Add(textPanel);

            // Close button (X)
            var closeBtn = new System.Windows.Shapes.Path {
                Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370")),
                StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 15, 5, 0), Cursor = Cursors.Hand, Width = 10, Height = 10
            };
            Grid.SetColumn(closeBtn, 3);
            contentGrid.Children.Add(closeBtn);

            // Accent corner (bottom-right L-bracket)
            var cornerPath = new Border {
                Width = 35, Height = 35,
                BorderBrush = accentBrush, BorderThickness = new Thickness(0, 0, 3, 3),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 0)
            };
            mainGrid.Children.Add(cornerPath);

            // Bottom progress bar (shrinking)
            var barBg = new Border { 
                Height = 2, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(15, 0, 15, 2), Background = Brushes.Transparent
            };
            var progressBar = new Border { 
                Height = 2, Background = accentBrush, Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RenderTransformOrigin = new Point(0, 0.5)
            };
            var barScale = new ScaleTransform(1, 1);
            progressBar.RenderTransform = barScale;
            barBg.Child = progressBar;
            mainGrid.Children.Add(barBg);

            // Slide transform
            TranslateTransform transform = new TranslateTransform(400, 0);
            mainGrid.RenderTransform = transform;

            toast.Content = mainGrid;

            // Close button click
            bool isClosed = false;
            closeBtn.MouseLeftButtonUp += async (s, ev) => {
                if (isClosed) return; isClosed = true;
                DoubleAnimation slideOut = new DoubleAnimation(0, 400, TimeSpan.FromSeconds(0.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                DoubleAnimation fadeO = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
                transform.BeginAnimation(TranslateTransform.XProperty, slideOut);
                toast.BeginAnimation(OpacityProperty, fadeO);
                await Task.Delay(300);
                try { toast.Close(); } catch { }
            };
            closeBtn.MouseEnter += (s, ev) => { ((System.Windows.Shapes.Path)s).Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff7b72")); };
            closeBtn.MouseLeave += (s, ev) => { ((System.Windows.Shapes.Path)s).Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370")); };

            toast.Show();

            // Slide in with bounce easing
            var slideInEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation slideIn = new DoubleAnimation(400, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = slideInEase };
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
            transform.BeginAnimation(TranslateTransform.XProperty, slideIn); 
            toast.BeginAnimation(OpacityProperty, fadeIn);

            // Progress bar shrink animation (5s)
            DoubleAnimation shrink = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(5));
            barScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);

            await Task.Delay(5000);

            if (!isClosed) {
                isClosed = true;
                DoubleAnimation slideOut2 = new DoubleAnimation(0, 400, TimeSpan.FromSeconds(0.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                DoubleAnimation fadeOut2 = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
                transform.BeginAnimation(TranslateTransform.XProperty, slideOut2);
                toast.BeginAnimation(OpacityProperty, fadeOut2);
                await Task.Delay(300); 
                try { toast.Close(); } catch { }
            }
        }

        private bool _isUpdatingConfig = false;

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            
            ShowCustomToast("КОНФИГ ОБНОВЛЕН", "Настройки сохранены для ASI.", "green", "DuranOverlay.asi готов.");
            _isUpdatingConfig = false;

        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            InstallASI_Click(null, null);
        }

        private void BtnUpdate_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            
        }

        private void BtnUpdate_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            
        }

        private void BtnInstall_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            
        }

        private void BtnInstall_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            
        }

        private void InitDefaults()
        {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var p = MasterData[CurrentProfile];
            if (p.Variables == null || p.Variables.Count == 0) p.Variables = new Dictionary<string, string> { {"*ИМЯ*", ""}, {"*ФАМ*", ""}, {"*ТЕГ*", ""}, {"*ЗВ*", ""} };
        }

        private void ApplyTheme(string t)
        {
            void setCol(string k, string c) {
                var color = (Color)ColorConverter.ConvertFromString(c);
                if (Application.Current.Resources[k] is SolidColorBrush existing && !existing.IsFrozen) {
                    existing.Color = color;
                } else {
                    Application.Current.Resources[k] = new SolidColorBrush(color);
                }
                Application.Current.Resources[k + "Color"] = color;
            }
            
            if (CustomThemes.ContainsKey(t)) { 
                var cust = CustomThemes[t]; 
                foreach(var kv in cust) { try { setCol(kv.Key, kv.Value); } catch {} } 
                return; 
            }
            
            if(t.Contains("Black")) { 
                setCol("BgBrush","#000000"); setCol("SideBrush","#121212"); setCol("SidebarBrush","#0a0a0a"); setCol("BoxBrush","#121212"); setCol("InputBrush","#000000"); setCol("DeepBgBrush","#000000");
                setCol("TextBrush", "White"); setCol("LineBrush", "#1f1f1f");
                setCol("GoldBrush", "#b8904f"); setCol("GrayBrush", "#8b949e"); setCol("GreenBrush", "#2ea043"); setCol("RedBrush", "#ff7b72");
                setCol("MenuHoverBrush", "#151515"); setCol("SectionBtnBrush", "#151515"); setCol("HoverBrush", "#1a3d6e"); setCol("BtnHoverBrush", "#0f0f0f");
                setCol("OverlayBrush", "#30000000");
                setCol("GoldBrushTransparent", "#15b8904f");
                setCol("ImportExportBgBrush", "#051024");
            } 
            else if (t.Contains("Grey") || t.Contains("Sport red")) { 
                setCol("BgBrush","#121214"); setCol("SideBrush","#1a1a1d"); setCol("SidebarBrush","#0f0f11"); setCol("BoxBrush","#1a1a1d"); setCol("InputBrush","#0a0a0c"); setCol("DeepBgBrush","#0a0a0c");
                setCol("TextBrush", "White"); setCol("LineBrush", "#2d2d33");
                setCol("GoldBrush", "#a51a1a"); setCol("GrayBrush", "#7e8590"); setCol("GreenBrush", "#a51a1a"); setCol("RedBrush", "#ff4d4d");
                setCol("MenuHoverBrush", "#232328"); setCol("SectionBtnBrush", "#232328"); setCol("HoverBrush", "#8b1212"); setCol("BtnHoverBrush", "#1f2228");
                setCol("OverlayBrush", "#30000000");
                setCol("GoldBrushTransparent", "#15a51a1a");
                setCol("ImportExportBgBrush", "Transparent");
            } 
            else { 
                setCol("BgBrush","#0d1117"); setCol("SideBrush","#161b22"); setCol("SidebarBrush","#11151d"); setCol("BoxBrush","#161b22"); setCol("InputBrush","#0a0d12"); setCol("DeepBgBrush","#0a0d12");
                setCol("TextBrush", "White"); setCol("LineBrush", "#30363d");
                setCol("GoldBrush", "#d2a65e"); setCol("GrayBrush", "#8b949e"); setCol("GreenBrush", "#2ea043"); setCol("RedBrush", "#ff7b72");
                setCol("MenuHoverBrush", "#21262d"); setCol("SectionBtnBrush", "#21262d"); setCol("HoverBrush", "#1f6feb"); setCol("BtnHoverBrush", "#1f2937");
                setCol("OverlayBrush", "#30000000");
                setCol("ImportExportBgBrush", "#051024");
            }
            
            // Refresh splash bar and separator colors to match new theme
            try {
                var goldColor = ((SolidColorBrush)Application.Current.Resources["GoldBrush"]).Color;
                if (splashBarBrush != null) splashBarBrush.Color = goldColor;
                if (barGlow != null) barGlow.Color = goldColor;

            } catch {}
            
            // Re-apply dashboard state colors so locally-set Foregrounds update
            try {
                if (!string.IsNullOrEmpty(_dashboardState)) {
                    switch (_dashboardState) {
                        case "Active": ApplyDashboardActive(null); break;
                        case "NoProfile": ApplyDashboardNoProfile(); break;
                        case "NoASI": ApplyDashboardNoASI(null); break;
                    }
                }
            } catch {}
            
            // Force-refresh ComboBox backgrounds (TemplateBinding caching workaround)
            try {
                if (cbSections != null) {
                    cbSections.SetResourceReference(BackgroundProperty, "DeepBgBrush");
                    cbSections.SetResourceReference(BorderBrushProperty, "LineBrush");
                }
                if (cbLawType != null) {
                    cbLawType.SetResourceReference(BackgroundProperty, "SideBrush");
                    cbLawType.SetResourceReference(BorderBrushProperty, "LineBrush");
                }
                if (cbProfiles != null) {
                    cbProfiles.SetResourceReference(BorderBrushProperty, "GoldBrush");
                }
                if (cbProfiles != null) {
                    cbProfiles.SetResourceReference(BorderBrushProperty, "GoldBrush");
                }
            } catch {}

            // Refresh radial menu with new theme colors
            try {
                if (canvasRadial != null && canvasRadial.Children.Count > 0) {
                    DrawRadialCircle(GetRadialConfig());
                }
            } catch {}
        }

        private void LoadData()
        {
            if (File.Exists("Profiles.json")) { 
                try { MasterData = JsonConvert.DeserializeObject<Dictionary<string, ProfileData>>(File.ReadAllText("Profiles.json")) ?? new(); } 
                catch { MasterData = new(); } 
            }
            
            if (File.Exists("Settings.json")) {
                try {
                    var s = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("Settings.json"));
                    
                    if (s != null && s.ContainsKey("CustomThemesText")) { 
                        try { CustomThemes = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(s["CustomThemesText"]); } catch {} 
                    }
                    cbThemeOverlay.Items.Clear();
                    cbThemeOverlay.Items.Add("Default (Dark Blue)");
                    cbThemeOverlay.Items.Add("Black (AMOLED)");
                    cbThemeOverlay.Items.Add("Grey (Sport red)");
                    if (CustomThemes != null) {
                        foreach(var k in CustomThemes.Keys) cbThemeOverlay.Items.Add(k);
                    }
                    
                    _ignoreEvents = true;
                    if (s != null && s.ContainsKey("ThemeLauncher")) { 
                        string content = s["ThemeLauncher"];
                        if (content == "Default (Dark Blue)") { rbThemeDefault.IsChecked = true; ApplyTheme(content); }
                        else if (content == "Black (AMOLED)") { rbThemeBlack.IsChecked = true; ApplyTheme(content); }
                        else if (content == "Grey (Sport red)") { rbThemeGrey.IsChecked = true; ApplyTheme(content); }
                    }
                    if (s != null && s.ContainsKey("ThemeOverlay")) { 
                        foreach(string i in cbThemeOverlay.Items) { 
                            if (i == s["ThemeOverlay"]) { cbThemeOverlay.SelectedItem = i; break; } 
                        } 
                    }
                    if (rbThemeDefault.IsChecked != true && rbThemeBlack.IsChecked != true && rbThemeGrey.IsChecked != true) { rbThemeDefault.IsChecked = true; ApplyTheme("Default (Dark Blue)"); }
                    if (cbThemeOverlay.SelectedItem == null) cbThemeOverlay.SelectedIndex = 0;
                    
                    if (s != null && s.ContainsKey("KeyToggle")) {
                        string val = s["KeyToggle"];
                        btnKeyToggle.Content = (val == "КЛАВИША: НЕТ" || val == "None" || string.IsNullOrWhiteSpace(val)) ? "F9" : val;
                    } else {
                        btnKeyToggle.Content = "F9";
                    }
                    if (s != null && s.ContainsKey("KeyPrev")) btnKeyPrev.Content = s["KeyPrev"];
                    if (s != null && s.ContainsKey("KeyNext")) btnKeyNext.Content = s["KeyNext"];
                    if (s != null && s.ContainsKey("LastGroup")) _currentBindGroup = s["LastGroup"];
                    if (s != null && s.ContainsKey("LastSection")) _lastLawSection = s["LastSection"];
                    
                    if (s != null && s.ContainsKey("Notifications")) {
                        chkNotifications.IsChecked = s["Notifications"] != "False";
                    }
                    if (s != null && s.ContainsKey("CheckUpdates")) {
                        chkCheckUpdates.IsChecked = s["CheckUpdates"] != "False";
                    }
                    if (s != null && s.ContainsKey("DisableOverlay")) {
                        chkSysOverlay.IsChecked = s["DisableOverlay"] != "True";
                        chkDisableOverlay.IsChecked = s["DisableOverlay"] == "True";
                    }
                    if (s != null && s.ContainsKey("DisableBinder")) {
                        chkSysBinder.IsChecked = s["DisableBinder"] != "True";
                    }
                    if (lawsDisabledOverlay != null) lawsDisabledOverlay.Visibility = chkSysOverlay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
                    if (bindsDisabledOverlay != null) bindsDisabledOverlay.Visibility = chkSysBinder.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

                    if (s != null && s.ContainsKey("BinderEngine")) {

                    }
                    cbBinderEngine.SelectedIndex = 1; // Always ASI

                    if (s != null && s.ContainsKey("GamePath")) {
                        txtGamePath.Text = s["GamePath"];
                        // lblGameFolder was replaced by new Bento dashboard labels
                    }

                    if (s != null && s.ContainsKey("OverlayOpacity")) {
                        if (double.TryParse(s["OverlayOpacity"], out double op)) {
                            slOverlayOpacity.Value = op;
                            Application.Current.Resources["OverlayOpacity"] = op;
                        }
                    } else {
                        Application.Current.Resources["OverlayOpacity"] = 0.80;
                    }
                    if (s != null && s.ContainsKey("OverlayActivationType")) {
                        if (s["OverlayActivationType"] == "Advanced") {
                            chkAdvancedOverlay.IsChecked = true;
                            pnlDefaultOverlayActivators.Visibility = Visibility.Collapsed;
                            pnlAdvancedOverlayActivators.Visibility = Visibility.Visible;
                        } else {
                            chkAdvancedOverlay.IsChecked = false;
                            pnlDefaultOverlayActivators.Visibility = Visibility.Visible;
                            pnlAdvancedOverlayActivators.Visibility = Visibility.Collapsed;
                        }
                    } else {
                        chkAdvancedOverlay.IsChecked = false;
                        pnlDefaultOverlayActivators.Visibility = Visibility.Visible;
                        pnlAdvancedOverlayActivators.Visibility = Visibility.Collapsed;
                    }
                    
                    _ignoreEvents = false;

                    if (MasterData.Count > 0) {
                        if (s.ContainsKey("LastProfile") && MasterData.ContainsKey(s["LastProfile"])) CurrentProfile = s["LastProfile"];
                        else CurrentProfile = MasterData.Keys.First();
                    }
                } catch { 
                    ApplyTheme("Default (Dark Blue)");
                    if (MasterData.Count > 0) CurrentProfile = MasterData.Keys.First(); 
                }
            } else {
                cbThemeOverlay.Items.Clear();
                cbThemeOverlay.Items.Add("Default (Dark Blue)");
                cbThemeOverlay.Items.Add("Black (AMOLED)");
                cbThemeOverlay.Items.Add("Grey (Sport red)");
                
                _ignoreEvents = true;
                rbThemeDefault.IsChecked = true;
                cbThemeOverlay.SelectedIndex = 0;
                ApplyTheme("Default (Dark Blue)");
                Application.Current.Resources["OverlayOpacity"] = 0.85;
                _ignoreEvents = false;

                if (MasterData.Count > 0) CurrentProfile = MasterData.Keys.First();
                
                Dictionary<string, string> initialSettings = new Dictionary<string, string>();
                initialSettings["KeyToggle"] = "КЛАВИША: НЕТ";
                initialSettings["KeyPrev"] = "КЛАВИША: НЕТ";
                initialSettings["KeyNext"] = "КЛАВИША: НЕТ";
                initialSettings["ThemeLauncher"] = "Default (Dark Blue)";
                initialSettings["ThemeOverlay"] = "Default (Dark Blue)";
                initialSettings["OverlayOpacity"] = "0.85";
                initialSettings["OverlayActivationType"] = "Default";
                initialSettings["Notifications"] = "True";
                initialSettings["CheckUpdates"] = "True";
                initialSettings["BinderEngine"] = "0";
                initialSettings["GamePath"] = "";
                File.WriteAllText("Settings.json", JsonConvert.SerializeObject(initialSettings, Formatting.Indented));
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        internal void SaveData(Action onLiveUpdateComplete = null)
        {
            if (!_isLoaded || _ignoreEvents) return;
            if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) MasterData[CurrentProfile].OverlayText = txtOverlayName.Text;
            
            File.WriteAllText("Profiles.json", JsonConvert.SerializeObject(MasterData, Formatting.Indented));
            
            // Avoid blocking toggle animations after Cloud catalog load:
            // refresh cloud state only when Cloud tab is active, and do it asynchronously.
            if (Tabs != null && Tabs.SelectedIndex == 4 && ViewCloud != null)
            {
                Dispatcher.BeginInvoke(new Action(() => ViewCloud.RefreshState()), System.Windows.Threading.DispatcherPriority.Background);
            }

            string tLauncher = "Default (Dark Blue)";
            if (rbThemeBlack.IsChecked == true) tLauncher = "Black (AMOLED)";
            else if (rbThemeGrey.IsChecked == true) tLauncher = "Grey (Sport red)";
            string tOverlay = cbThemeOverlay.SelectedItem?.ToString() ?? "Default (Dark Blue)";

            string overlayType = chkAdvancedOverlay.IsChecked == true ? "Advanced" : "Default";

            var settings = new Dictionary<string, string> { 
                { "LastProfile", CurrentProfile }, 
                { "ThemeLauncher", tLauncher }, 
                { "ThemeOverlay", tOverlay }, 
                { "KeyToggle", btnKeyToggle?.Content?.ToString() ?? "КЛАВИША: НЕТ" }, 
                { "KeyPrev", btnKeyPrev?.Content?.ToString() ?? "КЛАВИША: НЕТ" }, 
                { "KeyNext", btnKeyNext?.Content?.ToString() ?? "КЛАВИША: НЕТ" }, 
                { "LastGroup", _currentBindGroup ?? "" }, 
                { "LastSection", _lastLawSection ?? "" }, 
                { "CustomThemesText", JsonConvert.SerializeObject(CustomThemes ?? new Dictionary<string, Dictionary<string, string>>()) },
                { "OverlayOpacity", slOverlayOpacity?.Value.ToString() ?? "0.85" },
                { "OverlayActivationType", overlayType },
                { "Notifications", (chkNotifications?.IsChecked == true).ToString() },
                { "CheckUpdates", (chkCheckUpdates?.IsChecked == true).ToString() },
                { "DisableOverlay", (chkSysOverlay?.IsChecked == false).ToString() },
                { "DisableBinder", (chkSysBinder?.IsChecked == false).ToString() },
                { "BinderEngine", cbBinderEngine?.SelectedIndex.ToString() ?? "1" },
                { "GamePath", txtGamePath?.Text ?? "" },
                { "Version", UpdateManager.APP_VERSION }
            };
            File.WriteAllText("Settings.json", JsonConvert.SerializeObject(settings));
            
            // Mirror configs to Game Directory for the ASI plugin
            // (No longer needed since ASI directly reads from Documents folder)
            
            // Export laws.json for the ASI overlay from current profile's Laws data
            if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                try {
                    var lawsExport = new Dictionary<string, object>();
                    foreach (var kvp in MasterData[CurrentProfile].Laws) {
                        if (kvp.Value.Type == "text") {
                            lawsExport[kvp.Key] = new { 
                                Type = "text", 
                                Content = GetPlainTextFromRtf(kvp.Value.RtfData ?? ""),
                                RtfData = kvp.Value.RtfData ?? ""
                            };
                        } else {
                            lawsExport[kvp.Key] = new { 
                                Type = kvp.Value.Type ?? "2col", 
                                Items = kvp.Value.Items ?? new List<LawItem>() 
                            };
                        }
                    }
                    File.WriteAllText("laws.json", JsonConvert.SerializeObject(lawsExport, Formatting.Indented));
                } catch { }

                // Export fines.json for the ASI overlay
                try {
                    var finesExport = new { items = MasterData[CurrentProfile].Fines ?? new List<FineArticle>() };
                    File.WriteAllText("fines.json", JsonConvert.SerializeObject(finesExport, Formatting.Indented));
                } catch { }

                // Export version.json for the ASI overlay
                try {
                    string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
                    File.WriteAllText("version.json", "{\"version\": \"" + ver + "\"}");
                } catch { }

                // Export radial.json for the ASI overlay (radial menu)
                try {
                    var rm = MasterData[CurrentProfile].RadialMenu ?? new RadialMenuConfig();
                    var radialExport = new {
                        enabled = rm.Enabled,
                        mode = rm.Mode ?? "Standard",
                        sectorCount = rm.SectorCount,
                        sectors = (rm.Sectors ?? new List<RadialMenuSector>()).Select(s => new {
                            bindId = s.BindId ?? "",
                            bindName = s.BindName ?? "",
                            icon = s.Icon ?? "star",
                            requiresId = s.RequiresId
                        }).ToList(),
                        groupCount = rm.GroupCount,
                        groups = (rm.Groups ?? new List<RadialMenuGroup>()).Select(g => new {
                            name = g.Name ?? "",
                            sectorCount = g.SectorCount,
                            sectors = (g.Sectors ?? new List<RadialMenuSector>()).Select(s => new {
                                bindId = s.BindId ?? "",
                                bindName = s.BindName ?? "",
                                icon = s.Icon ?? "star",
                                requiresId = s.RequiresId
                            }).ToList()
                        }).ToList()
                    };
                    File.WriteAllText("radial.json", JsonConvert.SerializeObject(radialExport, Formatting.Indented));
                } catch { }
            }
            
            
            if (!_isUpdatingConfig) {
                // Notifying game client about the changes for Live Update (Live Sync)
                IntPtr hwnd = FindWindow("Grand theft auto San Andreas", null);
                if (hwnd != IntPtr.Zero) {
                    uint WM_APP = 0x8000;
                    PostMessage(hwnd, WM_APP + 777, IntPtr.Zero, IntPtr.Zero);
                }
            }
            onLiveUpdateComplete?.Invoke();
        }

        private string GetPlainTextFromRtf(string rtf)
        {
            if (string.IsNullOrWhiteSpace(rtf)) return "";
            try {
                var rt = new System.Windows.Controls.RichTextBox();
                var tr = new System.Windows.Documents.TextRange(rt.Document.ContentStart, rt.Document.ContentEnd);
                using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(rtf))) {
                    tr.Load(ms, System.Windows.DataFormats.Rtf);
                }
                return tr.Text;
            } catch { return ""; }
        }

        private void BinderEngine_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _ignoreEvents) return;
            SaveData();

        }

        private void ShowOverlayMessage(string title, string msg, bool isError = true)
        {
            _dialogHost.ShowInfo(title.ToUpper(), msg, !isError);
        }

        private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.Description = "Выберите папку с игрой GTA San Andreas";
                fbd.SelectedPath = string.IsNullOrEmpty(txtGamePath.Text) ? "C:\\" : txtGamePath.Text;
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!System.IO.File.Exists(System.IO.Path.Combine(fbd.SelectedPath, "gta_sa.exe")) && !System.IO.File.Exists(System.IO.Path.Combine(fbd.SelectedPath, "radmir_crmp.exe"))) {
                        ShowOverlayMessage("ОШИБКА ПУТИ", "Неверно указан путь к игре.\nФайл gta_sa.exe не найден в выбранной папке.\n\nУзнать корневую папку с игрой вы сможете в RADMIR Launcher в настройках лаунчера.\nПараметр \"Путь к папке\".");
                        return;
                    }
                    txtGamePath.Text = fbd.SelectedPath;
                    if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                        MasterData[CurrentProfile].GamePath = fbd.SelectedPath;
                    }
                    SaveData();
                    UpdateDashboardState();
                }
            }
        }

        private void ImportData_Click(object sender, RoutedEventArgs e)
        {
            if (WinWizard.Visibility == Visibility.Visible && _wizardStep != 1)
            {
                _dialogHost.ShowInfo("ОШИБКА", "Завершите мастер настройки перед импортом данных.");
                return;
            }

            // Show dimming overlay behind the modal
            DialogOverlay.Visibility = Visibility.Visible;
            DialogOverlay.Opacity = 0;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15));
            DialogOverlay.BeginAnimation(OpacityProperty, fadeIn);

            var win = new DataImportWindow(this);
            win.Owner = this;
            win.ShowDialog();

            // Hide dimming overlay after modal closes
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fadeOut.Completed += (s, _) => DialogOverlay.Visibility = Visibility.Collapsed;
            DialogOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void CloseImportModal_Click(object sender, RoutedEventArgs e)
        {
            Dialog_Close(null, null);
        }

        internal void ShowImportProfile_Click(object sender, RoutedEventArgs e)
        {
            ImportProfile_Click(null, null);
        }

        internal void ShowImportLaws_Click(object sender, RoutedEventArgs e)
        {
            ImportLaws_Click(null, null);
        }

        internal void ShowImportBinds_Click(object sender, RoutedEventArgs e)
        {
            ImportBinds_Click(null, null);
        }

        internal void ShowImportFines_Click(object sender, RoutedEventArgs e)
        {
            ImportFines_Click(null, null);
        }


        private void ExportData_Click(object sender, RoutedEventArgs e)
        {
            if (WinWizard.Visibility == Visibility.Visible || !_hasProfile)
            {
                _dialogHost.ShowInfo("ОШИБКА", "Для экспорта данных необходимо сначала создать или выбрать профиль.");
                return;
            }
            
            // Show dimming overlay behind the modal
            DialogOverlay.Visibility = Visibility.Visible;
            DialogOverlay.Opacity = 0;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15));
            DialogOverlay.BeginAnimation(OpacityProperty, fadeIn);

            var win = new DataExportWindow(this);
            win.Owner = this;
            win.ShowDialog();

            // Hide dimming overlay after modal closes
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fadeOut.Completed += (s, _) => DialogOverlay.Visibility = Visibility.Collapsed;
            DialogOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void InstallASI_Click(object sender, RoutedEventArgs e)
        {
            string path = txtGamePath.Text;
            if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path)) {
                ShowOverlayMessage("ОШИБКА", "Пожалуйста, сначала выберите корректную папку с игрой!");
                return;
            }

            if (!System.IO.File.Exists(System.IO.Path.Combine(path, "gta_sa.exe")) && !System.IO.File.Exists(System.IO.Path.Combine(path, "radmir_crmp.exe"))) {
                ShowOverlayMessage("ОШИБКА ПУТИ", "Неверно указан путь к игре.\nФайл gta_sa.exe не найден в выбранной папке.\n\nУзнать корневую папку с игрой вы сможете в RADMIR Launcher в настройках лаунчера.\nПараметр \"Путь к папке\".");
                return;
            }

            string asiSource = System.IO.Path.Combine(AppContext.BaseDirectory, "DuranOverlay.asi");
            if (!System.IO.File.Exists(asiSource)) {
                ShowOverlayMessage("ОШИБКА УСТАНОВКИ", "Файл DuranOverlay.asi не найден в папке с лаунчером.\nПожалуйста, убедитесь что плагин скомпилирован и лежит рядом с DURANHELPER.exe.");
                return;
            }

            if (IsFileLockedForWrite(System.IO.Path.Combine(path, "DuranOverlay.asi"))) {
                ShowOverlayMessage("ВНИМАНИЕ", "Файл плагина занят процессом.\nПожалуйста, полностью закройте игру (gta_sa.exe) перед установкой плагина.");
                return;
            }

            try {
                System.IO.File.Copy(asiSource, System.IO.Path.Combine(path, "DuranOverlay.asi"), true);
                ShowOverlayMessage("УСПЕХ", "ASI плагин успешно установлен в папку с игрой!\nТеперь вы можете запускать игру (и не забудьте выключить встроенный AHK биндер!).", false);
                UpdateDashboardState();
    
            } catch (Exception ex) {
                ShowOverlayMessage("ОШИБКА", "Не удалось скопировать файл. Возможно игра запущена или у лаунчера нет прав.\nОшибка: " + ex.Message);
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e) => SaveData();
        private void Setting_Toggled(object sender, RoutedEventArgs e) 
        { 
            if (!_isLoaded || _ignoreEvents) return;

            // Specific logic for AutoRun (Registry)
            if (sender == chkAutoRun)
            {
                try {
                    string exePath = Environment.ProcessPath ?? "";
                    if (string.IsNullOrEmpty(exePath) || exePath.EndsWith(".dll")) {
                        exePath = System.IO.Path.Combine(AppContext.BaseDirectory, System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "DURANHELPER") + ".exe");
                    }
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                        if (key != null) {
                            if (chkAutoRun.IsChecked == true) 
                                key.SetValue("DuranHelper", $"\"{exePath}\" -autorun");
                            else 
                                key.DeleteValue("DuranHelper", false);
                        }
                    }
                } catch { }
            }



            SaveData();
            
            if (sender == chkSysOverlay && lawsDisabledOverlay != null) {
                lawsDisabledOverlay.Visibility = (chkSysOverlay.IsChecked == true) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (sender == chkSysBinder && bindsDisabledOverlay != null) {
                bindsDisabledOverlay.Visibility = (chkSysBinder.IsChecked == true) ? Visibility.Collapsed : Visibility.Visible;
            }
            


            // Specific logic for Advanced Overlay toggle
            if (sender == chkAdvancedOverlay)
            {
                bool isAdvanced = chkAdvancedOverlay.IsChecked == true;
                if (pnlDefaultOverlayActivators != null) pnlDefaultOverlayActivators.Visibility = isAdvanced ? Visibility.Collapsed : Visibility.Visible;
                if (pnlAdvancedOverlayActivators != null) pnlAdvancedOverlayActivators.Visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        internal void RefreshUI() 
        { 
            _ignoreEvents = true; 
            cbProfiles.ItemsSource = null; 
            cbProfiles.ItemsSource = MasterData.Keys.ToList(); 
            bool hasProfile = !string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile); 
            
            if (hasProfile) cbProfiles.SelectedItem = CurrentProfile; 
            

            lblNoProfLaws.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible; 
            gridLawsContent.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed; 
            lblNoProfBinds.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible; 
            gridBindsContent.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
            if (Tabs != null && Tabs.SelectedIndex == 4 && ViewCloud != null)
            {
                Dispatcher.BeginInvoke(new Action(() => ViewCloud.RefreshState()), System.Windows.Threading.DispatcherPriority.Background);
            }

            // Shield + button state based on profile
            _hasProfile = hasProfile;
            if (this.IsLoaded) {
                // btnCreateProfile.ApplyTemplate();

            }


            
            if (hasProfile) { 
                InitDefaults(); 
                txtOverlayName.Text = MasterData[CurrentProfile].OverlayText ?? ""; 
                UpdateLawsList(); 
                UpdateBindGroups(); 
                UpdateBindsList(); 
                
                // Update radial key in options UI
                if (MasterData[CurrentProfile].Binds.TryGetValue("Radial", out var bi))
                    btnRadialKey.Content = bi.DisplayKey;
                else
                    btnRadialKey.Content = "M3";
                
                
                // Calculate and set stats
                UpdateBindStatsUI();
                var profile = MasterData[CurrentProfile];
                int sectionsCount = profile.Laws.Count;
                int lawsCount = CountRealArticles(profile.Laws);
                
                lblStatSections.Text = sectionsCount.ToString();
                lblStatLaws.Text = lawsCount.ToString();
            } 
            _ignoreEvents = false; 

            // Switch dashboard panels based on profile/ASI state
            if (this.IsLoaded) UpdateDashboardState();

            // Update header subtitle with active profile
            if (lblHeaderSub != null) {
                string subText = hasProfile ? "АКТИВНЫЙ ПРОФИЛЬ  ·  " + CurrentProfile.ToUpper() : "";
                if (lblHeaderSub.Text != subText) {
                    lblHeaderSub.Text = subText;
                    var fadeIn = new DoubleAnimation(0, hasProfile ? 0.85 : 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    lblHeaderSub.BeginAnimation(OpacityProperty, fadeIn);
                }
            }

            if (Tabs.SelectedIndex >= 3) {
                canvasShieldBg.Visibility = Visibility.Collapsed;
                canvasShieldBg.Opacity = 0;
            }
        }

        private void UpdateBindStatsUI()
        {
            // [OLD UI] if (lblStatBinds == null || arcStatBinds == null || string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var profile = MasterData[CurrentProfile];
            int total = profile.Binds.Count;
            int active = profile.Binds.Values.Count(b => b.active);
            lblStatBinds.Text = active.ToString();
            
            double p = total > 0 ? (double)active / total : 0;
            double maxDash = 33.51; 
            double dash = maxDash * p;
            double gap = maxDash - dash + 10;
            // [OLD UI] arcStatBinds.StrokeDashArray = new DoubleCollection(new double[] { dash, gap });
            // [OLD UI] arcStatBinds.Opacity = total > 0 ? 1.0 : 0.25;
        }

        // Count real (non-placeholder) articles across all law sections.
        // Section_Changed adds empty filler items to balance columns, so Items.Count is unreliable.
        private int CountRealArticles(Dictionary<string, LawSection> laws) {
            return laws.Values.Sum(s => s.Items.Count(i => 
                i.type == "head" || !string.IsNullOrWhiteSpace(i.id) || !string.IsNullOrWhiteSpace(i.txt) || !string.IsNullOrWhiteSpace(i.pun)));
        }

        private Task AnimateDashboardStatsAsync()
        {
            // Guard: no profile loaded
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) {
                _isStartupStatsAnimating = false;
                return Task.CompletedTask;
            }

            var profile = MasterData[CurrentProfile];
            int tgtBinds = profile.Binds.Values.Count(b => b.active);
            int totalBinds = profile.Binds.Count;
            int tgtSections = profile.Laws.Count;
            int tgtLaws = CountRealArticles(profile.Laws);
            
            double p = totalBinds > 0 ? (double)tgtBinds / totalBinds : 0;
            double maxDash = 33.51;
            double targetDash = maxDash * p;
            
            double durationMs = 800.0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tcs = new TaskCompletionSource<bool>();
            
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render);
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (s, e) => {
                double t = sw.ElapsedMilliseconds / durationMs;
                if (t >= 1) {
                    t = 1;
                    timer.Stop();
                    tcs.TrySetResult(true);
                }
                
                // Linear ease exactly like GTA Money, avoids the dragged-out slow deceleration at the end.
                double easeT = t; 
                
                int curBinds = (int)(tgtBinds * easeT);
                int curSections = (int)(tgtSections * easeT);
                int curLaws = (int)(tgtLaws * easeT);
                
                lblStatBinds.Text = curBinds.ToString();
                lblStatSections.Text = curSections.ToString();
                lblStatLaws.Text = curLaws.ToString();
                
                double curDash = targetDash * easeT;
                double curGap = maxDash - curDash + 10;
                // [OLD UI] arcStatBinds.StrokeDashArray = new DoubleCollection(new double[] { curDash, curGap });
                
                if (t == 1) {
                    lblStatBinds.Text = tgtBinds.ToString();
                    lblStatSections.Text = tgtSections.ToString();
                    lblStatLaws.Text = tgtLaws.ToString();
                    _isStartupStatsAnimating = false;
                    // [OLD UI] arcStatBinds.StrokeDashArray = new DoubleCollection(new double[] { targetDash, maxDash - targetDash + 10 });
                }
            };
            timer.Start();
            return tcs.Task;
        }

        private async void SwitchTabAnim(int index) { 
            if (Tabs.SelectedIndex == index) return;
            
            // Instantly hide and reset position while WPF builds the heavy visual tree
            Tabs.BeginAnimation(OpacityProperty, null);
            if (Tabs_TT != null) Tabs_TT.BeginAnimation(TranslateTransform.YProperty, null);
            Tabs.Opacity = 0;
            if (Tabs_TT != null) Tabs_TT.Y = 15;
            
            // Set old tab to Hidden (NOT Collapsed!) — preserves measure cache for instant re-entry
            int oldIndex = Tabs.SelectedIndex;
            if (oldIndex >= 0 && oldIndex < Tabs.Items.Count && Tabs.Items[oldIndex] is TabItem oldTab) {
                oldTab.Visibility = Visibility.Hidden;
            }
            
            // Make new tab Visible before selecting
            bool firstVisit = false;
            if (index >= 0 && index < Tabs.Items.Count && Tabs.Items[index] is TabItem newTab)
            {
                firstVisit = newTab.Visibility == Visibility.Collapsed;
                newTab.Visibility = Visibility.Visible;
            }
            
            Tabs.SelectedIndex = index; 
            
            // Yield render frames for WPF to build/measure the visual tree
            await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Render);
            if (firstVisit)
            {
                // First visit — WPF needs more time to build the visual tree from scratch
                await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Loaded);
                await Task.Delay(32);
            }
            else
            {
                await Task.Delay(16);
            }
            
            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)); 
            DoubleAnimation slide = new DoubleAnimation(15, 0, TimeSpan.FromSeconds(0.25)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Tabs.BeginAnimation(OpacityProperty, fade); 
            if (Tabs_TT != null) Tabs_TT.BeginAnimation(TranslateTransform.YProperty, slide);

            // Cancel cloud loading when switching away from Cloud tab (now tab 4)
            if (index != 4 && ViewCloud != null) {
                ViewCloud.CancelLoading();
            }

            // Fade out shield on Radial (3), Cloud (4) and Options (5)
            if (index >= 3) {
                if (canvasShieldBg.Opacity > 0 || canvasShieldBg.Visibility == Visibility.Visible) {
                    DoubleAnimation fadeOutShield = new DoubleAnimation(canvasShieldBg.Opacity, 0, TimeSpan.FromSeconds(0.3));
                    fadeOutShield.Completed += (s, e) => { if (Tabs.SelectedIndex >= 3) canvasShieldBg.Visibility = Visibility.Collapsed; };
                    canvasShieldBg.BeginAnimation(OpacityProperty, fadeOutShield);
                }
            } else {
                // Fade in shield on Home (0), Laws (1), Binds (2) - but only if not in Wizard mode
                if (WinWizard.Visibility != Visibility.Visible && _hasProfile) {
                    canvasShieldBg.Visibility = Visibility.Visible;
                    DoubleAnimation fadeInShield = new DoubleAnimation(canvasShieldBg.Opacity, 1, TimeSpan.FromSeconds(0.3));
                    canvasShieldBg.BeginAnimation(OpacityProperty, fadeInShield);
                }
            }
        }
        private bool _hasProfile = true;

        private async void UpdateSidebarForSetup(bool setupMode) {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (setupMode) {
                if (navLaws.Visibility == Visibility.Visible) {
                    // Fade out Laws, Binds & Cloud
                    var fadeOut = new DoubleAnimation(navLaws.Opacity, 0, TimeSpan.FromSeconds(0.25)) { EasingFunction = ease };
                    navLaws.BeginAnimation(OpacityProperty, fadeOut);
                    navBinds.BeginAnimation(OpacityProperty, fadeOut);
                    navRadial.BeginAnimation(OpacityProperty, fadeOut);
                    navCloud.BeginAnimation(OpacityProperty, fadeOut);
                    
                    navOptions_TT.BeginAnimation(TranslateTransform.YProperty, null);
                    var slideUp = new DoubleAnimation(0, -320, TimeSpan.FromSeconds(0.25)) { EasingFunction = ease };
                    navOptions_TT.BeginAnimation(TranslateTransform.YProperty, slideUp);

                    await Task.Delay(260);
                }
                // Collapse removes layout space (Laws+Binds+Radial+Cloud); navOptions slides up
                navLaws.Visibility = Visibility.Collapsed;
                navBinds.Visibility = Visibility.Collapsed;
                navRadial.Visibility = Visibility.Collapsed;
                navCloud.Visibility = Visibility.Collapsed;
                navLaws.BeginAnimation(OpacityProperty, null);
                navBinds.BeginAnimation(OpacityProperty, null);
                navRadial.BeginAnimation(OpacityProperty, null);
                navCloud.BeginAnimation(OpacityProperty, null);
                navOptions_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navOptions_TT.Y = 0;
            } else {
                if (navLaws.Visibility == Visibility.Visible) return;

                // Pre-offset navOptions to compensate for the 240px that laws/binds/cloud will add
                navOptions_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navOptions_TT.Y = -320;
                navLaws.Opacity = 0;
                navBinds.Opacity = 0;
                navRadial.Opacity = 0;
                navCloud.Opacity = 0;
                navLaws_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navBinds_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navRadial_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navCloud_TT.BeginAnimation(TranslateTransform.YProperty, null);
                navLaws_TT.Y = 0;
                navBinds_TT.Y = 0;
                navRadial_TT.Y = 0;
                navCloud_TT.Y = 0;
                navLaws.Visibility = Visibility.Visible;
                navBinds.Visibility = Visibility.Visible;
                navRadial.Visibility = Visibility.Visible;
                navCloud.Visibility = Visibility.Visible;
                // Animate navOptions from -320 to 0 (sliding down to its natural spot)
                var slideOptDown = new DoubleAnimation(-320, 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
                navOptions_TT.BeginAnimation(TranslateTransform.YProperty, slideOptDown);
                // Fade in laws, binds, radial and cloud
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(0.1) };
                var fadeIn2 = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(0.16) };
                var fadeIn3 = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(0.22) };
                var fadeIn4 = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(0.28) };
                navLaws.BeginAnimation(OpacityProperty, fadeIn);
                navBinds.BeginAnimation(OpacityProperty, fadeIn2);
                navRadial.BeginAnimation(OpacityProperty, fadeIn3);
                navCloud.BeginAnimation(OpacityProperty, fadeIn4);
            }
        }

        private bool IsSetupIncomplete() {
            if (_isUpgrade) return true;
            bool hasGame = !string.IsNullOrEmpty(txtGamePath.Text) && System.IO.Directory.Exists(txtGamePath.Text);
            return !_hasProfile || !hasGame;
        }

        private void Nav_Main(object sender, RoutedEventArgs e) { 
            if (!_hasProfile || IsSetupIncomplete()) {
                UpdateDashboardState();
            }
            SwitchTabAnim(0);
        }
        private void Nav_Laws(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) {
                OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите первичную настройку (создайте профиль и укажите путь к игре) во вкладке «ГЛАВНАЯ».");
                navHome.IsChecked = true;
                return;
            }
            SwitchTabAnim(1);
        }
        private void Nav_Binds(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) {
                OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите первичную настройку (создайте профиль и укажите путь к игре) во вкладке «ГЛАВНАЯ».");
                navHome.IsChecked = true;
                return;
            }
            SwitchTabAnim(2);
        }
        private void Nav_Radial(object sender, RoutedEventArgs e) {
            if (IsSetupIncomplete()) {
                OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите первичную настройку (создайте профиль и укажите путь к игре) во вкладке «ГЛАВНАЯ».");
                navHome.IsChecked = true;
                return;
            }
            SwitchTabAnim(3);
            LoadRadialTab();
        }
        private void Nav_Opts(object sender, RoutedEventArgs e) { 
            WinWizard.Visibility = Visibility.Collapsed;
            SwitchTabAnim(5);
        }

        private void Nav_Cloud(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) {
                OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите первичную настройку (создайте профиль и укажите путь к игре) во вкладке «ГЛАВНАЯ».");
                navHome.IsChecked = true;
                return;
            }
            SwitchTabAnim(4);
            if (ViewCloud != null)
            {
                ViewCloud.LoadCatalog();
            }
        }

        // Background draw animation (geometric accent fade-in)
        private void AnimateShieldDraw() {
            if (ShieldFillBg == null || canvasShieldBg.Visibility != Visibility.Visible) return;
            var fadeEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            double targetOpacity = (_dashboardState == "Active") ? 1.0 : 0.15;

            // Fade in the base canvas
            canvasShieldBg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, targetOpacity, TimeSpan.FromSeconds(0.3)));
            
            // Fade in grid pattern
            ShieldFillBg.BeginAnimation(UIElement.OpacityProperty, 
                new DoubleAnimation(0, 0.3, TimeSpan.FromSeconds(0.6)) { EasingFunction = fadeEase });

            // Fade in accent lines
            if (ShieldInnerBg != null) {
                ShieldInnerBg.BeginAnimation(UIElement.OpacityProperty, 
                    new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.1), EasingFunction = fadeEase });
            }

            // Fade in + slide up details group (corner brackets + footer)
            if (ShieldDetailsBg != null && ShieldDetailsBg_TT != null) {
                ShieldDetailsBg.BeginAnimation(UIElement.OpacityProperty, 
                    new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.2), EasingFunction = fadeEase });
                ShieldDetailsBg_TT.BeginAnimation(TranslateTransform.YProperty, 
                    new DoubleAnimation(15, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(0.2), EasingFunction = fadeEase });
            }
        }

        private void ShowShieldButtons(bool show) {
            // Buttons are now physical children of GlobalShield, so they follow it automatically.
            // We only need to toggle their visibility based on the tab and profile existence.
            // CRITICAL: On Main tab, shield is on top (ZIndex=10) so buttons are clickable.
            //           On other tabs, shield goes behind content (ZIndex=-1) so tab content is visible.
            // if (canvasShieldContainer != null) {
            //     Panel.SetZIndex(canvasShieldContainer, show ? 10 : -1);
            // }
            // if (show) {
            //     if (btnCreateProfile != null) btnCreateProfile.Visibility = _hasProfile ? Visibility.Collapsed : Visibility.Visible;
            //     UpdateLaunchButtonForProfile(_hasProfile);
            // } else {
            //     if (btnCreateProfile != null) btnCreateProfile.Visibility = Visibility.Collapsed;
            //     if (btnUpdate != null) btnUpdate.Visibility = Visibility.Collapsed;
            //     if (btnInstall != null) btnInstall.Visibility = Visibility.Collapsed;
            // }
        }


        private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

        // Инициализация: загрузка данных профиля и отрисовка элементов в окне
        private void UpdateLawsList() { 
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return; 
            cbSections.ItemsSource = null; 
            var sections = MasterData[CurrentProfile].Laws.Keys.ToList();
            sections.Insert(0, "Калькулятор штрафов 📌");
            cbSections.ItemsSource = sections; 
            if (cbSections.Items.Count > 0) { 
                if (!string.IsNullOrEmpty(_lastLawSection) && cbSections.Items.Contains(_lastLawSection)) cbSections.SelectedItem = _lastLawSection;
                else cbSections.SelectedIndex = 0; 
                Section_Changed(null, null); 
                lblNoLaws.Visibility = Visibility.Collapsed; 
            } 
            else { 
                pnlLaws2Col.Visibility = pnlLawsText.Visibility = Visibility.Collapsed; 
                pnlFineCalcList.Visibility = Visibility.Collapsed;
                lblSectionType.Text = ""; 
                lblNoLaws.Visibility = Visibility.Visible; 
            } 
        }
        
        private void Section_Changed(object sender, SelectionChangedEventArgs e) 
        { 
            if (pnlEmptyLawsHint != null) pnlEmptyLawsHint.Visibility = Visibility.Collapsed;
            if (cbSections.SelectedItem == null || string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) { pnlLaws2Col.Visibility = pnlLawsText.Visibility = pnlFineCalcList.Visibility = Visibility.Collapsed; lblSectionType.Text = ""; lblNoLaws.Visibility = Visibility.Visible; return; } 
            lblNoLaws.Visibility = Visibility.Collapsed;
            btnSectionAdd.Visibility = Visibility.Visible;
            btnSectionDel.Visibility = Visibility.Visible;
            btnSectionAdd.Content = "+ СОЗДАТЬ РАЗДЕЛ";
            btnSectionDel.Content = "УДАЛИТЬ РАЗДЕЛ";
            string sec = cbSections.SelectedItem.ToString();
            _lastLawSection = sec;
            if (sec == "Калькулятор штрафов 📌") {
                pnlLaws2Col.Visibility = pnlLawsText.Visibility = Visibility.Collapsed;
                pnlFineCalcList.Visibility = Visibility.Visible;
                btnSectionDel.Visibility = Visibility.Visible;
                btnSectionDel.Content = "ОЧИСТИТЬ РАЗДЕЛ";
                btnSectionAdd.Content = "+ СОЗДАТЬ РАЗДЕЛ";
                btnSectionAdd.ClearValue(Button.WidthProperty);
                btnSectionAdd.SetResourceReference(BackgroundProperty, "LineBrush");
                btnSectionAdd.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#c9d1d9");
                cbSections.SetResourceReference(BackgroundProperty, "DeepBgBrush");
                cbSections.SetResourceReference(BorderBrushProperty, "GoldBrush");
                lblSectionType.Text = "";
                UpdateFinesList();
                return;
            }

            if (!MasterData[CurrentProfile].Laws.ContainsKey(sec)) return;
            pnlFineCalcList.Visibility = Visibility.Collapsed;
            btnSectionDel.Visibility = Visibility.Visible;
            btnSectionAdd.Content = "+ СОЗДАТЬ РАЗДЕЛ";
            btnSectionAdd.ClearValue(Button.WidthProperty);
            btnSectionAdd.SetResourceReference(BackgroundProperty, "LineBrush");
            btnSectionAdd.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#c9d1d9");
            cbSections.SetResourceReference(BackgroundProperty, "DeepBgBrush");
            cbSections.SetResourceReference(BorderBrushProperty, "LineBrush");
            
            var section = MasterData[CurrentProfile].Laws[sec];
            pnlLaws2Col.Visibility = pnlLawsText.Visibility = Visibility.Collapsed;

            if (section.Items == null) section.Items = new List<LawItem>();

            while (section.Items.Count > 0 && section.Items.Last().type == "art" && string.IsNullOrWhiteSpace(section.Items.Last().id) && string.IsNullOrWhiteSpace(section.Items.Last().txt) && string.IsNullOrWhiteSpace(section.Items.Last().pun)) {
                section.Items.RemoveAt(section.Items.Count - 1);
            }

            if (section.Type == "text") {
                lblSectionType.Text = "[ Блокнот ]"; 
                pnlLawsText.Visibility = Visibility.Visible; 
                _isLoadingRtf = true; 
                rtbNotepad.Document.Blocks.Clear();
                if (!string.IsNullOrEmpty(section.RtfData)) { 
                    try { 
                        using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(section.RtfData))) { 
                            TextRange range = new TextRange(rtbNotepad.Document.ContentStart, rtbNotepad.Document.ContentEnd); 
                            range.Load(ms, DataFormats.Rtf); 
                        } 
                    } catch { } 
                }

                _isLoadingRtf = false; 
                if (btnSaveRtf != null) { btnSaveRtf.Background = (SolidColorBrush)Application.Current.Resources["LineBrush"]; btnSaveRtf.Foreground = (SolidColorBrush)Application.Current.Resources["GrayBrush"]; }
            } 

            else {
                bool is1col = (section.Type == "1col");
                lblSectionType.Text = is1col ? "[ 1 столбец ]" : "[ 2 столбца ]"; 
                pnlLaws2Col.Visibility = Visibility.Visible;
                
                var left = new List<LawItem>();
                var right = new List<LawItem>();

                int GetSlotSize(LawItem l) => l.type == "head" ? 1 : Math.Max(1, (int)Math.Ceiling((l.txt ?? "").Length / 45.0));

                if (is1col) {
                    // 1-column mode: all items go to left, right stays empty
                    foreach (var it in section.Items) {
                        it.col = 1;
                        left.Add(it);
                    }
                    
                    int reqL = left.Count > 0 ? left.Sum(GetSlotSize) : 0;
                    int finalTarget = Math.Max(26, reqL + 1);
                    int currentL = reqL;
                    while (currentL < finalTarget) { left.Add(new LawItem { type = "art", col = 1 }); currentL++; }
                } else {
                    // 2-column mode: split items between left and right
                    if (section.Items.Count > 0 && section.Items.All(i => i.col == 0)) {
                        int totalSlots = section.Items.Sum(GetSlotSize);
                        int targetCapacity = totalSlots / 2;
                        if (targetCapacity < 26) targetCapacity = 26;
                        int curL = 0;
                        foreach (var it in section.Items) {
                            int slots = GetSlotSize(it);
                            if (curL + slots <= targetCapacity) {
                                it.col = 1;
                                curL += slots;
                            } else {
                                it.col = 2;
                            }
                        }
                    }

                    if (section.Items.Any(i => i.col == 0) && !section.Items.All(i => i.col == 0) && !section.Items.Any(k => k.col >= 2)) {
                        foreach (var i in section.Items) i.col += 1;
                    }

                    left = section.Items.Where(i => i.col == 1).ToList();
                    right = section.Items.Where(i => i.col == 2).ToList();

                    int currentL = left.Sum(GetSlotSize);
                    int currentR = right.Sum(GetSlotSize);

                    int GetMaxFilledSlot(List<LawItem> items) {
                        int s = 0, m = 0;
                        foreach(var l in items) {
                            int sz = GetSlotSize(l);
                            bool isEmpty = string.IsNullOrWhiteSpace(l.id) && string.IsNullOrWhiteSpace(l.txt) && string.IsNullOrWhiteSpace(l.pun) && l.type != "head";
                            s += sz;
                            if (!isEmpty) m = s;
                        }
                        return m;
                    }

                    int reqL = GetMaxFilledSlot(left);
                    int reqR = GetMaxFilledSlot(right);
                    int finalTarget = Math.Max(26, Math.Max(reqL + 1, reqR + 1));

                    if (finalTarget > currentL) {
                        while (currentL < finalTarget) { left.Add(new LawItem { type = "art", col = 1 }); currentL++; }
                    }
                    if (finalTarget > currentR) {
                        while (currentR < finalTarget) { right.Add(new LawItem { type = "art", col = 2 }); currentR++; }
                    }

                    void Shrink(List<LawItem> list, ref int curSlots) {
                        while (curSlots > finalTarget && list.Count > 0) {
                            var last = list.Last();
                            if (string.IsNullOrWhiteSpace(last.id) && string.IsNullOrWhiteSpace(last.txt) && string.IsNullOrWhiteSpace(last.pun) && last.type != "head") {
                                curSlots -= GetSlotSize(last);
                                list.RemoveAt(list.Count - 1);
                            } else break; 
                        }
                    }

                    if (finalTarget < currentL) Shrink(left, ref currentL);
                    if (finalTarget < currentR) Shrink(right, ref currentR);
                }

                section.Items = left.Concat(right).ToList();
                
                // Cancel any previous progressive render
                if (_renderLawsCts != null) {
                    _renderLawsCts.Cancel();
                    _renderLawsCts.Dispose();
                }
                _renderLawsCts = new System.Threading.CancellationTokenSource();
                
                // Start rendering asynchronously chunk-by-chunk to avoid UI freezes
                _lawsRenderTask = RenderLawsProgressivelyAsync(left, is1col ? null : right, _renderLawsCts.Token);

                
                // Toggle 1col/2col visual elements
                if (lawsHeaderRight != null) lawsHeaderRight.Visibility = is1col ? Visibility.Collapsed : Visibility.Visible;
                if (lawsDividerOverlay != null) lawsDividerOverlay.Visibility = is1col ? Visibility.Collapsed : Visibility.Visible;
                lvLawsRight2.Visibility = is1col ? Visibility.Collapsed : Visibility.Visible;
                // Adjust inner grid columns: 1col = single column, 2col = two columns with gap
                if (InnerLawsGrid2Col != null) {
                    InnerLawsGrid2Col.ColumnDefinitions.Clear();
                    if (is1col) {
                        InnerLawsGrid2Col.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    } else {
                        InnerLawsGrid2Col.ColumnDefinitions.Add(new ColumnDefinition());
                        InnerLawsGrid2Col.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                        InnerLawsGrid2Col.ColumnDefinitions.Add(new ColumnDefinition());
                    }
                }
                
                // Adjust header wrapper grid columns natively like InnerLawsGrid2Col
                if (lawsHeaderWrapper != null) {
                    lawsHeaderWrapper.ColumnDefinitions.Clear();
                    if (is1col) {
                        lawsHeaderWrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    } else {
                        lawsHeaderWrapper.ColumnDefinitions.Add(new ColumnDefinition());
                        lawsHeaderWrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                        lawsHeaderWrapper.ColumnDefinitions.Add(new ColumnDefinition());
                    }
                }
                
                // Rows are universal — always use fixed column widths (40 / text / 35)

                // Adjust left header
                if (lawsHeaderLeft != null) {
                    lawsHeaderLeft.Width = is1col ? 822 : 400;
                    lawsHeaderLeft.ColumnDefinitions[0].Width = new GridLength(40);
                    lawsHeaderLeft.ColumnDefinitions[1].Width = new GridLength(is1col ? 747 : 325);
                    lawsHeaderLeft.ColumnDefinitions[2].Width = new GridLength(35);
                }

                // Adjust right header
                if (lawsHeaderRight != null) {
                    lawsHeaderRight.Width = is1col ? 822 : 400; 
                    lawsHeaderRight.ColumnDefinitions[0].Width = new GridLength(40);
                    lawsHeaderRight.ColumnDefinitions[1].Width = new GridLength(is1col ? 747 : 325);
                    lawsHeaderRight.ColumnDefinitions[2].Width = new GridLength(35);
                }

                // Scales icons always visible
                if (scalesIconLeft != null) {
                    scalesIconLeft.Visibility = Visibility.Visible;
                    scalesIconLeft.Margin = is1col ? new Thickness(3, 0, 0, 0) : new Thickness(0);
                    scalesIconLeft.UseLayoutRounding = true;
                    
                    if (scalesPathLeft != null) {
                        scalesPathLeft.StrokeThickness = 1.6;
                    }
                }
                if (scalesIconRight != null) scalesIconRight.Visibility = Visibility.Visible;

                // Adjust left ListView column widths and ItemContainerStyle
                if (lvLawsLeft2 != null) {
                    string resBase = is1col ? "LawItemContainer1Col" : "LawItemContainer";
                    lvLawsLeft2.ItemContainerStyle = (Style)FindResource(resBase);
                    if (lvLawsRight2 != null) {
                        lvLawsRight2.ItemContainerStyle = (Style)FindResource(resBase);
                    }
                    
                    if (lvLawsLeft2.View is GridView gv && gv.Columns.Count >= 3) {
                        gv.Columns[0].Width = 40;
                        gv.Columns[1].Width = is1col ? 747 : 325;
                        gv.Columns[2].Width = 35;
                    }
                    if (lvLawsRight2 != null && lvLawsRight2.View is GridView gv2 && gv2.Columns.Count >= 3) {
                        gv2.Columns[0].Width = 40;
                        gv2.Columns[1].Width = 325;
                        gv2.Columns[2].Width = 35;
                    }
                }

                // Show hint if there are no real articles in both columns
                bool hasReal = left.Any(i => i.type == "head" || !string.IsNullOrWhiteSpace(i.id) || !string.IsNullOrWhiteSpace(i.txt) || !string.IsNullOrWhiteSpace(i.pun))
                            || right.Any(i => i.type == "head" || !string.IsNullOrWhiteSpace(i.id) || !string.IsNullOrWhiteSpace(i.txt) || !string.IsNullOrWhiteSpace(i.pun));
                
                if (pnlEmptyLawsHint != null) {
                    pnlEmptyLawsHint.Visibility = hasReal ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }


        private async Task RenderLawsProgressivelyAsync(List<LawItem> left, List<LawItem> right, System.Threading.CancellationToken token) {
            lvLawsLeft2.ItemsSource = null;
            lvLawsRight2.ItemsSource = null;
            
            var obsLeft = new System.Collections.ObjectModel.ObservableCollection<LawItem>();
            lvLawsLeft2.ItemsSource = obsLeft;
            
            System.Collections.ObjectModel.ObservableCollection<LawItem> obsRight = null;
            if (right != null) {
                obsRight = new System.Collections.ObjectModel.ObservableCollection<LawItem>();
                lvLawsRight2.ItemsSource = obsRight;
            }
            
            // Chunk size defines how many items are added per frame. 
            // 20 is optimal so the UI never pauses for more than ~5-10ms.
            int chunkSize = 20; 
            int maxLines = Math.Max(left.Count, right != null ? right.Count : 0);
            
            for (int i = 0; i < maxLines; i += chunkSize) {
                if (token.IsCancellationRequested) return;
                
                for (int j = 0; j < chunkSize; j++) {
                    if (i + j < left.Count) obsLeft.Add(left[i + j]);
                    if (obsRight != null && i + j < right.Count) obsRight.Add(right[i + j]);
                }
                
                // Yield to the background dispatcher allows WPF to render this chunk, keeping UI perfectly responsive
                await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Background);
                await Task.Delay(2); // tiny physical pause for smooth animation pacing
            }
        }

        private void Law_Edit_Click(object sender, MouseButtonEventArgs e) { 
            // Если клавиша не найдена сохраняем по всем найденным кнопкам
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is ListViewItem)) {
                if (dep is System.Windows.Controls.GridViewColumnHeader) return;
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is ListViewItem lvi && lvi.DataContext is LawItem item) {
                _tempLaw = item; 
                lblLawEditError.Visibility = Visibility.Collapsed;
                txtLawID.Text = _tempLaw.id; 
                txtLawText.Text = _tempLaw.txt; 
                txtLawPunish.Text = _tempLaw.pun; 
                cbLawType.SelectedIndex = _tempLaw.type == "head" ? 1 : 0; 
                
                string currentSection = cbSections.SelectedItem?.ToString();
                // Punishment field is always visible — rows are universal
                pnlLawPun.Visibility = Visibility.Visible;
                
                OpenOverlay(WinLawEdit); 
            }
        }

        private void ShowSaveLoading() {
            WinSaveLoading.Opacity = 0;
            WinSaveLoading.Visibility = Visibility.Visible;
            WinSaveLoading.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
        }

        private void HideSaveLoading(Action onComplete) {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
            anim.Completed += (s, e) => {
                WinSaveLoading.Visibility = Visibility.Collapsed;
                onComplete?.Invoke();
            };
            WinSaveLoading.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void Law_Save_Click(object sender, RoutedEventArgs e) { 
            lblLawEditError.Visibility = Visibility.Collapsed;


            string currentSection = cbSections.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(currentSection) || !MasterData[CurrentProfile].Laws.ContainsKey(currentSection)) return;
            var section = MasterData[CurrentProfile].Laws[currentSection];
            int index = section.Items.IndexOf(_tempLaw);

            int slotDiff = 0;
            List<int> itemsToRemove = new List<int>();

            if (index != -1 && cbLawType.SelectedIndex != 1) {
                int oldSlots = _tempLaw.type == "head" ? 1 : Math.Max(1, (int)Math.Ceiling((_tempLaw.txt ?? "").Length / 45.0));
                int newSlots = Math.Max(1, (int)Math.Ceiling(txtLawText.Text.Length / 45.0));
                slotDiff = newSlots - oldSlots;

                if (slotDiff > 0) {
                    int checkedSlots = 0;
                    for (int i = 1; i < section.Items.Count - index; i++) {
                        var nextItem = section.Items[index + i];
                        if (nextItem.type == "art" && string.IsNullOrWhiteSpace(nextItem.id) && string.IsNullOrWhiteSpace(nextItem.txt) && string.IsNullOrWhiteSpace(nextItem.pun)) {
                            itemsToRemove.Add(index + i);
                            checkedSlots++;
                            if (checkedSlots == slotDiff) break;
                        } else {
                            // If we hit a filled row, just stop tracking empty slots to delete.
                            // The system will effectively insert visual space and expand the panel normally.
                            break;
                        }
                    }
                }
            }

            string oldType = _tempLaw.type;
            string oldTxt = _tempLaw.txt;
            string oldId = _tempLaw.id;
            string oldPun = _tempLaw.pun;

            _tempLaw.type = cbLawType.SelectedIndex == 1 ? "head" : "art"; 
            _tempLaw.id = _tempLaw.type == "head" ? "" : txtLawID.Text; 
            _tempLaw.txt = txtLawText.Text; 
            _tempLaw.pun = _tempLaw.type == "head" ? "" : txtLawPunish.Text;  

            if (index != -1 && cbLawType.SelectedIndex != 1) {
                for (int i = itemsToRemove.Count - 1; i >= 0; i--) {
                    section.Items.RemoveAt(itemsToRemove[i]);
                }
                if (slotDiff < 0) {
                    for (int i = 0; i < -slotDiff; i++) {
                        section.Items.Insert(index + 1, new LawItem { type = "art", id = "", txt = "", pun = "", col = _tempLaw.col });
                    }
                }
            }
            
            Action onComplete = () => {
                Section_Changed(null, null);
                // Refresh stats live
                if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                    var pf = MasterData[CurrentProfile];
                    lblStatSections.Text = pf.Laws.Count.ToString();
                    lblStatLaws.Text = CountRealArticles(pf.Laws).ToString();
                }
                Overlay_Close(null, null); 
            };
            
            if (_isEngineRunning) {
                ShowSaveLoading();
                SaveData(() => Dispatcher.Invoke(() => HideSaveLoading(onComplete)));
            } else {
                SaveData();
                onComplete();
            }
        }

        private void Law_Delete_Click(object sender, RoutedEventArgs e) { 
            _tempLaw.id = ""; 
            _tempLaw.txt = ""; 
            _tempLaw.pun = ""; 
            _tempLaw.type = "art"; 

            Action onComplete = () => {
                Section_Changed(null, null);
                // Refresh stats live
                if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                    var pf = MasterData[CurrentProfile];
                    lblStatSections.Text = pf.Laws.Count.ToString();
                    lblStatLaws.Text = CountRealArticles(pf.Laws).ToString();
                }
                Overlay_Close(null, null); 
            };

            if (_isEngineRunning) {
                ShowSaveLoading();
                SaveData(() => Dispatcher.Invoke(() => HideSaveLoading(onComplete)));
            } else {
                SaveData();
                onComplete();
            }
        }

        private void Law_Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) return;
            _dragStartPoint = e.GetPosition(null);
        }

        private void Law_Item_MouseMove(object sender, MouseEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed) {
                var lvi = sender as ListViewItem;
                var item = lvi?.DataContext as LawItem;
                
                if (item == null || item.type == "head" || (string.IsNullOrWhiteSpace(item.id) && string.IsNullOrWhiteSpace(item.txt))) return; 

                Vector diff = _dragStartPoint - e.GetPosition(null);
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance) {
                    var layer = AdornerLayer.GetAdornerLayer(MainAppUI);
                    if (layer != null) {
                        _dragAdorner = new DragAdorner(lvi, e.GetPosition(lvi));
                        layer.Add(_dragAdorner);
                        DragDrop.DoDragDrop(lvi, item, DragDropEffects.Move);
                        layer.Remove(_dragAdorner);
                        _dragAdorner = null;
                    }
                }
            }
        }

        private void Law_Item_DragEnter(object sender, DragEventArgs e) {
            var lvi = sender as ListViewItem;
            if (lvi != null && e.Data.GetDataPresent(typeof(LawItem))) {
                lvi.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            }
        }
        
        private void Law_Item_DragLeave(object sender, DragEventArgs e) {
            if (sender is ListViewItem lvi) lvi.Background = Brushes.Transparent;
        }

        private void Law_Item_Drop(object sender, DragEventArgs e) {
            var lvi = sender as ListViewItem;
            if (lvi != null) lvi.Background = Brushes.Transparent; 

            if (e.Data.GetDataPresent(typeof(LawItem))) {
                var src = e.Data.GetData(typeof(LawItem)) as LawItem;
                var trg = lvi?.DataContext as LawItem;
                string sec = cbSections.SelectedItem?.ToString();
                
                if (src != null && trg != null && src != trg && !string.IsNullOrEmpty(sec)) {
                    var items = MasterData[CurrentProfile].Laws[sec].Items;
                    
                    int i1 = items.IndexOf(src);
                    int i2 = items.IndexOf(trg);
                    
                    if (i1 > -1 && i2 > -1) {
                        if (string.IsNullOrWhiteSpace(trg.id) && trg.type != "head") {
                            // Полное удаление в режиме спискак, чтобы не сломать переключение
                            items[i1] = trg;
                            items[i2] = src;
                        } else {
                            // Удаляем элемент списка через
                            items.RemoveAt(i1);
                            int newTarget = items.IndexOf(trg);
                            items.Insert(newTarget, src);
                        }
                        
                        SaveData();
                        Section_Changed(null, null);
                    }
                }
            }
        }

        private void cbLawType_SelectionChanged(object sender, SelectionChangedEventArgs e) { 
            if (pnlLawID == null) return; 
            bool isHead = cbLawType.SelectedIndex == 1; 
            pnlLawID.Visibility = isHead ? Visibility.Collapsed : Visibility.Visible; 
            pnlLawPun.Visibility = isHead ? Visibility.Collapsed : Visibility.Visible; 
            if (lblLawTextTitle != null) {
                lblLawTextTitle.Text = isHead ? "Текст заголовка:" : "Текст статьи (без ограничений):";
            }
        }
        private void RtbNotepad_PreviewKeyDown(object sender, KeyEventArgs e) { }

        private void RtbNotepad_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e) { }

        private void RtbNotepad_TextChanged(object sender, TextChangedEventArgs e) { 
            if (_isLoadingRtf) return;

            if (btnSaveRtf != null) { 
                btnSaveRtf.Background = (SolidColorBrush)Application.Current.Resources["GreenBrush"]; 
                btnSaveRtf.Foreground = Brushes.White; 
            }
        }

        private void RtfColor_Click(object sender, RoutedEventArgs e) { var btn = sender as Button; if (btn != null && rtbNotepad.Selection != null && !rtbNotepad.Selection.IsEmpty) rtbNotepad.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, btn.Background); }
        private void RtfSave_Click(object sender, RoutedEventArgs e) { string sec = cbSections.SelectedItem?.ToString(); if (string.IsNullOrEmpty(sec) || !MasterData[CurrentProfile].Laws.ContainsKey(sec)) return; using (MemoryStream ms = new MemoryStream()) { TextRange range = new TextRange(rtbNotepad.Document.ContentStart, rtbNotepad.Document.ContentEnd); range.Save(ms, DataFormats.Rtf); MasterData[CurrentProfile].Laws[sec].RtfData = Encoding.UTF8.GetString(ms.ToArray()); } SaveData(); btnSaveRtf.Background = (SolidColorBrush)Application.Current.Resources["LineBrush"]; btnSaveRtf.Foreground = (SolidColorBrush)Application.Current.Resources["GrayBrush"]; ShowCustomToast("Сохранено!"); }

        
        private void Profile_Changed(object sender, SelectionChangedEventArgs e) {
            if (_ignoreEvents) return;
            if (cbProfiles?.SelectedItem == null) return;
            string sel = cbProfiles.SelectedItem.ToString();
            if (sel != CurrentProfile && MasterData.ContainsKey(sel)) {
                CurrentProfile = sel;
                SaveData();
                RefreshUI();
            }
        }

        private void Profile_Create_Click(object sender, RoutedEventArgs e) {
            _dialogHost.ShowInput("ИМЯ НОВОГО ПРОФИЛЯ", async (v) => {
                MasterData[v] = new ProfileData();
                MasterData[v].Groups = new List<string> { "ВСЕ" };
                MasterData[v].Variables = new Dictionary<string, string> { {"*ИМЯ*", ""}, {"*ФАМ*", ""}, {"*ТЕГ*", ""}, {"*ЗВ*", ""} };
                CurrentProfile = v; 
                await Task.Delay(200); // Allow dialog animation to finish
                RefreshUI(); SaveData();
            }, validator: (v) => MasterData.ContainsKey(v) ? "Группа с таким именем уже существует!" : null);
        }

private void Profile_Clone_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag != null) { var target = btn.Tag.ToString(); _dialogHost.ShowInput("ИМЯ КЛОНА ПРОФИЛЯ", async (v) => { var d = MasterData[target]; MasterData[v] = JsonConvert.DeserializeObject<ProfileData>(JsonConvert.SerializeObject(d)); CurrentProfile = v; await Task.Delay(200); RefreshUI(); SaveData(); }, validator: (v) => MasterData.ContainsKey(v) ? "Имя занято!" : null); } }
        private void Profile_Rename_Item_Click(object sender, RoutedEventArgs e) { var target = (sender as Button).Tag.ToString(); _dialogHost.ShowInput("НОВОЕ ИМЯ ПРОФИЛЯ", async (v) => { var d = MasterData[target]; MasterData.Remove(target); MasterData[v] = d; CurrentProfile = v; await Task.Delay(200); RefreshUI(); SaveData(); }, validator: (v) => MasterData.ContainsKey(v) ? "Имя занято!" : null); }
        private void Profile_Delete_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrEmpty(CurrentProfile)) { _dialogHost.ShowAlert("УДАЛЕНИЕ", "Удалить профиль " + CurrentProfile + "?", async () => { if (MasterData.ContainsKey(CurrentProfile)) { MasterData.Remove(CurrentProfile); CurrentProfile = MasterData.Count > 0 ? MasterData.Keys.First() : ""; await Task.Delay(200); SaveData(); RefreshUI(); } }); } }
        private void Disable_Scroll(object sender, MouseWheelEventArgs e) { e.Handled = true; }
        private void Disable_KeyScroll(object sender, KeyEventArgs e) { 
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.PageDown || e.Key == Key.PageUp || e.Key == Key.Home || e.Key == Key.End) e.Handled = true; 
        }

        private void Law_Item_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }
        
        private void Section_Add_Click(object sender, RoutedEventArgs e) {
            _dialogHost.ShowInput("НАЗВАНИЕ РАЗДЕЛА", (v) => {
                string t = "1col"; if (_dialogHost.GetSectionTypeIndex() == 1) t = "2col"; else if (_dialogHost.GetSectionTypeIndex() == 2) t = "text";
                string newName = v.Trim();
                string baseName = newName;
                int count = 1;
                while (MasterData[CurrentProfile].Laws.Keys.Any(k => string.Equals(k, newName, StringComparison.OrdinalIgnoreCase))) { 
                    newName = $"{baseName} #{count}"; 
                    count++; 
                }
                MasterData[CurrentProfile].Laws[newName] = new LawSection { Type = t };
                _lastLawSection = newName;
                UpdateLawsList();
                int sectionsCount = MasterData[CurrentProfile].Laws.Count;
                int lawsCount = CountRealArticles(MasterData[CurrentProfile].Laws);
                lblStatSections.Text = sectionsCount.ToString();
                lblStatLaws.Text = lawsCount.ToString();
                SaveData();
            }, watermark: "Введите имя раздела", maxLength: 20, showSectionType: true);
        }
        private void Section_Rename_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag != null) { var old = btn.Tag.ToString(); _dialogHost.ShowInput("НОВОЕ ИМЯ РАЗДЕЛА", (v) => { string newName = v.Trim(); string baseName = newName; if (!string.Equals(old, newName, StringComparison.OrdinalIgnoreCase)) { int count = 1; while (MasterData[CurrentProfile].Laws.Keys.Any(k => string.Equals(k, newName, StringComparison.OrdinalIgnoreCase))) { newName = $"{baseName} #{count}"; count++; } } var d = MasterData[CurrentProfile].Laws[old]; MasterData[CurrentProfile].Laws.Remove(old); MasterData[CurrentProfile].Laws[newName] = d; _lastLawSection = newName; UpdateLawsList(); SaveData(); }, maxLength: 20); } }

        private void Section_Delete_Click(object sender, RoutedEventArgs e) { 
            if (cbSections.SelectedItem != null) { 
                var sec = cbSections.SelectedItem.ToString(); 
                int oldIndex = cbSections.SelectedIndex;
                if (sec == "Калькулятор штрафов 📌") {
                    _dialogHost.ShowAlert("ОЧИСТИТЬ РАЗДЕЛ", $"Вы уверены, что хотите удалить ВСЕ статьи из калькулятора штрафов?", () => {
                        if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                            MasterData[CurrentProfile].Fines.Clear();
                            UpdateFinesList();
                            UpdateBindStatsUI();
                            SaveData();
                        }
                    });
                } else {
                    _dialogHost.ShowAlert("УДАЛИТЬ РАЗДЕЛ", $"Вы уверены, что хотите удалить раздел «{sec}» и все его данные?", () => { 
                        if (!string.IsNullOrEmpty(sec) && MasterData[CurrentProfile].Laws.ContainsKey(sec)) { 
                            // Determine next selection
                            string nextSec = "Калькулятор штрафов 📌";
                            if (oldIndex > 0 && cbSections.Items.Count > 1) {
                                int nextIndex = oldIndex - 1;
                                nextSec = cbSections.Items[nextIndex].ToString();
                            }

                            MasterData[CurrentProfile].Laws.Remove(sec); 
                            _lastLawSection = nextSec;
                            UpdateLawsList(); 
                            SaveData(); 
                        } 
                    }); 
                }
            } 
        }

        internal void UpdateBindGroups() { 
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return; 
            var p = MasterData[CurrentProfile]; 
            if (p.Groups == null || p.Groups.Count == 0) p.Groups = new List<string> { "ВСЕ" }; 
            p.Groups = p.Groups.Distinct().ToList(); 
            if (p.Groups.IndexOf("ВСЕ") != 0) { p.Groups.Remove("ВСЕ"); p.Groups.Insert(0, "ВСЕ"); } 
            if (!p.Groups.Contains(_currentBindGroup)) _currentBindGroup = "ВСЕ";
            icBindGroups.ItemsSource = p.Groups.Select(g => new BindGroupItem { Name = g, BindsCount = (g == "ВСЕ" ? p.Binds.Count : p.Binds.Values.Count(b => b.group == g)) }).ToList(); 
            UpdateToggleGroupButton();
        }

        private async void Bind_Group_Click(object sender, RoutedEventArgs e) { 
            _currentBindGroup = (sender as RadioButton).Tag.ToString(); 
            // Yield UI thread to let radio button animate state
            await Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.Background);
            await Task.Delay(20);
            UpdateBindsList(); 
            UpdateToggleGroupButton(); 

            // causes a 2-second bottleneck. Settings states are naturally persisted during App_Close.
        }
        private void Group_Radio_Loaded(object sender, RoutedEventArgs e) { if (sender is RadioButton rb && rb.Tag != null) { if (rb.Tag.ToString() == _currentBindGroup) rb.IsChecked = true; } }

        private void UpdateToggleGroupButton()
        {

        }

        private void Groups_Edit_Click(object sender, RoutedEventArgs e) { 
            if (string.IsNullOrEmpty(CurrentProfile)) return;
            DialogOverlay.Visibility = Visibility.Visible;
            DialogOverlay.Opacity = 0;
            DialogOverlay.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
            var win = new GroupEditorWindow(this, CurrentProfile);
            win.Owner = this;
            win.ShowDialog();
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fadeOut.Completed += (s, _) => DialogOverlay.Visibility = Visibility.Collapsed;
            DialogOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
        private void Group_Add_Click(object sender, RoutedEventArgs e) { _dialogHost.ShowInput("ИМЯ ГРУППЫ", (v) => { MasterData[CurrentProfile].Groups.Add(v); UpdateBindGroups(); SaveData(); }, validator: (v) => MasterData[CurrentProfile].Groups.Contains(v) ? "Группа уже существует!" : null); }
        private void Group_Rename_Click(object sender, RoutedEventArgs e) { var target = (sender as Button).Tag.ToString(); _dialogHost.ShowInput("НОВОЕ ИМЯ", (v) => { int i = MasterData[CurrentProfile].Groups.IndexOf(target); MasterData[CurrentProfile].Groups[i] = v; foreach(var b in MasterData[CurrentProfile].Binds.Values) { if (b.group == target) b.group = v; } UpdateBindGroups(); Groups_Edit_Click(null, null); UpdateBindsList(); SaveData(); }, validator: (v) => MasterData[CurrentProfile].Groups.Contains(v) ? "Группа уже существует!" : null); }
        private void Group_Delete_Click(object sender, RoutedEventArgs e) { string g = (sender as Button).Tag.ToString(); MasterData[CurrentProfile].Groups.Remove(g); foreach (var b in MasterData[CurrentProfile].Binds.Values) { if (b.group == g) b.group = "ВСЕ"; } SaveData(); UpdateBindGroups(); UpdateBindsList(); Groups_Edit_Click(null, null); }
        private void Group_Clear_Click(object sender, RoutedEventArgs e) {
            string groupName = null;
            if (sender is MenuItem mi) groupName = mi.Tag?.ToString();
            if (string.IsNullOrEmpty(groupName) || !MasterData.ContainsKey(CurrentProfile)) return;
            _dialogHost.ShowAlert("ОЧИСТИТЬ ГРУППУ", $"Удалить все бинды из группы \"{groupName}\"?", () => {
                if (groupName == "ВСЕ") {
                    MasterData[CurrentProfile].Binds.Clear();
                } else {
                    var toRemove = MasterData[CurrentProfile].Binds.Where(kvp => kvp.Value.group == groupName).Select(kvp => kvp.Key).ToList();
                    foreach (var id in toRemove) MasterData[CurrentProfile].Binds.Remove(id);
                }
                InvalidateCloudBindPacks();
                Action onComplete = () => { UpdateBindsList(); UpdateBindGroups(); UpdateBindStatsUI(); };
                if (_isEngineRunning) { ShowSaveLoading(); SaveData(() => Dispatcher.Invoke(() => HideSaveLoading(onComplete))); } else { SaveData(); onComplete(); }
            });
        }
        private void InvalidateCloudBindPacks() {
            try {
                if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
                var p = MasterData[CurrentProfile];
                if (p.InstalledCloudIds == null || p.InstalledCloudIds.Count == 0) return;
                if (ViewCloud != null) {
                    var bindPackIds = ViewCloud.GetBindTypeCloudIds();
                    if (bindPackIds != null && bindPackIds.Count > 0) {
                        p.InstalledCloudIds.RemoveAll(id => bindPackIds.Contains(id));
                    }
                    // Refresh cloud state asynchronously to prevent UI lag during bind deletion
                    Dispatcher.InvokeAsync(() => ViewCloud.RefreshState(), System.Windows.Threading.DispatcherPriority.Background);
                }
            } catch { }
        }

        private void Group_Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(sender as IInputElement);
        private void Group_Item_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) { var b = sender as Border; if(b != null) { var layer = AdornerLayer.GetAdornerLayer(b); if (layer != null) { _dragAdorner = new DragAdorner(b, e.GetPosition(b)); layer.Add(_dragAdorner); DragDrop.DoDragDrop(b, b.DataContext, DragDropEffects.Move); layer.Remove(_dragAdorner); _dragAdorner = null; } } } }
        private void Group_Item_Drop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(typeof(BindGroupItem))) { var src = e.Data.GetData(typeof(BindGroupItem)) as BindGroupItem; var trg = (sender as Border)?.DataContext as BindGroupItem; if (src != null && trg != null && src.Name != trg.Name) { var list = MasterData[CurrentProfile].Groups; int i1 = list.IndexOf(src.Name); int i2 = list.IndexOf(trg.Name); if (i1 > -1 && i2 > -1) { list.RemoveAt(i1); list.Insert(i2, src.Name); SaveData(); Groups_Edit_Click(null, null); UpdateBindGroups(); } } } }

        private void Bind_Group_DragEnter(object sender, DragEventArgs e) 
        { 
            if (!e.Data.GetDataPresent(typeof(BindItem))) return;
            if (sender is RadioButton rb) 
            { 
                // Find the inner Border in the template and highlight it
                var bd = rb.Template.FindName("bd", rb) as System.Windows.Controls.Border;
                if (bd != null) { bd.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2d)); bd.BorderBrush = (SolidColorBrush)Application.Current.Resources["GoldBrush"]; bd.BorderThickness = new Thickness(1.5); bd.RenderTransform = new ScaleTransform(1.03, 1.03); bd.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); }
            } 
        }
        private void Bind_Group_DragLeave(object sender, DragEventArgs e) 
        { 
            if (sender is RadioButton rb) 
            { 
                var bd = rb.Template.FindName("bd", rb) as System.Windows.Controls.Border;
                if (bd != null) { bd.Background = Brushes.Transparent; bd.BorderBrush = Brushes.Transparent; bd.BorderThickness = new Thickness(0); bd.RenderTransform = null; }
            } 
        }
        private void Bind_Group_DragOver(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(typeof(BindItem))) { e.Effects = DragDropEffects.Move; e.Handled = true; } else { e.Effects = DragDropEffects.None; } }
        private void Bind_Group_Drop(object sender, DragEventArgs e) 
        { 
            if (sender is RadioButton rb) 
            { 
                // Restore drag highlight visual state
                var bd = rb.Template.FindName("bd", rb) as System.Windows.Controls.Border;
                if (bd != null) { bd.Background = Brushes.Transparent; bd.BorderBrush = Brushes.Transparent; bd.BorderThickness = new Thickness(0); bd.RenderTransform = null; }
                var bind = e.Data.GetData(typeof(BindItem)) as BindItem; 
                if (bind != null) 
                { 
                    bind.group = rb.Tag.ToString(); 
                    // Switch view to the group we just dropped into
                    _currentBindGroup = rb.Tag.ToString();
                    SaveData(); 
                    UpdateBindGroups();   // refreshes radio buttons - Group_Radio_Loaded will check the right one
                    UpdateBindsList();    // now filters by the new _currentBindGroup
                } 
            } 
        }

        private bool _canDragBindTile = false;
        private void Bind_Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragStartPoint = e.GetPosition(null); _canDragBindTile = true; }
        private void Bind_Tile_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && _canDragBindTile) { var border = sender as Border; if (border == null || border.DataContext == null) return; Vector diff = _dragStartPoint - e.GetPosition(null); if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance) { var layer = AdornerLayer.GetAdornerLayer(MainAppUI); if (layer != null) { _dragAdorner = new DragAdorner(border, e.GetPosition(border)); layer.Add(_dragAdorner); DragDrop.DoDragDrop(border, border.DataContext, DragDropEffects.Move); layer.Remove(_dragAdorner); _dragAdorner = null; _canDragBindTile = false; } } } else if (e.LeftButton != MouseButtonState.Pressed) { _canDragBindTile = false; } }
        protected override void OnGiveFeedback(GiveFeedbackEventArgs e) { base.OnGiveFeedback(e); if (_dragAdorner != null) { Win32Point w32Mouse = new Win32Point(); GetCursorPos(ref w32Mouse); Point pos = this.PointFromScreen(new Point(w32Mouse.X, w32Mouse.Y)); _dragAdorner.UpdatePosition(pos); e.UseDefaultCursors = false; if (e.Effects == DragDropEffects.Move) Mouse.SetCursor(Cursors.Hand); else Mouse.SetCursor(Cursors.SizeAll); e.Handled = true; } }
        
        private void svBinds_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (svBinds == null) return;
            Point position = e.GetPosition(svBinds);
            double scrollMargin = 30.0;
            double scrollStep = 10.0;

            if (position.Y < scrollMargin) { svBinds.ScrollToVerticalOffset(svBinds.VerticalOffset - scrollStep); }
            else if (position.Y > svBinds.ActualHeight - scrollMargin) { svBinds.ScrollToVerticalOffset(svBinds.VerticalOffset + scrollStep); }
        }

        private void Bind_Tile_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(BindItem))) {
                var border = sender as Border;
                if (border != null) {
                    border.SetResourceReference(Border.BorderBrushProperty, "GreenBrush");
                    border.SetResourceReference(Border.BackgroundProperty, "MenuHoverBrush");
                }
            }
        }

        private void Bind_Tile_DragLeave(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            if (border != null && border.DataContext is BindItem bind) {
                if (bind.isAuto) border.SetResourceReference(Border.BorderBrushProperty, "GoldBrush");
                else border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                
                // Reset background to original: list tiles use Transparent, grid tiles use BgBrush
                if (border.Height > 80) // grid tile = 110
                    border.SetResourceReference(Border.BackgroundProperty, "BgBrush");
                else
                    border.Background = System.Windows.Media.Brushes.Transparent;
            }
        }

        private void Bind_Tile_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Bind_Tile_Drop(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                var targetBind = border.DataContext as BindItem;
                Bind_Tile_DragLeave(sender, e);

                if (e.Data.GetDataPresent(typeof(BindItem)))
                {
                    var sourceBind = e.Data.GetData(typeof(BindItem)) as BindItem;
                    if (sourceBind != null && targetBind != null && sourceBind != targetBind)
                    {
                        var bindsDict = MasterData[CurrentProfile].Binds;
                        var allBindsList = bindsDict.Values.ToList();
                        
                        int globalSourceIndex = allBindsList.IndexOf(sourceBind);
                        int globalTargetIndex = allBindsList.IndexOf(targetBind);

                        if (globalSourceIndex >= 0 && globalTargetIndex >= 0)
                        {
                            allBindsList.RemoveAt(globalSourceIndex);
                            allBindsList.Insert(globalTargetIndex, sourceBind);

                            var newDict = new Dictionary<string, BindItem>();
                            foreach (var b in allBindsList) newDict[b.id] = b;
                            
                            MasterData[CurrentProfile].Binds = newDict;
                            SaveData();
                            UpdateBindsList();
                        }
                    }
                }
            }
        }
        internal void UpdateBindsList() { 
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return; 
            
            foreach (var kvp in MasterData[CurrentProfile].Binds) {
                if (string.IsNullOrEmpty(kvp.Value.id)) kvp.Value.id = kvp.Key;
            }

            var all = MasterData[CurrentProfile].Binds.Values.Where(b => b.id != "Radial").ToList(); 
            var filtered = _currentBindGroup == "ВСЕ" ? all : all.Where(b => b.group == _currentBindGroup).ToList();
            
            if (txtBindSearch != null && !string.IsNullOrWhiteSpace(txtBindSearch.Text)) {
                string q = txtBindSearch.Text.ToLower();
                filtered = filtered.Where(b => b.name.ToLower().Contains(q) || (b.key != null && b.key.ToLower().Contains(q))).ToList();
            }
            
            // SIGNIFICANT PERFORMANCE FIX:
            // Do NOT bind the 5000 elements to BOTH the Grid ItemsControl and the List ItemsControl simultaneously!
            // This forces the Main Thread to generate 10000 DOM structures, causing arbitrary 2-3 sec hangs!
            // Bind exclusively to the currently active View.
            if (_isListView) {
                icBindsGrid.ItemsSource = null;
                icBinds.ItemsSource = filtered;
            } else {
                icBinds.ItemsSource = null;
                icBindsGrid.ItemsSource = filtered;
            }

            if (pnlNoBinds != null) {
                pnlNoBinds.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (filtered.Count == 0 && all.Count > 0 && _currentBindGroup != "ВСЕ") {
                    if (lblNoBindsTitle != null) lblNoBindsTitle.Text = "ГРУППА ПУСТАЯ";
                    if (lblNoBindsDesc != null) lblNoBindsDesc.Text = "Перетащите плашку бинда за левый верхний угол\nв любую группу из списка \"Ваши группы\".";
                } else {
                    if (lblNoBindsTitle != null) lblNoBindsTitle.Text = "БИНДЫ НЕ НАСТРОЕНЫ";
                    if (lblNoBindsDesc != null) lblNoBindsDesc.Text = "Создайте новый бинд или скачайте готовый\nнабор из облачного каталога.";
                }
            }

            if (lblStatBinds != null) lblStatBinds.Text = all.Count(b => b.active).ToString();
        }
        
        private void BindSearch_TextChanged(object sender, TextChangedEventArgs e) { 
            UpdateBindsList(); 
        }

        private bool _isListView = false;
        private void ToggleBindsView_Click(object sender, MouseButtonEventArgs e)
        {
            _isListView = !_isListView;
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\DuranSettings"))
            {
                key.SetValue("BindsListView", _isListView ? "1" : "0");
            }
            SetBindsViewMode();
            UpdateBindsList(); // Inject the data exclusively into the newly visible list
        }

        private void SetBindsViewMode()
        {
            Brush activeBg = (Brush)Application.Current.Resources["SideBrush"];
            Brush activeIcon = (Brush)Application.Current.Resources["GoldBrush"];
            Brush inactiveIcon = (Brush)Application.Current.Resources["GrayBrush"];

            if (_isListView) {
                icBinds.Visibility = Visibility.Visible;
                icBindsGrid.Visibility = Visibility.Collapsed;
                bgViewList.Background = activeBg;
                bgViewGrid.Background = Brushes.Transparent;
                iconListPath.Stroke = activeIcon;
                iconTilesPath.Fill = inactiveIcon;
            } else {
                icBinds.Visibility = Visibility.Collapsed;
                icBindsGrid.Visibility = Visibility.Visible;
                bgViewList.Background = Brushes.Transparent;
                bgViewGrid.Background = activeBg;
                iconListPath.Stroke = inactiveIcon;
                iconTilesPath.Fill = activeIcon;
            }
        }
        
        private void Vars_Edit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentProfile)) return;
            DialogOverlay.Visibility = Visibility.Visible;
            DialogOverlay.Opacity = 0;
            DialogOverlay.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
            var win = new VariableEditorWindow(this, CurrentProfile);
            win.Owner = this;
            win.ShowDialog();
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fadeOut.Completed += (s, _) => DialogOverlay.Visibility = Visibility.Collapsed;
            DialogOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }


        private void Bind_Create_Click(object sender, RoutedEventArgs e) { _dialogHost.ShowInput("Создание бинда", (v) => { CreateBindWithname(v); SaveData(); }, watermark: "Введите имя бинда"); }
        private void CreateBindWithname(string name) 
        { 
            string id = Guid.NewGuid().ToString().Substring(0, 6); 
            _tempBind = new BindItem { id = id, name = name, group = _currentBindGroup, steps = new List<BindStep>() }; 
            BinderEditorControl.txtBindAutoTrigger.Text = ""; 
            BinderEditorControl.rbTypeKey.IsChecked = true;
            BinderEditorControl.btnBindKey.Content = "КЛАВИША: НЕТ";
            AddStep(new BindStep { action = "CHAT", desc = "ЧАТ", value="", isEnter = true, ColorCode="#1f6feb" }); 
            BinderEditorControl.icBindSteps.ItemsSource = null; 
            UpdateBindsStepsIndex();
            OpenBindEditor(); 
        }

        private void Bind_Edit_Click(object sender, RoutedEventArgs e) 
        { 
            try {
                string id = (sender as Button)?.Tag?.ToString();
                if (string.IsNullOrEmpty(id)) { OpenInfo("ОШИБКА", "Бинду не назначен ID!"); return; }
                if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
                
                var bindToEdit = MasterData[CurrentProfile].Binds.Values.FirstOrDefault(b => b.id == id);
                if (bindToEdit == null) { OpenInfo("ОШИБКА", $"Бинд с ID {id} не найден!"); return; }
                
                string json = JsonConvert.SerializeObject(bindToEdit);
                _tempBind = JsonConvert.DeserializeObject<BindItem>(json);
                if (_tempBind.steps == null) _tempBind.steps = new List<BindStep>();
                BinderEditorControl.icBindSteps.ItemsSource = null; 
                UpdateBindsStepsIndex(); 
                OpenBindEditor(); 
            } catch (Exception ex) {
                OpenInfo("ОШИБКА", "Не удалось открыть редактор: " + ex.Message);
            }
        }

        private void Bind_Tile_Click(object sender, MouseButtonEventArgs e)
        {
            try {
                DependencyObject obj = e.OriginalSource as DependencyObject;
                while (obj != null)
                {
                    if (obj is Button || obj is CheckBox) return;
                    obj = VisualTreeHelper.GetParent(obj);
                }

                if (sender is Border border)
                {
                    string id = border.Tag?.ToString();
                    if (string.IsNullOrEmpty(id)) { return; }
                    if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;

                    var bindToEdit = MasterData[CurrentProfile].Binds.Values.FirstOrDefault(b => b.id == id);
                    if (bindToEdit == null) return;
                    
                    string json = JsonConvert.SerializeObject(bindToEdit);
                    _tempBind = JsonConvert.DeserializeObject<BindItem>(json);
                    if (_tempBind.steps == null) _tempBind.steps = new List<BindStep>();
                    BinderEditorControl.icBindSteps.ItemsSource = null; 
                    UpdateBindsStepsIndex(); 
                    OpenBindEditor(); 
                }
            } catch (Exception ex) {
                OpenInfo("ОШИБКА", "Не удалось открыть редактор: " + ex.Message);
            }
        }
        
        private void OpenBindEditor() 
        { 
            BinderEditorControl.txtBindName.Text = _tempBind.name; 
            BinderEditorControl.rbTypeAuto.IsChecked = _tempBind.isAuto;
            BinderEditorControl.rbTypeKey.IsChecked = !_tempBind.isAuto;
            BinderEditorControl.txtBindAutoTrigger.Text = _tempBind.isAuto ? _tempBind.key : "";
            BinderEditorControl.btnBindKey.Content = "КЛАВИША: " + ((_tempBind.isAuto || string.IsNullOrEmpty(_tempBind.key)) ? "НЕТ" : _tempBind.DisplayKey); 
            OpenOverlay(BinderEditorControl); 
        }

        internal void BindType_Changed(object sender, RoutedEventArgs e)
        {
            if (BinderEditorControl.btnBindKey == null || BinderEditorControl.pnlBindAutoTrigger == null) return;
            
            bool isAutoUI = BinderEditorControl.rbTypeAuto.IsChecked == true;
            if (isAutoUI) { 
                BinderEditorControl.btnBindKey.Visibility = Visibility.Collapsed; 
                BinderEditorControl.pnlBindAutoTrigger.Visibility = Visibility.Visible; 
            }
            else { 
                BinderEditorControl.btnBindKey.Visibility = Visibility.Visible; 
                BinderEditorControl.pnlBindAutoTrigger.Visibility = Visibility.Collapsed; 
            }

            if (_tempBind != null && _tempBind.isAuto != isAutoUI) {
                _tempBind.isAuto = isAutoUI;
                _tempBind.key = "";
                BinderEditorControl.txtBindAutoTrigger.Text = "";
                BinderEditorControl.btnBindKey.Content = "КЛАВИША: НЕТ";
            }
        }

        internal void Bind_Save_Click(object sender, RoutedEventArgs e) 
        { 
            _tempBind.name = BinderEditorControl.txtBindName.Text; 
            if(string.IsNullOrWhiteSpace(_tempBind.name)) _tempBind.name="Бинд"; 
            _tempBind.isAuto = BinderEditorControl.rbTypeAuto.IsChecked == true;
            if (_tempBind.isAuto) _tempBind.key = BinderEditorControl.txtBindAutoTrigger.Text;
            if (_tempBind.active && !string.IsNullOrEmpty(_tempBind.key)) {
                var conflicts = MasterData[CurrentProfile].Binds.Values.Where(b => b.key == _tempBind.key && b.id != _tempBind.id && b.active).ToList(); 
                if (conflicts.Count > 0) { 
                    foreach(var c in conflicts) c.active = false;
                    OpenInfo("ВНИМАНИЕ", $"Прошлый бинд («{conflicts[0].name}»), назначенный на " + _tempBind.key + ", был отключен."); 
                }
            }
            MasterData[CurrentProfile].Binds[_tempBind.id] = _tempBind;
            
            Action onComplete = () => {
                UpdateBindsList();
                UpdateBindGroups();
                UpdateBindStatsUI();
                Overlay_Close(null, null);
            };

            if (_isEngineRunning) {
                ShowSaveLoading();
                SaveData(() => Dispatcher.Invoke(() => HideSaveLoading(onComplete)));
            } else {
                SaveData();
                onComplete();
            }
        }

        private void Bind_Delete_Click(object sender, RoutedEventArgs e) { var bindId = (sender as Button).Tag.ToString(); _dialogHost.ShowAlert("УДАЛИТЬ БИНД", "Удалить этот бинд навсегда?", () => { if (MasterData[CurrentProfile].Binds.ContainsKey(bindId)) { MasterData[CurrentProfile].Binds.Remove(bindId); InvalidateCloudBindPacks(); Action onComplete = () => { UpdateBindsList(); UpdateBindGroups(); UpdateBindStatsUI(); }; if (_isEngineRunning) { ShowSaveLoading(); SaveData(() => Dispatcher.Invoke(() => HideSaveLoading(onComplete))); } else { SaveData(); onComplete(); } } }); }
        private void Bind_Toggle_Click(object sender, RoutedEventArgs e) 
        { 
            if (sender is CheckBox cb) 
            {
                var borderOn = cb.Template.FindName("BorderOn", cb) as Border;
                var dot = cb.Template.FindName("Dot", cb) as System.Windows.Shapes.Ellipse;
                if (borderOn != null && dot != null) 
                {
                    bool isChecked = cb.IsChecked == true;
                    var op = new System.Windows.Media.Animation.DoubleAnimation(isChecked ? 0 : 1, isChecked ? 1 : 0, TimeSpan.FromSeconds(0.15));
                    var th = new System.Windows.Media.Animation.ThicknessAnimation(isChecked ? new Thickness(2,0,0,0) : new Thickness(20,0,0,0), isChecked ? new Thickness(20,0,0,0) : new Thickness(2,0,0,0), TimeSpan.FromSeconds(0.15));
                    th.EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                    borderOn.BeginAnimation(Border.OpacityProperty, op);
                    dot.BeginAnimation(System.Windows.Shapes.Ellipse.MarginProperty, th);

                    // Key stealing logic
                    if (isChecked && cb.Tag != null) {
                        string bid = cb.Tag.ToString();
                        if (CurrentProfile != null && MasterData.ContainsKey(CurrentProfile) && MasterData[CurrentProfile].Binds.TryGetValue(bid, out var toggledBind)) {
                            if (!string.IsNullOrEmpty(toggledBind.key)) {
                                var conflicts = MasterData[CurrentProfile].Binds.Values.Where(b => b.key == toggledBind.key && b.id != bid && b.active).ToList();
                                if (conflicts.Count > 0) {
                                    foreach (var c in conflicts) c.active = false;
                                    OpenInfo("ВНИМАНИЕ", $"Прошлый бинд («{conflicts[0].name}»), назначенный на " + toggledBind.key + ", был отключен.");
                                    UpdateBindsList();
                                }
                            }
                        }
                    }
                }
            }
            UpdateBindGroups();
            UpdateBindStatsUI();
            SaveData();
        }

        private void AddStep(BindStep step)
        {
            if (_tempBind.steps.Count >= 50) { OpenInfo("ЛИМИТ", "Максимум 50 строк!"); return; }
            _tempBind.steps.Add(step);
            UpdateBindsStepsIndex();
            Application.Current.Dispatcher.InvokeAsync(() => BinderEditorControl.svBindSteps.ScrollToBottom());
        }

        private void UpdateBindsStepsIndex() 
        { 
            if (_tempBind == null) return; 
            for (int i = 0; i < _tempBind.steps.Count; i++) {
                _tempBind.steps[i].Index = i + 1; 
                _tempBind.steps[i].IsLast = (i == _tempBind.steps.Count - 1);
            }
            if (BinderEditorControl.icBindSteps.ItemsSource == null) BinderEditorControl.icBindSteps.ItemsSource = _tempBind.steps;
            else System.Windows.Data.CollectionViewSource.GetDefaultView(BinderEditorControl.icBindSteps.ItemsSource).Refresh();
        }

        internal void Step_Add_Chat(object sender, RoutedEventArgs e) => AddStep(new BindStep { action = "CHAT", desc = "ЧАТ", value="", isEnter=true, ColorCode="#1f6feb" });
        internal void Step_Add_Wait(object sender, RoutedEventArgs e) => AddStep(new BindStep { action = "WAIT", desc = "ПАУЗА", value = "1000", ColorCode="#d2a65e" });
        internal void Step_Add_Key(object sender, RoutedEventArgs e) { if (_tempBind.steps.Count >= 50) { OpenInfo("ЛИМИТ", "Максимум 50 строк!"); return; } _captureTarget = "BindStep"; StartKeyCapture(); }
        
        internal void Step_Delete_Click(object sender, RoutedEventArgs e) { var step = (sender as Button).Tag as BindStep; if (step != null) { _tempBind.steps.Remove(step); UpdateBindsStepsIndex(); } }

        internal void Bind_SetKey_Editor_Click(object sender, RoutedEventArgs e) { _captureTarget = "BindHotKey"; StartKeyCapture(); }
        private void OptKey_Click(object sender, RoutedEventArgs e) { 
            _captureTarget = (sender as Button).Tag.ToString(); 
            // For system hotkeys (Toggle/Prev/Next), check profile BEFORE opening the capture dialog
            if (_captureTarget == "Toggle" || _captureTarget == "Prev" || _captureTarget == "Next") {
                if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) {
                    OpenInfo("ОШИБКА", "Сначала создайте профиль во вкладке Главная!");
                    return;
                }
            }
            StartKeyCapture(); 
        }

        private void StartKeyCapture() { lblPressedKey.Text = "..."; _capturedKey = ""; OpenDialog(WinKeyWait); WinKeyWait.Focusable = true; WinKeyWait.Focus(); this.PreviewKeyDown += Window_PreviewKeyDown; this.PreviewMouseDown += Window_PreviewMouseDown; }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (WinKeyWait.Visibility != Visibility.Visible) return;
            string mouseKey = null;
            switch (e.ChangedButton) {
                case MouseButton.XButton1: mouseKey = "XButton1"; break;
                case MouseButton.XButton2: mouseKey = "XButton2"; break;
                case MouseButton.Middle: mouseKey = "MButton"; break;
            }
            if (mouseKey != null) {
                e.Handled = true;
                string txt = "";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) txt += "Ctrl + ";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) txt += "Alt + ";
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) txt += "Shift + ";
                txt += mouseKey;
                lblPressedKey.Text = txt; _capturedKey = txt;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) 
        {
            if (WinKeyWait.Visibility != Visibility.Visible) return;
            e.Handled = true;
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            
            if (k == Key.Escape) { _capturedKey = "Escape"; lblPressedKey.Text = "Escape"; return; }
            if (k == Key.Back || k == Key.Delete) {
                if (_captureTarget == "SectionAdvanced") {
                    _capturedKey = "";
                    KeyWait_Apply(null, null);
                    return;
                }
            }

            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.System) 
            { 
                string modName = k.ToString();
                if (modName == "System") modName = e.SystemKey.ToString();
                if (modName == "LeftShift") modName = "LShift";
                if (modName == "RightShift") modName = "RShift";
                if (modName == "LeftCtrl") modName = "LCtrl";
                if (modName == "RightCtrl") modName = "RCtrl";
                if (modName == "LeftAlt") modName = "LAlt";
                if (modName == "RightAlt") modName = "RAlt";

                // Build combo prefix from OTHER held modifiers (e.g. Alt+RShift)
                string prefix = "";
                if (modName != "LCtrl" && modName != "RCtrl" && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) prefix += "Ctrl + ";
                if (modName != "LAlt" && modName != "RAlt" && (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || e.Key == Key.System)) prefix += "Alt + ";
                if (modName != "LShift" && modName != "RShift" && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) prefix += "Shift + ";

                lblPressedKey.Text = prefix + modName; 
                _capturedKey = prefix + modName;
                return; 
            }

            string txt = "";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) txt += "Ctrl + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) txt += "Alt + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) txt += "Shift + ";
            
            string kn = k.ToString(); 
            if (kn.StartsWith("D") && kn.Length == 2 && char.IsDigit(kn[1])) kn = kn.Substring(1);
            
            // Convert WPF key name to AHK key name
            if (WpfToAhkKey.ContainsKey(kn)) kn = WpfToAhkKey[kn];

            // Use display name for UI if available
            string displayKn = WpfToDisplayKey.ContainsKey(k.ToString()) ? WpfToDisplayKey[k.ToString()] : kn;
            
            lblPressedKey.Text = txt + displayKn; 
            _capturedKey = txt + kn;
        }

        private void KeyWait_Apply(object sender, RoutedEventArgs e) 
        { 
            this.PreviewKeyDown -= Window_PreviewKeyDown;
            this.PreviewMouseDown -= Window_PreviewMouseDown; 

            // Profile check for system hotkeys is now done in OptKey_Click before opening the dialog.
            // Here we only check for bind-related targets that still need a profile.
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) {
                if (_captureTarget == "BindHotKey" || _captureTarget == "SectionAdvanced") {
                    Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", "Сначала создайте профиль во вкладке Главная!");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_capturedKey) && _capturedKey != "ESC" && _captureTarget != "BindStep" && !string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile))
            {
                bool isJustModifier = (_capturedKey == "LAlt" || _capturedKey == "RAlt" || _capturedKey == "LCtrl" || _capturedKey == "RCtrl" || _capturedKey == "LShift" || _capturedKey == "RShift");
                string modPrefix = "";
                if (_capturedKey.Contains("Alt")) modPrefix = "Alt + ";
                if (_capturedKey.Contains("Ctrl")) modPrefix = "Ctrl + ";
                if (_capturedKey.Contains("Shift")) modPrefix = "Shift + ";

                if (isJustModifier) {
                    var conflictBind = MasterData[CurrentProfile].Binds.Values.FirstOrDefault(b => b.key != null && b.key.StartsWith(modPrefix));
                    if (conflictBind != null) {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", $"Вы пытаетесь назначить модификатор '{_capturedKey}', но у вас уже есть бинд ('{conflictBind.name}'), использующий его в комбинации. Измените бинд перед назначением!");
                        return;
                    }
                }
                
                if (_capturedKey.Contains(" + ")) {
                    string bareMod = "";
                    if (_capturedKey.StartsWith("Alt + ")) bareMod = "Alt";
                    if (_capturedKey.StartsWith("Ctrl + ")) bareMod = "Ctrl";
                    if (_capturedKey.StartsWith("Shift + ")) bareMod = "Shift";
                    
                    if (bareMod != "") {
                        if (btnKeyToggle.Content?.ToString() == "L" + bareMod || btnKeyToggle.Content?.ToString() == "R" + bareMod ||
                            btnKeyPrev.Content?.ToString() == "L" + bareMod || btnKeyPrev.Content?.ToString() == "R" + bareMod ||
                            btnKeyNext.Content?.ToString() == "L" + bareMod || btnKeyNext.Content?.ToString() == "R" + bareMod ||
                            btnRadialKey.Content?.ToString() == "L" + bareMod || btnRadialKey.Content?.ToString() == "R" + bareMod) {
                            
                            Dialog_Close(null, null);
                            OpenInfo("ОШИБКА", $"Нельзя назначить комбинацию с '{bareMod}', так как эта клавиша-модификатор уже используется как самостоятельная кнопка вызова меню!");
                            return;
                        }
                    }
                }
            }

            if (_captureTarget == "SectionAdvanced")
            {
                if (_capturedKey == "ESC")
                {
                    _tempAdvancedKeys[_capturingSection] = "";
                    UpdateAdvancedActivationList();
                    OpenDialog(WinAdvancedActivation); // открываем в режим настройки
                    return;
                }
                
                if (!string.IsNullOrEmpty(_capturedKey))
                {
                    // Ignore global keys conflict for Advanced Overlay mode:
                    // By user request, keys used in Default mode should still be assignable here without conflict alerts.
                    
                    var conflictSection = _tempAdvancedKeys.FirstOrDefault(kvp => kvp.Value == _capturedKey && kvp.Key != _capturingSection).Key;
                    if (!string.IsNullOrEmpty(conflictSection))
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", $"Клавиша уже используется разделом:\n«{conflictSection}»");
                        return;
                    }
                }

                _tempAdvancedKeys[_capturingSection] = _capturedKey;
                UpdateAdvancedActivationList();
                OpenDialog(WinAdvancedActivation); // открываем в режим настройки
                return;
            }

            if (_captureTarget == "BindStep") 
            { 
                if (string.IsNullOrEmpty(_capturedKey)) { Dialog_Close(null, null); return; }
                AddStep(new BindStep { action = "PRESS", desc = "КНОПКА", value = _capturedKey, ColorCode="#ff7b72" }); 
                Dialog_Close(null, null);
            } 
            else if (_captureTarget == "BindHotKey") 
            { 
                if (!string.IsNullOrEmpty(_capturedKey))
                {
                    if (_capturedKey == btnKeyToggle.Content?.ToString() || _capturedKey == btnKeyPrev.Content?.ToString() || _capturedKey == btnKeyNext.Content?.ToString())
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", "Эта клавиша уже назначена в системных клавишах!");
                        return;
                    }
                    
                    var cs2 = MasterData[CurrentProfile].Laws.FirstOrDefault(kvp => kvp.Value.Hotkey == _capturedKey).Key;
                    if (!string.IsNullOrEmpty(cs2))
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", $"Клавиша уже используется разделом:\n«{cs2}»");
                        return;
                    }
                }

                _tempBind.key = _capturedKey; 
                BinderEditorControl.btnBindKey.Content = string.IsNullOrEmpty(_tempBind.key) ? "КЛАВИША: НЕТ" : "КЛАВИША: " + _tempBind.DisplayKey; 
                SaveData();
                Dialog_Close(null, null);
            }
            else if (_captureTarget == "Toggle" || _captureTarget == "Prev" || _captureTarget == "Next" || _captureTarget == "Radial")
            {
                if (!string.IsNullOrEmpty(_capturedKey))
                {
                    string cT = btnKeyToggle.Content?.ToString();
                    string cP = btnKeyPrev.Content?.ToString();
                    string cN = btnKeyNext.Content?.ToString();
                    string cR = btnRadialKey.Content?.ToString();
                    
                    bool conflictOpt = false;
                    if (_captureTarget == "Toggle" && (_capturedKey == cP || _capturedKey == cN || _capturedKey == cR)) conflictOpt = true;
                    if (_captureTarget == "Prev" && (_capturedKey == cT || _capturedKey == cN || _capturedKey == cR)) conflictOpt = true;
                    if (_captureTarget == "Next" && (_capturedKey == cT || _capturedKey == cP || _capturedKey == cR)) conflictOpt = true;
                    if (_captureTarget == "Radial" && (_capturedKey == cT || _capturedKey == cP || _capturedKey == cN)) conflictOpt = true;

                    if (conflictOpt)
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", "Эта клавиша уже назначена в системных клавишах!");
                        return;
                    }

                    var cb3 = MasterData[CurrentProfile].Binds.Values.FirstOrDefault(b => b.key == _capturedKey && !b.isAuto && b.id != "Radial");
                    if (cb3 != null)
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", $"Клавиша уже используется биндом:\n«{cb3.name}»");
                        return;
                    }

                    var cs3 = MasterData[CurrentProfile].Laws.FirstOrDefault(kvp => kvp.Value.Hotkey == _capturedKey).Key;
                    if (!string.IsNullOrEmpty(cs3))
                    {
                        Dialog_Close(null, null);
                        OpenInfo("ОШИБКА", $"Клавиша уже используется разделом:\n«{cs3}»");
                        return;
                    }
                }

                string keyName = string.IsNullOrEmpty(_capturedKey) ? "НЕТ" : _capturedKey;
                if (_captureTarget == "Toggle") btnKeyToggle.Content = keyName;
                else if (_captureTarget == "Prev") btnKeyPrev.Content = keyName;
                else if (_captureTarget == "Next") btnKeyNext.Content = keyName;
                else if (_captureTarget == "Radial") {
                    keyName = string.IsNullOrEmpty(_capturedKey) ? "M3" : _capturedKey;
                    btnRadialKey.Content = keyName;
                    if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                        if (!MasterData[CurrentProfile].Binds.ContainsKey("Radial")) {
                            MasterData[CurrentProfile].Binds["Radial"] = new BindItem { id = "Radial", name = "Radial Menu" };
                        }
                        MasterData[CurrentProfile].Binds["Radial"].key = _capturedKey;
                    }
                }

                SaveData(); 
                Dialog_Close(null, null);
            }
        }

        private void StopKeyCapture() { this.PreviewKeyDown -= Window_PreviewKeyDown; Dialog_Close(null, null); }
        
        private void OpenOverlay(UIElement win) { OverlayContainer.Visibility = Visibility.Visible; BinderEditorControl.Visibility = WinLawEdit.Visibility = WinReorder.Visibility = Visibility.Collapsed; win.Visibility = Visibility.Visible; DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)); OverlayContainer.BeginAnimation(OpacityProperty, fade); }
        internal async void Overlay_Close(object sender, RoutedEventArgs e) { DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15)); OverlayContainer.BeginAnimation(OpacityProperty, fade); await Task.Delay(150); OverlayContainer.Visibility = Visibility.Collapsed; }
        
        // ===== Fine Calculator =====
        private void UpdateFinesList() {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var fines = MasterData[CurrentProfile].Fines;
            icFinesList.ItemsSource = null;
            icFinesList.ItemsSource = fines;
            txtEmptyFines.Visibility = (fines == null || fines.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Fine_Add_Click(object sender, RoutedEventArgs e) {
            _tempFineEditId = "";
            txtFineId.Text = "";
            rbTypeKoAP.IsChecked = true;
            rbTypeUK.IsChecked = false;
            txtFineName.Text = "";
            txtFineAmt.Text = "5000";
            chkFineRevoke.IsChecked = false;
            OpenDialog(WinFineEdit);
        }

        private void Fine_Delete_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            string fineId = (sender as System.Windows.Controls.Primitives.ButtonBase)?.Tag?.ToString();
            if (string.IsNullOrEmpty(fineId)) return;
            var fines = MasterData[CurrentProfile].Fines;
            fines.RemoveAll(f => f.id == fineId);
            UpdateFinesList();
            SaveData();
        }

        private void Fine_Edit_Click(object sender, RoutedEventArgs e) {
            string fineId = (sender as System.Windows.Controls.Primitives.ButtonBase)?.Tag?.ToString();
            if (string.IsNullOrEmpty(fineId) || string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var fines = MasterData[CurrentProfile].Fines;
            var fine = fines.FirstOrDefault(f => f.id == fineId);
            if (fine == null) return;
            
            _tempFineEditId = fineId;
            txtFineId.Text = fine.id;
            if (fine.type == "УК РФ" || fine.type == "УК") { rbTypeUK.IsChecked = true; rbTypeKoAP.IsChecked = false; }
            else { rbTypeKoAP.IsChecked = true; rbTypeUK.IsChecked = false; }
            txtFineName.Text = fine.name;
            txtFineAmt.Text = fine.amount.ToString();
            chkFineRevoke.IsChecked = fine.revoke;
            
            OpenDialog(WinFineEdit);
        }



        private void FineRow_Click(object sender, MouseButtonEventArgs e) {
            // Sender is the Border (with DataContext) because we use PreviewMouseLeftButtonUp on it
            if (sender is FrameworkElement fe && fe.DataContext is FineArticle fine) {
                if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
                
                // Don't open editor if user clicked on edit/delete buttons
                if (e.OriginalSource is DependencyObject src) {
                    DependencyObject check = src;
                    while (check != null) {
                        if (check is Button) return; // Let button's own Click handler fire
                        check = VisualTreeHelper.GetParent(check);
                    }
                }
                _tempFineEditId = fine.id;
                txtFineId.Text = fine.id;
                if (fine.type == "УК РФ" || fine.type == "УК") { rbTypeUK.IsChecked = true; rbTypeKoAP.IsChecked = false; }
                else { rbTypeKoAP.IsChecked = true; rbTypeUK.IsChecked = false; }
                txtFineName.Text = fine.name;
                txtFineAmt.Text = fine.amount.ToString();
                chkFineRevoke.IsChecked = fine.revoke;
                OpenDialog(WinFineEdit);
            }
        }
        
        private void Fine_Edit_Cancel_Click(object sender, RoutedEventArgs e) {
            Dialog_Close(null, null);
        }
        
        private void Fine_Save_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var fines = MasterData[CurrentProfile].Fines;
            
            string fineId = txtFineId.Text.Trim();
            if (string.IsNullOrEmpty(fineId)) return; // ID is required

            var existing = fines.FirstOrDefault(f => f.id == _tempFineEditId);
            if (existing == null) {
                existing = new FineArticle();
                fines.Add(existing);
            }
            
            existing.id = fineId;
            existing.type = rbTypeUK.IsChecked == true ? "УК" : "КоАП";
            existing.name = txtFineName.Text.Trim();
            existing.note = "";
            int.TryParse(txtFineAmt.Text.Replace(" ", "").Trim(), out int amt);
            existing.amount = amt;
            existing.revoke = chkFineRevoke.IsChecked == true;
            
            UpdateFinesList();
            SaveData();
            Dialog_Close(null, null);
        }

        private string _tempFineEditId = "";
        
        private void OpenDialog(UIElement win) { Panel.SetZIndex(DialogOverlay, 1300); Panel.SetZIndex(win, 1310); DialogOverlay.Visibility = Visibility.Visible; WinKeyWait.Visibility = WinGuide.Visibility = WinDevMenu.Visibility = WinImportLaws.Visibility = WinImportBinds.Visibility = WinImportProfiles.Visibility = WinImportConflict.Visibility = WinAdvancedActivation.Visibility = WinFineEdit.Visibility = WinSectionGuide.Visibility = WinExportLaws.Visibility = WinExportBinds.Visibility = WinExportProfiles.Visibility = Visibility.Collapsed; win.Visibility = Visibility.Visible; DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)); DialogOverlay.BeginAnimation(OpacityProperty, fade); }
        private async void Dialog_Close(object sender, RoutedEventArgs e) { this.PreviewKeyDown -= Window_PreviewKeyDown; DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15)); DialogOverlay.BeginAnimation(OpacityProperty, fade); await Task.Delay(150); DialogOverlay.Visibility = Visibility.Collapsed; Panel.SetZIndex(DialogOverlay, 1000); WinKeyWait.Visibility = WinGuide.Visibility = WinDevMenu.Visibility = WinImportLaws.Visibility = WinImportBinds.Visibility = WinImportProfiles.Visibility = WinImportConflict.Visibility = WinAdvancedActivation.Visibility = WinFineEdit.Visibility = WinSectionGuide.Visibility = WinExportLaws.Visibility = WinExportBinds.Visibility = WinExportProfiles.Visibility = Visibility.Collapsed; }




        private void Update_Yes_Click(object sender, RoutedEventArgs e) { _updateDecision?.TrySetResult(true); }
        private void Update_No_Click(object sender, RoutedEventArgs e) { _updateDecision?.TrySetResult(false); }

        private void OpenAlert(string title, string msg) { _dialogHost.ShowAlert(title, msg, null); }
        internal void OpenInfo(string title, string msg, bool isSuccess = false) { 
            _dialogHost.ShowInfo(title, msg, isSuccess);
        }
        internal void Open_Guide(object sender, RoutedEventArgs e) { OpenDialog(WinGuide); }

        private void Open_VK(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("https://vk.com/duranhelper") { UseShellExecute = true }); } catch { } }


        


        private void Theme_Changed(object sender, SelectionChangedEventArgs e) { 
            if(!_isLoaded) return;
            string tLauncher = "Default (Dark Blue)";
            if (rbThemeBlack.IsChecked == true) tLauncher = "Black (AMOLED)";
            else if (rbThemeGrey.IsChecked == true) tLauncher = "Grey (Sport red)";
            if(!string.IsNullOrEmpty(tLauncher)) ApplyTheme(tLauncher);
            
            string tOverlay = cbThemeOverlay.SelectedItem?.ToString();
            if(!string.IsNullOrEmpty(tOverlay)) {
                bool isDef = tOverlay.Contains("Default") || tOverlay.Contains("Black") || tOverlay.Contains("Grey");
            }

            SaveData(); 
        }

        private void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            Application.Current.Resources["OverlayOpacity"] = e.NewValue;
            // Debounce: only save/reload after user stops dragging for 500ms
            if (_opacityDebounceTimer == null) {
                _opacityDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _opacityDebounceTimer.Tick += (s, args) => { _opacityDebounceTimer.Stop(); SaveData(); };
            }
            _opacityDebounceTimer.Stop();
            _opacityDebounceTimer.Start();
        }

        private void AdvancedOverlay_Toggle(object sender, RoutedEventArgs e)
        {
            // Logic moved to Setting_Toggled to avoid redundancy and event conflicts
            Setting_Toggled(sender, e);
        }

        private Dictionary<string, string> _tempAdvancedKeys = new Dictionary<string, string>();

        private void AdvancedOverlay_Click(object sender, RoutedEventArgs e)
        {
            _tempAdvancedKeys = new Dictionary<string, string>();
            if (MasterData.ContainsKey(CurrentProfile)) {
                foreach (var kv in MasterData[CurrentProfile].Laws) {
                    _tempAdvancedKeys[kv.Key] = kv.Value.Hotkey;
                }
            }
            UpdateAdvancedActivationList();
            OpenDialog(WinAdvancedActivation);
        }

        private void AdvancedOverlay_Save_Click(object sender, RoutedEventArgs e)
        {
            if (MasterData.ContainsKey(CurrentProfile)) {
                foreach (var kv in _tempAdvancedKeys) {
                    if (MasterData[CurrentProfile].Laws.ContainsKey(kv.Key)) {
                        MasterData[CurrentProfile].Laws[kv.Key].Hotkey = kv.Value;
                    }
                }
            }
            SaveData();
            Dialog_Close(null, null);
        }

        private void UpdateAdvancedActivationList()
        {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            var list = new List<AdvancedActivationItem>();
            foreach (var kv in MasterData[CurrentProfile].Laws)
            {
                string hk = _tempAdvancedKeys.ContainsKey(kv.Key) ? _tempAdvancedKeys[kv.Key] : "";
                list.Add(new AdvancedActivationItem { SectionName = kv.Key, HotkeyText = string.IsNullOrEmpty(hk) ? "Не назначен" : hk });
            }
            icAdvancedList.ItemsSource = null;
            icAdvancedList.ItemsSource = list;
        }

        private void AdvancedKey_Click(object sender, RoutedEventArgs e)
        {
            _captureTarget = "SectionAdvanced";
            _capturingSection = (sender as Button).Tag.ToString();
            StartKeyCapture();
        }
        




        private void Open_VK(object sender, MouseButtonEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://vk.com/duranhelper") { UseShellExecute = true }); } catch { }
        }

        private void ImportLaws_Click(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) { OpenInfo("ОШИБКА", "Сначала создайте или импортируйте профиль!"); return; }
            var d = new OpenFileDialog { Filter = "JSON|*.json" }; 
            if (d.ShowDialog() == true) { 
                try { 
                    var res = JsonConvert.DeserializeObject<Dictionary<string, LawSection>>(File.ReadAllText(d.FileName)); 
                    if (res != null) { 
                        _importTargetContext = "Laws";
                        _tempImportList.Clear();
                        chkImportLawsAll.IsChecked = true;
                        foreach (var kv in res) {
                            int ptCount = 0;
                            int hdCount = 0;
                            if (kv.Value.Items != null) {
                                ptCount = kv.Value.Items.Count(i => i.type == "art");
                                hdCount = kv.Value.Items.Count(i => i.type == "head");
                            }
                            string countsTxt = $"{ptCount} пунктов, {hdCount} заголовков";
                            _tempImportList.Add(new ImportItem { DisplayName = $"Раздел: {kv.Key}", CountText = countsTxt, OriginalKey = kv.Key, Data = kv.Value, IsSelected = true });
                        }
                        icImportLawsList.ItemsSource = null;
                        icImportLawsList.ItemsSource = _tempImportList;
                        UpdateLawsSelectedCount();
                        OpenDialog(WinImportLaws);
                    } 
                } catch { OpenInfo("ОШИБКА", "Неверный формат файла законов!"); } 
            } 
        }


        private void ImportBinds_Click(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) { OpenInfo("ОШИБКА", "Сначала создайте профиль во вкладке Главная!"); return; }
            var d = new OpenFileDialog { Filter = "JSON|*.json" }; 
            if (d.ShowDialog() == true) { 
                try { 
                    var ex = JsonConvert.DeserializeObject<ExportBindsData>(File.ReadAllText(d.FileName)); 
                    if (ex != null) { 
                        _importTargetContext = "Binds";
                        _tempImportList.Clear();
                        chkImportBindsAll.IsChecked = true;
                        
                        if (ex.Groups != null) {
                            foreach (var g in ex.Groups) {
                                if (g == "ВСЕ") continue; // Убираем [ВСЕ] (Задача 19)
                                _tempImportList.Add(new ImportItem { DisplayName = $"Группа: {g}", CountText = "Новая группа", InfoStringVisibility=Visibility.Visible, OriginalKey = g, Data = "Group", IsSelected = true });
                            }
                        }
                        
                        if (ex.Variables != null) {
                            foreach (var v in ex.Variables) {
                                if (v.Key == "*ВРЕМЯ*") continue;
                                _tempImportList.Add(new ImportItem { DisplayName = $"Переменная: {v.Key}", CountText = "Новая переменная", InfoStringVisibility=Visibility.Visible, OriginalKey = v.Key, Data = v.Value, IsSelected = true });
                            }
                        }
                        
                        if (ex.Binds != null) {
                            foreach (var b in ex.Binds.Values) {
                                if (b.name == "Radial Menu") continue;
                                string gp = b.group == "ВСЕ" ? "" : $" [{b.group}]";
                                bool keyConf = !string.IsNullOrEmpty(b.key) && MasterData[CurrentProfile].Binds.Values.Any(e => e.key == b.key);
                                bool nameConf = MasterData[CurrentProfile].Binds.Values.Any(e => e.name == b.name);
                                _tempImportList.Add(new ImportItem { DisplayName = $"{b.name}{gp}", OriginalKey = b.id, Data = b, IsSelected = true,
                                KeyConflict = keyConf, NameConflict = nameConf, CountText = "Новый бинд",
                                InfoStringVisibility = (keyConf || nameConf) ? Visibility.Collapsed : Visibility.Visible });
                            }
                        }
                        
                        // Radial menu config
                        if (ex.RadialMenu != null) {
                            string modeText = ex.RadialMenu.Mode == "Grouped" ? "С группами" : "Стандартное";
                            _tempImportList.Add(new ImportItem { DisplayName = "Круговое меню", OriginalKey = "RADIAL", Data = ex.RadialMenu, IsSelected = true, CountText = modeText, InfoStringVisibility = Visibility.Visible });
                        }
                        UpdateBindsConflictCount();
                        
                        icImportBindsList.ItemsSource = null;
                        icImportBindsList.ItemsSource = _tempImportList;
                        OpenDialog(WinImportBinds);
                    } 
                } catch { OpenInfo("ОШИБКА", "Ошибка чтения файла биндов!"); } 
            } 
        }

        private void UpdateBindsConflictCount() {
            if (_tempImportList == null) return;
            int count = _tempImportList.Count(i => i.IsSelected && (i.KeyConflict || i.NameConflict));
            if (lblImportBindsConflictCount != null) lblImportBindsConflictCount.Text = count.ToString();
            if (btnImportBindsConfirm != null) btnImportBindsConfirm.Content = $"ИМПОРТИРОВАТЬ ({_tempImportList.Count(i => i.IsSelected)})";
        }

        private void ImportLawsAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkImportLawsAll.IsChecked == true;
            foreach (var item in _tempImportList) item.IsSelected = check;
            icImportLawsList.ItemsSource = null;
            icImportLawsList.ItemsSource = _tempImportList;
            UpdateLawsSelectedCount();
        }

        private void UpdateLawsSelectedCount() {
            if (_tempImportList == null) return;
            int count = _tempImportList.Count(i => i.IsSelected);
            if (btnImportLawsConfirm != null) btnImportLawsConfirm.Content = $"ИМПОРТИРОВАТЬ ({count})";
        }

        private void ImportItem_Click(object sender, RoutedEventArgs e) {
            if (_importTargetContext == "Laws") UpdateLawsSelectedCount();
            else if (_importTargetContext == "Binds") UpdateBindsConflictCount();
            else if (_importTargetContext == "Profile") UpdateProfilesSelectedCount();
        }

        private void ImportItemCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (sender is FrameworkElement fe && fe.DataContext is ImportItem item) {
                item.IsSelected = !item.IsSelected;
                
                if (_importTargetContext == "Laws") UpdateLawsSelectedCount();
                else if (_importTargetContext == "Binds") UpdateBindsConflictCount();
                else if (_importTargetContext == "Profile") UpdateProfilesSelectedCount();
            }
        }

        private void ExportItemCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (sender is FrameworkElement fe && fe.DataContext is ImportItem item) {
                item.IsSelected = !item.IsSelected;
            }
        }

        private void ImportBindsAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkImportBindsAll.IsChecked == true;
            foreach (var item in _tempImportList) item.IsSelected = check;
            icImportBindsList.ItemsSource = null;
            icImportBindsList.ItemsSource = _tempImportList;
            UpdateBindsConflictCount();
        }

        private bool _hadImportConflicts = false;

        private void CheckAndClearHotkeyConflict(ref string hotkey) {
            if (string.IsNullOrEmpty(hotkey)) return;
            bool conflict = false;
            string hk = hotkey;
            if (hk == btnKeyToggle.Content?.ToString() || hk == btnKeyPrev.Content?.ToString() || hk == btnKeyNext.Content?.ToString()) conflict = true;
            if (MasterData[CurrentProfile].Binds.Values.Any(b => b.key == hk && !b.isAuto)) conflict = true;
            if (MasterData[CurrentProfile].Laws.Any(l => l.Value.Hotkey == hk)) conflict = true;
            if (conflict) { hotkey = ""; _hadImportConflicts = true; }
        }

        private void ImportLaws_Confirm_Click(object sender, RoutedEventArgs e) {
            int imported = 0;
            _hadImportConflicts = false;
            _conflictQueue.Clear();
            foreach (var item in _tempImportList.Where(i => i.IsSelected)) {
                if (MasterData[CurrentProfile].Laws.ContainsKey(item.OriginalKey)) {
                    _conflictQueue.Enqueue(item);
                } else {
                    var law = item.Data as LawSection;
                    string hk = law.Hotkey;
                    CheckAndClearHotkeyConflict(ref hk);
                    law.Hotkey = hk;
                    MasterData[CurrentProfile].Laws[item.OriginalKey] = law;
                    imported++;
                }
            }
            ProcessNextImportConflict();
        }

        private void ImportBinds_Confirm_Click(object sender, RoutedEventArgs e) {
            int imported = 0;
            _hadImportConflicts = false;
            _conflictQueue.Clear();
            _importBindIdMap.Clear();
            foreach (var item in _tempImportList.Where(i => i.IsSelected)) {
                if (item.Data is string str && str == "Group" && !MasterData[CurrentProfile].Groups.Contains(item.OriginalKey)) {
                    MasterData[CurrentProfile].Groups.Add(item.OriginalKey); imported++;
                }
                else if (item.Data is string varVal && !MasterData[CurrentProfile].Variables.ContainsKey(item.OriginalKey)) {
                    MasterData[CurrentProfile].Variables[item.OriginalKey] = varVal; imported++;
                }
                else if (item.Data is BindItem bind) {
                    string oldId = item.OriginalKey;
                    string nId = Guid.NewGuid().ToString();
                    string nKey = bind.key;
                    CheckAndClearHotkeyConflict(ref nKey);
                    bind.id = nId; bind.key = nKey;
                    
                    if (MasterData[CurrentProfile].Binds.Values.Any(b => b.name == bind.name)) {
                        _conflictQueue.Enqueue(item);
                    } else {
                        MasterData[CurrentProfile].Binds[nId] = bind; imported++;
                        _importBindIdMap[oldId] = nId;
                    }
                }
                else if (item.Data is RadialMenuConfig rm) {
                    MasterData[CurrentProfile].RadialMenu = rm; imported++;
                }
            }
            ProcessNextImportConflict();
        }

        internal void ImportFines_Click(object sender, RoutedEventArgs e) { 
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) { OpenInfo("ОШИБКА", "Сначала создайте или импортируйте профиль!"); return; }
            var d = new OpenFileDialog { Filter = "JSON|*.json" }; 
            if (d.ShowDialog() == true) { 
                try { 
                    var fines = JsonConvert.DeserializeObject<List<FineArticle>>(File.ReadAllText(d.FileName)); 
                    if (fines != null) { 
                        MasterData[CurrentProfile].Fines = fines;
                        SaveData();
                        RefreshUI();
                        // RefreshFinesListUI(); 
                        OpenInfo("УСПЕШНО", "База штрафов успешно обновлена!", true);
                        AddLog("Штрафы импортированы из файла", "#8957e5");
                    } 
                } catch { OpenInfo("ОШИБКА", "Неверный формат файла штрафов!"); } 
            } 
        }




        private List<ImportItem> _tempProfileImportLawsList = new List<ImportItem>();
        private List<ImportItem> _tempProfileImportBindsList = new List<ImportItem>();

        private List<ImportItem> _tempExportLawsList = new List<ImportItem>();
        private List<ImportItem> _tempExportBindsList = new List<ImportItem>();
        private List<ImportItem> _tempExportProfileLawsList = new List<ImportItem>();
        private List<ImportItem> _tempExportProfileBindsList = new List<ImportItem>();

        public void ShowExportLaws_Click(object sender, RoutedEventArgs e) {
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            
            _tempExportLawsList.Clear();
            foreach (var kv in MasterData[CurrentProfile].Laws) {
                _tempExportLawsList.Add(new ImportItem { 
                    DisplayName = kv.Key, 
                    OriginalKey = kv.Key, 
                    Data = kv.Value, 
                    IsSelected = true,
                    CountText = $"{kv.Value.Items.Count} ЭЛЕМ." 
                });
            }
            icExportLawsList.ItemsSource = null;
            icExportLawsList.ItemsSource = _tempExportLawsList;
            chkExportLawsAll.IsChecked = true;
            OpenDialog(WinExportLaws);
        }

        private void ExportLawsAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkExportLawsAll.IsChecked == true;
            foreach (var item in _tempExportLawsList) item.IsSelected = check;
            icExportLawsList.ItemsSource = null; icExportLawsList.ItemsSource = _tempExportLawsList;
        }

        private void ExportLaws_Confirm_Click(object sender, RoutedEventArgs e) {
            var selected = _tempExportLawsList.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { OpenInfo("ОШИБКА", "Выберите хотя бы один раздел!"); return; }

            var d = new SaveFileDialog { Filter = "Laws JSON|*.json", FileName = "Laws_Export.json" };
            if (d.ShowDialog() == true) {
                var exportData = new Dictionary<string, LawSection>();
                foreach (var item in selected) {
                    exportData[item.OriginalKey] = (LawSection)item.Data;
                }
                File.WriteAllText(d.FileName, JsonConvert.SerializeObject(exportData, Formatting.Indented));
                Dialog_Close(null, null);
                OpenInfo("УСПЕШНО", $"Экспортировано разделов: {selected.Count}", true);
                AddLog("База законов экспортирована", "#d2a65e");
            }
        }

        public void ShowExportBinds_Click(object sender, RoutedEventArgs e) {
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;

            _tempExportBindsList.Clear();
            // Groups
            foreach (var g in MasterData[CurrentProfile].Groups) {
                if (g == "ВСЕ") continue;
                _tempExportBindsList.Add(new ImportItem { DisplayName = $"ГРУППА: {g}", OriginalKey = g, Data = "Group", IsSelected = true, CountText = "КАТЕГОРИЯ" });
            }
            // Variables
            foreach (var v in MasterData[CurrentProfile].Variables) {
                if (v.Key == "*ВРЕМЯ*") continue;
                _tempExportBindsList.Add(new ImportItem { DisplayName = $"ПЕРЕМЕННАЯ: {v.Key}", OriginalKey = v.Key, Data = v.Value, IsSelected = true, CountText = "ЗНАЧЕНИЕ" });
            }
            // Binds
            foreach (var b in MasterData[CurrentProfile].Binds.Values) {
                _tempExportBindsList.Add(new ImportItem { DisplayName = b.name, OriginalKey = b.id, Data = b, IsSelected = true, CountText = $"КЛАВ: {b.key}" });
            }
            
            // Radial Menu
            if (MasterData[CurrentProfile].RadialMenu != null) {
                string modeText = MasterData[CurrentProfile].RadialMenu.Mode == "Grouped" ? "С группами" : "Стандартное";
                _tempExportBindsList.Add(new ImportItem { DisplayName = "Круговое меню", OriginalKey = "RADIAL", Data = MasterData[CurrentProfile].RadialMenu, IsSelected = true, CountText = modeText });
            }

            icExportBindsList.ItemsSource = null;
            icExportBindsList.ItemsSource = _tempExportBindsList;
            chkExportBindsAll.IsChecked = true;
            OpenDialog(WinExportBinds);
        }

        private void ExportBindsAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkExportBindsAll.IsChecked == true;
            foreach (var item in _tempExportBindsList) item.IsSelected = check;
            icExportBindsList.ItemsSource = null; icExportBindsList.ItemsSource = _tempExportBindsList;
        }

        private void ExportBinds_Confirm_Click(object sender, RoutedEventArgs e) {
            var selected = _tempExportBindsList.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) { OpenInfo("ОШИБКА", "Выберите хотя бы один макрос!"); return; }

            var d = new SaveFileDialog { Filter = "Binds JSON|*.json", FileName = "Binds_Export.json" };
            if (d.ShowDialog() == true) {
                var exportData = new ExportBindsData();
                foreach (var item in selected) {
                    if (item.Data is string str && str == "Group") exportData.Groups.Add(item.OriginalKey);
                    else if (item.Data is string varVal) exportData.Variables[item.OriginalKey] = varVal;
                    else if (item.Data is BindItem bind) exportData.Binds[item.OriginalKey] = bind;
                    else if (item.Data is RadialMenuConfig rm) exportData.RadialMenu = rm;
                }
                File.WriteAllText(d.FileName, JsonConvert.SerializeObject(exportData, Formatting.Indented));
                Dialog_Close(null, null);
                OpenInfo("УСПЕШНО", $"Экспортировано элементов: {selected.Count}", true);
                AddLog("Бинды экспортированы", "#2ea043");
            }
        }

        public void ShowExportProfile_Click(object sender, RoutedEventArgs e) {
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;

            _tempExportProfileLawsList.Clear();
            _tempExportProfileBindsList.Clear();
            var p = MasterData[CurrentProfile];

            // Laws
            foreach (var kv in p.Laws) {
                _tempExportProfileLawsList.Add(new ImportItem { DisplayName = kv.Key, OriginalKey = kv.Key, Data = kv.Value, IsSelected = true });
            }
            // Meta (Groups/Vars)
            foreach (var g in p.Groups) {
                if (g == "ВСЕ") continue;
                _tempExportProfileBindsList.Add(new ImportItem { DisplayName = $"Группа: {g}", OriginalKey = g, Data = "Group", IsSelected = true });
            }
            foreach (var v in p.Variables) {
                if (v.Key == "*ВРЕМЯ*") continue;
                _tempExportProfileBindsList.Add(new ImportItem { DisplayName = $"Перем: {v.Key}", OriginalKey = v.Key, Data = v.Value, IsSelected = true });
            }
            // Binds
            foreach (var b in p.Binds.Values) {
                _tempExportProfileBindsList.Add(new ImportItem { DisplayName = b.name, OriginalKey = b.id, Data = b, IsSelected = true });
            }
            
            // Radial Menu
            if (p.RadialMenu != null) {
                string modeText = p.RadialMenu.Mode == "Grouped" ? "С группами" : "Стандартное";
                _tempExportProfileBindsList.Add(new ImportItem { DisplayName = "Круговое меню", OriginalKey = "RADIAL", Data = p.RadialMenu, IsSelected = true, CountText = modeText });
            }
            // Fines (as a single item if exists)
            if (p.Fines != null && p.Fines.Count > 0) {
                _tempExportProfileBindsList.Add(new ImportItem { DisplayName = "БАЗА ШТРАФОВ", OriginalKey = "FINES", Data = "Fines", IsSelected = true });
            }

            icExportProfileLaws.ItemsSource = null;
            icExportProfileBinds.ItemsSource = null;
            icExportProfileLaws.ItemsSource = _tempExportProfileLawsList;
            icExportProfileBinds.ItemsSource = _tempExportProfileBindsList;
            chkExportProfileAll.IsChecked = true;
            OpenDialog(WinExportProfiles);
        }

        private void ExportProfileAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkExportProfileAll.IsChecked == true;
            foreach (var item in _tempExportProfileLawsList) item.IsSelected = check;
            foreach (var item in _tempExportProfileBindsList) item.IsSelected = check;
            icExportProfileLaws.ItemsSource = null; icExportProfileBinds.ItemsSource = null;
            icExportProfileLaws.ItemsSource = _tempExportProfileLawsList;
            icExportProfileBinds.ItemsSource = _tempExportProfileBindsList;
        }

        private void ExportProfile_Confirm_Click(object sender, RoutedEventArgs e) {
            var selLaws = _tempExportProfileLawsList.Where(x => x.IsSelected).ToList();
            var selBinds = _tempExportProfileBindsList.Where(x => x.IsSelected).ToList();
            if (selLaws.Count == 0 && selBinds.Count == 0) { OpenInfo("ОШИБКА", "Ничего не выбрано!"); return; }

            var d = new SaveFileDialog { Filter = "Duran Profile|*.duran", FileName = $"{CurrentProfile}.duran" };
            if (d.ShowDialog() == true) {
                var exportProfile = new ProfileData();
                foreach (var item in selLaws) exportProfile.Laws[item.OriginalKey] = (LawSection)item.Data;
                foreach (var item in selBinds) {
                    if (item.Data is string str && str == "Group") exportProfile.Groups.Add(item.OriginalKey);
                    else if (item.Data is string varVal && (string)item.Data != "Fines") exportProfile.Variables[item.OriginalKey] = varVal;
                    else if (item.Data is BindItem bind) exportProfile.Binds[item.OriginalKey] = bind;
                    else if (item.Data is RadialMenuConfig rm) exportProfile.RadialMenu = rm;
                    else if (item.Data is string f && f == "Fines") exportProfile.Fines = MasterData[CurrentProfile].Fines;
                }
                // Variables fix: ensure *ВРЕМЯ* is there if needed (usually it is)
                if (!exportProfile.Variables.ContainsKey("*ВРЕМЯ*")) exportProfile.Variables["*ВРЕМЯ*"] = "0";

                File.WriteAllText(d.FileName, JsonConvert.SerializeObject(exportProfile, Formatting.Indented));
                Dialog_Close(null, null);
                OpenInfo("УСПЕШНО", "Профиль экспортирован!", true);
                AddLog("Профиль экспортирован выборочно", "#d2a65e");
            }
        }

        public void ShowExportFines_Click(object sender, RoutedEventArgs e) {
             if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
             if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
             
             // Fines is usually all-or-nothing export currently, but we can just use the existing ExportFines_Confirm logic
             var d = new SaveFileDialog { Filter = "Fines JSON|*.json", FileName = "Fines.json" };
             if (d.ShowDialog() == true) {
                 File.WriteAllText(d.FileName, JsonConvert.SerializeObject(MasterData[CurrentProfile].Fines, Formatting.Indented));
                 OpenInfo("УСПЕШНО", "База штрафов экспортирована!", true);
             }
        }

        private void ExportItem_Click(object sender, RoutedEventArgs e) {
            // Placeholder if needed for auto-updating counts or something
        }

        private void ImportProfile_Click(object sender, RoutedEventArgs e) {
            if (IsSetupIncomplete()) { OpenInfo("ОШИБКА ДОСТУПА", "Сначала завершите настройку на вкладке «ГЛАВНАЯ»."); return; }
            var d = new OpenFileDialog { Filter = "Duran Profile|*.duran" };
            if (d.ShowDialog() == true) {
                try {
                    var p = JsonConvert.DeserializeObject<ProfileData>(File.ReadAllText(d.FileName));
                    if (p != null) {
                        _tempProfileData = p;
                        _tempProfileName = Path.GetFileNameWithoutExtension(d.FileName);
                        
                        _tempProfileImportLawsList.Clear();
                        _tempProfileImportBindsList.Clear();
                        chkImportProfileAll.IsChecked = true;
                        
                        // Laws
                        if (p.Laws != null) {
                            foreach (var kv in p.Laws) {
                                _tempProfileImportLawsList.Add(new ImportItem { DisplayName = kv.Key, OriginalKey = kv.Key, Data = kv.Value, IsSelected = true });
                            }
                        }
                        
                        // Groups/Vars as Binds meta
                        if (p.Groups != null) {
                            foreach (var g in p.Groups) {
                                if (g == "ВСЕ") continue;
                                _tempProfileImportBindsList.Add(new ImportItem { DisplayName = $"Группа: {g}", OriginalKey = g, Data = "Group", IsSelected = true });
                            }
                        }
                        if (p.Variables != null) {
                            foreach (var v in p.Variables) {
                                if (v.Key == "*ВРЕМЯ*") continue;
                                _tempProfileImportBindsList.Add(new ImportItem { DisplayName = $"Пер-я: {v.Key}", OriginalKey = v.Key, Data = v.Value, IsSelected = true });
                            }
                        }
                        // Binds
                        if (p.Binds != null) {
                            foreach (var b in p.Binds.Values) {
                                _tempProfileImportBindsList.Add(new ImportItem { DisplayName = b.name, OriginalKey = b.id, Data = b, IsSelected = true });
                            }
                        }
                        
                        // Radial Menu
                        if (p.RadialMenu != null) {
                            string modeText = p.RadialMenu.Mode == "Grouped" ? "С группами" : "Стандартное";
                            _tempProfileImportBindsList.Add(new ImportItem { DisplayName = "Круговое меню", OriginalKey = "RADIAL", Data = p.RadialMenu, IsSelected = true, CountText = modeText, InfoStringVisibility = Visibility.Visible });
                        }
                        
                        icImportProfileLaws.ItemsSource = null;
                        icImportProfileBinds.ItemsSource = null;
                        icImportProfileLaws.ItemsSource = _tempProfileImportLawsList;
                        icImportProfileBinds.ItemsSource = _tempProfileImportBindsList;
                        
                        UpdateProfilesSelectedCount();
                        if (lblImportProfileTitle != null) lblImportProfileTitle.Text = $"ИМПОРТ ПРОФИЛЯ: \"{_tempProfileName.ToUpper()}\"";
                        OpenDialog(WinImportProfiles);
                    }
                } catch { OpenInfo("ОШИБКА", "Файл профиля повреждён!"); }
            }
        }

        private void ImportProfileAll_Click(object sender, RoutedEventArgs e) {
            bool check = chkImportProfileAll.IsChecked == true;
            foreach (var l in _tempProfileImportLawsList) l.IsSelected = check;
            foreach (var b in _tempProfileImportBindsList) b.IsSelected = check;
            icImportProfileLaws.ItemsSource = null; icImportProfileBinds.ItemsSource = null;
            icImportProfileLaws.ItemsSource = _tempProfileImportLawsList;
            icImportProfileBinds.ItemsSource = _tempProfileImportBindsList;
            UpdateProfilesSelectedCount();
        }

        private void UpdateProfilesSelectedCount() {
            if (_tempProfileImportLawsList == null || _tempProfileImportBindsList == null) return;
            int count = _tempProfileImportLawsList.Count(i => i.IsSelected) + _tempProfileImportBindsList.Count(i => i.IsSelected);
            if (btnImportProfileConfirm != null) btnImportProfileConfirm.Content = $"СОХРАНИТЬ ПРОФИЛЬ ({count})";
        }

        private void ThemeRadioButton_Checked(object sender, RoutedEventArgs e) {
            if (!_isLoaded) return;
            string themeName = "Default (Dark Blue)";
            if (rbThemeBlack.IsChecked == true) themeName = "Black (AMOLED)";
            else if (rbThemeGrey.IsChecked == true) themeName = "Grey (Sport red)";
            ApplyTheme(themeName);
            SaveData();
        }

        private void ImportProfile_Confirm_Click(object sender, RoutedEventArgs e) {
            string baseName = _tempProfileName;
            string newName = baseName;
            int counter = 1;
            while (MasterData.ContainsKey(newName)) {
                newName = $"{baseName} ({counter})";
                counter++;
            }
            
            // Build the new profile Data
            ProfileData p = new ProfileData {
                Theme = _tempProfileData.Theme,
                OverlayTheme = _tempProfileData.OverlayTheme,
                OverlayText = _tempProfileData.OverlayText,
                Fines = _tempProfileData.Fines != null ? new List<FineArticle>(_tempProfileData.Fines) : new List<FineArticle>(),
                GamePath = _tempProfileData.GamePath,
                RadialMenu = _tempProfileData.RadialMenu ?? new RadialMenuConfig(),
                InstalledCloudIds = _tempProfileData.InstalledCloudIds != null ? new List<string>(_tempProfileData.InstalledCloudIds) : new List<string>(),
                CustomThemes = _tempProfileData.CustomThemes != null ? new Dictionary<string, Dictionary<string, string>>(_tempProfileData.CustomThemes) : new Dictionary<string, Dictionary<string, string>>(),
                Groups = new List<string> { "ВСЕ" },
                Variables = new Dictionary<string, string> { {"*ИМЯ*", ""} }
            };
            
            // Apply selected laws
            foreach(var l in _tempProfileImportLawsList.Where(i => i.IsSelected)) {
                if(l.Data is LawSection ls) p.Laws[l.OriginalKey] = ls;
            }
            
            // Apply selected binds/meta
            var bindIdMap = new Dictionary<string, string>(); // oldId → newId
            foreach(var b in _tempProfileImportBindsList.Where(i => i.IsSelected)) {
                if (b.Data is string str && str == "Group" && !p.Groups.Contains(b.OriginalKey)) p.Groups.Add(b.OriginalKey);
                else if (b.Data is string varVal) p.Variables[b.OriginalKey] = varVal;
                else if (b.Data is BindItem bind) {
                    string oldId = b.OriginalKey;
                    string nId = Guid.NewGuid().ToString();
                    bind.id = nId;
                    p.Binds[nId] = bind;
                    bindIdMap[oldId] = nId;
                }
            }
            
            // Remap RadialMenu bind IDs to match the new GUIDs
            if (p.RadialMenu != null && bindIdMap.Count > 0) {
                if (p.RadialMenu.Sectors != null) {
                    foreach (var sec in p.RadialMenu.Sectors) {
                        if (!string.IsNullOrEmpty(sec.BindId) && bindIdMap.ContainsKey(sec.BindId))
                            sec.BindId = bindIdMap[sec.BindId];
                    }
                }
                if (p.RadialMenu.Groups != null) {
                    foreach (var grp in p.RadialMenu.Groups) {
                        if (grp.Sectors != null) {
                            foreach (var sec in grp.Sectors) {
                                if (!string.IsNullOrEmpty(sec.BindId) && bindIdMap.ContainsKey(sec.BindId))
                                    sec.BindId = bindIdMap[sec.BindId];
                            }
                        }
                    }
                }
            }
            
            MasterData[newName] = p;
            CurrentProfile = newName;
            
            // If wizard is visible (import from wizard step 1), advance to step 2
            if (WinWizard.Visibility == Visibility.Visible) {
                _wizardProfileName = newName;
                _wizardImportedProfile = true;
                _wizardStep = 2;
                SaveData();
                UpdateWizardUI();
                WinImportProfiles.Visibility = Visibility.Collapsed;
                DialogOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            
            SaveData(); RefreshUI(); UpdateBindsList(); UpdateBindGroups();
            
            Dialog_Close(null, null);
            OpenInfo("УСПЕШНО", $"Профиль «{newName}» импортирован!", true);
            AddLog($"Импортирован профиль: '{newName}'", "#3388ff");
        }


        private ProfileData _tempProfileData;
        private string _tempProfileName;

        private Queue<ImportItem> _conflictQueue = new Queue<ImportItem>();
        private ImportItem _currentConflictItem;
        private Dictionary<string, string> _importBindIdMap = new Dictionary<string, string>();

        private void ProcessNextImportConflict() {
            if (_conflictQueue.Count == 0) {
                Dialog_Close(null, null);
                
                if (_importTargetContext == "Laws") {
                    UpdateLawsList();
                    if (cbSections.Items.Count > 0) { cbSections.SelectedIndex = 0; Section_Changed(null, null); }
                    if (_hadImportConflicts) OpenInfo("ВНИМАНИЕ", "Законы импортированы.\nНесколько разделов были переименованы из-за конфликтов.");
                    else OpenInfo("УСПЕШНО", "Законы импортированы!", true);
                    AddLog($"Законы импортированы", "#3388ff");
                } else if (_importTargetContext == "Binds") {
                    var p = MasterData[CurrentProfile];
                    if (p.RadialMenu != null && _importBindIdMap.Count > 0) {
                        if (p.RadialMenu.Sectors != null) {
                            foreach (var sec in p.RadialMenu.Sectors) {
                                if (!string.IsNullOrEmpty(sec.BindId) && _importBindIdMap.ContainsKey(sec.BindId))
                                    sec.BindId = _importBindIdMap[sec.BindId];
                            }
                        }
                        if (p.RadialMenu.Groups != null) {
                            foreach (var grp in p.RadialMenu.Groups) {
                                if (grp.Sectors != null) {
                                    foreach (var sec in grp.Sectors) {
                                        if (!string.IsNullOrEmpty(sec.BindId) && _importBindIdMap.ContainsKey(sec.BindId))
                                            sec.BindId = _importBindIdMap[sec.BindId];
                                    }
                                }
                            }
                        }
                    }
                    UpdateBindGroups();
                    UpdateBindsList();
                    if (_hadImportConflicts) OpenInfo("ВНИМАНИЕ", "Бинды импортированы.\nНесколько бинд были переименованы из-за конфликтов.");
                    else OpenInfo("УСПЕШНО", "Бинды импортированы!", true);
                    AddLog($"Бинды импортированы", "#3388ff");
                }
                
                SaveData(); 
                RefreshUI();
                return;
            }
            
            _currentConflictItem = _conflictQueue.Dequeue();
            string typeName = _importTargetContext == "Laws" ? "Раздел" : "Бинд";
            string itemName = _importTargetContext == "Laws" ? _currentConflictItem.OriginalKey : (_currentConflictItem.Data as BindItem)?.name;
            lblConflictMsg.Text = $"[{typeName}] '{itemName}' уже существует.\nКак поступить?";
            OpenDialog(WinImportConflict);
        }

        private void ThemeLauncher_Changed(object sender, SelectionChangedEventArgs e) {
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item) {
                string themeName = item.Content?.ToString() ?? "Default (Dark Blue)";
                if (_isLoaded) {
                    ApplyTheme(themeName);
                    // Refresh UI elements that use cached theme colors
                    try { SetBindsViewMode(); } catch {}
                    try { if (Tabs.SelectedIndex == 0) { } } catch {}
                }
                if (!string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                    MasterData[CurrentProfile].Theme = themeName;
                    SaveData();
                }
            }
        }

        private void ThemeOverlay_Changed(object sender, SelectionChangedEventArgs e) {
            if (!_isLoaded) return;
            if (cbThemeOverlay.SelectedItem is string tOverlay) {
                if (MasterData.ContainsKey(CurrentProfile)) {
                    MasterData[CurrentProfile].OverlayTheme = tOverlay;
                }
                SaveData();

            }
        }



        
        private void SectionHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (cbSections.SelectedItem == null || string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            string sec = cbSections.SelectedItem.ToString();
            
            _captureTarget = "SectionHotkey";
            _tempRenameTarget = sec; 
            
            lblKeyWaitTitle.Text = "Нажмите клавишу для смены кнопки\n(нажмите Backspace или Delete для очистки)";
            StartKeyCapture();
        }

        private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e) {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ImportConflict_Rename_Click(object sender, RoutedEventArgs e) {
            _hadImportConflicts = true;
            string name = _currentConflictItem.OriginalKey;
            if (_importTargetContext == "Laws") {
                string uniqueName = name;
                int count = 2;
                while (MasterData[CurrentProfile].Laws.ContainsKey(uniqueName)) { uniqueName = $"{name} #{count}"; count++; }
                var law = _currentConflictItem.Data as LawSection;
                if (law != null) {
                    string hk = law.Hotkey;
                    CheckAndClearHotkeyConflict(ref hk);
                    law.Hotkey = hk;
                }
                MasterData[CurrentProfile].Laws[uniqueName] = law;
            } else if (_importTargetContext == "Binds" && _currentConflictItem.Data is BindItem bind) {
                BindItem newBind = new BindItem {
                    id = Guid.NewGuid().ToString().Substring(0, 6),
                    name = bind.name + " (копия)",
                    group = bind.group,
                    key = bind.key,
                    isAuto = bind.isAuto,
                    steps = bind.steps == null ? new List<BindStep>() : JsonConvert.DeserializeObject<List<BindStep>>(JsonConvert.SerializeObject(bind.steps))
                };
                string hk = newBind.key;
                CheckAndClearHotkeyConflict(ref hk);
                newBind.key = hk;
                MasterData[CurrentProfile].Binds[newBind.id] = newBind;
                _importBindIdMap[_currentConflictItem.OriginalKey] = newBind.id;
            }
            ProcessNextImportConflict();
        }

        private void ImportConflict_Skip_Click(object sender, RoutedEventArgs e) {
            _hadImportConflicts = true;
            ProcessNextImportConflict();
        }

        private void ImportConflict_Cancel_Click(object sender, RoutedEventArgs e) {
            _conflictQueue.Clear();
            ProcessNextImportConflict();
        }


        // --- NEW BENTO DASHBOARD EVENTS ---



        private bool IsFileLockedForWrite(string filePath) {
            if (!System.IO.File.Exists(filePath)) return false;
            try {
                using (var stream = System.IO.File.Open(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.None)) {
                    stream.Close();
                }
            } catch {
                return true;
            }
            return false;
        }

        private async void InstallPlugin_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            string gamePath = txtGamePath?.Text ?? "";
            if (string.IsNullOrEmpty(gamePath) || !System.IO.Directory.Exists(gamePath)) { OpenInfo("ОШИБКА", "Сначала укажите путь к игре!"); return; }
            if (IsFileLockedForWrite(System.IO.Path.Combine(gamePath, "DuranOverlay.asi"))) {
                OpenInfo("ВНИМАНИЕ", "Файл плагина занят процессом.\nПожалуйста, полностью закройте игру (gta_sa.exe) перед установкой плагина.");
                return;
            }

            if (btnInstallPlugin != null) {
                btnInstallPlugin.IsHitTestVisible = false;
                btnInstallPlugin.Content = "ЗАГРУЗКА...";
                var pulse = new DoubleAnimation(1, 0.4, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                btnInstallPlugin.BeginAnimation(OpacityProperty, pulse);
            }

            try {
                await Task.Run(() => {
                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                    string localAsi = System.IO.Path.Combine(appDir, "DuranOverlay.asi");
                    string targetAsi = System.IO.Path.Combine(gamePath, "DuranOverlay.asi");
                    
                    if (!System.IO.File.Exists(localAsi)) {
                        throw new Exception("Локальный файл плагина не найден в папке хелпера.");
                    }
                    
                    System.IO.File.Copy(localAsi, targetAsi, true);
                });

            } catch (Exception ex) { 
                OpenInfo("ОШИБКА", "Не удалось скопировать файл плагина с папки хелпера.\nУбедитесь, что файл DuranOverlay.asi присутствует рядом с программой.\n\n" + ex.Message); 
            } finally {
                Dispatcher.Invoke(() => {
                    // Stop pulse animation on both buttons before RefreshUI changes visibility
                    if (btnInstallPlugin != null) {
                        btnInstallPlugin.BeginAnimation(OpacityProperty, null);
                        btnInstallPlugin.Opacity = 1;
                        btnInstallPlugin.IsHitTestVisible = true;
                    }
                    if (btnCheckScript != null) {
                        btnCheckScript.BeginAnimation(OpacityProperty, null);
                        btnCheckScript.Opacity = 1;
                        btnCheckScript.IsHitTestVisible = true;
                    }
                    RefreshUI();
                });
            }
        }

        private async void CheckPlugin_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return;
            string gamePath = txtGamePath?.Text ?? "";
            if (string.IsNullOrEmpty(gamePath) || !System.IO.Directory.Exists(gamePath)) { OpenInfo("ОШИБКА", "Сначала укажите путь к игре!"); return; }
            if (IsFileLockedForWrite(System.IO.Path.Combine(gamePath, "DuranOverlay.asi"))) {
                OpenInfo("ВНИМАНИЕ", "Файл плагина занят процессом.\nПожалуйста, полностью закройте игру (gta_sa.exe) перед проверкой плагина.");
                return;
            }

            if (btnCheckScript != null) {
                btnCheckScript.IsHitTestVisible = false;
                var pulse = new DoubleAnimation(1, 0.4, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                btnCheckScript.BeginAnimation(OpacityProperty, pulse);
            }

            try {
                await Task.Run(() => {
                    string appDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
                    string localAsi = System.IO.Path.Combine(appDir, "DuranOverlay.asi");
                    string targetAsi = System.IO.Path.Combine(gamePath, "DuranOverlay.asi");
                    
                    if (!System.IO.File.Exists(localAsi)) {
                        throw new Exception("Локальный файл плагина не найден в папке хелпера.");
                    }
                    
                    System.IO.File.Copy(localAsi, targetAsi, true);
                });

                } catch (Exception ex) {
                OpenInfo("ОШИБКА", "Не удалось скопировать файл плагина с папки хелпера.\nУбедитесь, что файл DuranOverlay.asi присутствует рядом с программой.\n\n" + ex.Message);
            } finally {
                if (btnCheckScript != null) {
                    btnCheckScript.BeginAnimation(OpacityProperty, null);
                    btnCheckScript.Opacity = 1;
                    btnCheckScript.IsHitTestVisible = true;
                }
                RefreshUI();
            }
        }

        // --- WIZARD LOGIC ---
        private int _wizardStep = 1;
        private string _wizardProfileName = "";
        private bool _wizardImportedProfile = false;

        private void Wizard_Start_Click(object sender, RoutedEventArgs e) {
            _wizardStep = 1;
            pnlDashboard_Unified.Visibility = Visibility.Collapsed;
            WinWizard.Visibility = Visibility.Visible;
            canvasShieldBg.Visibility = Visibility.Collapsed;
            UpdateSidebarForSetup(true);
            
            _wizardProfileName = "";
            _wizardImportedProfile = false;
            lblWizardFolder.Text = "C:/";
            UpdateWizardUI();
        }

        private void Wizard_ImportProfile_Click(object sender, RoutedEventArgs e) {
            // Open file dialog directly instead of custom import dashboard window
            using (var ofd = new System.Windows.Forms.OpenFileDialog()) {
                ofd.Filter = "Duran Profile (*.duran)|*.duran";
                ofd.Title = "Выберите файл профиля";
                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    try {
                        string d = System.IO.File.ReadAllText(ofd.FileName);
                        var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<ProfileData>(d);
                        if (profile != null) {
                            string pName = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
                            int idx = 1;
                            string orig = pName;
                            while (MasterData.ContainsKey(pName)) {
                                idx++;
                                pName = orig + $" ({idx})";
                            }
                            MasterData[pName] = profile;
                            SaveData();

                            if (MasterData.Count > 0 && _wizardStep == 1) {
                                CurrentProfile = pName;
                                _wizardProfileName = CurrentProfile;
                                _wizardImportedProfile = true;
                                _wizardStep = 2;
                                UpdateWizardUI();
                            }
                        }
                    } catch {
                        OpenInfo("ОШИБКА", "Не удалось прочитать файл профиля.");
                    }
                }
            }
        }

        private void UpdateWizardUI() {
            var gold = (SolidColorBrush)FindResource("GoldBrush");
            var green = (SolidColorBrush)FindResource("GreenBrush");
            var blue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1f6feb"));
            var purple = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8957e5"));
            var gray = (SolidColorBrush)FindResource("GrayBrush");
            var line = (SolidColorBrush)FindResource("LineBrush");
            var boxBrushColor = ((SolidColorBrush)FindResource("BoxBrush")).Color;
            var inputBrushColor = ((SolidColorBrush)FindResource("InputBrush")).Color;
            var disabledColor = (Color)ColorConverter.ConvertFromString("#5c6370");
            
            // Generate a subtle green background derived from GreenBrush and DeepBgBrush
            var deepBgColor = ((SolidColorBrush)FindResource("DeepBgBrush")).Color;
            var greenBgColor = Color.FromArgb(255, (byte)((deepBgColor.R + green.Color.R * 0.15)), (byte)((deepBgColor.G + green.Color.G * 0.15)), (byte)((deepBgColor.B + green.Color.B * 0.15)));

            if (_wizardStep == 1) {
                // STEP 1 ACTIVE
                wizCard1.BeginAnimation(OpacityProperty, null);
                wizCard1.Opacity = 1;
                wizCard1.BorderBrush = gold;
                wizCard1.Background = new SolidColorBrush(boxBrushColor);
                wizGlow1.Color = gold.Color;
                wizGlow1.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.4, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                wizGlow1.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0, 20, TimeSpan.FromSeconds(0.5)));
                wizTitle1.Text = "ШАГ 1: ИНИЦИАЛИЗАЦИЯ БАЗЫ ДАННЫХ";
                wizTitle1.Foreground = gold;
                wizLine1.Stroke = gold;
                btnWizBack2.Visibility = Visibility.Collapsed;
                btnWizBack3.Visibility = Visibility.Collapsed;
                
                lblWizDesc1.Visibility = Visibility.Visible;
                lblWizSub1.Text = "Профиль необходим для локального хранения данных.";
                
                btnWizAction1.Content = "+ СОЗДАТЬ ПРОФИЛЬ";
                btnWizAction1.Foreground = gold;
                btnWizAction1.BorderBrush = gold;
                btnWizAction1.IsHitTestVisible = true;

                // Steps 2 & 3 disabled
                wizCard2.Opacity = 0.5; wizGlow2.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(wizGlow2.Opacity, 0, TimeSpan.FromSeconds(0.4))); wizGlow2.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(wizGlow2.BlurRadius, 0, TimeSpan.FromSeconds(0.4)));
                wizCard2.BorderBrush = line; wizTitle2.Text = "ШАГ 2: СОЕДИНЕНИЕ С ИГРОЙ"; wizTitle2.Foreground = gray; wizLine2.Stroke = line;
                btnWizAction2.Content = "ОЖИДАНИЕ"; btnWizAction2.Foreground = new SolidColorBrush(disabledColor); btnWizAction2.BorderBrush = line; btnWizAction2.IsHitTestVisible = false;
                wizCard3.Opacity = 0.5; wizGlow3.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(wizGlow3.Opacity, 0, TimeSpan.FromSeconds(0.4))); wizGlow3.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(wizGlow3.BlurRadius, 0, TimeSpan.FromSeconds(0.4)));
                wizCard3.Background = new SolidColorBrush(boxBrushColor);
                wizTitle3.Foreground = gray;
                lblWizSub3.Foreground = new SolidColorBrush(disabledColor);
                btnWizardInstall.Content = "ОЖИДАНИЕ"; btnWizardInstall.Background = new SolidColorBrush(inputBrushColor); btnWizardInstall.Foreground = new SolidColorBrush(disabledColor); btnWizardInstall.BorderBrush = line;
            } 
            else if (_wizardStep == 2) {
                // STEP 1 DONE - animate to green
                var fromBorderColor = ((SolidColorBrush)wizCard1.BorderBrush).Color;
                var fromBgColor = ((SolidColorBrush)wizCard1.Background).Color;
                var colorEase = new CubicEase { EasingMode = EasingMode.EaseOut };
                wizCard1.BorderBrush = new SolidColorBrush(fromBorderColor);
                wizCard1.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fromBorderColor, green.Color, TimeSpan.FromSeconds(0.5)) { EasingFunction = colorEase });
                wizCard1.Background = new SolidColorBrush(fromBgColor);
                wizCard1.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fromBgColor, greenBgColor, TimeSpan.FromSeconds(0.5)) { EasingFunction = colorEase });
                wizGlow1.Color = green.Color;
                wizTitle1.Text = "ШАГ 1 ВЫПОЛНЕН";
                wizTitle1.Foreground = green;
                wizLine1.Stroke = green;
                
                lblWizDesc1.Visibility = Visibility.Visible;
                lblWizDesc1.Text = _wizardImportedProfile ? "Профиль базы данных импортирован." : "Профиль базы данных создан.";
                lblWizSub1.Text = _wizardImportedProfile ? $"Импортирован: «{_wizardProfileName}»" : $"Выбран: «{_wizardProfileName}»";
                
                btnWizAction1.Content = "ВЫПОЛНЕНО";
                btnWizAction1.Foreground = green;
                btnWizAction1.BorderBrush = green;
                btnWizAction1.IsHitTestVisible = false;
                
                // Dim Step 1
                wizCard1.BeginAnimation(OpacityProperty, new DoubleAnimation(wizCard1.Opacity, 0.5, TimeSpan.FromSeconds(0.4)));

                // STEP 2 ACTIVE
                wizCard2.Opacity = 1;
                wizCard2.BorderBrush = blue;
                wizCard2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161b22"));
                wizGlow2.Color = blue.Color;
                wizGlow2.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0, 20, TimeSpan.FromSeconds(0.5)));
                wizGlow2.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.6, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                wizTitle2.Text = "ШАГ 2: СОЕДИНЕНИЕ С ИГРОЙ";
                wizTitle2.Foreground = blue;
                wizLine2.Stroke = blue;
                btnWizBack2.Visibility = Visibility.Visible;
                btnWizAction2.IsHitTestVisible = true;
                btnWizAction2.Cursor = Cursors.Hand;
                
                lblWizDesc2.Text = "Требуется указать путь к RADMIR CRMP.";
                lblWizardFolder.Visibility = Visibility.Collapsed;
                lblWizSub2.Visibility = Visibility.Visible;
                lblWizSub2.Text = "Системе нужно получить доступ к файлам игры.";

                btnWizAction2.Content = "УКАЗАТЬ ПАПКУ";
                btnWizAction2.Foreground = blue;
                btnWizAction2.BorderBrush = blue;

                // Reset Step 3
                wizCard3.Opacity = 0.5;
                wizGlow3.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(wizGlow3.Opacity, 0, TimeSpan.FromSeconds(0.4)));
                wizGlow3.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(wizGlow3.BlurRadius, 0, TimeSpan.FromSeconds(0.4)));
                wizCard3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161b22"));
                wizCard3.BorderBrush = line;
                wizTitle3.Foreground = gray;
                lblWizSub3.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370"));
                wizIcon3.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370"));
                wizIconBorder3.BorderBrush = line;
                btnWizardInstall.Content = "ОЖИДАНИЕ";
                btnWizardInstall.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0d12"));
                btnWizardInstall.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5c6370"));
                btnWizardInstall.BorderBrush = line;
                btnWizardInstall.Cursor = Cursors.Arrow;
                btnWizBack3.Visibility = Visibility.Collapsed;
            }
            else if (_wizardStep == 3) {
                // STEP 2 DONE - animate to green
                var fromBorderColor2 = ((SolidColorBrush)wizCard2.BorderBrush).Color;
                var fromBgColor2 = ((SolidColorBrush)wizCard2.Background).Color;
                var colorEase2 = new CubicEase { EasingMode = EasingMode.EaseOut };
                wizCard2.BorderBrush = new SolidColorBrush(fromBorderColor2);
                wizCard2.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fromBorderColor2, green.Color, TimeSpan.FromSeconds(0.5)) { EasingFunction = colorEase2 });
                wizCard2.Background = new SolidColorBrush(fromBgColor2);
                wizCard2.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fromBgColor2, greenBgColor, TimeSpan.FromSeconds(0.5)) { EasingFunction = colorEase2 });
                wizGlow2.Color = green.Color;
                wizGlow2.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(wizGlow2.Opacity, 0.4, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                wizGlow2.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(wizGlow2.BlurRadius, 15, TimeSpan.FromSeconds(0.5)));
                wizTitle2.Text = "ШАГ 2 ВЫПОЛНЕН";
                wizTitle2.Foreground = green;
                wizLine2.Stroke = green;
                
                lblWizDesc2.Text = "Директория RADMIR CRMP найдена.";
                lblWizDesc2.Foreground = Brushes.White;
                lblWizardFolder.Visibility = Visibility.Visible;
                lblWizardFolder.Foreground = Brushes.White;
                lblWizSub2.Visibility = Visibility.Collapsed;
                btnWizBack2.Visibility = Visibility.Collapsed;
                
                btnWizAction2.Content = "ВЫПОЛНЕНО";
                btnWizAction2.Foreground = green;
                btnWizAction2.BorderBrush = green;
                btnWizAction2.IsHitTestVisible = false;
                
                // Dim Step 2
                wizCard2.BeginAnimation(OpacityProperty, new DoubleAnimation(wizCard2.Opacity, 0.5, TimeSpan.FromSeconds(0.4)));

                // STEP 3 ACTIVE
                wizCard3.Opacity = 1;
                wizCard3.BorderBrush = purple;
                wizCard3.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#140f1a"));
                wizTitle3.Foreground = Brushes.White;
                lblWizSub3.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c9d1d9"));
                wizGlow3.Color = purple.Color;
                wizGlow3.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0, 10, TimeSpan.FromSeconds(0.5)));
                wizGlow3.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(0.5)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                wizIcon3.Fill = purple;
                wizIconBorder3.BorderBrush = purple;
                btnWizBack3.Visibility = Visibility.Visible;
                
                btnWizardInstall.Content = "УСТАНОВИТЬ";
                btnWizardInstall.Background = purple;
                btnWizardInstall.Foreground = Brushes.White;
                btnWizardInstall.BorderBrush = purple;
                btnWizardInstall.Cursor = Cursors.Hand;
            }
        }

        private void Wizard_CreateProfile_Click(object sender, RoutedEventArgs e) {
            if (_wizardStep == 1) {
                _dialogHost.ShowInput("ВВЕДИТЕ ИМЯ НОВОГО ПРОФИЛЯ", (v) => {
                    _wizardProfileName = v;
                    _wizardStep = 2;
                    UpdateWizardUI();
                }, validator: (v) => MasterData.ContainsKey(v) ? "Группа с таким именем уже существует!" : null);
            }
        }

        private void Wizard_Browse_Click(object sender, RoutedEventArgs e) {
            if (_wizardStep != 2) return;
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Выберите корневую папку с игрой GTA San Andreas";
            dialog.UseDescriptionForTitle = true;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                string folder = dialog.SelectedPath;
                bool isGameFolder = System.IO.File.Exists(System.IO.Path.Combine(folder, "gta_sa.exe"))
                    || System.IO.File.Exists(System.IO.Path.Combine(folder, "gta-sa.exe"))
                    || System.IO.File.Exists(System.IO.Path.Combine(folder, "radmir.exe"));
                if (!isGameFolder) {
                    OpenInfo("ОШИБКА", "Выбранная папка не является корневой папкой игры.\n\nВ ней не найден файл gta_sa.exe.\n\nУзнать корневую папку с игрой вы сможете в RADMIR Launcher в настройках лаунчера.\nПараметр \"Путь к папке\".");
                    return;
                }
                lblWizardFolder.Text = folder;
                _wizardStep = 3;
                UpdateWizardUI();
            }
        }

        private async void Wizard_Install_Click(object sender, RoutedEventArgs e) {
            if (_wizardStep != 3) return;

            // Animate button pulse lighter
            btnWizardInstall.IsHitTestVisible = false;
            btnWizardInstall.Content = "ЗАГРУЗКА...";
            var animBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8957e5"));
            btnWizardInstall.Background = animBrush;
            var colorAnim = new ColorAnimation((Color)ColorConverter.ConvertFromString("#8957e5"), (Color)ColorConverter.ConvertFromString("#b396eb"), TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

            try {
                string prf = _wizardProfileName?.Trim() ?? "";
                if (string.IsNullOrEmpty(prf)) {
                    if (MasterData.Count > 0 && !string.IsNullOrEmpty(CurrentProfile)) {
                        prf = CurrentProfile;
                    } else {
                        prf = "Основной";
                    }
                }
                if (!MasterData.ContainsKey(prf)) {
                    MasterData[prf] = new ProfileData();
                    MasterData[prf].Groups = new List<string> { "ВСЕ" };
                    MasterData[prf].Variables = new Dictionary<string, string> { {"*ИМЯ*", ""}, {"*ФАМ*", ""}, {"*ТЕГ*", ""}, {"*ЗВ*", ""} };
                }
                CurrentProfile = prf;
                string gamePath = lblWizardFolder.Text;
                MasterData[CurrentProfile].GamePath = gamePath;
                txtGamePath.Text = gamePath; // sync global game path

                string sourceAsi = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DuranOverlay.asi");
                string targetAsi = System.IO.Path.Combine(gamePath, "DuranOverlay.asi");

                if (IsFileLockedForWrite(targetAsi)) {
                    OpenInfo("ВНИМАНИЕ", "Файл плагина занят процессом.\nПожалуйста, полностью закройте игру (gta_sa.exe) через диспетчер задач перед обновлением плагина.");
                    btnWizardInstall.IsHitTestVisible = true;
                    btnWizardInstall.Content = "УСТАНОВИТЬ";
                    return;
                }
                
                if (System.IO.File.Exists(sourceAsi)) {
                    await Task.Run(() => {
                        System.IO.File.Copy(sourceAsi, targetAsi, true);
                    });
                } else {
                    OpenInfo("ОШИБКА", "Локальный файл плагина не найден в папке хелпера.\nУбедитесь, что файл DuranOverlay.asi присутствует рядом с программой.");
                    btnWizardInstall.IsHitTestVisible = true;
                    btnWizardInstall.Content = "УСТАНОВИТЬ";
                    return;
                }

                SaveData();
                WinWizard.Visibility = Visibility.Collapsed;
                RefreshUI();
                AnimateShieldDraw();
                AddLog("Профиль '" + prf + "' создан, плагин установлен", "#2ea043");
                ShowCustomToast("УСПЕШНО", "ASI скрипт успешно скачан и установлен!", "green", null);
            } catch (Exception ex) {
                OpenInfo("ОШИБКА", ex.Message);
            } finally {
                var purple = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8957e5"));
                btnWizardInstall.Background = purple;
                btnWizardInstall.IsHitTestVisible = true;
                btnWizardInstall.Content = "УСТАНОВИТЬ";
            }
        }

        private void Wizard_Back_Click(object sender, RoutedEventArgs e) {
            if (_wizardStep == 2 && _wizardImportedProfile && !string.IsNullOrEmpty(_wizardProfileName) && MasterData.ContainsKey(_wizardProfileName)) {
                // Ask user if they want to remove the imported profile
                _dialogHost.ShowAlert("НАЗАД", "Вернуться на шаг 1?\n\nИмпортированный профиль '" + _wizardProfileName + "' будет удалён.", () => {
                    if (!string.IsNullOrEmpty(_wizardProfileName) && MasterData.ContainsKey(_wizardProfileName)) {
                        MasterData.Remove(_wizardProfileName);
                        SaveData();
                    }
                    _wizardProfileName = "";
                    _wizardImportedProfile = false;
                    _wizardStep = 1;
                    UpdateWizardUI();
                });
                return;
            }
            if (_wizardStep > 1) {
                _wizardStep--;
                UpdateWizardUI();
            }
        }

        private void Wizard_Cancel_Click(object sender, RoutedEventArgs e) {
            WinWizard.Visibility = Visibility.Collapsed;
            canvasShieldBg.Visibility = Visibility.Visible;
            if (MasterData.Count == 0) {
                pnlDashboard_Unified.Visibility = Visibility.Visible;
                ApplyDashboardNoProfile();
                UpdateSidebarForSetup(true);
            }
        }

        private void WelcomeStart_Click(object sender, RoutedEventArgs e) {
            _isUpgrade = false;
            WinWelcome.Visibility = Visibility.Collapsed;
            Wizard_Start_Click(null, null);
            if (MasterData.Count > 0) {
                _wizardProfileName = CurrentProfile;
                _wizardStep = 2;
                UpdateWizardUI();
            }
        }

        private string _dashboardState = "";

        private void ShowDashboardPlates() {
            if (pnlDashboard_Unified.Visibility != Visibility.Visible) {
                pnlDashboard_Unified.Visibility = Visibility.Visible;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                pnlDashboard_Unified.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease });
                var tt = new TranslateTransform(0, 15);
                pnlDashboard_Unified.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(15, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = ease });
            }
        }

        private void UpdateDashboardState() {
            // Keep dashboard colors in sync with currently selected launcher theme,
            // but do not re-apply the same theme repeatedly (can stutter splash expansion).
            string activeLauncherTheme = "Default (Dark Blue)";
            if (rbThemeBlack?.IsChecked == true) activeLauncherTheme = "Black (AMOLED)";
            else if (rbThemeGrey?.IsChecked == true) activeLauncherTheme = "Grey (Sport red)";
            if (!string.Equals(_lastAppliedLauncherTheme, activeLauncherTheme, StringComparison.Ordinal))
            {
                _lastAppliedLauncherTheme = activeLauncherTheme;
                ApplyTheme(activeLauncherTheme);
            }

            if (_isUpgrade) {
                canvasShieldBg.Visibility = Visibility.Collapsed;
                pnlDashboard_Unified.Visibility = Visibility.Collapsed;
                UpdateSidebarForSetup(true);
                if (WinWelcome.Visibility != Visibility.Visible) {
                    WinWelcome.Visibility = Visibility.Visible;
                }
                return;
            }

            string gamePath = txtGamePath?.Text ?? "";
            
            // Bulletproof recovery: if global game path is somehow lost, restore from current profile
            if (string.IsNullOrEmpty(gamePath) && MasterData.Count > 0 && !string.IsNullOrEmpty(CurrentProfile) && MasterData.ContainsKey(CurrentProfile)) {
                string profileGamePath = MasterData[CurrentProfile].GamePath;
                if (!string.IsNullOrEmpty(profileGamePath) && System.IO.Directory.Exists(profileGamePath)) {
                    gamePath = profileGamePath;
                    txtGamePath.Text = gamePath;
                    SaveData();
                }
            }
            
            bool hasGamePath = !string.IsNullOrEmpty(gamePath) && System.IO.Directory.Exists(gamePath);

            if (MasterData.Count == 0 || string.IsNullOrEmpty(CurrentProfile) || !hasGamePath) {
                _hasProfile = (MasterData.Count > 0 && !string.IsNullOrEmpty(CurrentProfile));

                if (!_hasProfile && !hasGamePath) {
                    // No profile AND no game path → full wizard needed (first launch)
                    canvasShieldBg.Visibility = Visibility.Collapsed;
                    pnlDashboard_Unified.Visibility = Visibility.Collapsed;
                    UpdateSidebarForSetup(true);
                    if (WinWizard.Visibility != Visibility.Visible) {
                        WinWizard.Visibility = Visibility.Visible;
                        Wizard_Start_Click(null, null);
                    }
                } else if (_hasProfile && !hasGamePath) {
                    // Has profile but no game path → wizard step 2 (game path setup)
                    canvasShieldBg.Visibility = Visibility.Collapsed;
                    pnlDashboard_Unified.Visibility = Visibility.Collapsed;
                    UpdateSidebarForSetup(true);
                    if (WinWizard.Visibility != Visibility.Visible) {
                        WinWizard.Visibility = Visibility.Visible;
                        if (_wizardStep == 1) {
                            _wizardProfileName = CurrentProfile;
                            _wizardStep = 2;
                            UpdateWizardUI();
                        }
                    }
                } else {
                    // No profile but game path exists → dashboard "create profile" screen
                    canvasShieldBg.Visibility = Visibility.Visible;
                    canvasShieldBg.BeginAnimation(OpacityProperty, new DoubleAnimation(canvasShieldBg.Opacity, 0.15, TimeSpan.FromSeconds(0.3)));
                    if (WinWizard.Visibility == Visibility.Visible) WinWizard.Visibility = Visibility.Collapsed;
                    UpdateSidebarForSetup(true);
                    ShowDashboardPlates();
                    ApplyDashboardNoProfile();
                    lblGameFolder.Text = gamePath;
                }
                return;
            }

            _hasProfile = true;
            canvasShieldBg.Visibility = Visibility.Visible;
            if (WinWizard.Visibility == Visibility.Visible) WinWizard.Visibility = Visibility.Collapsed;
            UpdateSidebarForSetup(false);
            ShowDashboardPlates();

            string path = txtGamePath?.Text ?? "";
            bool asiOk = !string.IsNullOrEmpty(path) && System.IO.File.Exists(System.IO.Path.Combine(path, "DuranOverlay.asi"));

            if (asiOk) {
                ApplyDashboardActive(path);
            } else {
                ApplyDashboardNoASI(path);
            }
        }

        // ======= UNIFIED DASHBOARD STATE APPLIERS =======

        private void ApplyDashboardActive(string path) {
            _dashboardState = "Active";
            var gold = (SolidColorBrush)FindResource("GoldBrush");
            var green = (SolidColorBrush)FindResource("GreenBrush");
            var line = (SolidColorBrush)FindResource("LineBrush");

            lblDashTitle.Text = "ЦЕНТРАЛЬНАЯ ПАНЕЛЬ";
            lblDashSubtitle.Text = "> ВАШИ ИЗМЕНЕНИЯ СИНХРОНИЗИРУЮТСЯ В ЛАЙВ-РЕЖИМЕ";
            lblDashSubtitle.Foreground = green;

            canvasShieldBg.BeginAnimation(OpacityProperty, new DoubleAnimation(canvasShieldBg.Opacity, 1.0, TimeSpan.FromSeconds(0.3)));

            dashProfileCard.Opacity = 1;
            dashProfileGlow.Opacity = 0;
            dashProfileBorder.BorderBrush = line;
            dashProfileBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1b1612"));
            dashProfileBadge.BorderBrush = gold;
            dashProfileBadgeText.Text = "АКТИВЕН";
            dashProfileBadgeText.Foreground = gold;
            dashProfileDot.Fill = gold;
            dashProfileSubText.Text = "Синхронизация профиля с игрой установлена.";

            dashAvatarActive.Visibility = Visibility.Visible;
            dashAvatarEmpty.Visibility = Visibility.Collapsed;
            dashAvatarInactive.Visibility = Visibility.Collapsed;

            cbProfiles.Visibility = Visibility.Visible;
            dashProfilePlaceholder.Visibility = Visibility.Collapsed;
            lblProfileName_NoASI.Visibility = Visibility.Collapsed;
            cbProfiles.ItemsSource = MasterData.Keys.ToList();
            cbProfiles.SelectedItem = CurrentProfile;

            dashBtnsActive.Visibility = Visibility.Visible;
            btnNoProfileCreate.Visibility = Visibility.Collapsed;
            dashBtnsNoASI.Visibility = Visibility.Collapsed;

            dashWarningBanner.Visibility = Visibility.Collapsed;

            dashGameBorder.BorderBrush = line;
            dashGameCard.Opacity = 1;
            dashGameBadgeGreen.Visibility = Visibility.Visible;
            dashGameBadgeRed.Visibility = Visibility.Collapsed;
            btnCheckScript.Visibility = Visibility.Visible;
            btnInstallPlugin.Visibility = Visibility.Collapsed;
            dashGameOverlay.Opacity = 0;
            if (path != null) lblGameFolder.Text = path;

            dashStatsCard.Opacity = 1;
            dashStatsContent.Opacity = 1;
            dashStatsOverlay.Opacity = 0;
            if (!_isStartupStatsAnimating && MasterData.ContainsKey(CurrentProfile)) {
                lblStatBinds.Text = MasterData[CurrentProfile].Binds.Values.Count(b => b.active).ToString();
                lblStatSections.Text = MasterData[CurrentProfile].Laws.Count.ToString();
                int arts = CountRealArticles(MasterData[CurrentProfile].Laws);
                lblStatLaws.Text = arts.ToString();
            }
            lblStatBindsLabel.Foreground = green;
            lblStatSectionsLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1f6feb"));
            lblStatLawsLabel.Foreground = gold;
        }

        private void ApplyDashboardNoProfile() {
            _dashboardState = "NoProfile";
            var gold = (SolidColorBrush)FindResource("GoldBrush");
            var line = (SolidColorBrush)FindResource("LineBrush");

            lblDashTitle.Text = "ЦЕНТРАЛЬНАЯ ПАНЕЛЬ";
            lblDashSubtitle.Text = "> СОЗДАЙТЕ ПРОФИЛЬ ДЛЯ НАЧАЛА РАБОТЫ";
            lblDashSubtitle.Foreground = gold;

            canvasShieldBg.BeginAnimation(OpacityProperty, new DoubleAnimation(canvasShieldBg.Opacity, 0.15, TimeSpan.FromSeconds(0.3)));

            dashProfileCard.Opacity = 1;
            dashProfileGlow.Opacity = 1;
            dashProfileBorder.BorderBrush = gold;
            dashProfileBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1b1612"));
            dashProfileBadge.BorderBrush = gold;
            dashProfileBadgeText.Text = "ТРЕБУЕТСЯ";
            dashProfileBadgeText.Foreground = gold;
            dashProfileDot.Fill = gold;
            dashProfileSubText.Text = "Создайте профиль чтобы начать работу.";

            dashAvatarActive.Visibility = Visibility.Collapsed;
            dashAvatarEmpty.Visibility = Visibility.Visible;
            dashAvatarInactive.Visibility = Visibility.Collapsed;

            cbProfiles.Visibility = Visibility.Collapsed;
            dashProfilePlaceholder.Visibility = Visibility.Visible;
            lblProfileName_NoASI.Visibility = Visibility.Collapsed;

            dashBtnsActive.Visibility = Visibility.Collapsed;
            btnNoProfileCreate.Visibility = Visibility.Visible;
            dashBtnsNoASI.Visibility = Visibility.Collapsed;

            dashWarningBanner.Visibility = Visibility.Collapsed;

            dashGameBorder.BorderBrush = line;
            dashGameCard.Opacity = 0.4;
            dashGameBadgeGreen.Visibility = Visibility.Collapsed;
            dashGameBadgeRed.Visibility = Visibility.Collapsed;
            btnCheckScript.Visibility = Visibility.Collapsed;
            btnInstallPlugin.Visibility = Visibility.Collapsed;
            dashGameOverlay.Opacity = 1;

            dashStatsCard.Opacity = 0.4;
            dashStatsContent.Opacity = 0.5;
            dashStatsOverlay.Opacity = 1;
            dashStatsOverlayText.Text = "ПРОФИЛЬ НЕ СОЗДАН";
            lblStatBinds.Text = "--";
            lblStatSections.Text = "--";
            lblStatLaws.Text = "--";
            lblStatBindsLabel.Foreground = line;
            lblStatSectionsLabel.Foreground = line;
            lblStatLawsLabel.Foreground = line;
        }

        private void ApplyDashboardNoASI(string path) {
            _dashboardState = "NoASI";
            var red = (SolidColorBrush)FindResource("RedBrush");
            var line = (SolidColorBrush)FindResource("LineBrush");
            var gray = (SolidColorBrush)FindResource("GrayBrush");

            lblDashTitle.Text = "ПРОБЛЕМА СВЯЗИ";
            lblDashSubtitle.Text = "> СВЯЗЬ С ИГРОВЫМ КЛИЕНТОМ ПОТЕРЯНА";
            lblDashSubtitle.Foreground = red;

            canvasShieldBg.BeginAnimation(OpacityProperty, new DoubleAnimation(canvasShieldBg.Opacity, 0.15, TimeSpan.FromSeconds(0.3)));

            dashProfileCard.Opacity = 0.4;
            dashProfileGlow.Opacity = 0;
            dashProfileBorder.BorderBrush = line;
            dashProfileBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1c0f12"));
            dashProfileBadge.BorderBrush = red;
            dashProfileBadgeText.Text = "НЕАКТИВЕН";
            dashProfileBadgeText.Foreground = red;
            dashProfileDot.Fill = red;
            dashProfileSubText.Text = "Синхронизация профиля с игрой установлена.";

            dashAvatarActive.Visibility = Visibility.Collapsed;
            dashAvatarEmpty.Visibility = Visibility.Collapsed;
            dashAvatarInactive.Visibility = Visibility.Visible;

            cbProfiles.Visibility = Visibility.Collapsed;
            dashProfilePlaceholder.Visibility = Visibility.Collapsed;
            lblProfileName_NoASI.Visibility = Visibility.Visible;
            lblProfileName_NoASI.Text = CurrentProfile;

            dashBtnsActive.Visibility = Visibility.Collapsed;
            btnNoProfileCreate.Visibility = Visibility.Collapsed;
            dashBtnsNoASI.Visibility = Visibility.Visible;

            dashWarningBanner.Visibility = Visibility.Visible;

            dashGameBorder.BorderBrush = red;
            dashGameCard.Opacity = 1;
            dashGameBadgeGreen.Visibility = Visibility.Collapsed;
            dashGameBadgeRed.Visibility = Visibility.Visible;
            btnCheckScript.Visibility = Visibility.Collapsed;
            btnInstallPlugin.Visibility = Visibility.Visible;
            dashGameOverlay.Opacity = 0;
            if (path != null) lblGameFolder.Text = string.IsNullOrEmpty(path) ? "Путь не установлен" : path;

            dashStatsCard.Opacity = 0.4;
            dashStatsContent.Opacity = 0.5;
            dashStatsOverlay.Opacity = 1;
            dashStatsOverlayText.Text = "ПОТЕРЯНА СВЯЗЬ С ASI";
            lblStatBinds.Text = "--";
            lblStatSections.Text = "--";
            lblStatLaws.Text = "--";
            lblStatBindsLabel.Foreground = gray;
            lblStatSectionsLabel.Foreground = gray;
            lblStatLawsLabel.Foreground = gray;
        }

        // ===========================================================
        // ======= RADIAL MENU TAB LOGIC =======
        // ===========================================================
        private int _radialSelectedGroupIndex = 0;
        private int _radialSelectedSectorIndex = 0; // -1 means group ring is selected
        private System.Windows.Threading.DispatcherTimer _radialSaveTimer;
        private string _radialSelectedIcon = "none";
        private readonly string[] _radialIconKeys = { "none", "star", "megaphone", "lightning", "document", "car" };


        private RadialMenuConfig GetRadialConfig() {
            if (string.IsNullOrEmpty(CurrentProfile) || !MasterData.ContainsKey(CurrentProfile)) return new RadialMenuConfig();
            var cfg = MasterData[CurrentProfile].RadialMenu;
            if (cfg == null) { cfg = new RadialMenuConfig(); MasterData[CurrentProfile].RadialMenu = cfg; }
            
            // Clean up ghost binds (deleted binds that are still referenced)
            var allBindNames = MasterData[CurrentProfile].Binds.Values.Select(b => b.name).ToHashSet();
            Action<List<RadialMenuSector>> cleanSectors = (sectors) => {
                if (sectors == null) return;
                foreach (var s in sectors) {
                    if (!string.IsNullOrEmpty(s.BindName) && !allBindNames.Contains(s.BindName)) {
                        s.BindId = null;
                        s.BindName = null;
                    }
                }
            };
            cleanSectors(cfg.Sectors);
            if (cfg.Groups != null) {
                foreach (var g in cfg.Groups) cleanSectors(g.Sectors);
            }
            
            return cfg;
        }

        private void EnsureSectors(RadialMenuConfig cfg) {
            if (cfg.Mode == "Grouped") {
                while (cfg.Groups.Count < cfg.GroupCount)
                    cfg.Groups.Add(new RadialMenuGroup { Name = $"ГРУППА {cfg.Groups.Count + 1}", SectorCount = 4 });
                if (cfg.Groups.Count > cfg.GroupCount)
                    cfg.Groups = cfg.Groups.Take(cfg.GroupCount).ToList();
                
                foreach (var g in cfg.Groups) {
                    while (g.Sectors.Count < g.SectorCount) g.Sectors.Add(new RadialMenuSector());
                    if (g.Sectors.Count > g.SectorCount) g.Sectors = g.Sectors.Take(g.SectorCount).ToList();
                }
            } else {
                while (cfg.Sectors.Count < cfg.SectorCount)
                    cfg.Sectors.Add(new RadialMenuSector());
                if (cfg.Sectors.Count > cfg.SectorCount)
                    cfg.Sectors = cfg.Sectors.Take(cfg.SectorCount).ToList();
            }
        }

        private void RadialMode_Changed(object sender, RoutedEventArgs e) {
            if (_ignoreEvents || !_isLoaded) return;
            var cfg = GetRadialConfig();
            cfg.Mode = (rbRadialGrouped.IsChecked == true) ? "Grouped" : "Standard";
            _radialSelectedGroupIndex = 0;
            _radialSelectedSectorIndex = (cfg.Mode == "Grouped") ? -1 : 0;
            LoadRadialTab(true);
            QueueRadialSave();
        }

        private ScaleTransform _radialScaleTransform;
        private bool _radialAnimateOuterSectors = false;

        private void LoadRadialTab(bool animate = false) {
            if (_ignoreEvents) return;
            var cfg = GetRadialConfig();

            if (_radialScaleTransform == null) {
                _radialScaleTransform = new ScaleTransform(1.0, 1.0);
                _radialScaleTransform.CenterX = SvgX(250);
                _radialScaleTransform.CenterY = SvgY(250);
                canvasRadial.RenderTransform = _radialScaleTransform;
            }
            double targetScale = (cfg.Mode == "Standard") ? 1.15 : 1.0;
            _radialAnimateOuterSectors = animate && (cfg.Mode == "Grouped");
            if (animate) {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(250)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                _radialScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                _radialScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            } else {
                _radialScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _radialScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _radialScaleTransform.ScaleX = targetScale;
                _radialScaleTransform.ScaleY = targetScale;
            }
            
            _ignoreEvents = true;
            if (rbRadialGrouped != null) rbRadialGrouped.IsChecked = (cfg.Mode == "Grouped");
            if (rbRadialStandard != null) rbRadialStandard.IsChecked = (cfg.Mode == "Standard");
            if (cfg.Mode != "Grouped" && _radialSelectedSectorIndex == -1) _radialSelectedSectorIndex = 0;
            _ignoreEvents = false;

            EnsureSectors(cfg);
            
            if (chkSysRadial != null) chkSysRadial.IsChecked = cfg.Enabled;
            radialDisabledOverlay.Visibility = cfg.Enabled ? Visibility.Collapsed : Visibility.Visible;
            if (MasterData.ContainsKey(CurrentProfile) && MasterData[CurrentProfile].Binds.TryGetValue("Radial", out var bi))
                btnRadialKey.Content = bi.DisplayKey;
            else
                btnRadialKey.Content = "M3";

            DrawRadialCircle(cfg);
            LoadRadialSectorSettings(cfg);
            BuildRadialIconPicker();
            PopulateRadialBindCombo();
        }

        // === SVG-matched arc path (no gap, exact SVG coords) ===
        private string GetArcPath(double cx, double cy, double rIn, double rOut, double startDeg, double endDeg) {
            double startRad = (startDeg - 90) * Math.PI / 180.0;
            double endRad   = (endDeg   - 90) * Math.PI / 180.0;

            double p1_out_x = cx + rOut * Math.Cos(startRad);
            double p1_out_y = cy + rOut * Math.Sin(startRad);
            double p2_out_x = cx + rOut * Math.Cos(endRad);
            double p2_out_y = cy + rOut * Math.Sin(endRad);
            double p2_in_x  = cx + rIn  * Math.Cos(endRad);
            double p2_in_y  = cy + rIn  * Math.Sin(endRad);
            double p1_in_x  = cx + rIn  * Math.Cos(startRad);
            double p1_in_y  = cy + rIn  * Math.Sin(startRad);

            double span = endDeg - startDeg; if (span < 0) span += 360;
            int largeArc = (span > 180) ? 1 : 0;
            var ic = System.Globalization.CultureInfo.InvariantCulture;

            return $"M {p1_out_x.ToString(ic)},{p1_out_y.ToString(ic)} " +
                   $"A {rOut.ToString(ic)},{rOut.ToString(ic)} 0 {largeArc} 1 {p2_out_x.ToString(ic)},{p2_out_y.ToString(ic)} " +
                   $"L {p2_in_x.ToString(ic)},{p2_in_y.ToString(ic)} " +
                   $"A {rIn.ToString(ic)},{rIn.ToString(ic)} 0 {largeArc} 0 {p1_in_x.ToString(ic)},{p1_in_y.ToString(ic)} Z";
        }

        // SVG transform: translate(230,250) scale(0.7) translate(-250,-250)
        // Canvas starts at y=60 in panel, so center Y = 250-60 = 190
        private const double _svgCx = 250, _svgCy = 250, _svgScale = 0.7;
        private const double _canvasCx = 230, _canvasCy = 190;
        private double SvgX(double x) => _canvasCx + (x - _svgCx) * _svgScale;
        private double SvgY(double y) => _canvasCy + (y - _svgCy) * _svgScale;
        private double SvgR(double r) => r * _svgScale;

        // Point on circle (0°=top, clockwise)
        private (double x, double y) SvgPolar(double angleDeg, double radius) {
            double rad = (angleDeg - 90) * Math.PI / 180.0;
            return (_svgCx + radius * Math.Cos(rad), _svgCy + radius * Math.Sin(rad));
        }

        private void AddEllipse(double svgCx, double svgCy, double svgR, Brush fill, Brush stroke, double strokeW) {
            double cx = SvgX(svgCx);
            double cy = SvgY(svgCy);
            double r = SvgR(svgR);
            var geom = new EllipseGeometry(new Point(cx, cy), r, r);
            var path = new System.Windows.Shapes.Path {
                Data = geom,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = strokeW,
                IsHitTestVisible = false
            };
            canvasRadial.Children.Add(path);
        }

        private System.Windows.Shapes.Path AddArcSector(double rIn, double rOut, double startDeg, double endDeg,
            Brush fill, Brush stroke, double strokeW, bool glow = false) {
            string d = GetArcPath(_svgCx, _svgCy, rIn, rOut, startDeg, endDeg);
            var path = new System.Windows.Shapes.Path();
            path.Data = Geometry.Parse(d);
            path.Fill = fill; path.Stroke = stroke; path.StrokeThickness = strokeW;
            path.RenderTransform = new TransformGroup {
                Children = { new TranslateTransform(-_svgCx, -_svgCy), new ScaleTransform(_svgScale, _svgScale), new TranslateTransform(_canvasCx, _canvasCy) }
            };
            if (glow) path.Effect = new DropShadowEffect { Color = Color.FromRgb(210,166,94), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4 };
            canvasRadial.Children.Add(path);
            return path;
        }

        private void AddSvgLine(double x1, double y1, double x2, double y2, Brush stroke, double strokeW) {
            var ln = new System.Windows.Shapes.Line {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = strokeW
            };
            ln.IsHitTestVisible = false;
            ln.RenderTransform = new TransformGroup {
                Children = { new TranslateTransform(-_svgCx, -_svgCy), new ScaleTransform(_svgScale, _svgScale), new TranslateTransform(_canvasCx, _canvasCy) }
            };
            Panel.SetZIndex(ln, 5);
            canvasRadial.Children.Add(ln);
        }

        private void AddSvgText(double svgX, double svgY, string text, double fontSize, Brush fg, bool bold = true) {
            var tb = new TextBlock {
                Text = text, Foreground = fg, FontSize = fontSize * _svgScale,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI"), IsHitTestVisible = false
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, SvgX(svgX) - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, SvgY(svgY) - tb.DesiredSize.Height / 2);
            canvasRadial.Children.Add(tb);
        }

        private void AddVectorIcon(Canvas container, double cx, double cy, double r, string iconKey, Brush col, double scale = 1.0) {
            if (iconKey == "none" || string.IsNullOrEmpty(iconKey)) return;
            var _savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try {
            var path = new System.Windows.Shapes.Path { Stroke = col, StrokeThickness = 1.8 * scale, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            string d = "";
            double sr = r * scale;
            if (iconKey == "star") {
                path.Fill = col; path.StrokeThickness = 0;
                double outer = sr * 0.98, inner = sr * 0.45;
                d = $"M {cx},{cy - outer} ";
                for (int i = 1; i < 10; ++i) {
                    double ang = -Math.PI / 2.0 + i * Math.PI / 5.0;
                    double rr = (i % 2 == 0) ? outer : inner;
                    d += $"L {cx + rr * Math.Cos(ang)},{cy + rr * Math.Sin(ang)} ";
                }
                d += "Z";
            } else if (iconKey == "megaphone" || iconKey == "speaker") {
                path.StrokeThickness = 1.9 * scale;
                double r1 = sr * 0.38, ox = sr * 0.34;
                d = $"M {cx - ox + r1},{cy} A {r1},{r1} 0 1,1 {cx - ox - r1},{cy} A {r1},{r1} 0 1,1 {cx - ox + r1},{cy} " +
                    $"M {cx + ox + r1},{cy} A {r1},{r1} 0 1,1 {cx + ox - r1},{cy} A {r1},{r1} 0 1,1 {cx + ox + r1},{cy} " +
                    $"M {cx - ox},{cy - r1} L {cx + ox},{cy - r1}";
            } else if (iconKey == "lightning" || iconKey == "bolt") {
                path.StrokeThickness = 2.2 * scale;
                d = $"M {cx},{cy - sr * 0.95} L {cx - sr * 0.34},{cy - sr * 0.02} L {cx + sr * 0.22},{cy - sr * 0.02} L {cx - sr * 0.08},{cy + sr * 0.94}";
            } else if (iconKey == "document" || iconKey == "doc") {
                path.StrokeThickness = 2.0 * scale;
                double w = sr * 0.5, h = sr * 0.68;
                d = $"M {cx - w},{cy - h} L {cx + w},{cy - h} L {cx + w},{cy + h} L {cx - w},{cy + h} Z ";
                var p2 = new System.Windows.Shapes.Path { Stroke = col, StrokeThickness = 1.3 * scale, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                p2.Data = Geometry.Parse($"M {cx - sr * 0.28},{cy - sr * 0.3} L {cx + sr * 0.28},{cy - sr * 0.3} M {cx - sr * 0.28},{cy} L {cx + sr * 0.28},{cy} M {cx - sr * 0.28},{cy + sr * 0.3} L {cx + sr * 0.08},{cy + sr * 0.3}".Replace(',', ' '));
                container.Children.Add(p2);
            } else if (iconKey == "car") {
                path.StrokeThickness = 3.0 * scale;
                d = $"M {cx - sr * 0.68},{cy - sr * 0.26} L {cx + sr * 0.68},{cy - sr * 0.26} L {cx + sr * 0.68},{cy + sr * 0.2} L {cx - sr * 0.68},{cy + sr * 0.2} Z";
                var wheels = new System.Windows.Shapes.Path { Fill = col, Data = Geometry.Parse(
                    $"M {cx - sr * 0.38 + sr * 0.18},{cy + sr * 0.34} A {sr*0.18},{sr*0.18} 0 1,1 {cx - sr * 0.38 - sr * 0.18},{cy + sr * 0.34} A {sr*0.18},{sr*0.18} 0 1,1 {cx - sr * 0.38 + sr * 0.18},{cy + sr * 0.34} " +
                    $"M {cx + sr * 0.38 + sr * 0.18},{cy + sr * 0.34} A {sr*0.18},{sr*0.18} 0 1,1 {cx + sr * 0.38 - sr * 0.18},{cy + sr * 0.34} A {sr*0.18},{sr*0.18} 0 1,1 {cx + sr * 0.38 + sr * 0.18},{cy + sr * 0.34} ".Replace(',', ' '))};
                container.Children.Add(wheels);
            } else if (iconKey == "badge" || iconKey == "id") {
                path.StrokeThickness = 2.0 * scale;
                double w = sr * 0.4, h = sr * 0.58;
                d = $"M {cx - w},{cy - h} L {cx + w},{cy - h} L {cx + w},{cy + h} L {cx - w},{cy + h} Z ";
                var p2 = new System.Windows.Shapes.Path { Stroke = col, StrokeThickness = 1.3 * scale, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                p2.Data = Geometry.Parse($"M {cx - sr * 0.24},{cy - sr * 0.3} L {cx + sr * 0.24},{cy - sr * 0.3} M {cx - sr * 0.24},{cy} L {cx + sr * 0.24},{cy} M {cx - sr * 0.24},{cy + sr * 0.3} L {cx + sr * 0.24},{cy + sr * 0.3}".Replace(',', ' '));
                container.Children.Add(p2);
            } else if (iconKey == "radio") {
                path.StrokeThickness = 2.0 * scale;
                double w = sr * 0.4, h1 = sr * 0.3, h2 = sr * 0.6;
                d = $"M {cx - w},{cy - h1} L {cx + w},{cy - h1} L {cx + w},{cy + h2} L {cx - w},{cy + h2} Z ";
                var p2 = new System.Windows.Shapes.Path { Stroke = col, StrokeThickness = 1.8 * scale, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                p2.Data = Geometry.Parse($"M {cx},{cy - sr * 0.3} L {cx + sr * 0.3},{cy - sr * 0.8}".Replace(',', ' '));
                container.Children.Add(p2);
                var dot = new System.Windows.Shapes.Path { Fill = col, Data = Geometry.Parse($"M {cx + sr * 0.3 + 1.8*scale},{cy - sr * 0.8} A {1.8*scale},{1.8*scale} 0 1,1 {cx + sr * 0.3 - 1.8*scale},{cy - sr * 0.8} A {1.8*scale},{1.8*scale} 0 1,1 {cx + sr * 0.3 + 1.8*scale},{cy - sr * 0.8}".Replace(',', ' '))};
                container.Children.Add(dot);
            } else {
                path.Fill = col; path.StrokeThickness = 0;
                d = $"M {cx + sr * 0.4},{cy} A {sr*0.4},{sr*0.4} 0 1,1 {cx - sr*0.4},{cy} A {sr*0.4},{sr*0.4} 0 1,1 {cx + sr*0.4},{cy}";
            }
            if (!string.IsNullOrEmpty(d)) path.Data = Geometry.Parse(d.Replace(',', ' '));
            container.Children.Add(path);
            } finally {
                System.Threading.Thread.CurrentThread.CurrentCulture = _savedCulture;
            }
        }

        private void DrawRadialCircle(RadialMenuConfig cfg) {
            canvasRadial.Children.Clear();
            bool isGrouped = (cfg.Mode == "Grouped");

            var goldFill    = new SolidColorBrush(Color.FromArgb(66, 210, 166, 94));   // rgba(210,166,94,0.26)
            var goldStroke  = new SolidColorBrush(Color.FromArgb(230, 210, 166, 94));  // rgba(210,166,94,0.9)
            var goldLine    = new SolidColorBrush(Color.FromArgb(245, 210, 166, 94));  // rgba(210,166,94,0.96)

            // Derive sector colors from current theme
            var bgColor = ((SolidColorBrush)Application.Current.Resources["BgBrush"]).Color;
            var lineColor = ((SolidColorBrush)Application.Current.Resources["LineBrush"]).Color;

            var darkFill       = new SolidColorBrush(Color.FromArgb(222, bgColor.R, bgColor.G, bgColor.B));
            var darkStroke     = new SolidColorBrush(Color.FromArgb(209, lineColor.R, lineColor.G, lineColor.B));
            var bindDarkFill   = new SolidColorBrush(Color.FromArgb(234, bgColor.R, bgColor.G, bgColor.B));
            var bindDarkStroke = new SolidColorBrush(Color.FromArgb(178, lineColor.R, lineColor.G, lineColor.B));
            var bindGoldFill   = new SolidColorBrush(Color.FromArgb(51, 210, 166, 94));  // rgba(210,166,94,0.2)
            var bindGoldStroke = new SolidColorBrush(Color.FromArgb(217, 210, 166, 94)); // rgba(210,166,94,0.85)
            var whiteTxt    = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
            var goldTxt     = new SolidColorBrush(Color.FromRgb(210, 166, 94));

            if (isGrouped) {
                // === INNER RING: 4 groups, each 90°, starting at 315° ===
                int nGroups = cfg.GroupCount;
                double grpStep = 360.0 / nGroups;
                double grpStart = 360.0 - (grpStep / 2.0); // 0=top in WPF (GetArcPath subtracts 90)

                for (int i = 0; i < nGroups; i++) {
                    double a1 = grpStart + i * grpStep;
                    double a2 = a1 + grpStep;
                    bool sel = (i == _radialSelectedGroupIndex);

                    var p = AddArcSector(42, 150, a1, a2,
                        sel ? goldFill : darkFill, sel ? goldStroke : darkStroke,
                        2.2, sel);
                    p.Cursor = Cursors.Hand;
                    int idx = i;
                    p.MouseDown += (s, ev) => {
                        _radialSelectedGroupIndex = idx; _radialSelectedSectorIndex = -1;
                        DrawRadialCircle(GetRadialConfig()); LoadRadialSectorSettings(GetRadialConfig()); PopulateRadialBindCombo();
                    };

                    // Group name text
                    double midDeg = a1 + grpStep / 2.0;
                    var (tx, ty) = SvgPolar(midDeg, 96); // midpoint of 42..150
                    string gName = (i < cfg.Groups.Count) ? cfg.Groups[i].Name : $"ГР {i+1}";
                    var tb = new TextBlock {
                        Text = gName, Foreground = sel ? Brushes.White : whiteTxt,
                        FontSize = (gName.Length > 8 ? 13 : 14) * _svgScale, FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Segoe UI"), IsHitTestVisible = false
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(tb, SvgX(tx) - tb.DesiredSize.Width / 2);
                    Canvas.SetTop(tb, SvgY(ty) - tb.DesiredSize.Height / 2);
                    tb.Cursor = Cursors.Hand;
                    tb.MouseDown += (s, ev) => {
                        _radialSelectedGroupIndex = idx; _radialSelectedSectorIndex = -1;
                        DrawRadialCircle(GetRadialConfig()); LoadRadialSectorSettings(GetRadialConfig()); PopulateRadialBindCombo();
                    };
                    canvasRadial.Children.Add(tb);

                    // Arrow indicators on active group
                    if (sel) {
                        var (arrowX, arrowY) = SvgPolar(midDeg, 136);
                        var groupTr = new TransformGroup();
                        groupTr.Children.Add(new RotateTransform(midDeg));
                        groupTr.Children.Add(new TranslateTransform(SvgX(arrowX), SvgY(arrowY)));

                        var arrow1 = new System.Windows.Shapes.Polygon();
                        arrow1.Points.Add(new Point(-5, 3.5));
                        arrow1.Points.Add(new Point(0, -3.5));
                        arrow1.Points.Add(new Point(5, 3.5));
                        arrow1.Fill = new SolidColorBrush(Color.FromArgb(153, 210, 166, 94));
                        arrow1.IsHitTestVisible = false;
                        arrow1.RenderTransform = groupTr;
                        canvasRadial.Children.Add(arrow1);
                        
                        var arrow2 = new System.Windows.Shapes.Polygon();
                        arrow2.Points.Add(new Point(-5, 8.5));
                        arrow2.Points.Add(new Point(0, 1.5));
                        arrow2.Points.Add(new Point(5, 8.5));
                        arrow2.Fill = new SolidColorBrush(Color.FromArgb(77, 210, 166, 94));
                        arrow2.IsHitTestVisible = false;
                        arrow2.RenderTransform = groupTr;
                        canvasRadial.Children.Add(arrow2);
                    }
                }
                
                if (_radialSelectedGroupIndex >= 0 && _radialSelectedGroupIndex < nGroups) {
                    double selA1 = grpStart + _radialSelectedGroupIndex * grpStep;
                    double selA2 = selA1 + grpStep;
                    var (ix1, iy1) = SvgPolar(selA1, 42); var (ox1, oy1) = SvgPolar(selA1, 150);
                    var (ix2, iy2) = SvgPolar(selA2, 42); var (ox2, oy2) = SvgPolar(selA2, 150);
                    AddSvgLine(ix1, iy1, ox1, oy1, goldStroke, 2.5);
                    AddSvgLine(ix2, iy2, ox2, oy2, goldStroke, 2.5);
                }



                // === OUTER RING: bind sectors, each 60°, fan centered on active group ===
                int _outerRingStartIdx = canvasRadial.Children.Count;
                int nBinds = cfg.Groups[_radialSelectedGroupIndex].SectorCount;
                var activeSectors = cfg.Groups[_radialSelectedGroupIndex].Sectors;
                double activeGroupMid = grpStart + _radialSelectedGroupIndex * grpStep + grpStep / 2.0;
                double fanTotal = nBinds * 60.0;
                double fanStart = activeGroupMid - fanTotal / 2.0;

                for (int i = 0; i < nBinds; i++) {
                    double a1 = fanStart + i * 60.0;
                    double a2 = a1 + 60.0;
                    bool sel = (_radialSelectedSectorIndex == i);

                    var p = AddArcSector(162, 255, a1, a2,
                        sel ? bindGoldFill : bindDarkFill, sel ? bindGoldStroke : bindDarkStroke,
                        sel ? 2.2 : 1.8, sel);
                    p.Cursor = Cursors.Hand;
                    int idx = i;
                    p.MouseDown += (s, ev) => {
                        _radialSelectedSectorIndex = idx;
                        DrawRadialCircle(GetRadialConfig()); LoadRadialSectorSettings(GetRadialConfig()); PopulateRadialBindCombo();
                    };

                    if (sel) {
                        var (ix1, iy1) = SvgPolar(a1, 162); var (ox1, oy1) = SvgPolar(a1, 255);
                        var (ix2, iy2) = SvgPolar(a2, 162); var (ox2, oy2) = SvgPolar(a2, 255);
                        AddSvgLine(ix1, iy1, ox1, oy1, new SolidColorBrush(Color.FromArgb(230, 210, 166, 94)), 2.5);
                        AddSvgLine(ix2, iy2, ox2, oy2, new SolidColorBrush(Color.FromArgb(230, 210, 166, 94)), 2.5);
                    }

                    // Bind name text
                    double midA = a1 + 30;
                    var (btx, bty) = SvgPolar(midA, 208);
                    string bname = (i < activeSectors.Count) ? activeSectors[i].BindName : "";
                    if (string.IsNullOrEmpty(bname)) bname = $"Бинд {i + 1}";
                    if (bname.Length > 14) bname = bname.Substring(0, 12) + "..";
                    AddSvgText(btx, bty - 12, bname, 13, sel ? Brushes.White : whiteTxt);

                    // Bind icon symbol (below name)
                    if (i < activeSectors.Count && !string.IsNullOrEmpty(activeSectors[i].Icon) && activeSectors[i].Icon != "none") {
                        int iconIdx = Array.IndexOf(_radialIconKeys, activeSectors[i].Icon);
                        if (iconIdx >= 0) {
                            var iconBrush = new SolidColorBrush(Color.FromArgb(178, 210, 166, 94));
                            AddVectorIcon(canvasRadial, SvgX(btx), SvgY(bty + 16), SvgR(11), activeSectors[i].Icon, iconBrush, 1.0);
                        }
                    }
                }

                // Outer arc (decorative, connects bind fan ends)
                if (nBinds < 6) {
                    double arcStart = fanStart;
                    double arcEnd = fanStart + fanTotal;
                    string arcD = "";
                    var (as1x, as1y) = SvgPolar(arcStart, 255);
                    var (as2x, as2y) = SvgPolar(arcEnd, 255);
                    int la = (fanTotal > 180) ? 1 : 0;
                    var ic = System.Globalization.CultureInfo.InvariantCulture;
                    arcD = $"M {as1x.ToString(ic)},{as1y.ToString(ic)} A 255,255 0 {la} 1 {as2x.ToString(ic)},{as2y.ToString(ic)}";
                    var arcPath = new System.Windows.Shapes.Path();
                    arcPath.Data = Geometry.Parse(arcD);
                    arcPath.Stroke = new SolidColorBrush(Color.FromArgb(153, lineColor.R, lineColor.G, lineColor.B));
                    arcPath.StrokeThickness = 2; arcPath.Fill = Brushes.Transparent; arcPath.IsHitTestVisible = false;
                    arcPath.RenderTransform = new TransformGroup {
                        Children = { new TranslateTransform(-_svgCx, -_svgCy), new ScaleTransform(_svgScale, _svgScale), new TranslateTransform(_canvasCx, _canvasCy) }
                    };
                    canvasRadial.Children.Add(arcPath);

                    // "+" buttons at fan edges
                    var (pb1x, pb1y) = SvgPolar(arcStart, 208.5);
                    var (pb2x, pb2y) = SvgPolar(arcEnd, 208.5);
                    Action addSector = () => {
                        var cfg2 = GetRadialConfig();
                        if (cfg2.Groups[_radialSelectedGroupIndex].SectorCount < 6) {
                            cfg2.Groups[_radialSelectedGroupIndex].SectorCount++;
                            EnsureSectors(cfg2);
                            DrawRadialCircle(cfg2);
                            LoadRadialSectorSettings(cfg2);
                            QueueRadialSave();
                        }
                    };
                    DrawPlusButton(pb1x, pb1y, addSector);
                    DrawPlusButton(pb2x, pb2y, addSector);
                }

            // Apply delayed fade-in to ALL outer ring elements at once
            if (_radialAnimateOuterSectors) {
                for (int ci = _outerRingStartIdx; ci < canvasRadial.Children.Count; ci++) {
                    var el = canvasRadial.Children[ci];
                    el.Opacity = 0;
                    el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                }
            }
            _radialAnimateOuterSectors = false;
            } else {
                // === STANDARD MODE: single ring ===
                int nSectors = cfg.SectorCount;
                double step = 360.0 / nSectors;
                double stdStart = 360.0 - (step / 2.0); // 0=top in WPF

                for (int i = 0; i < nSectors; i++) {
                    double a1 = stdStart + i * step;
                    double a2 = a1 + step;
                    bool sel = (i == _radialSelectedSectorIndex);

                    var p = AddArcSector(42, 190, a1, a2,
                        sel ? goldFill : darkFill, sel ? goldStroke : darkStroke,
                        2.2, sel);
                    p.Cursor = Cursors.Hand;
                    int idx = i;
                    p.MouseDown += (s, ev) => {
                        _radialSelectedSectorIndex = idx;
                        DrawRadialCircle(GetRadialConfig()); LoadRadialSectorSettings(GetRadialConfig()); PopulateRadialBindCombo();
                    };

                    double midA = a1 + step / 2.0;
                    var (tx, ty) = SvgPolar(midA, 116);
                    string label = (i + 1).ToString();
                    bool hasIcon = false;
                    string iconKey = "none";
                    if (i < cfg.Sectors.Count && cfg.Sectors[i].Icon != "none" && cfg.Sectors[i].Icon != null) {
                        int ii = Array.IndexOf(_radialIconKeys, cfg.Sectors[i].Icon);
                        if (ii >= 0) { iconKey = cfg.Sectors[i].Icon; hasIcon = true; }
                    }
                    
                    if (i < cfg.Sectors.Count && !string.IsNullOrEmpty(cfg.Sectors[i].BindId)) {
                        string disp = cfg.Sectors[i].BindName ?? "";
                        if (disp.Length > 12) disp = disp.Substring(0, 10) + "..";
                        AddSvgText(tx, ty - 10, disp, 11, new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)));
                        if (hasIcon) {
                            var iconBrush = new SolidColorBrush(Color.FromArgb(178, 210, 166, 94));
                            AddVectorIcon(canvasRadial, SvgX(tx), SvgY(ty + 14), SvgR(9.5), iconKey, iconBrush, 1.0);
                        } else {
                            AddSvgText(tx, ty + 10, label, sel ? 15 : 14, sel ? goldTxt : whiteTxt);
                        }
                    } else {
                        if (hasIcon) {
                            var iconBrush = new SolidColorBrush(Color.FromArgb(178, 210, 166, 94));
                            AddVectorIcon(canvasRadial, SvgX(tx), SvgY(ty), SvgR(9.5), iconKey, iconBrush, 1.0);
                        } else {
                            AddSvgText(tx, ty, label, sel ? 15 : 14, sel ? goldTxt : whiteTxt);
                        }
                    }
                }
                
                if (_radialSelectedSectorIndex >= 0 && _radialSelectedSectorIndex < nSectors) {
                    double selA1 = stdStart + _radialSelectedSectorIndex * step;
                    double selA2 = selA1 + step;
                    var (ix1, iy1) = SvgPolar(selA1, 42); var (ox1, oy1) = SvgPolar(selA1, 190);
                    var (ix2, iy2) = SvgPolar(selA2, 42); var (ox2, oy2) = SvgPolar(selA2, 190);
                    AddSvgLine(ix1, iy1, ox1, oy1, goldStroke, 2.5);
                    AddSvgLine(ix2, iy2, ox2, oy2, goldStroke, 2.5);
                }
            }

            // === DECORATIVE RINGS ===
            double outR = isGrouped ? 150 : 190;
            AddEllipse(250, 250, outR,   Brushes.Transparent, new SolidColorBrush(Color.FromArgb(214, lineColor.R, lineColor.G, lineColor.B)), 2);
            AddEllipse(250, 250, outR+1.5, Brushes.Transparent, new SolidColorBrush(Color.FromArgb(74, 210, 166, 94)), 2.2);
            if (!isGrouped) {
                AddEllipse(250, 250, outR+4, Brushes.Transparent, new SolidColorBrush(Color.FromArgb(23, 210, 166, 94)), 3);
            }
            // === CENTER HUB ===
            AddEllipse(250, 250, 42, new SolidColorBrush(Color.FromArgb(248, bgColor.R, bgColor.G, bgColor.B)), null, 0);
            var deepBg = ((SolidColorBrush)Application.Current.Resources["DeepBgBrush"]).Color;
            AddEllipse(250, 250, 36, new SolidColorBrush(Color.FromArgb(170, deepBg.R, deepBg.G, deepBg.B)), null, 0);
            AddEllipse(250, 250, 42, Brushes.Transparent, new SolidColorBrush(Color.FromArgb(140, 210, 166, 94)), 2.0);
            AddEllipse(250, 250, 38, Brushes.Transparent, new SolidColorBrush(Color.FromArgb(80, 210, 166, 94)), 1.0);
            AddEllipse(250, 250, 16, new SolidColorBrush(Color.FromArgb(24, 210, 166, 94)), null, 0);

            // Letter "D"
            var dText = new TextBlock {
                Text = "D", Foreground = goldTxt, FontSize = 24 * _svgScale,
                FontFamily = new FontFamily("Arial Black"), FontWeight = FontWeights.Black, IsHitTestVisible = false
            };
            dText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(dText, SvgX(250) - dText.DesiredSize.Width / 2 + 0.5);
            Canvas.SetTop(dText, SvgY(250) - dText.DesiredSize.Height / 2);
            canvasRadial.Children.Add(dText);

            // === UPDATE HEADER ===
            if (isGrouped) {
                int nBinds = cfg.Groups[_radialSelectedGroupIndex].SectorCount;
                pnlRadialSelectedGroup.Visibility = Visibility.Visible;
                if (_radialSelectedSectorIndex == -1) {
                    lblRadialActiveGroupTitle.Text = "ВЫБРАННАЯ ГРУППА";
                    lblRadialActiveGroupName.Text = $"{cfg.Groups[_radialSelectedGroupIndex].Name}  ({nBinds} биндов)";
                } else {
                    lblRadialActiveGroupTitle.Text = "ВЫБРАННЫЙ БИНД";
                    var activeSectors2 = cfg.Groups[_radialSelectedGroupIndex].Sectors;
                    string bn = (_radialSelectedSectorIndex < activeSectors2.Count) ? activeSectors2[_radialSelectedSectorIndex].BindName : "";
                    lblRadialActiveGroupName.Text = string.IsNullOrEmpty(bn) ? $"Бинд {_radialSelectedSectorIndex + 1}" : bn;
                }
                lblRadialCanvasHint.Text = $"{nBinds} биндов в группе • кликните для настройки";
            } else {
                pnlRadialSelectedGroup.Visibility = Visibility.Visible;
                lblRadialActiveGroupTitle.Text = "ВЫБРАННЫЙ СЕКТОР";
                int selIdx = Math.Max(0, _radialSelectedSectorIndex);
                string[] dirs = { "Вверх", "Вверх-Вправо", "Вправо", "Вниз-Вправо", "Вниз", "Вниз-Влево", "Влево", "Вверх-Влево" };
                string dir = (selIdx < dirs.Length && cfg.SectorCount == 8) ? dirs[selIdx] : "";
                string sn = (selIdx < cfg.Sectors.Count && !string.IsNullOrEmpty(cfg.Sectors[selIdx].BindName)) ? cfg.Sectors[selIdx].BindName : "";
                lblRadialActiveGroupName.Text = string.IsNullOrEmpty(sn) ? $"СЕКТОР {selIdx + 1} — {dir}" : sn;
                lblRadialCanvasHint.Text = "Нажмите на сектор для настройки бинда";
            }
        }

        private void DrawPlusButton(double svgX, double svgY, Action onClick) {
            double cx = SvgX(svgX), cy = SvgY(svgY);
            double r = SvgR(14);
            var circle = new System.Windows.Shapes.Ellipse {
                Width = r*2, Height = r*2,
                Fill = new SolidColorBrush(Color.FromRgb(17, 21, 29)),
                Stroke = new SolidColorBrush(Color.FromRgb(210, 166, 94)),
                StrokeThickness = 1.5, Cursor = Cursors.Hand
            };
            Canvas.SetLeft(circle, cx - r); Canvas.SetTop(circle, cy - r);
            circle.MouseDown += (s, ev) => onClick?.Invoke();
            canvasRadial.Children.Add(circle);
            double s = SvgR(6);
            var hLine = new System.Windows.Shapes.Line { X1 = cx-s, Y1 = cy, X2 = cx+s, Y2 = cy, Stroke = new SolidColorBrush(Color.FromRgb(210,166,94)), StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
            var vLine = new System.Windows.Shapes.Line { X1 = cx, Y1 = cy-s, X2 = cx, Y2 = cy+s, Stroke = new SolidColorBrush(Color.FromRgb(210,166,94)), StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
            canvasRadial.Children.Add(hLine); canvasRadial.Children.Add(vLine);
        }


        private void PopulateRadialBindCombo() {
            if (cbRadialBind == null || rbRadialInputId == null) return;
            _ignoreEvents = true;
            cbRadialBind.Items.Clear();
            cbRadialBind.Items.Add("(Не назначен)");
            if (_hasProfile && MasterData.ContainsKey(CurrentProfile)) {
                foreach (var kvp in MasterData[CurrentProfile].Binds) {
                    if (kvp.Value.name == "Radial Menu") continue;
                    cbRadialBind.Items.Add(kvp.Value.name ?? kvp.Key);
                }
            }
            var cfg = GetRadialConfig();
            EnsureSectors(cfg);
            var activeSectors = (cfg.Mode == "Grouped") ? cfg.Groups[_radialSelectedGroupIndex].Sectors : cfg.Sectors;
            
            if (_radialSelectedSectorIndex >= 0 && _radialSelectedSectorIndex < activeSectors.Count && !string.IsNullOrEmpty(activeSectors[_radialSelectedSectorIndex].BindName)) {
                string bname = activeSectors[_radialSelectedSectorIndex].BindName;
                for (int i = 0; i < cbRadialBind.Items.Count; i++) {
                    if (cbRadialBind.Items[i].ToString() == bname) { cbRadialBind.SelectedIndex = i; break; }
                }
                if (cbRadialBind.SelectedIndex < 0) cbRadialBind.SelectedIndex = 0;
            } else {
                cbRadialBind.SelectedIndex = 0;
            }
            _ignoreEvents = false;
        }

        private void LoadRadialSectorSettings(RadialMenuConfig cfg) {
            _ignoreEvents = true;
            bool isGrouped = (cfg.Mode == "Grouped");
            
            if (isGrouped && _radialSelectedSectorIndex == -1) {
                if (pnlRadialGroupSettings != null) pnlRadialGroupSettings.Visibility = Visibility.Visible;
                if (pnlRadialBindSettings != null) pnlRadialBindSettings.Visibility = Visibility.Collapsed;
                if (lblRadialGroupSubtitle != null) lblRadialGroupSubtitle.Visibility = Visibility.Collapsed;
                if (_radialSelectedGroupIndex < cfg.Groups.Count) {
                    var grp = cfg.Groups[_radialSelectedGroupIndex];
                    if (lblRadialGroupCount != null) lblRadialGroupCount.Text = $"{cfg.GroupCount} гр.";
                    if (txtRadialGroupName != null) txtRadialGroupName.Text = grp.Name;
                }
            } else {
                if (pnlRadialGroupSettings != null) pnlRadialGroupSettings.Visibility = Visibility.Collapsed;
                if (pnlRadialBindSettings != null) pnlRadialBindSettings.Visibility = Visibility.Visible;
                if (lblRadialGroupSubtitle != null) lblRadialGroupSubtitle.Visibility = Visibility.Collapsed;
                
                var activeSectors = isGrouped ? cfg.Groups[_radialSelectedGroupIndex].Sectors : cfg.Sectors;
                int nBinds = isGrouped ? cfg.Groups[_radialSelectedGroupIndex].SectorCount : cfg.SectorCount;
                
                if (_radialSelectedSectorIndex >= 0 && _radialSelectedSectorIndex < activeSectors.Count) {
                    var sector = activeSectors[_radialSelectedSectorIndex];
                    // Title: "Бинд N — NAME" format like the SVG mockup
                    string sectorBindName = !string.IsNullOrEmpty(sector.BindName) ? sector.BindName.ToUpper() : $"СЕКТОР {_radialSelectedSectorIndex + 1}";
                    if (lblRadialSectorTitle != null) lblRadialSectorTitle.Text = $"Бинд {_radialSelectedSectorIndex + 1} — {sectorBindName}";
                    if (lblRadialBindCount != null) lblRadialBindCount.Text = $"{nBinds} шт.";
                    
                    // Show group subtitle in grouped mode
                    if (isGrouped && lblRadialGroupSubtitle != null && _radialSelectedGroupIndex < cfg.Groups.Count) {
                        lblRadialGroupSubtitle.Text = $"Гр: {cfg.Groups[_radialSelectedGroupIndex].Name.ToUpper()}";
                        lblRadialGroupSubtitle.Visibility = Visibility.Visible;
                    }
                    
                    if (rbRadialDirect != null) rbRadialDirect.IsChecked = !sector.RequiresId;
                    if (rbRadialInputId != null) rbRadialInputId.IsChecked = sector.RequiresId;
                    _radialSelectedIcon = sector.Icon ?? "none";
                }
            }
            _ignoreEvents = false;
            UpdateIconPickerSelection();
        }

        private void BuildRadialIconPicker() {
            if (pnlRadialIcons == null) return;
            pnlRadialIcons.Children.Clear();
            for (int i = 0; i < _radialIconKeys.Length; i++) {
                string key = _radialIconKeys[i];
                bool selected = (key == _radialSelectedIcon);
                bool isLast = (i == _radialIconKeys.Length - 1);
                var border = new Border {
                    Width = isLast ? 34 : 36, Height = 36, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, isLast ? 0 : 6, 0),
                    Background = (Brush)FindResource("DeepBgBrush"), Cursor = System.Windows.Input.Cursors.Hand,
                    BorderBrush = selected ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("LineBrush"),
                    BorderThickness = new Thickness(selected ? 1.5 : 1)
                };
                if (key == "none") {
                    var tb = new TextBlock { Text = "∅", FontSize = 17, FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    tb.Foreground = selected ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("GrayBrush");
                    border.Child = tb;
                } else {
                    var canvas = new Canvas { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    AddVectorIcon(canvas, 10, 10, 10, key, selected ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("GrayBrush"), 1.0);
                    border.Child = canvas;
                }
                border.Tag = key;
                border.MouseDown += (s, ev) => {
                    _radialSelectedIcon = (string)((Border)s).Tag;
                    UpdateIconPickerSelection();
                    if (!_ignoreEvents) RadialSave_Click(null, null);
                };
                pnlRadialIcons.Children.Add(border);
            }
        }

        private void UpdateIconPickerSelection() {
            if (pnlRadialIcons == null) return;
            foreach (var child in pnlRadialIcons.Children) {
                if (child is Border b) {
                    bool sel = (string)b.Tag == _radialSelectedIcon;
                    b.BorderBrush = sel ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("LineBrush");
                    b.BorderThickness = new Thickness(sel ? 1.5 : 1);
                    if (b.Child is TextBlock tb) {
                        tb.Foreground = sel ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("GrayBrush");
                    } else if (b.Child is Canvas cv) {
                        var c = sel ? (Brush)FindResource("GoldBrush") : (Brush)FindResource("GrayBrush");
                        foreach (var path in cv.Children.OfType<System.Windows.Shapes.Path>()) {
                            if (path.Stroke != null && path.StrokeThickness > 0) path.Stroke = c;
                            if (path.Fill != null) path.Fill = c;
                        }
                    }
                }
            }
        }

        private void RadialGroupName_Changed(object sender, TextChangedEventArgs e) {
            if (_ignoreEvents) return;
            var cfg = GetRadialConfig();
            if (cfg.Mode == "Grouped" && _radialSelectedGroupIndex < cfg.Groups.Count && txtRadialGroupName != null) {
                cfg.Groups[_radialSelectedGroupIndex].Name = txtRadialGroupName.Text;
                DrawRadialCircle(cfg);
                QueueRadialSave();
            }
        }

        private void RadialCount_Minus(object sender, RoutedEventArgs e) {
            var cfg = GetRadialConfig();
            if (cfg.Mode == "Grouped" && _radialSelectedSectorIndex == -1) {
                if (cfg.GroupCount > 2) {
                    cfg.GroupCount--;
                    _radialSelectedGroupIndex = Math.Min(_radialSelectedGroupIndex, cfg.GroupCount - 1);
                    LoadRadialTab();
                    QueueRadialSave();
                }
            } else {
                if (cfg.Mode == "Grouped") {
                    if (cfg.Groups[_radialSelectedGroupIndex].SectorCount > 2) {
                        cfg.Groups[_radialSelectedGroupIndex].SectorCount--;
                        _radialSelectedSectorIndex = Math.Min(_radialSelectedSectorIndex, cfg.Groups[_radialSelectedGroupIndex].SectorCount - 1);
                        LoadRadialTab();
                        QueueRadialSave();
                    }
                } else {
                    if (cfg.SectorCount > 2) {
                        cfg.SectorCount--;
                        _radialSelectedSectorIndex = Math.Min(_radialSelectedSectorIndex, cfg.SectorCount - 1);
                        LoadRadialTab();
                        QueueRadialSave();
                    }
                }
            }
        }

        private void RadialCount_Plus(object sender, RoutedEventArgs e) {
            var cfg = GetRadialConfig();
            if (cfg.Mode == "Grouped" && _radialSelectedSectorIndex == -1) {
                if (cfg.GroupCount < 8) {
                    cfg.GroupCount++;
                    LoadRadialTab();
                    QueueRadialSave();
                }
            } else {
                if (cfg.Mode == "Grouped") {
                    if (cfg.Groups[_radialSelectedGroupIndex].SectorCount < 6) {
                        cfg.Groups[_radialSelectedGroupIndex].SectorCount++;
                        LoadRadialTab();
                        QueueRadialSave();
                    }
                } else {
                    if (cfg.SectorCount < 8) {
                        cfg.SectorCount++;
                        LoadRadialTab();
                        QueueRadialSave();
                    }
                }
            }
        }

        private void RadialEnable_Changed(object sender, RoutedEventArgs e) {
            if (_ignoreEvents || !_isLoaded) return;
            bool enabled = chkSysRadial?.IsChecked == true;
            var cfg = GetRadialConfig();
            cfg.Enabled = enabled;
            if (radialDisabledOverlay != null) radialDisabledOverlay.Visibility = cfg.Enabled ? Visibility.Collapsed : Visibility.Visible;
            SaveData();
        }

        private void RadialBind_Changed(object sender, SelectionChangedEventArgs e) {
            if (_ignoreEvents || cbRadialBind == null || cbRadialBind.SelectedIndex < 0) return;
            RadialSave_Click(null, null);
        }

        private void RadialAction_Changed(object sender, RoutedEventArgs e) {
            if (_ignoreEvents || !_isLoaded) return;
            PopulateRadialBindCombo();
            RadialSave_Click(null, null);
        }

        private void RadialClear_Click(object sender, RoutedEventArgs e) {
            var cfg = GetRadialConfig();
            EnsureSectors(cfg);
            if (cfg.Mode == "Grouped" && _radialSelectedSectorIndex == -1) {
                var grp = cfg.Groups[_radialSelectedGroupIndex];
                grp.Name = $"ГРУППА {_radialSelectedGroupIndex + 1}";
                if (txtRadialGroupName != null) txtRadialGroupName.Text = grp.Name;
                grp.SectorCount = 2; grp.Sectors = new List<RadialMenuSector>(); for (int i = 0; i < 2; i++) grp.Sectors.Add(new RadialMenuSector { Icon = "none" });
            } else {
                var activeSectors = (cfg.Mode == "Grouped") ? cfg.Groups[_radialSelectedGroupIndex].Sectors : cfg.Sectors;
                if (_radialSelectedSectorIndex >= 0 && _radialSelectedSectorIndex < activeSectors.Count) {
                    activeSectors[_radialSelectedSectorIndex] = new RadialMenuSector { Icon = "none" };
                }
            }
            DrawRadialCircle(cfg);
            LoadRadialSectorSettings(cfg);
            PopulateRadialBindCombo();
            QueueRadialSave();
        }

        private void RadialSave_Click(object sender, RoutedEventArgs e) {
            var cfg = GetRadialConfig();
            EnsureSectors(cfg);
            
            if (cfg.Mode == "Grouped" && _radialSelectedSectorIndex == -1) return; 

            var activeSectors = (cfg.Mode == "Grouped") ? cfg.Groups[_radialSelectedGroupIndex].Sectors : cfg.Sectors;
            if (_radialSelectedSectorIndex < 0 || _radialSelectedSectorIndex >= activeSectors.Count) return;

            var sector = activeSectors[_radialSelectedSectorIndex];
            sector.Icon = _radialSelectedIcon;
            sector.RequiresId = (rbRadialInputId?.IsChecked == true);
            if (cbRadialBind != null && cbRadialBind.SelectedIndex > 0) {
                string bindName = cbRadialBind.SelectedItem.ToString();
                sector.BindName = bindName;
                if (MasterData.ContainsKey(CurrentProfile)) {
                    foreach (var kvp in MasterData[CurrentProfile].Binds) {
                        if (kvp.Value.name == bindName) { sector.BindId = kvp.Key; break; }
                    }
                }
            } else {
                sector.BindId = "";
                sector.BindName = "";
            }

            DrawRadialCircle(cfg);
            QueueRadialSave();
        }

        private void QueueRadialSave() {
            if (_radialSaveTimer == null) {
                _radialSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _radialSaveTimer.Tick += (s, ev) => { _radialSaveTimer.Stop(); SaveData(); };
            }
            _radialSaveTimer.Stop();
            _radialSaveTimer.Start();
        }
    }

    public class DragAdorner : Adorner 
    {
        private VisualBrush _brush; private Point _offset;
        public DragAdorner(UIElement adornedElement, Point offset) : base(adornedElement) { _brush = new VisualBrush(adornedElement) { Opacity = 0.8 }; _offset = offset; this.IsHitTestVisible = false; }
        public void UpdatePosition(Point p) { this.RenderTransform = new TranslateTransform(p.X - _offset.X, p.Y - _offset.Y); }
        protected override void OnRender(DrawingContext drawingContext) { drawingContext.DrawRectangle(_brush, null, new Rect(0, 0, this.AdornedElement.RenderSize.Width, this.AdornedElement.RenderSize.Height)); }
    }
}



