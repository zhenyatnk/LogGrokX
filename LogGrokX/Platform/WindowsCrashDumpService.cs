using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LogGrokX.Platform;

public sealed class WindowsCrashDumpService : ICrashDumpService
{
    private const string EnableWerOnly = "EnableWerOnly";
    private const string DisableWerOnly = "DisableWerOnly";
    private static readonly string LogGrokErrorReportingKeyPath = $@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{Path.GetFileName(Environment.ProcessPath)}";

    private static void EvaluateIfNeed(Action func, string elevatedCommand, CrashDumpCommand currentCommand)
    {
        try
        {
            func();
        }
        catch (UnauthorizedAccessException)
        {
            if (currentCommand != CrashDumpCommand.Configure)
                return;

            StartElevatedInstanceWithParam(elevatedCommand);
        }
    }

    public void Configure(bool enabled, int maxDumpCount, CrashDumpCommand command)
    {
        var action = GetRequestedCommand(enabled, maxDumpCount, ReadDumpCount(), command);
        if (action == CrashDumpCommand.Enable)
        {
            EvaluateIfNeed(() => EnableErrorReporting(maxDumpCount), EnableWerOnly, command);
        }
        else if (action == CrashDumpCommand.Disable)
        {
            EvaluateIfNeed(DisableErrorReporting, DisableWerOnly, command);
        }
    }

    internal static CrashDumpCommand GetRequestedCommand(bool enabled, int maxDumpCount,
        int? currentDumpCount, CrashDumpCommand command)
    {
        if (command != CrashDumpCommand.Configure) return command;
        if (enabled && currentDumpCount != maxDumpCount) return CrashDumpCommand.Enable;
        if (!enabled && currentDumpCount != null) return CrashDumpCommand.Disable;
        return CrashDumpCommand.Configure;
    }

    private static int? ReadDumpCount()
    {
        using var key = Registry.LocalMachine.OpenSubKey(LogGrokErrorReportingKeyPath);
        return key == null ? null : key.GetValue("DumpCount", -1) as int? ?? -1;
    }

    private static void DisableErrorReporting()
    {
        Registry.LocalMachine.DeleteSubKey(LogGrokErrorReportingKeyPath, false);
    }

    private static void EnableErrorReporting(int maxDumpCount)
    {
        using var key = Registry.LocalMachine.CreateSubKey(LogGrokErrorReportingKeyPath);
        key.SetValue("DumpFolder",
            HomeDirectoryPathProvider.GetDirectoryFullPath("Dumps"),
            RegistryValueKind.ExpandString);

        key.SetValue("DumpCount", maxDumpCount, RegistryValueKind.DWord);
        key.SetValue("DumpType", 2, RegistryValueKind.DWord);
    }

    private static void StartElevatedInstanceWithParam(string argsToInstance)
    {
        var processPath = Environment.ProcessPath;
        if (processPath == null)
            return;

        var info = new ProcessStartInfo(processPath)
        {
            UseShellExecute = true,
            Arguments = argsToInstance,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(info);
            process?.WaitForExit(30000);
        }
        catch (Exception)
        {
            // ignored
        }
    }
}
