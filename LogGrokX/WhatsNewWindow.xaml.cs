using System;

namespace LogGrokX
{
    public partial class WhatsNewWindow
    {
        private readonly WhatsNewViewModel _viewModel;

        public WhatsNewWindow(WhatsNewViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += Close;
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.CloseRequested -= Close;
            base.OnClosed(e);
        }
    }
}
