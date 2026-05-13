using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Windows;

namespace FSB_helper_C__
{
    public static class LawsImporter
    {
        public static Dictionary<string, List<LawItem>> LoadFromFile(string path)
        {
            try { return JsonConvert.DeserializeObject<Dictionary<string, List<LawItem>>>(File.ReadAllText(path)); }
            catch { MessageBox.Show("Ошибка чтения файла!"); return null; }
        }
    }
}