using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LogGrokX.Data.Symbols;

public sealed record SymbolSearchResult(string? Path, IReadOnlyList<string> Attempts);

// Looks a PDB up along a symbol path the way symsrv does: flat directories, symbol store layout
// "<file>/<GUID><age>/<file>", downstream caches and http(s) symbol servers (with "file.ptr" redirects).
public sealed class SymbolStore
{
    private readonly SymbolPath _path;
    private readonly HttpClient? _http;

    public SymbolStore(SymbolPath path, HttpClient? http)
    {
        _path = path;
        _http = http;
    }

    public async Task<SymbolSearchResult> FindAsync(string fileName, PdbIdentity? pdb, CancellationToken cancellationToken)
    {
        var attempts = new List<string>();
        foreach (var element in _path.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = element switch
            {
                SymbolDirectory directory => FindInDirectory(directory.Path, fileName, pdb, attempts),
                SymbolServer server when pdb != null => await FindOnServerAsync(server, pdb, attempts, cancellationToken)
                    .ConfigureAwait(false),
                _ => null
            };
            if (found != null)
                return new SymbolSearchResult(found, attempts);
        }

        return new SymbolSearchResult(null, attempts);
    }

    private static string? FindInDirectory(string directory, string fileName, PdbIdentity? pdb, List<string> attempts)
    {
        if (pdb != null)
        {
            var stored = Path.Combine(directory, pdb.FileName, pdb.Index, pdb.FileName);
            attempts.Add(stored);
            if (File.Exists(stored))
                return stored;
        }

        var flat = Path.Combine(directory, fileName);
        attempts.Add(flat);
        if (!File.Exists(flat))
            return null;

        if (pdb == null)
            return flat;

        return PdbFile.TryReadIdentity(flat, out var signature, out var age, out var dbiAge) &&
               signature == pdb.Signature && (age == pdb.Age || dbiAge == pdb.Age)
            ? flat
            : null;
    }

    private async Task<string?> FindOnServerAsync(SymbolServer server, PdbIdentity pdb, List<string> attempts,
        CancellationToken cancellationToken)
    {
        var relative = Path.Combine(pdb.FileName, pdb.Index, pdb.FileName);
        foreach (var cache in server.Caches)
        {
            var cached = Path.Combine(cache, relative);
            attempts.Add(cached);
            if (File.Exists(cached))
                return cached;
        }

        if (!server.IsHttp)
        {
            var stored = Path.Combine(server.Server, relative);
            attempts.Add(stored);
            if (File.Exists(stored))
                return stored;

            var pointer = ReadPointer(Path.Combine(server.Server, pdb.FileName, pdb.Index, "file.ptr"));
            return pointer != null && File.Exists(pointer) ? pointer : null;
        }

        if (_http == null)
            return null;

        var baseUrl = server.Server.TrimEnd('/') + "/" + Uri.EscapeDataString(pdb.FileName) + "/" + pdb.Index + "/";
        var url = baseUrl + Uri.EscapeDataString(pdb.FileName);
        attempts.Add(url);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await SaveAsync(response, server, relative, cancellationToken).ConfigureAwait(false);

            attempts.Add($"{url}: {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.StatusCode != HttpStatusCode.NotFound)
                return null;

            var pointerUrl = baseUrl + "file.ptr";
            attempts.Add(pointerUrl);
            using var pointerResponse = await _http.GetAsync(pointerUrl, cancellationToken).ConfigureAwait(false);
            if (!pointerResponse.IsSuccessStatusCode)
                return null;

            var pointer = ParsePointer(await pointerResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (pointer == null)
                return null;
            attempts.Add(pointer);
            return File.Exists(pointer) ? pointer : null;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException ||
                                  e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            attempts.Add($"{url}: {e.Message}");
            return null;
        }
    }

    private static async Task<string?> SaveAsync(HttpResponseMessage response, SymbolServer server, string relative,
        CancellationToken cancellationToken)
    {
        if (server.Caches.Count == 0)
            return null;

        var target = Path.Combine(server.Caches[0], relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string? ReadPointer(string path)
    {
        try
        {
            return File.Exists(path) ? ParsePointer(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? ParsePointer(string content)
    {
        content = content.Trim();
        return content.StartsWith("PATH:", StringComparison.OrdinalIgnoreCase) ? content[5..].Trim() : null;
    }
}
