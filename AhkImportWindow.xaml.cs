using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FSB_helper_C__
{
    public partial class AhkImportWindow : Window
    {
        public AhkParseResult ImportResult { get; private set; } = null;
        private AhkParseResult _parsedResult = null;

        public AhkImportWindow(AhkParseResult parsedResult)
        {
            InitializeComponent();
            _parsedResult = parsedResult;

            int bindCount = parsedResult.Binds.Count;
            int varCount = parsedResult.Variables.Count;

            string confirmStr = $"Будет импортировано {bindCount} новых биндов";
            if (varCount > 0)
                confirmStr += $" и {varCount} переменных";
            confirmStr += ":";

            ConfirmText.Text = confirmStr;
            
            // Create a view model list for display
            var displayList = parsedResult.Binds.Select(b => new 
            {
                name = b.name,
                description = GetBindDescription(b)
            }).ToList();

            BindsList.ItemsSource = displayList;
        }

        private string GetBindDescription(BindItem b)
        {
            if (b.steps == null || b.steps.Count == 0) return "Пустой бинд";
            string firstAction = b.steps[0].action;
            if (firstAction.Length > 40)
                firstAction = firstAction.Substring(0, 40) + "...";
            
            if (b.steps.Count > 1)
                return $"{firstAction} (+ еще {b.steps.Count - 1} шаг(ов))";
            return firstAction;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            ImportResult = _parsedResult;
            DialogResult = true;
            Close();
        }
    }
}
