using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LogGrokX
{
    public enum BulkCopyResult
    {
        Copied,
        TooLarge
    }

    public static class BulkSelection
    {
        public const int MaxClipboardCharacters = 64_000_000;
        private const int BatchSize = 128;

        public static async Task ProcessAsync<T>(IEnumerable<T> items, Action<T> action, CancellationToken cancellationToken,
            Action<long>? report = null)
        {
            var count = 0L;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                action(item);
                count++;
                if (count % BatchSize != 0)
                    continue;
                report?.Invoke(count);
                await YieldToUiAsync();
            }
        }

        public static async Task<BulkCopyResult> CopyAsync(IEnumerable<string?> lines, Action<string> setClipboardText,
            CancellationToken cancellationToken, Action<long>? report = null)
        {
            var text = new StringBuilder();
            try
            {
                await ProcessAsync(lines, line =>
                {
                    var value = line?.TrimEnd() ?? string.Empty;
                    if (text.Length + value.Length + 2 > MaxClipboardCharacters)
                        throw new ClipboardLimitException();
                    text.Append(value).Append("\r\n");
                }, cancellationToken, report);
            }
            catch (ClipboardLimitException)
            {
                return BulkCopyResult.TooLarge;
            }

            setClipboardText(text.Replace("\0", string.Empty).ToString());
            return BulkCopyResult.Copied;
        }

        public static async Task SaveAsync(IEnumerable<string?> lines, string fileName, CancellationToken cancellationToken,
            Action<long>? report = null)
        {
            await using var writer = new StreamWriter(fileName, false, Encoding.UTF8);
            var batch = new StringBuilder();
            var count = 0;
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Append(line?.TrimEnd()).Append("\r\n");
                if (++count % BatchSize != 0)
                    continue;
                await writer.WriteAsync(batch.Replace("\0", string.Empty).ToString());
                batch.Clear();
                report?.Invoke(count);
                await YieldToUiAsync();
            }
            if (batch.Length > 0)
                await writer.WriteAsync(batch.Replace("\0", string.Empty).ToString());
        }

        private static async Task YieldToUiAsync()
        {
            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            if (dispatcher is null)
            {
                await Task.Yield();
                return;
            }

            await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        private sealed class ClipboardLimitException : Exception;
    }
}
