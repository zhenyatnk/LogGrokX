using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Data.Symbols;

public abstract record SymbolPathElement;

public sealed record SymbolDirectory(string Path) : SymbolPathElement;

// srv*[cache*...]*server: the server is an http(s) URL or a symbol store directory (UNC or local),
// the caches receive copies of the downloaded files.
public sealed record SymbolServer(string Server, IReadOnlyList<string> Caches) : SymbolPathElement
{
    public bool IsHttp => Server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          Server.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

// Parses a WinDbg / _NT_SYMBOL_PATH style symbol path:
// "C:\symbols;cache*C:\cache;srv*C:\cache*https://msdl.microsoft.com/download/symbols".
public sealed class SymbolPath
{
    public const string MicrosoftSymbolServer = "https://msdl.microsoft.com/download/symbols";

    private SymbolPath(IReadOnlyList<SymbolPathElement> elements) => Elements = elements;

    public IReadOnlyList<SymbolPathElement> Elements { get; }

    public static SymbolPath Empty { get; } = new(Array.Empty<SymbolPathElement>());

    public static SymbolPath Parse(string? path, string defaultCache)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Empty;

        var elements = new List<SymbolPathElement>();
        var caches = new List<string>();
        foreach (var rawElement in Environment.ExpandEnvironmentVariables(path).Split(';'))
        {
            var element = rawElement.Trim().Trim('"');
            if (element.Length == 0)
                continue;

            var parts = element.Split('*').Select(part => part.Trim().Trim('"')).ToList();
            var kind = parts[0].ToLowerInvariant();
            if (parts.Count == 1)
            {
                elements.Add(new SymbolDirectory(element));
                continue;
            }

            if (kind == "cache")
            {
                var cache = parts.Skip(1).FirstOrDefault(part => part.Length > 0) ?? defaultCache;
                if (!caches.Contains(cache, StringComparer.OrdinalIgnoreCase))
                    caches.Add(cache);
                continue;
            }

            if (kind is "symsrv")
                parts.RemoveRange(0, Math.Min(2, parts.Count));
            else if (kind is "srv")
                parts.RemoveAt(0);
            else
            {
                elements.Add(new SymbolDirectory(element));
                continue;
            }

            var stores = parts.ToList();
            var server = stores.Count > 0 ? stores[^1] : string.Empty;
            if (stores.Count > 0)
                stores.RemoveAt(stores.Count - 1);
            if (server.Length == 0)
                server = MicrosoftSymbolServer;

            var serverCaches = stores.Select(store => store.Length == 0 ? defaultCache : store).ToList();
            if (serverCaches.Count == 0)
                serverCaches.AddRange(caches.Count > 0 ? caches : [defaultCache]);
            elements.Add(new SymbolServer(server, serverCaches.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return new SymbolPath(elements);
    }
}
