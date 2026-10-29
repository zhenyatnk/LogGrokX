using System;
using System.IO;

namespace LogGrokX.Data.Symbols;

public sealed record CallStackFrame(int Number, string Module, PdbIdentity? Pdb, ulong Offset, int Start, int Length)
{
    public string PdbFileName => Pdb?.FileName ?? PdbIdentity.GetPdbFileName(Module);

    public override string ToString() => $"{Number}# {Module}!{Offset:x}";
}

public sealed record PdbIdentity(string FileName, Guid Signature, int Age)
{
    // Symbol server index of a PDB: GUID without dashes followed by the age in hexadecimal.
    public string Index => Signature.ToString("N").ToUpperInvariant() + Age.ToString("X");

    public static string GetPdbFileName(string module) =>
        Path.GetFileNameWithoutExtension(module.Replace('\\', '/').Split('/')[^1]) + ".pdb";
}
