using System;

namespace LogGrokX.Data;

public sealed class ValueMappingItem
{
    public string Value { get; set; } = string.Empty;

    public string Mapping { get; set; } = string.Empty;
}

public sealed class ValueMappingList
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool IgnoreCase { get; set; }

    public string[] Fields { get; set; } = Array.Empty<string>();

    public ValueMappingItem[] Items { get; set; } = Array.Empty<ValueMappingItem>();
}

public readonly record struct ValueMappingMatch(int Start, int Length, string Mapping);
