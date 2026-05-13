using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FSB_helper_C__
{
    public class EditableVarItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string OriginalKey { get; set; } // Used for tracking renames
    }

    public partial class VariableEditorWindow : Window
    {
        private MainWindow _parent;
        private string _activeProfile;
        private List<EditableVarItem> _tempVars = new List<EditableVarItem>();

        public VariableEditorWindow(MainWindow parent, string activeProfile)
        {
            InitializeComponent();
            _parent = parent;
            _activeProfile = activeProfile;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_parent.MasterData.ContainsKey(_activeProfile))
            {
                // load variables excluding the system *ВРЕМЯ* property (which we showcase fixed in UI)
                _tempVars = _parent.MasterData[_activeProfile].Variables
                    .Where(kv => kv.Key != "*ВРЕМЯ*")
                    .Select(kv => new EditableVarItem { Key = kv.Key, Value = kv.Value, OriginalKey = kv.Key })
                    .ToList();
                RefreshList();
            }
        }




        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SaveChanges();
            this.Close();
            // Show duplicate warning after modal closes (so DialogHost is not blocked)
            if (_duplicateWarning != null)
            {
                _parent._dialogHost.ShowInfo("ОШИБКА", _duplicateWarning);
            }
        }

        private void RefreshList()
        {
            icVariablesList.ItemsSource = null;
            icVariablesList.ItemsSource = _tempVars;
        }

        private void AddVariable_Click(object sender, MouseButtonEventArgs e)
        {
            _tempVars.Insert(0, new EditableVarItem { Key = "*НОВАЯ*", Value = "Новое значение", OriginalKey = "" });
            RefreshList();
        }

        private void Vars_Delete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.DataContext is EditableVarItem item)
            {
                _tempVars.Remove(item);
                RefreshList();
            }
        }

        private void VarKey_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb != null && tb.DataContext is EditableVarItem item)
            {
                // Auto-add asterisks if user forgets
                string k = tb.Text.Trim();
                if (!k.StartsWith("*")) k = "*" + k;
                if (!k.EndsWith("*")) k = k + "*";
                
                item.Key = k.ToUpper();
                tb.Text = item.Key; // Update UI immediately
            }
        }

        private void VarValue_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb != null && tb.DataContext is EditableVarItem item)
            {
                item.Value = tb.Text;
            }
        }

        private string _duplicateWarning = null;

        private void SaveChanges()
        {
            _duplicateWarning = null;
            var keys = new HashSet<string>();
            foreach (var v in _tempVars)
            {
                if (string.IsNullOrWhiteSpace(v.Key) || v.Key == "**") continue;
                if (keys.Contains(v.Key))
                {
                    _duplicateWarning = $"У вас обнаружены дубликаты переменной «{v.Key}». Сохранен только первый вариант.";
                    continue;
                }
                keys.Add(v.Key);
            }

            if (_parent.MasterData.ContainsKey(_activeProfile))
            {
                // retain the built-in time
                string timeVal = "Часы:Минуты:Секунды";
                if (_parent.MasterData[_activeProfile].Variables.ContainsKey("*ВРЕМЯ*"))
                    timeVal = _parent.MasterData[_activeProfile].Variables["*ВРЕМЯ*"];

                _parent.MasterData[_activeProfile].Variables.Clear();
                _parent.MasterData[_activeProfile].Variables["*ВРЕМЯ*"] = timeVal;

                foreach (var v in _tempVars)
                {
                    if (!string.IsNullOrWhiteSpace(v.Key) && v.Key != "**")
                    {
                        var keyToSave = v.Key;
                        if (!keys.Contains(keyToSave)) continue; // skip duplicates logic processed above
                        _parent.MasterData[_activeProfile].Variables[keyToSave] = v.Value;
                        keys.Remove(keyToSave); // to ensure we only save once
                    }
                }
                _parent.SaveData();
            }
        }
    }
}
