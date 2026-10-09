using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX;

public sealed class CurrentThreadFilter
{
    public string? Thread { get; private set; }

    public event Action? Changed;

    public void SetThread(string? thread)
    {
        if (string.Equals(Thread, thread, StringComparison.Ordinal))
            return;

        Thread = thread;
        Changed?.Invoke();
    }

    public static IReadOnlyDictionary<int, IEnumerable<string>> AddThreadExclusions(
        IReadOnlyDictionary<int, IEnumerable<string>> exclusions,
        int indexedThreadFieldIndex,
        string? thread,
        IEnumerable<string> allThreads)
    {
        if (thread == null || indexedThreadFieldIndex < 0)
            return exclusions;

        var otherThreads = allThreads
            .Where(value => !string.Equals(value, thread, StringComparison.Ordinal))
            .ToArray();
        if (otherThreads.Length == 0)
            return exclusions;

        var result = exclusions.ToDictionary(kv => kv.Key, kv => kv.Value);
        result[indexedThreadFieldIndex] = result.TryGetValue(indexedThreadFieldIndex, out var existing)
            ? existing.Concat(otherThreads).Distinct(StringComparer.Ordinal).ToArray()
            : otherThreads;
        return result;
    }
}
