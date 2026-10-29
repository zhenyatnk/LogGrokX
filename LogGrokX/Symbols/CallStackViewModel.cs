using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LogGrokX.Data.Symbols;
using LogGrokX.Localization;
using LogGrokX.Platform;

namespace LogGrokX.Symbols;

public sealed class CallStackViewModel : ViewModelBase
{
    private readonly CallStackSymbolService _symbols;
    private readonly IDesktopService _desktop;
    private readonly IUiDispatcher _dispatcher;
    private CallStackFrameViewModel? _selectedFrame;
    private IReadOnlyList<SourceLineViewModel> _sourceLines = Array.Empty<SourceLineViewModel>();
    private SourceLineViewModel? _currentSourceLine;
    private string? _sourcePath;
    private string? _sourceMessage;
    private int _sourceRequest;
    private bool _isResolving;

    public CallStackViewModel(IReadOnlyList<CallStackFrame> frames, CallStackSymbolService symbols, IDesktopService desktop,
        IUiDispatcher dispatcher)
    {
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Frames = new ObservableCollection<CallStackFrameViewModel>(frames.Select(frame => new CallStackFrameViewModel(frame)));
        ShowSourceCommand = new DelegateCommand(parameter =>
        {
            if (parameter is CallStackFrameViewModel frame)
                SelectedFrame = frame;
        });
        OpenSourceCommand = new DelegateCommand(() =>
        {
            if (_sourcePath != null)
                _desktop.OpenPath(_sourcePath);
        });
        CopyCommand = new DelegateCommand(() => _desktop.SetClipboardText(GetText()));
        RefreshCommand = new DelegateCommand(() =>
        {
            if (_isResolving)
                return;
            _symbols.Refresh();
            Resolve();
        });
        Resolve();
    }

    public ObservableCollection<CallStackFrameViewModel> Frames { get; }

    public ICommand ShowSourceCommand { get; }

    public ICommand OpenSourceCommand { get; }

    public ICommand CopyCommand { get; }

    public ICommand RefreshCommand { get; }

    public string SymbolPath => string.IsNullOrWhiteSpace(_symbols.Options.EffectiveSymbolPath)
        ? Strings.Get("SymbolPathNotConfigured")
        : _symbols.Options.EffectiveSymbolPath;

    public bool IsResolving
    {
        get => _isResolving;
        private set => SetAndRaiseIfChanged(ref _isResolving, value);
    }

    public string Status => IsResolving
        ? Strings.Get("ResolvingSymbols")
        : Strings.Format("ResolvedFrames", Frames.Count(frame => frame.Status == FrameSymbolStatus.Resolved), Frames.Count);

    public CallStackFrameViewModel? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            if (ReferenceEquals(_selectedFrame, value))
                return;
            _selectedFrame = value;
            InvokePropertyChanged();
            LoadSource(value);
        }
    }

    public IReadOnlyList<SourceLineViewModel> SourceLines
    {
        get => _sourceLines;
        private set => SetAndRaiseIfChanged(ref _sourceLines, value);
    }

    public SourceLineViewModel? CurrentSourceLine
    {
        get => _currentSourceLine;
        set => SetAndRaiseIfChanged(ref _currentSourceLine, value);
    }

    public string? SourcePath
    {
        get => _sourcePath;
        private set
        {
            SetAndRaiseIfChanged(ref _sourcePath, value);
            InvokePropertyChanged(nameof(HasSourceFile));
        }
    }

    public bool HasSourceFile => _sourcePath != null;

    public string? SourceMessage
    {
        get => _sourceMessage;
        private set => SetAndRaiseIfChanged(ref _sourceMessage, value);
    }

    public Task Completion { get; private set; } = Task.CompletedTask;

    public string GetText() => string.Join(Environment.NewLine, Frames.Select(frame => frame.Text));

    private void Resolve()
    {
        IsResolving = true;
        InvokePropertyChanged(nameof(Status));
        foreach (var frame in Frames)
            frame.Update(FrameSymbol.Pending);

        var tasks = Frames.Select(frame => _symbols.ResolveAsync(frame.Frame)
            .ContinueWith(task => OnUi(() => frame.Update(task.Result)), TaskScheduler.Default)).ToArray();
        Completion = Task.WhenAll(tasks).ContinueWith(_ => OnUi(() =>
        {
            IsResolving = false;
            InvokePropertyChanged(nameof(Status));
            if (SelectedFrame != null)
                LoadSource(SelectedFrame);
        }), TaskScheduler.Default);
    }

    private void LoadSource(CallStackFrameViewModel? frame)
    {
        var request = ++_sourceRequest;
        SourceLines = Array.Empty<SourceLineViewModel>();
        CurrentSourceLine = null;
        SourcePath = null;
        SourceMessage = null;
        if (frame?.Symbol is not { HasSource: true } symbol)
            return;

        SourceMessage = Strings.Get("LoadingSource");
        _symbols.FindSourceAsync(symbol).ContinueWith(task =>
        {
            var result = task.Result;
            string[]? lines = null;
            var message = result.Message;
            if (result.Path != null)
            {
                try
                {
                    lines = File.ReadAllLines(result.Path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    message = e.Message;
                }
            }

            OnUi(() =>
            {
                if (request != _sourceRequest)
                    return;

                SourcePath = lines == null ? null : result.Path;
                SourceMessage = lines == null ? message ?? Strings.Format("SourceFileNotFound", symbol.Symbol!.File!) : null;
                if (lines == null)
                    return;

                var current = symbol.Symbol!.Line;
                SourceLines = lines.Select((text, index) => new SourceLineViewModel(index + 1, text, index + 1 == current)).ToArray();
                CurrentSourceLine = SourceLines.FirstOrDefault(line => line.IsCurrent);
            });
        }, TaskScheduler.Default);
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Post(action);
    }
}

public sealed class CallStackFrameViewModel(CallStackFrame frame) : ViewModelBase
{
    private FrameSymbol _symbol = FrameSymbol.Pending;

    public CallStackFrame Frame { get; } = frame;

    public int Number => Frame.Number;

    public string Module => Frame.Module;

    public string Address => $"0x{Frame.Offset:x}";

    public FrameSymbol? Symbol => _symbol.Status == FrameSymbolStatus.Pending ? null : _symbol;

    public FrameSymbolStatus Status => _symbol.Status;

    public string Function => _symbol.Symbol is { } symbol
        ? symbol.Displacement == 0 ? symbol.Function : $"{symbol.Function}+0x{symbol.Displacement:x}"
        : _symbol.Status switch
        {
            FrameSymbolStatus.Pending => Strings.Get("ResolvingSymbols"),
            FrameSymbolStatus.NoSymbols => Strings.Get("SymbolsNotFound"),
            _ => Strings.Get("SymbolNotResolved")
        };

    public string Location => _symbol.HasSource ? $"{_symbol.Symbol!.File} @ {_symbol.Symbol.Line}" : string.Empty;

    public bool HasSource => _symbol.HasSource;

    public string Details => string.Join(Environment.NewLine,
        new[] { _symbol.PdbPath }.Concat(_symbol.PdbPath == null ? _symbol.Attempts : Array.Empty<string>())
            .Where(line => !string.IsNullOrEmpty(line)));

    public string Text => $"{Number,2}# {_symbol.FormatFrame(Frame)}";

    internal void Update(FrameSymbol symbol)
    {
        _symbol = symbol;
        RaiseAllPropertiesChanged();
    }
}

public sealed record SourceLineViewModel(int Number, string Text, bool IsCurrent);
