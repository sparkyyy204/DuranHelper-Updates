using System.Windows;

namespace FSB_helper_C__
{
    public partial class DataExportWindow : Window
    {
        private MainWindow _parent;

        public DataExportWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ExportProfile_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowExportProfile_Click(null, null);
        }

        private void ExportLaws_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowExportLaws_Click(null, null);
        }

        private void ExportBinds_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowExportBinds_Click(null, null);
        }

        private void ExportFines_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowExportFines_Click(null, null);
        }
    }
}
