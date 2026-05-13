using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FSB_helper_C__
{
    public partial class GroupEditorWindow : Window
    {
        private MainWindow _parent;
        private string _activeProfile;
        
        // Drag logic
        private Point _dragStartPoint;

        // Inline input mode
        private enum InputMode { None, Create, Rename }
        private InputMode _inputMode = InputMode.None;
        private string _renameTarget;

        public GroupEditorWindow(MainWindow parent, string activeProfile)
        {
            InitializeComponent();
            _parent = parent;
            _activeProfile = activeProfile;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGroupsList();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UpdateGroupsList()
        {
            if (_parent.MasterData.ContainsKey(_activeProfile))
            {
                var groups = _parent.MasterData[_activeProfile].Groups
                    .Where(g => g != "ВСЕ")
                    .Select(g => new BindGroupItem { 
                        Name = g, 
                        BindsCount = _parent.MasterData[_activeProfile].Binds.Values.Count(b => b.group == g) 
                    }).ToList();
                
                icGroupList.ItemsSource = groups;
                lblNoGroups.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ══════════════════════════════════════════
        //  INLINE INPUT (replaces broken _dialogHost)
        // ══════════════════════════════════════════
        private void ShowInlineInput(InputMode mode, string title, string prefill = "")
        {
            _inputMode = mode;
            lblInlineInputTitle.Text = title;
            txtInlineInput.Text = prefill;
            lblInlineError.Visibility = Visibility.Collapsed;
            pnlHint.Visibility = Visibility.Collapsed;
            pnlInlineInput.Visibility = Visibility.Visible;
            txtInlineInput.Focus();
            txtInlineInput.SelectAll();
        }

        private void HideInlineInput()
        {
            _inputMode = InputMode.None;
            _renameTarget = null;
            pnlInlineInput.Visibility = Visibility.Collapsed;
            pnlHint.Visibility = Visibility.Visible;
        }

        private void InlineInput_Confirm(object sender, RoutedEventArgs e) => ProcessInlineInput();
        private void InlineInput_Cancel(object sender, RoutedEventArgs e) => HideInlineInput();

        private void InlineInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ProcessInlineInput();
            else if (e.Key == Key.Escape) HideInlineInput();
        }

        private void ProcessInlineInput()
        {
            string v = txtInlineInput.Text.Trim();
            if (string.IsNullOrEmpty(v))
            {
                ShowInlineError("Введите имя группы!");
                return;
            }
            if (_parent.MasterData[_activeProfile].Groups.Contains(v))
            {
                ShowInlineError("Группа уже существует!");
                return;
            }

            if (_inputMode == InputMode.Create)
            {
                _parent.MasterData[_activeProfile].Groups.Add(v);
            }
            else if (_inputMode == InputMode.Rename && _renameTarget != null)
            {
                int i = _parent.MasterData[_activeProfile].Groups.IndexOf(_renameTarget);
                if (i >= 0) _parent.MasterData[_activeProfile].Groups[i] = v;
                foreach (var b in _parent.MasterData[_activeProfile].Binds.Values)
                {
                    if (b.group == _renameTarget) b.group = v;
                }
                _parent.UpdateBindsList();
            }

            _parent.UpdateBindGroups();
            _parent.SaveData();
            UpdateGroupsList();
            HideInlineInput();
        }

        private void ShowInlineError(string msg)
        {
            lblInlineError.Text = msg;
            lblInlineError.Visibility = Visibility.Visible;
        }

        // ══════════════════════════════════════════
        //  GROUP ACTIONS
        // ══════════════════════════════════════════
        private void AddGroup_Click(object sender, MouseButtonEventArgs e)
        {
            ShowInlineInput(InputMode.Create, "ИМЯ НОВОЙ ГРУППЫ");
        }

        private void Group_Rename_Click(object sender, RoutedEventArgs e)
        {
            _renameTarget = (sender as Button).Tag.ToString();
            ShowInlineInput(InputMode.Rename, "НОВОЕ ИМЯ ГРУППЫ", _renameTarget);
        }

        private void Group_Delete_Click(object sender, RoutedEventArgs e)
        {
            string g = (sender as Button).Tag.ToString(); 
            _parent.MasterData[_activeProfile].Groups.Remove(g); 
            foreach (var b in _parent.MasterData[_activeProfile].Binds.Values) { 
                if (b.group == g) b.group = "ВСЕ"; 
            } 
            _parent.SaveData(); 
            _parent.UpdateBindGroups(); 
            _parent.UpdateBindsList(); 
            UpdateGroupsList();
        }

        // ══════════════════════════════════════════
        //  DRAG & DROP REORDER
        // ══════════════════════════════════════════
        private void Group_Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(sender as IInputElement);
        }

        private void Group_Item_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(sender as IInputElement);
                Vector diff = pos - _dragStartPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var b = sender as Border;
                    if (b?.DataContext != null)
                    {
                        DragDrop.DoDragDrop(b, b.DataContext, DragDropEffects.Move);
                    }
                }
            }
        }

        private void Group_Item_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(BindGroupItem)))
            {
                var src = e.Data.GetData(typeof(BindGroupItem)) as BindGroupItem;
                var trg = (sender as Border)?.DataContext as BindGroupItem;
                
                if (src != null && trg != null && src.Name != trg.Name)
                {
                    var list = _parent.MasterData[_activeProfile].Groups;
                    int i1 = list.IndexOf(src.Name);
                    int i2 = list.IndexOf(trg.Name);
                    
                    if (i1 > -1 && i2 > -1)
                    {
                        list.RemoveAt(i1);
                        list.Insert(i2, src.Name);
                        _parent.SaveData();
                        _parent.UpdateBindGroups();
                        UpdateGroupsList();
                    }
                }
            }
        }
    }
}
