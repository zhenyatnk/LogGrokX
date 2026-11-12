using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LogGrokX.Controls
{
    public class RootDocumentControl : ContentControl
    {
        public static readonly DependencyProperty SearchTextCommandProperty = DependencyProperty.Register(
            "SearchTextCommand", typeof(ICommand), typeof(RootDocumentControl), new PropertyMetadata(default(ICommand)));

        public ICommand? SearchTextCommand
        {
            get => (ICommand?) GetValue(SearchTextCommandProperty);
            set => SetValue(SearchTextCommandProperty, value);
        }
        
        public static readonly DependencyProperty AppendToSearchCommandProperty = DependencyProperty.Register(
            "AppendToSearchCommand", typeof(ICommand), typeof(RootDocumentControl), new PropertyMetadata(default(ICommand)));

        public ICommand? AppendToSearchCommand
        {
            get => (ICommand?) GetValue(AppendToSearchCommandProperty);
            set => SetValue(AppendToSearchCommandProperty, value);
        }

        public RootDocumentControl()
        {
            CommandBindings.Add(new CommandBinding(
                RoutedCommands.AppendToSearch,
                (_, args) =>
                {
                    if (args.Parameter is string text && !string.IsNullOrEmpty(text))
                        AppendToSearchCommand?.Execute(text);
                    args.Handled = true;
                },
                (_, args) =>
                {
                    args.CanExecute =
                        args.Parameter is string text && !string.IsNullOrEmpty(text) &&
                        (AppendToSearchCommand?.CanExecute(text) ?? false);
                    args.Handled = true;
                }));
            CommandBindings.Add(new CommandBinding(
                RoutedCommands.SearchText,
                (_, args) =>
                {
                    if (args.Parameter is string text && !string.IsNullOrEmpty(text))
                        SearchTextCommand?.Execute(text);
                    args.Handled = true;
                },
                (_, args) =>
                {
                    args.CanExecute = 
                        args.Parameter is string text && !string.IsNullOrEmpty(text) &&
                        (SearchTextCommand?.CanExecute(text) ?? false);
                    args.Handled = true;
                }));
        }
    }
}