using System;
using System.Linq;
using System.Windows.Controls.Primitives;

namespace LogGrokX
{
    internal static class ProfileSelectorSync
    {
        internal static void ApplySelection(Selector selector, MainWindowViewModel viewModel, ProfileSettings selected)
        {
            if (ReferenceEquals(selected, viewModel.SelectedProfile))
                return;

            viewModel.SelectedProfile = selected;
            var applied = viewModel.SelectedProfile;
            var item = selector.Items.OfType<ProfileSettings>().FirstOrDefault(profile =>
                ReferenceEquals(profile, applied) ||
                string.Equals(profile.Name, applied.Name, StringComparison.OrdinalIgnoreCase));
            if (!ReferenceEquals(selector.SelectedItem, item))
                selector.SelectedItem = item;
        }
    }
}
