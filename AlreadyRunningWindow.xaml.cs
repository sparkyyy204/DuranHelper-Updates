using System.Windows;

namespace FSB_helper_C__
{
    public partial class AlreadyRunningWindow : Window
    {
        public AlreadyRunningWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
