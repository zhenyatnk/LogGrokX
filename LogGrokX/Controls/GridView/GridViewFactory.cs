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
            var view = new System.Windows.Controls.GridView();

            view.Columns.Add(new LogGridViewColumn
            {
                HeaderTemplate = new DataTemplate(typeof(DependencyObject))
                {
                    VisualTree = new FrameworkElementFactory(typeof(PinGridViewhHeader))
                },
                CellTemplate =  CreatePinCellTemplate(),
                Width = widths == null? 0 : widths[0]
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
                    var dataTemplate = new DataTemplate(typeof(DependencyObject))
                    {
                        VisualTree = frameworkElementFactory
                    };
                    return dataTemplate;
                }

                view.Columns.Add(new LogGridViewColumn
                {  
                    HeaderTemplate = BuildHeaderTemplate(),
                    CellTemplate = CreateCellTemplate(),
                    Width = widths == null ? 0 : widths[columnIndex++]
                });
            }

            return view;
        }

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
        
        private static DataTemplate CreatePinCellTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(PinControl));
            factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            var binding = new Binding
            {
                Path = new PropertyPath(nameof(LineViewModel.IsMarked)),
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            factory.SetBinding(ToggleButton.IsCheckedProperty, binding);
            return new DataTemplate {VisualTree = factory};
        }
    }
}
