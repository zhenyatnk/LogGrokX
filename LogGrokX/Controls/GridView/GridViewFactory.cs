using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Filter;

namespace LogGrokX.Controls.GridView
{
    public class GridViewFactory
    {
        private const string ThreadFieldName = "Thread";
        private const string ComponentFieldName = "Component";
        private const string DecodeTogglesTemplateKey = "DecodeTogglesTemplate";

        public const double PinColumnMinWidth = 30;
        private const double PinColumnPadding = 12;

        private readonly LogMetaInformation _meta;
        private readonly Func<string, FilterViewModel>? _filterViewModelFactory;
        public GridViewFactory(LogMetaInformation meta, 
                bool canFilter,
                Func<string, FilterViewModel>? filterViewModelFactory)
        {
            _meta = meta;
            _filterViewModelFactory = canFilter switch
            {
                true when filterViewModelFactory != null => filterViewModelFactory,
                true when filterViewModelFactory == null => throw new ArgumentException(
                    $"filterViewModelFactory cannot be null if canFilter is true."),
                false when filterViewModelFactory != null => throw new ArgumentException(
                    $"filterViewModelFactory must be null if canFilter is false."),
                _ => _filterViewModelFactory
            };
        }

        public ViewBase CreateView(double[]? widths) => CreateView(widths, null, null);

        public ViewBase CreateView(double[]? widths, DataTemplate? leadingCellTemplate) =>
            CreateView(widths, leadingCellTemplate, null);

        public ViewBase CreateView(double[]? widths, DataTemplate? leadingCellTemplate, string? sharedFoldingStatePath)
        {
            var columnCount = _meta.FieldNames.Length + 2 + (leadingCellTemplate != null ? 1 : 0);
            if (widths != null && widths.Length != columnCount)
                widths = null;

            var indexFieldName = "Index";
            var componentFieldName = _meta.FieldNames
                .FirstOrDefault(field => field.Equals(ComponentFieldName, StringComparison.OrdinalIgnoreCase));
            var view = new System.Windows.Controls.GridView();

            view.Columns.Add(new LogGridViewColumn
            {
                HeaderTemplate = new DataTemplate(typeof(DependencyObject))
                {
                    VisualTree = new FrameworkElementFactory(typeof(PinGridViewhHeader))
                },
                CellTemplate =  CreatePinCellTemplate(componentFieldName == null),
                Width = widths == null ? 0 : Math.Max(widths[0], PinColumnMinWidth + PinColumnPadding)
            });

            var columnIndex = 1;
            if (leadingCellTemplate != null)
            {
                view.Columns.Add(new LogGridViewColumn
                {
                    HeaderTemplate = CreateHeaderTemplate("", null),
                    CellTemplate = leadingCellTemplate,
                    Width = widths == null ? 0 : widths[columnIndex++]
                });
            }

            foreach (var fieldHeader in indexFieldName.Yield().Concat(_meta.FieldNames))
            {
                var fieldIndex = fieldHeader == indexFieldName
                    ? -1
                    : Array.IndexOf(_meta.FieldNames, fieldHeader);
                var contentTextProvider = fieldHeader == indexFieldName
                    ? static item => item is BaseLogLineViewModel line
                        ? line.IndexViewModel.OriginalText
                        : string.Empty
                    : new Func<object, string>(item => item is BaseLogLineViewModel line
                        ? line.GetFieldText(fieldIndex)
                        : string.Empty);

                DataTemplate BuildHeaderTemplate()
                {
                    FilterViewModel? filterViewModel = null;
                    if (_filterViewModelFactory != null && _meta.IsFieldIndexed(fieldHeader))
                    {
                        filterViewModel = _filterViewModelFactory(fieldHeader);
                    }

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
                                new PropertyPath(".[(0)]", Array.IndexOf(_meta.FieldNames, fieldHeader)),
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
                    if (fieldHeader != componentFieldName)
                    {
                        return new DataTemplate(typeof(DependencyObject))
                        {
                            VisualTree = frameworkElementFactory
                        };
                    }

                    var grid = new FrameworkElementFactory(typeof(Grid));
                    var contentColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
                    contentColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                    var togglesColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
                    togglesColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
                    grid.AppendChild(contentColumn);
                    grid.AppendChild(togglesColumn);

                    frameworkElementFactory.SetValue(Grid.ColumnProperty, 0);
                    grid.AppendChild(frameworkElementFactory);

                    var toggles = CreateDecodeToggles(HorizontalAlignment.Right);
                    toggles.SetValue(Grid.ColumnProperty, 1);
                    grid.AppendChild(toggles);

                    return new DataTemplate(typeof(DependencyObject))
                    {
                        VisualTree = grid
                    };
                }

                view.Columns.Add(new LogGridViewColumn
                {  
                    HeaderTemplate = BuildHeaderTemplate(),
                    CellTemplate = CreateCellTemplate(),
                    Width = widths == null ? 0 : widths[columnIndex++],
                    FitToContent = IsMessageField(fieldHeader),
                    ContentTextProvider = contentTextProvider
                });
            }

            return view;
        }

        private static bool IsMessageField(string fieldHeader) =>
            fieldHeader.Equals("Message", StringComparison.OrdinalIgnoreCase) ||
            fieldHeader.Equals("Text", StringComparison.OrdinalIgnoreCase);

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
