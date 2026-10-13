using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using LogGrokX.MarkedLines;

namespace LogGrokX
{
    public static class RoutedCommands
    {
        public static readonly RoutedCommand Cancel = new RoutedUICommand(
            Localization.Strings.Get("Cancel"), "Cancel", typeof(UIElement),
            new InputGestureCollection { new KeyGesture(Key.Escape) });

        public static readonly RoutedCommand SearchText = new RoutedUICommand(
            Localization.Strings.Get("SearchSelection"), "Search selection", typeof(UIElement));

        public static readonly RoutedCommand ClearFilters = new RoutedUICommand(
            Localization.Strings.Get("ClearAllFilters"), "Clear filters", typeof(UIElement));

        public static readonly RoutedUICommand CopyToClipboard = new RoutedUICommand(
            Localization.Strings.Get("CopyNative"), nameof(CopyToClipboard), typeof(UIElement));

        public static readonly RoutedUICommand CopyAsDisplayed = new RoutedUICommand(
            Localization.Strings.Get("Copy"), nameof(CopyAsDisplayed), typeof(UIElement),
            new InputGestureCollection(ApplicationCommands.Copy.InputGestures));

        public static readonly RoutedCommand ToggleMarks = new RoutedUICommand(
            Localization.Strings.Get("ToggleMarks"), "Mark lines", typeof(UIElement),
            new InputGestureCollection { new KeyGesture(Key.Space) });

        public static void ToggleMarksHandler(IEnumerable<ILineMark> items) 
        {
            var existsUnMarked = false;
            foreach (var item in items)
            {
                if (!item.IsMarked)
                {
                    item.IsMarked = !item.IsMarked;
                    existsUnMarked = true;
                }
            }
            if (!existsUnMarked)
            {
                foreach (var item in items)
                {
                    item.IsMarked = !item.IsMarked;
                }
            }
        }

        static RoutedCommands()
        {
            ApplicationCommands.Copy.InputGestures.Clear();
        }
    }
}