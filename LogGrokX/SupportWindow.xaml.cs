using System.Windows;

namespace LogGrokX
{
    public partial class SupportWindow
    {
        public SupportWindow()
        {
            DataContext = new SupportViewModel(Splat.Locator.Current.GetService(typeof(UpdateCheckService)) as UpdateCheckService) { Owner = this };
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
