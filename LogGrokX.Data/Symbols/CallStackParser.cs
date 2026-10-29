using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LogGrokX.Data.Symbols;

public static partial class CallStackParser
{
    // " 0# module.dll!GUID,age!rva" (the GUID and age identify the PDB on a symbol server),
    // " 0# module.dll!rva" and " 0# module.dll+0xrva".
    [GeneratedRegex(
        @"^[ \t]*(?<number>\d+)#[ \t]*(?<module>[^\s!+#]+)(?:!(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}),(?<age>[0-9A-Fa-f]{1,8})!(?:0[xX])?(?<offset>[0-9A-Fa-f]{1,16})|!(?:0[xX])?(?<offset>[0-9A-Fa-f]{1,16})|\+0[xX](?<offset>[0-9A-Fa-f]{1,16}))[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FrameRegex();

    public static bool ContainsCallStack(string text) =>
        text.Contains('#') && FrameRegex().IsMatch(text);

    public static IReadOnlyList<CallStackFrame> Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('#'))
            return Array.Empty<CallStackFrame>();

        var frames = new List<CallStackFrame>();
        foreach (Match match in FrameRegex().Matches(text))
        {
            if (!int.TryParse(match.Groups["number"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                !ulong.TryParse(match.Groups["offset"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var offset))
                continue;

            var module = match.Groups["module"].Value;
            PdbIdentity? pdb = null;
            if (match.Groups["guid"].Success &&
                Guid.TryParse(match.Groups["guid"].ValueSpan, out var signature) &&
                int.TryParse(match.Groups["age"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var age))
                pdb = new PdbIdentity(PdbIdentity.GetPdbFileName(module), signature, age);

            var value = match.Value.TrimEnd('\r', ' ', '\t');
            var leading = value.Length - value.TrimStart(' ', '\t').Length;
            frames.Add(new CallStackFrame(number, module, pdb, offset, match.Index + leading, value.Length - leading));
        }

        return frames;
    }
}
