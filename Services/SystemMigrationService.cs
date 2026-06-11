using System;
using System.IO;

namespace FSB_helper_C__.Services
{
    public static class SystemMigrationService
    {
        public static bool MigrateLegacyFilesToDocuments()
        {
            bool isUpgrade = false;
            try {
                string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DURAN HELPER");
                try {
                    if (!Directory.Exists(docPath)) Directory.CreateDirectory(docPath);
                    
                    string[] filesToMigrate = { "Profiles.json", "Settings.json", "laws.json", "fines.json", "calculator.json" };
                    foreach (var f in filesToMigrate) {
                        string src = Path.Combine(Environment.CurrentDirectory, f);
                        string dst = Path.Combine(docPath, f);
                        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                        
                        if (File.Exists(src)) {
                            if (!File.Exists(dst)) {
                                try { File.Move(src, dst); isUpgrade = true; } catch { }
                            } else {
                                try { File.Delete(src); } catch { }
                            }
                        }
                    }
                    Environment.CurrentDirectory = docPath;
                } catch { }
                
                string finalPath = Environment.CurrentDirectory;
                if (!finalPath.EndsWith("\\")) finalPath += "\\";
                
                try {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\DuranHelper")) {
                        if (key != null) key.SetValue("WorkingPath", finalPath, Microsoft.Win32.RegistryValueKind.String);
                    }
                } catch { }
                try {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"Software\DuranHelper")) {
                        if (key != null) key.SetValue("WorkingPath", finalPath, Microsoft.Win32.RegistryValueKind.String);
                    }
                } catch { }
            } catch { }
            
            return isUpgrade;
        }
    }
}
