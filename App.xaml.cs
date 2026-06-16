using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Media;

namespace FSB_helper_C__;


public partial class App : Application
{
    public static bool IsDebugMode = false;
    private static Mutex _mutex;
    public static Dictionary<string, Dictionary<string, string>> UserCustomThemes = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Pre-JIT animations for toggle switches to prevent lag on first click
        try {
            _ = new System.Windows.Media.Animation.DoubleAnimation();
            _ = new System.Windows.Media.Animation.ThicknessAnimation();
            _ = new System.Windows.Media.Animation.QuadraticEase();
            _ = new System.Windows.Media.Animation.Storyboard();
        } catch { }

        // Load and apply launcher theme on startup
        string theme = "Default (Dark Blue)";
        string logDetails = "";
        try
        {
            string docPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DURAN HELPER");
            string[] paths = {
                System.IO.Path.Combine(Environment.CurrentDirectory, "Settings.json"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.json"),
                System.IO.Path.Combine(docPath, "Settings.json")
            };
            logDetails += $"BaseDir: {AppDomain.CurrentDomain.BaseDirectory}\nWorkingDir: {Environment.CurrentDirectory}\nDocDir: {docPath}\n";
            foreach (var p in paths)
            {
                bool exists = System.IO.File.Exists(p);
                logDetails += $"Path: {p}, Exists: {exists}\n";
                if (exists)
                {
                    string content = System.IO.File.ReadAllText(p);
                    logDetails += $"Content size: {content.Length}\n";
                    var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                    if (settings != null)
                    {
                        if (settings.ContainsKey("CustomThemesText") && settings["CustomThemesText"] != null) {
                            try {
                                var customThemes = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(settings["CustomThemesText"].ToString());
                                if (customThemes != null) {
                                    foreach (var kv in customThemes) {
                                        UserCustomThemes[kv.Key] = kv.Value;
                                    }
                                }
                            } catch { }
                        }
                        if (settings.ContainsKey("ThemeLauncher"))
                        {
                            theme = settings["ThemeLauncher"]?.ToString();
                            logDetails += $"Found ThemeLauncher: {theme}\n";
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logDetails += $"Error loading: {ex.Message}\n{ex.StackTrace}\n";
        }
        try { ApplyTheme(theme); } catch (Exception ex) { logDetails += $"Error applying: {ex.Message}\n"; }
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_debug_startup.txt"), logDetails); } catch { }

        const string mutexName = "DuranHelperSingleInstanceMutex";
        bool createdNew;
        _mutex = new Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            var w = new AlreadyRunningWindow();
            w.ShowDialog();
            Environment.Exit(0);
            return;
        }

        DispatcherUnhandledException += (s, ex) =>
        {
            string fullMsg = $"ОШИБКА:\n{ex.Exception.GetType().Name}\n\n{ex.Exception.Message}";
            var inner = ex.Exception.InnerException;
            while (inner != null) {
                fullMsg += $"\n\n--- Inner: {inner.GetType().Name} ---\n{inner.Message}";
                inner = inner.InnerException;
            }
            fullMsg += $"\n\nStackTrace:\n{ex.Exception.StackTrace}";
            try { System.IO.File.WriteAllText("crash.txt", fullMsg); } catch { }
            System.Windows.MessageBox.Show(fullMsg, "Crash Diagnostic", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show(
                $"ФАТАЛЬНАЯ ОШИБКА:\n{((Exception)ex.ExceptionObject).Message}",
                "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    public static void ApplyTheme(string t)
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
        
        var customThemes = new Dictionary<string, Dictionary<string, string>>();
        ThemesData.Pop(customThemes);
        foreach (var kv in UserCustomThemes) {
            customThemes[kv.Key] = kv.Value;
        }
        
        if (customThemes.ContainsKey(t)) { 
            var cust = customThemes[t]; 
            foreach(var kv in cust) { try { setCol(kv.Key, kv.Value); } catch {} } 
            return; 
        }
        
        if(t.Contains("Black")) { 
            setCol("BgBrush","#000000"); setCol("SideBrush","#121212"); setCol("SidebarBrush","#0a0a0a"); setCol("BoxBrush","#121212"); setCol("InputBrush","#000000"); setCol("DeepBgBrush","#000000");
            setCol("TextBrush", "White"); setCol("LineBrush", "#1f1f1f");
            setCol("GoldBrush", "#b8904f"); setCol("GoldBrushTransparent", "#20b8904f"); setCol("GrayBrush", "#8b949e"); setCol("GreenBrush", "#2ea043"); setCol("RedBrush", "#ff7b72");
            setCol("MenuHoverBrush", "#151515"); setCol("SectionBtnBrush", "#151515"); setCol("HoverBrush", "#1a3d6e"); setCol("BtnHoverBrush", "#0f0f0f");
            setCol("OverlayBrush", "#30000000");
            setCol("GoldBrushTransparent", "#15b8904f");
            setCol("ImportExportBgBrush", "#051024");
        } 
        else if (t.Contains("Grey") || t.Contains("Sport red")) { 
            setCol("BgBrush","#121214"); setCol("SideBrush","#1a1a1d"); setCol("SidebarBrush","#0f0f11"); setCol("BoxBrush","#1a1a1d"); setCol("InputBrush","#0a0a0c"); setCol("DeepBgBrush","#0a0a0c");
            setCol("TextBrush", "White"); setCol("LineBrush", "#2d2d33");
            setCol("GoldBrush", "#a51a1a"); setCol("GoldBrushTransparent", "#20a51a1a"); setCol("GrayBrush", "#7e8590"); setCol("GreenBrush", "#a51a1a"); setCol("RedBrush", "#ff4d4d");
            setCol("MenuHoverBrush", "#232328"); setCol("SectionBtnBrush", "#232328"); setCol("HoverBrush", "#8b1212"); setCol("BtnHoverBrush", "#1f2228");
            setCol("OverlayBrush", "#30000000");
            setCol("GoldBrushTransparent", "#15a51a1a");
            setCol("ImportExportBgBrush", "Transparent");
        } 
        else { 
            setCol("BgBrush","#0d1117"); setCol("SideBrush","#161b22"); setCol("SidebarBrush","#11151d"); setCol("BoxBrush","#161b22"); setCol("InputBrush","#0a0d12"); setCol("DeepBgBrush","#0a0d12");
            setCol("TextBrush", "White"); setCol("LineBrush", "#30363d");
            setCol("GoldBrush", "#d2a65e"); setCol("GoldBrushTransparent", "#20d2a65e"); setCol("GrayBrush", "#8b949e"); setCol("GreenBrush", "#2ea043"); setCol("RedBrush", "#ff7b72");
            setCol("MenuHoverBrush", "#21262d"); setCol("SectionBtnBrush", "#21262d"); setCol("HoverBrush", "#1f6feb"); setCol("BtnHoverBrush", "#1f2937");
            setCol("OverlayBrush", "#30000000");
            setCol("ImportExportBgBrush", "#051024");
        }
    }
}
