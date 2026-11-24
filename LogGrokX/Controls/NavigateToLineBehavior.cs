using System.Collections.Generic;
using System.Windows;

namespace LogGrokX.Controls
{
    public static class NavigateToLineBehavior
    {
        public static readonly DependencyProperty NavigateToLineRequestProperty =
            DependencyProperty.RegisterAttached(
                "NavigateToLineRequest",
                typeof(NavigateToLineRequest), 
                typeof(NavigateToLineBehavior),
                new PropertyMetadata(null, OnRequestChanged));

        public static readonly DependencyProperty ChangeCurrentItemProperty = 
            DependencyProperty.RegisterAttached(
            "ChangeCurrentItem", 
            typeof(bool),
            typeof(NavigateToLineBehavior), 
            new PropertyMetadata(false));

        public static void SetChangeCurrentItem(ListControls.ListView listView, bool value)
        {
            listView.SetValue(ChangeCurrentItemProperty, value);
        }

        public static bool GetChangeCurrentItem(ListControls.ListView listView)
        {
            return (bool) listView.GetValue(ChangeCurrentItemProperty);
        }
        
        public static void SetNavigateToLineRequest(ListControls.ListView listView, NavigateToLineRequest request)
        {
            listView.SetValue(NavigateToLineRequestProperty, request);
        }

        public static NavigateToLineRequest? GetNavigateToLineRequest(ListControls.ListView listView)
        {
            return (NavigateToLineRequest) listView.GetValue(NavigateToLineRequestProperty);
        }

        private static void OnRequestChanged(DependencyObject? d , DependencyPropertyChangedEventArgs args)
        {
            if (!(d is ListControls.ListView listView)) return;
            
            if (args.OldValue != null)
            {
                var request = (NavigateToLineRequest) (args.OldValue);
                SubscribersMap.Remove(request);
            }

            if (args.NewValue != null)
            {
                var request = (NavigateToLineRequest) (args.NewValue);
                SubscribersMap[request] = listView;
                request.Navigate += (i, center) => RequestNavigate(request, i, center);
                request.RestorePosition += (current, scroll) => RequestRestorePosition(request, current, scroll);
            }
        }

        private static void RequestNavigate(NavigateToLineRequest request, int lineNumber, bool center)
        {
            var listView = SubscribersMap[request];
            if (GetChangeCurrentItem(listView))
                listView.NavigateTo(lineNumber, center);
            else 
                listView.BringIndexIntoView(lineNumber);
        }

        private static void RequestRestorePosition(NavigateToLineRequest request, int currentLineNumber, int scrollLineNumber)
        {
            SubscribersMap[request].RestorePosition(currentLineNumber, scrollLineNumber);
        }

        private static readonly Dictionary<NavigateToLineRequest, ListControls.ListView> SubscribersMap 
            = new();
    }
}
