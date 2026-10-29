using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LogGrokX.Platform;

// Loads PDB files directly (without the binaries) into an isolated DbgHelp session and resolves
// relative addresses to function names and source lines. DbgHelp is single-threaded, so every call is serialized.
public sealed class DbgHelpSymbolReader : ISymbolReader, IDisposable
{
    private const uint SymOptUndname = 0x00000002;
    private const uint SymOptLoadLines = 0x00000010;
    private const uint SymOptFailCriticalErrors = 0x00000200;
    private const uint SymOptIgnoreNtSymPath = 0x00001000;
    private const uint SymOptNoPrompts = 0x00080000;
    private const ulong ModuleSize = 0x80000000;
    private const ulong ModuleBaseStep = 0x100000000;
    private const int SymbolInfoSize = 88;
    private const int MaxNameLength = 2000;

    private readonly object _sync = new();
    private readonly IntPtr _session = new(0x4C47524B);
    private readonly Dictionary<string, ulong> _modules = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;
    private ulong _nextBase = ModuleBaseStep;

    public SymbolInfo? Resolve(string pdbPath, ulong offset)
    {
        if (offset >= ModuleSize)
            return null;

        lock (_sync)
        {
            var moduleBase = LoadModule(pdbPath);
            if (moduleBase == 0)
                return null;

            var address = moduleBase + offset;
            var buffer = Marshal.AllocHGlobal(SymbolInfoSize + MaxNameLength * sizeof(char));
            try
            {
                Marshal.Copy(new byte[SymbolInfoSize], 0, buffer, SymbolInfoSize);
                Marshal.WriteInt32(buffer, 0, SymbolInfoSize);
                Marshal.WriteInt32(buffer, 80, MaxNameLength);
                if (!SymFromAddrW(_session, address, out var displacement, buffer))
                    return null;

                var nameLength = Math.Min(Marshal.ReadInt32(buffer, 76), MaxNameLength);
                var name = Marshal.PtrToStringUni(buffer + 84, nameLength);
                var line = new ImagehlpLineW64 { SizeOfStruct = (uint)Marshal.SizeOf<ImagehlpLineW64>() };
                return SymGetLineFromAddrW64(_session, address, out _, ref line)
                    ? new SymbolInfo(name, displacement, Marshal.PtrToStringUni(line.FileName), (int)line.LineNumber)
                    : new SymbolInfo(name, displacement, null, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            if (_initialized)
                SymCleanup(_session);
            _initialized = false;
            _modules.Clear();
            _nextBase = ModuleBaseStep;
        }
    }

    public void Dispose() => Reset();

    private ulong LoadModule(string pdbPath)
    {
        if (_modules.TryGetValue(pdbPath, out var loaded))
            return loaded;

        if (!_initialized)
        {
            SymSetOptions(SymOptUndname | SymOptLoadLines | SymOptFailCriticalErrors | SymOptIgnoreNtSymPath | SymOptNoPrompts);
            if (!SymInitializeW(_session, null, false))
                return 0;
            _initialized = true;
        }

        var moduleBase = _nextBase;
        _nextBase += ModuleBaseStep;
        var result = SymLoadModuleExW(_session, IntPtr.Zero, pdbPath, Path.GetFileNameWithoutExtension(pdbPath),
            moduleBase, (uint)ModuleSize, IntPtr.Zero, 0);
        _modules[pdbPath] = result;
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImagehlpLineW64
    {
        public uint SizeOfStruct;
        public IntPtr Key;
        public uint LineNumber;
        public IntPtr FileName;
        public ulong Address;
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern uint SymSetOptions(uint options);

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SymInitializeW(IntPtr process, string? searchPath, bool invadeProcess);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool SymCleanup(IntPtr process);

    [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ulong SymLoadModuleExW(IntPtr process, IntPtr file, string imageName, string? moduleName,
        ulong baseOfDll, uint dllSize, IntPtr data, uint flags);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool SymFromAddrW(IntPtr process, ulong address, out ulong displacement, IntPtr symbol);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool SymGetLineFromAddrW64(IntPtr process, ulong address, out uint displacement,
        ref ImagehlpLineW64 line);
}
