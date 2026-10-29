using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data.Symbols;
using LogGrokX.Platform;

namespace LogGrokX.Symbols;

// Resolves call stack frames ("0# module.dll!GUID,age!rva") to function names and source lines: finds the PDB along
// the symbol path (downloading it from symbol servers into the cache), reads it with the platform symbol reader and
// locates the source files through the source path and the source server data of the PDB (#89).
public sealed class CallStackSymbolService : IDisposable
{
    private static readonly TimeSpan SourceCommandTimeout = TimeSpan.FromMinutes(2);

    private readonly ISymbolReader _reader;
    private readonly ICallStackPresenter _presenter;
    private readonly IDesktopService _desktop;
    private readonly IUiDispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly Action _onOptionsChanged;
    private readonly ConcurrentDictionary<(string file, PdbIdentity? pdb), Lazy<Task<SymbolSearchResult>>> _pdbs = new();
    private readonly ConcurrentDictionary<(string module, PdbIdentity? pdb, ulong offset), Lazy<Task<FrameSymbol>>> _frames = new();
    private readonly ConcurrentDictionary<string, Lazy<SourceServerData?>> _sourceServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<SourceFileResult>>> _sources = new(StringComparer.OrdinalIgnoreCase);
    private SymbolOptions _options;
    private SymbolStore _store;
    private int _version;

    public CallStackSymbolService(ISymbolReader reader, ICallStackPresenter presenter, IDesktopService desktop,
        IUiDispatcher dispatcher)
        : this(reader, presenter, desktop, dispatcher, new HttpClientHandler())
    {
    }

    internal CallStackSymbolService(ISymbolReader reader, ICallStackPresenter presenter, IDesktopService desktop,
        IUiDispatcher dispatcher, HttpMessageHandler handler)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Microsoft-Symbol-Server/10.0.0.0");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LogGrokX");
        _options = SymbolOptions.Current;
        _store = CreateStore(_options);
        _onOptionsChanged = OnOptionsChanged;
        SymbolOptions.CurrentChanged += _onOptionsChanged;
    }

    public int Version => Volatile.Read(ref _version);

    public bool IsEnabled => SymbolOptions.Current.Enabled;

    public SymbolOptions Options => _options;

    public event Action? Reset;

    public FrameSymbol Get(CallStackFrame frame)
    {
        var task = GetTask(frame);
        return task.IsCompletedSuccessfully ? task.Result : FrameSymbol.Pending;
    }

    public Task<FrameSymbol> ResolveAsync(CallStackFrame frame) => GetTask(frame);

    public Task<FrameSymbol[]> ResolveAllAsync(IEnumerable<CallStackFrame> frames) =>
        Task.WhenAll(frames.Select(GetTask));

    public Task<SourceFileResult> FindSourceAsync(FrameSymbol symbol)
    {
        if (symbol.Symbol?.File is not { Length: > 0 } file)
            return Task.FromResult(new SourceFileResult(null, null));

        var key = (symbol.PdbPath ?? string.Empty) + "|" + file;
        return _sources.GetOrAdd(key, _ => new Lazy<Task<SourceFileResult>>(
            () => Task.Run(() => FindSource(symbol.PdbPath, file, _options)))).Value;
    }

    public void ShowCallStack(string text)
    {
        var frames = CallStackParser.Parse(text);
        if (frames.Count > 0)
            _presenter.Show(new CallStackViewModel(frames, this, _desktop, _dispatcher));
    }

    public void Dispose()
    {
        _http.Dispose();
        _reader.Reset();
    }

    private Task<FrameSymbol> GetTask(CallStackFrame frame) =>
        _frames.GetOrAdd((frame.Module.ToLowerInvariant(), frame.Pdb, frame.Offset),
            _ => new Lazy<Task<FrameSymbol>>(() => ResolveFrameAsync(frame, _store))).Value;

    private async Task<FrameSymbol> ResolveFrameAsync(CallStackFrame frame, SymbolStore store)
    {
        try
        {
            var search = await _pdbs.GetOrAdd((frame.PdbFileName.ToLowerInvariant(), frame.Pdb),
                    _ => new Lazy<Task<SymbolSearchResult>>(() =>
                        Task.Run(() => store.FindAsync(frame.PdbFileName, frame.Pdb, CancellationToken.None))))
                .Value.ConfigureAwait(false);
            if (search.Path == null)
                return new FrameSymbol(FrameSymbolStatus.NoSymbols, null, null, search.Attempts);

            var symbol = await Task.Run(() => _reader.Resolve(search.Path, frame.Offset)).ConfigureAwait(false);
            return new FrameSymbol(symbol == null ? FrameSymbolStatus.NotResolved : FrameSymbolStatus.Resolved, symbol,
                search.Path, search.Attempts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or
                                      InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Cannot resolve {frame}: {e}");
            return new FrameSymbol(FrameSymbolStatus.NoSymbols, null, null, [e.Message]);
        }
    }

    private SourceFileResult FindSource(string? pdbPath, string file, SymbolOptions options)
    {
        var local = SourcePathResolver.Find(file, SourcePathResolver.ParseRoots(options.SourcePath));
        if (local != null)
            return new SourceFileResult(local, null);

        if (pdbPath == null || !SourcePathResolver.UsesSourceServer(options.SourcePath))
            return new SourceFileResult(null, Localization.Strings.Format("SourceFileNotFound", file));

        var data = _sourceServers.GetOrAdd(pdbPath, path => new Lazy<SourceServerData?>(() =>
            PdbFile.ReadSourceServerStream(path) is { } text ? SourceServerData.Parse(text) : null)).Value;
        var targetRoot = Path.Combine(options.EffectiveCachePath, "src");
        if (data == null || !data.TryGetTarget(file, targetRoot, out var target))
            return new SourceFileResult(null, Localization.Strings.Format("SourceFileNotFound", file));

        if (Uri.TryCreate(target.Target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return DownloadSource(uri, targetRoot);

        if (File.Exists(target.Target))
            return new SourceFileResult(target.Target, null);

        if (target.Command == null)
            return new SourceFileResult(null, Localization.Strings.Format("SourceFileNotFound", target.Target));

        if (!options.AllowSourceServerCommands)
            return new SourceFileResult(null, Localization.Strings.Format("SourceServerCommandDisabled", target.Command));

        return RunSourceCommand(target);
    }

    private SourceFileResult DownloadSource(Uri uri, string targetRoot)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => string.Concat(Uri.UnescapeDataString(segment).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)))
            .Where(segment => segment is not ("." or ".."));
        var target = Path.Combine([targetRoot, uri.Host, .. segments]);
        if (File.Exists(target))
            return new SourceFileResult(target, null);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var content = _http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            File.WriteAllBytes(target, content);
            return new SourceFileResult(target, null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return new SourceFileResult(null, $"{uri}: {e.Message}");
        }
    }

    private static SourceFileResult RunSourceCommand(SourceServerTarget target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target.Target)!);
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + target.Command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            if (process == null)
                return new SourceFileResult(null, target.Command);

            var error = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(SourceCommandTimeout))
            {
                process.Kill(entireProcessTree: true);
                return new SourceFileResult(null, target.Command);
            }

            return File.Exists(target.Target)
                ? new SourceFileResult(target.Target, null)
                : new SourceFileResult(null, $"{target.Command}{Environment.NewLine}{error.GetAwaiter().GetResult()}".Trim());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new SourceFileResult(null, $"{target.Command}: {e.Message}");
        }
    }

    private SymbolStore CreateStore(SymbolOptions options) =>
        new(SymbolPath.Parse(options.EffectiveSymbolPath, options.EffectiveCachePath), _http);

    // Forgets downloaded data and failures, so the next request searches the symbol path again.
    public void Refresh()
    {
        _options = SymbolOptions.Current;
        _store = CreateStore(_options);
        _pdbs.Clear();
        _frames.Clear();
        _sourceServers.Clear();
        _sources.Clear();
        _reader.Reset();
        Interlocked.Increment(ref _version);
        Reset?.Invoke();
    }

    private void OnOptionsChanged()
    {
        if (SymbolOptions.Current != _options)
            Refresh();
    }
}
