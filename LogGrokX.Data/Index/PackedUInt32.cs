using System;
using System.Threading;

namespace LogGrokX.Data.Index;

internal static class PackedUInt32
{
    public static uint Get(Array storage, int index) => storage switch
    {
        byte[] bytes => bytes[index],
        ushort[] shorts => shorts[index],
        _ => ((uint[])storage)[index]
    };

    public static void Set(ref Array storage, int index, uint value)
    {
        switch (storage)
        {
            case byte[] bytes when index < bytes.Length && value <= byte.MaxValue:
                bytes[index] = (byte)value;
                return;
            case ushort[] shorts when index < shorts.Length && value <= ushort.MaxValue:
                shorts[index] = (ushort)value;
                return;
            case uint[] ints when index < ints.Length:
                ints[index] = value;
                return;
        }

        var length = index < storage.Length
            ? storage.Length
            : Math.Max(index + 1, Math.Max(16, storage.Length * 2));
        if (storage is byte[] && value <= byte.MaxValue)
        {
            var bytes = new byte[length];
            Array.Copy(storage, bytes, storage.Length);
            bytes[index] = (byte)value;
            Volatile.Write(ref storage, bytes);
        }
        else if (storage is not uint[] && value <= ushort.MaxValue)
        {
            var shorts = new ushort[length];
            Array.Copy(storage, shorts, storage.Length);
            shorts[index] = (ushort)value;
            Volatile.Write(ref storage, shorts);
        }
        else
        {
            var ints = new uint[length];
            Array.Copy(storage, ints, storage.Length);
            ints[index] = value;
            Volatile.Write(ref storage, ints);
        }
    }
}
