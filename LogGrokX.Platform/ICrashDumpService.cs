namespace LogGrokX.Platform;

public enum CrashDumpCommand { Configure, Enable, Disable }

public interface ICrashDumpService
{
    void Configure(bool enabled, int maxDumpCount, CrashDumpCommand command);
}
