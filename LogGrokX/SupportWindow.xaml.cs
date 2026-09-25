using System.Windows;

namespace LogGrokX
{
    public partial class SupportWindow
    {
        public SupportWindow(UpdateCheckService? updateCheckService = null)
        {
            DataContext = new SupportViewModel(updateCheckService) { Owner = this };
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
