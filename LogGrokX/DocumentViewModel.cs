using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using LogGrokX.Colors;
using LogGrokX.Controls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Search;

namespace LogGrokX
{
    public class DocumentViewModel : ViewModelBase
    {
        private readonly Selection _markedLines;
        private bool _isCurrentDocument;
        private readonly LineProvider _lineProvider;
        private readonly ILineParser _lineParser;
        private readonly TransformationPerformer _transformationPerformer;
        private readonly LogModelFacade _logModelFacade;
        private readonly TimeIndex _timeIndex;
        private Stream _fileHolder;

        public DocumentViewModel(
            LineProvider lineProvider,
            LogModelFacade logModelFacade,
            LogViewModel logViewModel, 
            SearchViewModel searchViewModel,
            Selection markedLines,
            ColorSettings colorSettings,
            TransformationPerformer transformationPerformer,
            TextViewSharedFoldingState foldingState,
            TimeIndex timeIndex)
        {
            var logFileFilePath = logModelFacade.LogFile.FilePath;
            
            Title = 
                Path.GetFileName(logFileFilePath) 
                    ?? throw new InvalidOperationException($"Invalid path: {logFileFilePath}");

            LogViewModel = logViewModel;
            SearchViewModel = searchViewModel;
            ColorSettings = colorSettings;
            FoldingState = foldingState;
            
            SearchViewModel.CurrentLineChanged += lineNumber => NavigateTo(lineNumber);
            SearchViewModel.CurrentSearchChanged += regex => LogViewModel.HighlightRegex = regex;
            SearchViewModel.PropertyChanged += OnSearchViewModelPropertyChanged;
            LogViewModel.SetSearchMatches(SearchViewModel.CurrentMatchBuckets);
            LogViewModel.SetSearchMatchLine(SearchViewModel.CurrentMatchLine);
            LogViewModel.SetSearchSelectedLines(SearchViewModel.CurrentSelectedMatchLines);

            _markedLines = markedLines;
            _transformationPerformer = transformationPerformer;
            _lineProvider = lineProvider;
            _lineParser = logModelFacade.LineParser;
            _logModelFacade = logModelFacade;
            _timeIndex = timeIndex;
            _markedLines.Changed += () => MarkedLinesChanged?.Invoke();
            _fileHolder = logModelFacade.LogFile.Open();

            CopyPathToClipboardCommand =
                new DelegateCommand(() => TextCopy.ClipboardService.SetText(logFileFilePath));

            CopyFilenameToClipboardCommand = new DelegateCommand(() => TextCopy.ClipboardService.SetText(Path.GetFileName(logFileFilePath)));

            OpenContainingFolderCommand = new DelegateCommand(() => OpenContainingFolder(logFileFilePath));
            FindNextCommand = new DelegateCommand(() => SearchViewModel.FindNext(GetSearchAnchor()));
            FindPreviousCommand = new DelegateCommand(() => SearchViewModel.FindPrevious(GetSearchAnchor()));
            DocumentId = logFileFilePath;
        }

        private int GetSearchAnchor()
        {
            var selectionLine = LogViewModel.CurrentOriginalLine;
            if (selectionLine >= 0)
                return selectionLine;

            return SearchViewModel.CurrentMatchLine;
        }

        public string DocumentId { get; }
    
        public event Action? MarkedLinesChanged;
        
        public ICommand CopyPathToClipboardCommand { get; }

        public ICommand CopyFilenameToClipboardCommand { get; }

        public ICommand OpenContainingFolderCommand { get; }

        public ICommand FindNextCommand { get; }

        public ICommand FindPreviousCommand { get; }

        public void NavigateTo(int lineNumber)
        {
            LogViewModel.NavigateTo(lineNumber, true);
        }

        private void OnSearchViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchViewModel.CurrentMatchBuckets))
                LogViewModel.SetSearchMatches(SearchViewModel.CurrentMatchBuckets);

            if (e.PropertyName == nameof(SearchViewModel.CurrentMatchLine))
                LogViewModel.SetSearchMatchLine(SearchViewModel.CurrentMatchLine);

            if (e.PropertyName == nameof(SearchViewModel.CurrentSelectedMatchLines))
                LogViewModel.SetSearchSelectedLines(SearchViewModel.CurrentSelectedMatchLines);
        }
        
        public ObservableCollection<(int number, string text)> MarkedLineViewModels
        {
            get
            {
                var lineNumbers = _markedLines.ToList();
                lineNumbers.Sort();
                var collection = new ObservableCollection<(int number, string text)>();
                foreach (var lineNumber in lineNumbers)
                {
                    var lines = new (int, string)[1];
                    _lineProvider.Fetch(lineNumber, lines.AsSpan());
                    collection.Add((lineNumber, _transformationPerformer.Transform(lines[0].Item2)));
                }

                return collection;
            }
        }

        public Selection MarkedLines => _markedLines;        
        
        public string Title { get; }

        public LogViewModel LogViewModel { get; }

        public SearchViewModel SearchViewModel { get; }

        public LogMetaInformation MetaInformation => _logModelFacade.MetaInformation;

        public Indexer Indexer => _logModelFacade.Indexer;

        internal ILineParser LineParser => _lineParser;

        public ColorSettings ColorSettings { get; }

        public TextViewSharedFoldingState FoldingState { get; }

        public TimeIndex TimeIndex => _timeIndex;

        public bool HasTime => _timeIndex.HasTime;

        public int LineCount => _logModelFacade.LineCount;

        public string GetTransformedLine(int lineNumber)
        {
            var lines = new (int, string)[1];
            _lineProvider.Fetch(lineNumber, lines.AsSpan());
            return _transformationPerformer.Transform(lines[0].Item2);
        }

        public IReadOnlyList<int> FindMatchingLines(Regex regex, CancellationToken cancellationToken)
        {
            const int chunkSize = 4096;
            var total = _lineProvider.Count;
            var matches = new List<int>();
            var buffer = new (int, string)[chunkSize];

            for (var start = 0; start < total; start += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(chunkSize, total - start);
                _lineProvider.Fetch(start, buffer.AsSpan(0, count));

                for (var i = 0; i < count; i++)
                {
                    if (regex.IsMatch(buffer[i].Item2))
                        matches.Add(start + i);
                }
            }

            return matches;
        }

        public int GetFoldingComponentIndex(string transformedText)
        {
            var parseResult = _lineParser.Parse(transformedText);
            var lineMeta = parseResult.Get().ParsedLineComponents;
            for (var i = 0; i < parseResult.ComponentCount; i++)
            {
                var length = lineMeta.ComponentLength(i);
                if (length <= 0)
                    continue;

                var componentText = transformedText.Substring(lineMeta.ComponentStart(i), length);
                if (TextOperations.GetStructuredRanges(componentText).Count != 0)
                    return i;
            }

            return 0;
        }

        public bool IsCurrentDocument
        {
            get => _isCurrentDocument;
            set => SetAndRaiseIfChanged(ref _isCurrentDocument, value);
        }
        
        private void OpenContainingFolder(string path)
        {
            var filePath = path;

            var cmdLine = File.Exists(filePath)
                ? $"/select, {filePath}"
                : $"/select, {Directory.GetParent(filePath)?.FullName}";

            _ = Process.Start("explorer.exe", cmdLine);
        }

        public void CloseFile()
        {
            _fileHolder.Dispose();
        }
    }
}