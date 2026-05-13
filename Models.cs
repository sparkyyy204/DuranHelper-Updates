using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
namespace FSB_helper_C__
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public Brush ColorBrush { get; set; }
    }
    public class LawItem { public string type { get; set; } public string id { get; set; } public string txt { get; set; } public string pun { get; set; } public int col { get; set; } = 0; }
    
    // НОВЫЙ КЛАСС ДЛЯ РАЗДЕЛОВ (ПОДДЕРЖКА БЛОКНОТА И СТОЛБЦОВ)
    public class LawSection {
        public string Type { get; set; } = "2col"; // "1col", "2col", "3col", "text"
        public List<LawItem> Items { get; set; } = new List<LawItem>();
        public string RtfData { get; set; } = ""; // Для Блокнота
        public string Hotkey { get; set; } = ""; // Хоткей для оверлея
        public bool HasPunishments { get; set; } = true;
    }
    
    public class BindStep : System.ComponentModel.INotifyPropertyChanged { 
        public int Index { get; set; } 
        public string action { get; set; } 
        public string value { get; set; } 
        public string desc { get; set; } 
        public string ColorCode { get; set; } 
        private bool _isEnter = true;
        public bool isEnter { 
            get => _isEnter; 
            set { _isEnter = value; OnPropertyChanged("isEnter"); } 
        } 
        
        private bool _isLast = false;
        public bool IsLast { 
            get => _isLast; 
            set { _isLast = value; OnPropertyChanged("IsLast"); } 
        }

        public int CursorOffset { get; set; } = 0;
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
    }
    
    public class BindItem { 
        public string id { get; set; } public string name { get; set; } public string key { get; set; } = ""; public bool active { get; set; } = true; public string group { get; set; } = "ВСЕ"; public List<BindStep> steps { get; set; } = new List<BindStep>(); public bool isAuto { get; set; } = false;
        
        // AHK key → friendly display name
        private static readonly Dictionary<string, string> AhkToDisplay = new(StringComparer.OrdinalIgnoreCase) {
            {"NumpadMult", "Num *"}, {"NumpadDiv", "Num /"}, {"NumpadAdd", "Num +"}, {"NumpadSub", "Num -"},
            {"NumpadDot", "Num ."}, {"NumpadEnter", "Num Enter"},
            {"Numpad0", "Num 0"}, {"Numpad1", "Num 1"}, {"Numpad2", "Num 2"}, {"Numpad3", "Num 3"},
            {"Numpad4", "Num 4"}, {"Numpad5", "Num 5"}, {"Numpad6", "Num 6"}, {"Numpad7", "Num 7"},
            {"Numpad8", "Num 8"}, {"Numpad9", "Num 9"},
            {"XButton1", "Mouse 4"}, {"XButton2", "Mouse 5"}, {"MButton", "Mouse 3"},
            {"Backspace", "Backspace"}, {"PrintScreen", "PrtSc"}, {"AppsKey", "Menu"},
            {"``", "Ё"}, {"\\", "\\"},
        };
        
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayKey { 
            get {
                if (string.IsNullOrEmpty(key)) return "";
                // Handle combo keys like "Ctrl + NumpadAdd"
                string result = key;
                foreach (var kvp in AhkToDisplay) {
                    if (result.Contains(kvp.Key)) result = result.Replace(kvp.Key, kvp.Value);
                }
                return result;
            }
        }
    }
    
    [Serializable]
    public class BindGroupItem { public string Name { get; set; } public int BindsCount { get; set; } }
    
    public class FineArticle {
        public string id { get; set; } = "";
        public string type { get; set; } = "";
        public string name { get; set; } = "";
        public int amount { get; set; } = 0;
        public bool revoke { get; set; } = false;
        public string note { get; set; } = "";
    }

    public class RadialMenuSector {
        public string BindId { get; set; } = "";
        public string BindName { get; set; } = "";
        public string Icon { get; set; } = "none";
        public bool RequiresId { get; set; } = false;
    }

    public class RadialMenuGroup {
        public string Name { get; set; } = "НОВАЯ ГРУППА";
        public int SectorCount { get; set; } = 4;
        public List<RadialMenuSector> Sectors { get; set; } = new List<RadialMenuSector>();
    }

    public class RadialMenuConfig {
        public bool Enabled { get; set; } = true;
        public string Mode { get; set; } = "Standard"; // "Standard" | "Grouped"
        
        // Standard Mode
        public int SectorCount { get; set; } = 4;
        public List<RadialMenuSector> Sectors { get; set; } = new List<RadialMenuSector>();
        
        // Grouped Mode
        public int GroupCount { get; set; } = 4;
        public List<RadialMenuGroup> Groups { get; set; } = new List<RadialMenuGroup>();
    }

    public class ProfileData { 
        public Dictionary<string, LawSection> Laws { get; set; } = new Dictionary<string, LawSection>(); 
        public Dictionary<string, BindItem> Binds { get; set; } = new Dictionary<string, BindItem>(); 
        public List<string> Groups { get; set; } = new List<string>(); 
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>(); 
        public Dictionary<string, Dictionary<string, string>> CustomThemes { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public string Theme { get; set; } = "Default (Dark Blue)";
        public string OverlayTheme { get; set; } = "Default (Dark Blue)";
        public string OverlayText { get; set; } = ""; 
        public List<FineArticle> Fines { get; set; } = new List<FineArticle>();
        public string GamePath { get; set; } = "";
        public List<string> InstalledCloudIds { get; set; } = new List<string>();
        public RadialMenuConfig RadialMenu { get; set; } = new RadialMenuConfig();
    }
    
    public class VarItem { public string Key { get; set; } public string Val { get; set; } }

    // Класс для полного экспорта биндов
    public class ExportBindsData {
        public Dictionary<string, BindItem> Binds { get; set; } = new Dictionary<string, BindItem>();
        public List<string> Groups { get; set; } = new List<string>();
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
        public RadialMenuConfig RadialMenu { get; set; } = null;
    }

    public class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => !(bool)(value ?? false);
        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => !(bool)(value ?? false);
    }

    public class VarNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string s = value as string ?? "";
            if (s.StartsWith("*") && s.EndsWith("*") && s.Length >= 2) return s.Substring(1, s.Length - 2);
            return s;
        }
        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string s = value as string ?? "";
            s = s.Replace("*", "").Trim();
            if (string.IsNullOrEmpty(s)) return "*НОВАЯ*";
            return "*" + s + "*";
        }
    }

    public class ImportItem : System.ComponentModel.INotifyPropertyChanged {
        private bool _isSelected = true;
        public bool IsSelected {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }
        public string DisplayName { get; set; }
        public string OriginalKey { get; set; }
        public object Data { get; set; }
        public string CountText { get; set; }
        public Visibility InfoStringVisibility { get; set; } = Visibility.Collapsed;
        public Visibility KeyConflictVisible => KeyConflict ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NameConflictVisible => NameConflict ? Visibility.Visible : Visibility.Collapsed;
        // Conflict indicators
        public bool HasConflict => KeyConflict || NameConflict;
        public bool KeyConflict { get; set; }
        public bool NameConflict { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
    }

    public class AdvancedActivationItem {
        public string SectionName { get; set; }
        public string HotkeyText { get; set; }
    }

    public class DuranToolStripRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DuranToolStripRenderer() : base(new DuranColorTable()) { }

        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(48, 54, 61), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(48, 54, 61)))
                e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }
    }

    public class DuranColorTable : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(33, 38, 45);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(48, 54, 61);
        public override System.Drawing.Color MenuStripGradientBegin => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color MenuStripGradientEnd => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(13, 17, 23);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(48, 54, 61);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(48, 54, 61);
    }
}