using System.Collections.Generic;
using LogGrokX.Data.Symbols;
using LogGrokX.Platform;

namespace LogGrokX.Symbols;

public enum FrameSymbolStatus
{
    Pending,
    Resolved,
    NoSymbols,
    NotResolved
}

public sealed record FrameSymbol(FrameSymbolStatus Status, SymbolInfo? Symbol, string? PdbPath,
    IReadOnlyList<string> Attempts)
{
    public static FrameSymbol Pending { get; } = new(FrameSymbolStatus.Pending, null, null, []);

    public bool HasSource => Symbol is { File.Length: > 0 };

    public string FormatFrame(CallStackFrame frame)
    {
        if (Symbol == null)
            return $"{frame.Module}!{frame.Offset:x}";

        var function = Symbol.Displacement == 0
            ? $"{frame.Module}!{Symbol.Function}"
            : $"{frame.Module}!{Symbol.Function}+0x{Symbol.Displacement:x}";
        return HasSource ? $"{function} [{Symbol.File} @ {Symbol.Line}]" : function;
    }
}

public sealed record SourceFileResult(string? Path, string? Message);
