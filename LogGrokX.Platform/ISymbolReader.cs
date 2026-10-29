namespace LogGrokX.Platform;

public sealed record SymbolInfo(string Function, ulong Displacement, string? File, int Line);

public interface ISymbolReader
{
    // Resolves a relative virtual address of the module described by the PDB file.
    SymbolInfo? Resolve(string pdbPath, ulong offset);

    void Reset();
}
