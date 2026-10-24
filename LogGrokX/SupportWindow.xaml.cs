using System.Windows;
using LogGrokX.Platform;

namespace LogGrokX
{
    public partial class SupportWindow
    {
        public SupportWindow(IUpdateService? updateCheckService = null, IDesktopService? desktop = null)
        {
            DataContext = new SupportViewModel(updateCheckService, desktop);
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
