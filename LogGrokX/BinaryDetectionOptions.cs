using System;
using System.Collections.Generic;
using LogGrokX.Data;

namespace LogGrokX;

public sealed record BinaryDetectionOptions(bool Pem, bool Base64, bool Hex)
{
    public static BinaryDetectionOptions All { get; } = new(true, true, true);

    public static BinaryDetectionOptions None { get; } = new(false, false, false);

    private static readonly List<WeakReference<Action>> CurrentChangedHandlers = new();
    private static BinaryDetectionOptions _current = All;

    public static BinaryDetectionOptions Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            CurrentVersion++;
            RaiseCurrentChanged();
        }
    }

    public static int CurrentVersion { get; private set; }

    public static event Action CurrentChanged
    {
        add
        {
            if (value == null)
                return;
            lock (CurrentChangedHandlers)
                CurrentChangedHandlers.Add(new WeakReference<Action>(value));
        }
        remove { }
    }

    public Base64Content Base64Kinds =>
        (Pem ? Base64Content.Pem : Base64Content.None) | (Base64 ? Base64Content.Base64 : Base64Content.None);

    public static BinaryDetectionOptions FromSettings(ViewSettings settings) =>
        settings.DetectBinary
            ? new BinaryDetectionOptions(settings.DetectPem, settings.DetectBase64, settings.DetectHex)
            : None;

    private static void RaiseCurrentChanged()
    {
        List<WeakReference<Action>>? dead = null;
        Action[] targets;
        lock (CurrentChangedHandlers)
        {
            targets = new Action[CurrentChangedHandlers.Count];
            for (var i = 0; i < CurrentChangedHandlers.Count; i++)
            {
                if (CurrentChangedHandlers[i].TryGetTarget(out var target))
                    targets[i] = target;
                else
                    (dead ??= new List<WeakReference<Action>>()).Add(CurrentChangedHandlers[i]);
            }

            if (dead != null)
                foreach (var weak in dead)
                    CurrentChangedHandlers.Remove(weak);
        }

        foreach (var target in targets)
            target?.Invoke();
    }
}
