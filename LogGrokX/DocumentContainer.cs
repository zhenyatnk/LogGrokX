using LogGrokX.Platform.Colors;
using System;
using System.Diagnostics;
using LogGrokX.Platform;
using DryIoc;
using LogGrokX.Controls;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Data.Virtualization;
using LogGrokX.Filter;
using LogGrokX.Search;
using LogGrokX.Settings;

namespace LogGrokX
{
    internal class DocumentContainer : IDisposable
    {
        private enum ParserType
        {
            Full,
            OnlyIndexed
        }

        private enum GridViewType
        {
            FilteringGirdViewType,
            NotFilteringGridViewType
        }

        private readonly Container _container;
        public DocumentContainer(string fileName, ProfileSettings profile, ApplicationSettings applicationSettings,
            SearchAutocompleteCache autocompleteCache, SavedSearchPatternStore savedSearchPatternStore,
            TimelinePlacementService timelinePlacementService, ThreadGroupingService threadGroupingService, IUiDispatcher dispatcher, IDesktopService desktop,
            IAppDialogs dialogs, IColorValidator colorValidator, DisplaySettingsMonitor displaySettings)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            ArgumentNullException.ThrowIfNull(desktop);
            ArgumentNullException.ThrowIfNull(dialogs);
            ArgumentNullException.ThrowIfNull(colorValidator);
            ArgumentNullException.ThrowIfNull(displaySettings);
            var logFormats = LogFormatValidation.GetValidFormats(profile);
            _container = new Container(rules =>
                rules
                    .WithDefaultReuse(Reuse.Singleton)
                    .WithUnknownServiceResolvers
                        (Rules.AutoResolveConcreteTypeRule()));

            LoggerRegistrationHelper.Register(_container);
            
            _container.RegisterInstance<IUiDispatcher>(dispatcher);
            _container.RegisterInstance<IDesktopService>(desktop);
            _container.RegisterInstance(dialogs);
            _container.RegisterInstance(displaySettings, setup: Setup.With(preventDisposal: true));
            _container.RegisterInstance(autocompleteCache);
            _container.RegisterInstance(savedSearchPatternStore);
            _container.RegisterInstance(timelinePlacementService);
            _container.RegisterInstance(threadGroupingService);
            _container.Register<StringPool>(Reuse.Singleton);
            _container.Register<LogModelFacade>(
                made: Parameters.Of.Type<ILineParser>(serviceKey: ParserType.Full));
            
            // loader pipeline
            _container.Register<Loader>(Reuse.Singleton);
            _container.Register<ILineDataConsumer, LineProcessor>(
                made: Parameters.Of.Type<ILineParser>(serviceKey: ParserType.OnlyIndexed));
            _container.Register<ParsedBufferConsumer>(Reuse.Singleton);
            
            // log model
            _container.RegisterDelegate(c =>
            {
                var meta = c.Resolve<LogMetaInformation>();
                return new LogFile(fileName, meta.XorMask);
            });
            _container.RegisterDelegate(_ => LogMetaInformationProvider.GetLogMetaInformation(fileName,
                logFormats),
                Reuse.Singleton);
            _container.Register<LineIndex>();
            _container.RegisterMapping<ILineIndex, LineIndex>();
            _container.Register<TimeIndex>(Reuse.Singleton);
            _container.Register<Indexer>(Reuse.Singleton);
            _container.RegisterMapping<IComponentIndexer, Indexer>();
            _container.Register<IItemProvider<(int, string)>, LineProvider>();

            _container.RegisterDelegate<ILineParser>(
                r => new RegexBasedLineParser(r.Resolve<LogMetaInformation>(), true), 
                serviceKey: ParserType.OnlyIndexed);

            _container.RegisterDelegate<ILineParser>(
                r => new RegexBasedLineParser(r.Resolve<LogMetaInformation>()), 
                serviceKey: ParserType.Full);
            
            // Presentation
            _container.RegisterDelegate(_ => new Platform.Colors.ColorRuleSet(
                profile.ColorSettings ?? new Platform.Colors.Configuration.ColorSettings(), colorValidator));
            _container.RegisterDelegate(c => 
                applicationSettings.GetColumnSettings(c.Resolve<LogMetaInformation>().LineRegex), 
                reuse: Reuse.Singleton);

            _container.Register<LogViewModel>(
                made: Parameters.Of
                    .Type<ILineParser>(serviceKey: ParserType.Full)
                    .Type<LogGridModel>(serviceKey: GridViewType.FilteringGirdViewType));

            _container.Register<LineViewModelCollectionProvider>(Reuse.Singleton, 
                made: Parameters.Of
                    .Type<ILineParser>(serviceKey: ParserType.Full)
                    .Type(request => request.Container.Resolve<LogHeaderCollection>()));
            
            _container.Register<FilterSettings>(Reuse.Singleton);
            _container.Register<CurrentThreadFilter>(Reuse.Singleton);
            _container.Register<TextViewSharedFoldingState>(Reuse.Singleton);
            
            _container.Register<DocumentViewModel>();
            _container.Register<SearchViewModel>();
            _container.Register<Selection>(Reuse.Singleton);
            _container.RegisterDelegate(c => 
                new TransformationPerformer(c.Resolve<LogMetaInformation>().Transformations));
            
            _container.RegisterDelegate<Func<string, FilterViewModel>>(
                r => fieldName => new FilterViewModel(
                    fieldName, 
                    r.Resolve<FilterSettings>(),
                    r.Resolve<Indexer>(),
                    r.Resolve<LogMetaInformation>(), dispatcher));

            _container.RegisterDelegate<Func<SearchPattern, ISearchDocument>>(
                r =>
                {
                    var logModelFacade = r.Resolve<LogModelFacade>();
                    var filterSettings = r.Resolve<FilterSettings>();
                    var gridModel = r.Resolve<LogGridModel>(GridViewType.NotFilteringGridViewType);
                    var markedLines = r.Resolve<Selection>();
                    var columnSettings = r.Resolve<ColumnSettings>();
                    var transofrmationPerformer = r.Resolve<TransformationPerformer>();
                    var timeIndex = r.Resolve<TimeIndex>();
                    var foldingState = r.Resolve<TextViewSharedFoldingState>();
                    var currentThreadFilter = r.Resolve<CurrentThreadFilter>();
                    return pattern => new SearchDocumentViewModel(logModelFacade, filterSettings, gridModel, pattern,
                        markedLines, columnSettings, transofrmationPerformer, timeIndex, foldingState,
                        currentThreadFilter, displaySettings);
                });
            
            // view
            _container.RegisterDelegate(
                r => new LogGridModel(r.Resolve<LogMetaInformation>(),
                    r.Resolve<ColumnSettings>(), r.Resolve<Func<string, FilterViewModel>>()),
                serviceKey: GridViewType.FilteringGirdViewType);

            _container.RegisterDelegate(
                r => new LogGridModel(r.Resolve<LogMetaInformation>(),
                    r.Resolve<ColumnSettings>()),
                serviceKey: GridViewType.NotFilteringGridViewType);

            // start loading
            _ = _container.Resolve<Loader>();
            Trace.TraceInformation($"Start loading {fileName}");
        }

        public DocumentViewModel GetDocumentViewModel() => _container.Resolve<DocumentViewModel>(); 

        public void Dispose() => _container.Dispose();
    }
}
