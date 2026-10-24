using System;
using System.IO;
using System.Linq;
using LogGrokX.Platform;

namespace LogGrokX.Bootstrap
{
    static class EntryPoint
    {
        private const string EnableWerOnly = "EnableWerOnly";
        private const string DisableWerOnly = "DisableWerOnly";

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(BuildInfo.Version);
                return;
            }

            var command = args.FirstOrDefault();

            var dumpCommand = command == EnableWerOnly ? CrashDumpCommand.Enable
                : command == DisableWerOnly ? CrashDumpCommand.Disable : CrashDumpCommand.Configure;
            var settings = ApplicationSettings.Instance().DebugSettings;
            ICrashDumpService dumps = new WindowsCrashDumpService();
            dumps.Configure(settings.EnableCrashDumps, settings.MaxDumpsCount, dumpCommand);

            if (dumpCommand != CrashDumpCommand.Configure)
                return;

            var fullPaths = args.Select(Path.GetFullPath).ToArray();
            var manager = new SingleInstanceManager();
            manager.Run(fullPaths);
        }

    }
}
