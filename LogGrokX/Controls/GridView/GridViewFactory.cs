using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Filter;

namespace LogGrokX.Controls.GridView
{
    public static class GridViewFactory
    {
        private const string ThreadFieldName = "Thread";
        private const string ComponentFieldName = "Component";
        private const string DecodeTogglesTemplateKey = "DecodeTogglesTemplate";

        public const double PinColumnMinWidth = 30;
        private const double PinColumnPadding = 12;

        public static ViewBase CreateView(LogGridModel model, double[]? widths,
            DataTemplate? leadingCellTemplate = null, string? sharedFoldingStatePath = null)
        {
            var meta = model.MetaInformation;
            var columnCount = meta.FieldNames.Length + 2 + (leadingCellTemplate != null ? 1 : 0);
            if (widths != null && (widths.Length != columnCount ||
                                  widths.Any(width => !double.IsFinite(width) || width <= 0)))
                widths = null;

            var indexFieldName = "Index";
            var componentFieldName = meta.FieldNames
                .FirstOrDefault(field => field.Equals(ComponentFieldName, StringComparison.OrdinalIgnoreCase));
            var messageFieldIndex = Array.FindIndex(meta.FieldNames,
                field => field.Equals("Message", StringComparison.OrdinalIgnoreCase));
            var view = new System.Windows.Controls.GridView();

            view.Columns.Add(new LogGridViewColumn
            {
                HeaderTemplate = new DataTemplate(typeof(DependencyObject))
                {
                    VisualTree = new FrameworkElementFactory(typeof(PinGridViewhHeader))
                },
                CellTemplate =  CreatePinCellTemplate(componentFieldName == null),
                Width = widths == null ? double.NaN : Math.Max(widths[0], PinColumnMinWidth + PinColumnPadding)
            });

            var columnIndex = 1;
            if (leadingCellTemplate != null)
            {
                view.Columns.Add(new LogGridViewColumn
                {
                    HeaderTemplate = CreateHeaderTemplate("", null),
                    CellTemplate = leadingCellTemplate,
                    Width = widths == null ? double.NaN : widths[columnIndex++]
                });
            }

            foreach (var fieldHeader in indexFieldName.Yield().Concat(meta.FieldNames))
            {
                var fieldIndex = fieldHeader == indexFieldName
                    ? -1
                    : Array.IndexOf(meta.FieldNames, fieldHeader);
                var contentTextProvider = fieldHeader == indexFieldName
                    ? static item => item is BaseLogLineViewModel line
                        ? line.IndexViewModel.OriginalText
                        : string.Empty
                    : new Func<object, string>(item => item is BaseLogLineViewModel line
                        ? line.GetFieldText(fieldIndex)
                        : string.Empty);

                DataTemplate BuildHeaderTemplate()
                {
                    var filterViewModel = model.GetFilter(fieldHeader);
                    return CreateHeaderTemplate(fieldHeader, filterViewModel);
                }
                
                DataTemplate CreateCellTemplate()
                {
                    var frameworkElementFactory = new FrameworkElementFactory(typeof(LogGridViewCell));
                    var binding = new Binding
                    {
                        Path = 
                            fieldHeader == indexFieldName ? 
                                new PropertyPath(nameof(LineViewModel.IndexViewModel)) : 
                                new PropertyPath(".[(0)]", Array.IndexOf(meta.FieldNames, fieldHeader)),
                        Mode = BindingMode.OneWay
                    };
                    frameworkElementFactory.SetBinding(ContentControl.ContentProperty, binding);
                    if (sharedFoldingStatePath != null)
                    {
                        frameworkElementFactory.SetBinding(TextView.SharedFoldingStateProperty, new Binding
                        {
                            Path = new PropertyPath(sharedFoldingStatePath),
                            Mode = BindingMode.OneWay
                        });
                    }
                    if (messageFieldIndex >= 0 && fieldIndex == messageFieldIndex)
                    {
                        frameworkElementFactory.SetValue(TextView.HasExternalRootExpanderProperty, true);
                        frameworkElementFactory.SetValue(TextView.UseFixedFoldingGutterProperty, true);
                    }
                    if (fieldHeader == ThreadFieldName)
                    {
                        var opacityBinding = new Binding
                        {
                            Path = new PropertyPath(BaseLogListViewItem.IsGroupContinuationProperty),
                            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1),
                            Converter = new GroupContinuationToOpacityConverter()
                        };
                        frameworkElementFactory.SetBinding(UIElement.OpacityProperty, opacityBinding);
                    }

                    var hostsRootExpander = fieldIndex == messageFieldIndex - 1;
                    var includeDecodeToggles = fieldHeader == componentFieldName;
                    if (!hostsRootExpander && !includeDecodeToggles)
                    {
                        return new DataTemplate(typeof(DependencyObject))
                        {
                            VisualTree = frameworkElementFactory
                        };
                    }

                    var grid = new FrameworkElementFactory(typeof(Grid));
                    var contentColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
                    contentColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                    var actionsColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
                    actionsColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
                    grid.AppendChild(contentColumn);
                    grid.AppendChild(actionsColumn);

                    frameworkElementFactory.SetValue(Grid.ColumnProperty, 0);
                    grid.AppendChild(frameworkElementFactory);

                    var actions = new FrameworkElementFactory(typeof(StackPanel));
                    actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
                    actions.SetValue(Grid.ColumnProperty, 1);
                    if (hostsRootExpander)
                        actions.AppendChild(CreateRootExpander(messageFieldIndex, sharedFoldingStatePath));
                    if (includeDecodeToggles)
                        actions.AppendChild(CreateDecodeToggles(HorizontalAlignment.Right));
                    grid.AppendChild(actions);

                    return new DataTemplate(typeof(DependencyObject))
                    {
                        VisualTree = grid
                    };
                }

                var column = new LogGridViewColumn
                {
                    HeaderTemplate = BuildHeaderTemplate(),
                    CellTemplate = CreateCellTemplate(),
                    Width = widths == null ? double.NaN : widths[columnIndex++],
                    FitToContent = IsMessageField(fieldHeader),
                    ContentTextProvider = contentTextProvider,
                    FieldName = fieldHeader
                };
                if (messageFieldIndex >= 0 && fieldIndex == messageFieldIndex &&
                    view.Columns[^1] is LogGridViewColumn host)
                    column.RootExpanderHost = host;
                view.Columns.Add(column);
            }

            return view;
        }

        private static bool IsMessageField(string fieldName) =>
            fieldName.Equals("Message", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("Text", StringComparison.OrdinalIgnoreCase);

        private static DataTemplate CreateHeaderTemplate(string fieldHeader, FilterViewModel? filterViewModel)
        {
            var frameworkElementFactory = new FrameworkElementFactory(typeof(LogGridViewHeader));
            frameworkElementFactory.SetValue(FrameworkElement.DataContextProperty,
                new HeaderViewModel(fieldHeader, filterViewModel));
            return new DataTemplate(typeof(DependencyObject))
            {
                VisualTree = frameworkElementFactory
            };
        }
        
        private static DataTemplate CreatePinCellTemplate(bool includeDecodeToggles)
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            panel.SetValue(FrameworkElement.MinWidthProperty, PinColumnMinWidth);

            var pin = new FrameworkElementFactory(typeof(PinControl));
            pin.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            pin.SetBinding(ToggleButton.IsCheckedProperty, new Binding
            {
                Path = new PropertyPath(nameof(LineViewModel.IsMarked)),
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.AppendChild(pin);

            if (includeDecodeToggles)
                panel.AppendChild(CreateDecodeToggles(HorizontalAlignment.Center));

            return new DataTemplate {VisualTree = panel};
        }

        private static FrameworkElementFactory CreateRootExpander(int messageFieldIndex,
            string? sharedFoldingStatePath)
        {
            var root = new FrameworkElementFactory(typeof(RootFoldingExpander));
            root.SetValue(FrameworkElement.WidthProperty, 8d);
            root.SetValue(FrameworkElement.HeightProperty, 8d);
            root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            root.SetBinding(RootFoldingExpander.TextModelProperty, new Binding
            {
                Path = new PropertyPath(".[(0)].TextModel", messageFieldIndex),
                Mode = BindingMode.OneWay
            });
            var foldingStateBinding = sharedFoldingStatePath != null
                ? new Binding(sharedFoldingStatePath) { Mode = BindingMode.OneWay }
                : new Binding("DataContext.FoldingState")
                {
                    Mode = BindingMode.OneWay,
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.ListView), 1)
                };
            root.SetBinding(RootFoldingExpander.SharedFoldingStateProperty, foldingStateBinding);
            return root;
        }

        private static FrameworkElementFactory CreateDecodeToggles(HorizontalAlignment horizontalAlignment)
        {
            var toggles = new FrameworkElementFactory(typeof(ContentControl));
            toggles.SetValue(UIElement.FocusableProperty, false);
            toggles.SetValue(FrameworkElement.HorizontalAlignmentProperty, horizontalAlignment);
            toggles.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            toggles.SetBinding(ContentControl.ContentProperty, new Binding());
            toggles.SetResourceReference(ContentControl.ContentTemplateProperty, DecodeTogglesTemplateKey);
            return toggles;
        }
    }
}
