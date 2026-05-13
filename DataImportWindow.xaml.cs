using System.Windows;

namespace FSB_helper_C__
{
    public partial class DataImportWindow : Window
    {
        private MainWindow _parent;

        public DataImportWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;
        }




        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ImportProfile_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowImportProfile_Click(null, null);
        }

        private void ImportLaws_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowImportLaws_Click(null, null);
        }

        private void ImportBinds_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowImportBinds_Click(null, null);
        }

        private void ImportFines_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            _parent.ShowImportFines_Click(null, null);
        }
    }
}
