using System;
using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data;
using LogGrokX.Platform;
using LogGrokX.Symbols;

namespace LogGrokX;

public sealed class DisplaySettingsMonitor : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _onChanged;
    private readonly object _sync = new();
    private readonly List<WeakReference<LinePartViewModel>> _clients = new();
    private IDisposable? _pending;
    private bool _queued;
    private bool _disposed;

    public DisplaySettingsMonitor(IUiDispatcher dispatcher, CallStackSymbolService? symbols = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Symbols = symbols;
        _onChanged = OnChanged;
        BinaryDetectionOptions.CurrentChanged += _onChanged;
        ValueMapper.CurrentChanged += _onChanged;
        SymbolOptions.CurrentChanged += _onChanged;
        if (symbols != null)
            symbols.Reset += _onChanged;
    }

    internal CallStackSymbolService? Symbols { get; }

    internal void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        lock (_sync)
        {
            if (_disposed) return;
            try { _dispatcher.Post(action); }
            catch (InvalidOperationException) { }
        }
    }

    internal void Register(LinePartViewModel client)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clients.Count % 256 == 0)
                _clients.RemoveAll(reference => !reference.TryGetTarget(out _));
            _clients.Add(new WeakReference<LinePartViewModel>(client));
        }
    }

    private void OnChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            Refresh();
            return;
        }

        lock (_sync)
        {
            if (_disposed || _queued) return;
            _queued = true;
            try { _pending = _dispatcher.Post(Refresh); }
            catch (InvalidOperationException) { _queued = false; }
        }
    }

    private void Refresh()
    {
        LinePartViewModel[] clients;
        lock (_sync)
        {
            _pending = null;
            _queued = false;
            if (_disposed) return;
            _clients.RemoveAll(reference => !reference.TryGetTarget(out _));
            clients = _clients.Select(reference => reference.TryGetTarget(out var client) ? client : null)
                .OfType<LinePartViewModel>().ToArray();
        }

        foreach (var client in clients)
            client.RefreshSettingsIfNeeded();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
            _clients.Clear();
        }
        BinaryDetectionOptions.CurrentChanged -= _onChanged;
        ValueMapper.CurrentChanged -= _onChanged;
        if (Symbols != null)
            Symbols.Reset -= _onChanged;
    }
}
