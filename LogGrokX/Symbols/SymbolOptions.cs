using System;
using System.Collections.Generic;
using System.IO;

namespace LogGrokX.Symbols;

public sealed record SymbolOptions(bool Enabled, string SymbolPath, string SourcePath, string CachePath,
    bool AllowSourceServerCommands)
{
    public static SymbolOptions Default { get; } = FromSettings(new SymbolSettings());

    private static readonly List<WeakReference<Action>> CurrentChangedHandlers = new();
    private static SymbolOptions _current = Default;

    public static SymbolOptions Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            CurrentVersion++;
            RaiseCurrentChanged();
        }
    }

    public static int CurrentVersion { get; private set; }

    public static event Action CurrentChanged
    {
        add
        {
            if (value == null)
                return;
            lock (CurrentChangedHandlers)
                CurrentChangedHandlers.Add(new WeakReference<Action>(value));
        }
        remove { }
    }

    public string EffectiveSymbolPath =>
        string.IsNullOrWhiteSpace(SymbolPath) ? Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH") ?? string.Empty : SymbolPath;

    public string EffectiveCachePath =>
        string.IsNullOrWhiteSpace(CachePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogGrokX", "Symbols")
            : Environment.ExpandEnvironmentVariables(CachePath.Trim());

    public static SymbolOptions FromSettings(SymbolSettings settings) =>
        new(settings.Enabled, settings.SymbolPath?.Trim() ?? string.Empty, settings.SourcePath?.Trim() ?? string.Empty,
            settings.CachePath?.Trim() ?? string.Empty, settings.AllowSourceServerCommands);

    private static void RaiseCurrentChanged()
    {
        Action[] targets;
        lock (CurrentChangedHandlers)
        {
            CurrentChangedHandlers.RemoveAll(handler => !handler.TryGetTarget(out _));
            targets = new Action[CurrentChangedHandlers.Count];
            for (var i = 0; i < CurrentChangedHandlers.Count; i++)
                CurrentChangedHandlers[i].TryGetTarget(out targets[i]!);
        }

        foreach (var target in targets)
            target?.Invoke();
    }
}
