using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FSB_helper_C__
{
    public partial class BinderEditor : UserControl
    {
        public MainWindow Main;

        public BinderEditor()
        {
            InitializeComponent();
        }

        public void Init(MainWindow mainWindow)
        {
            Main = mainWindow;
        }

        private void Step_Add_Chat(object sender, RoutedEventArgs e) => Main?.Step_Add_Chat(sender, e);
        private void Step_Add_Key(object sender, RoutedEventArgs e) => Main?.Step_Add_Key(sender, e);
        private void Step_Add_Wait(object sender, RoutedEventArgs e) => Main?.Step_Add_Wait(sender, e);
        private void Step_Delete_Click(object sender, RoutedEventArgs e) => Main?.Step_Delete_Click(sender, e);
        private void Bind_SetKey_Editor_Click(object sender, RoutedEventArgs e) => Main?.Bind_SetKey_Editor_Click(sender, e);
        private void Overlay_Close(object sender, RoutedEventArgs e) => Main?.Overlay_Close(sender, e);
        private void StepVal_TextChanged(object sender, TextChangedEventArgs e) => Main?.StepVal_TextChanged(sender, e);
        private void StepVal_SelectionChanged(object sender, RoutedEventArgs e) => Main?.StepVal_SelectionChanged(sender, e);
        private void StepVal_Loaded(object sender, RoutedEventArgs e) => Main?.StepVal_Loaded(sender, e);
        private void TxtStepVal_ScrollChanged(object sender, ScrollChangedEventArgs e) => Main?.TxtStepVal_ScrollChanged(sender, e);
        private void Bind_Save_Click(object sender, RoutedEventArgs e) => Main?.Bind_Save_Click(sender, e);
        private void Open_Guide(object sender, RoutedEventArgs e) => Main?.Open_Guide(sender, e);
        private void BindType_Changed(object sender, RoutedEventArgs e) => Main?.BindType_Changed(sender, e);

        // --- Drag and Drop logic ---
        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement dragHandle && dragHandle.DataContext != null)
            {
                DataObject data = new DataObject("BindStep", dragHandle.DataContext);
                DragDrop.DoDragDrop(dragHandle, data, DragDropEffects.Move);
            }
        }

        private void BindStep_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("BindStep"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                
                if (sender is System.Windows.Controls.Panel grid)
                {
                    var pos = e.GetPosition(grid);
                    bool isTop = pos.Y < grid.ActualHeight / 2;
                    foreach (UIElement child in grid.Children)
                    {
                        if (child is FrameworkElement fe)
                        {
                            if (fe.Name == "DropIndicatorTop") fe.Visibility = isTop ? Visibility.Visible : Visibility.Collapsed;
                            if (fe.Name == "DropIndicatorBottom") fe.Visibility = isTop ? Visibility.Collapsed : Visibility.Visible;
                        }
                    }
                }
            }
        }

        private void BindStep_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is System.Windows.Controls.Panel grid)
            {
                foreach (UIElement child in grid.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        if (fe.Name == "DropIndicatorTop" || fe.Name == "DropIndicatorBottom") 
                            fe.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void BindStep_Drop(object sender, DragEventArgs e)
        {
            BindStep_DragLeave(sender, e); // Hide indicators

            if (e.Data.GetDataPresent("BindStep"))
            {
                var sourceStep = e.Data.GetData("BindStep");
                var targetElement = sender as FrameworkElement;
                var targetStep = targetElement?.DataContext;

                if (sourceStep != null && targetStep != null && sourceStep != targetStep)
                {
                    var position = e.GetPosition(targetElement);
                    bool isTop = position.Y < targetElement.ActualHeight / 2;
                    Main?.ReorderBindStep(sourceStep, targetStep, !isTop);
                }
            }
        }
    }
}