using System.Windows;
using System.Windows.Controls;

namespace LogGrokX.Controls
{
    public class SetFocusBehavior
    { 
        public static readonly DependencyProperty SetFocusRequestProperty =
            DependencyProperty.RegisterAttached(
                "SetFocusRequest", typeof(SetFocusRequest), typeof(SetFocusBehavior),
                new FrameworkPropertyMetadata(null, OnSetFocusRequestChanged));

        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.RegisterAttached(
                "SelectAllOnFocus", typeof(bool), typeof(SetFocusBehavior),
                new FrameworkPropertyMetadata(false));

        public static SetFocusRequest GetSetFocusRequest(DependencyObject obj)
        {
            return (SetFocusRequest)obj.GetValue(SetFocusRequestProperty);
        }

        public static void SetSetFocusRequest(DependencyObject obj, SetFocusRequest value)
        {
            obj.SetValue(SetFocusRequestProperty, value);
        }

        public static bool GetSelectAllOnFocus(DependencyObject obj)
        {
            return (bool)obj.GetValue(SelectAllOnFocusProperty);
        }

        public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
        {
            obj.SetValue(SelectAllOnFocusProperty, value);
        }

        private static void OnSetFocusRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement uiElement && e.NewValue is SetFocusRequest setFocusRequest)
            {
                setFocusRequest.SetFocus += () => InvokeSetFocus(uiElement);
            }
        }

        private static void InvokeSetFocus(UIElement uiElement)
        {
            uiElement.Focus();
            if (uiElement is TextBox textBox && GetSelectAllOnFocus(uiElement))
                textBox.SelectAll();
        }
    }
}