using System;
using System.ComponentModel;

namespace LogGrokX
{
    public partial class UpdateWindow
    {
        private readonly UpdateViewModel _viewModel;

        public UpdateWindow(UpdateViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += Close;
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.CancelDownload();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.CloseRequested -= Close;
            base.OnClosed(e);
        }
    }
}
