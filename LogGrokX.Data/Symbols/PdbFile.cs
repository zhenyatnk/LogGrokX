using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogGrokX.Data.Symbols;

// Minimal reader of the MSF 7.00 container of a native PDB: the PDB identity (GUID and ages) and named streams
// such as "srcsrv". Function names and line numbers are resolved by the platform symbol reader.
public sealed class PdbFile
{
    private const int InfoStream = 1;
    private const int DbiStream = 3;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0");

    private readonly Stream _stream;
    private readonly int _blockSize;
    private readonly uint[] _streamSizes;
    private readonly uint[][] _streamBlocks;
    private Dictionary<string, int>? _namedStreams;

    private PdbFile(Stream stream, int blockSize, uint[] streamSizes, uint[][] streamBlocks)
    {
        _stream = stream;
        _blockSize = blockSize;
        _streamSizes = streamSizes;
        _streamBlocks = streamBlocks;
    }

    public Guid Signature { get; private set; }

    public int Age { get; private set; }

    public int DbiAge { get; private set; }

    public bool Matches(PdbIdentity identity) =>
        Signature == identity.Signature && (Age == identity.Age || DbiAge == identity.Age);

    public static bool TryReadIdentity(string path, out Guid signature, out int age, out int dbiAge)
    {
        signature = Guid.Empty;
        age = dbiAge = 0;
        try
        {
            using var stream = File.OpenRead(path);
            var pdb = Open(stream);
            if (pdb == null)
                return false;
            signature = pdb.Signature;
            age = pdb.Age;
            dbiAge = pdb.DbiAge;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static string? ReadSourceServerStream(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var data = Open(stream)?.ReadNamedStream("srcsrv");
            return data == null ? null : Encoding.UTF8.GetString(data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    public static PdbFile? Open(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 0;
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
            return null;

        var blockSize = reader.ReadInt32();
        reader.ReadUInt32();
        var blockCount = reader.ReadUInt32();
        var directorySize = reader.ReadUInt32();
        reader.ReadUInt32();
        var blockMapAddress = reader.ReadUInt32();
        if (blockSize is not (512 or 1024 or 2048 or 4096 or 8192 or 16384 or 32768) ||
            blockMapAddress >= blockCount || directorySize > int.MaxValue)
            throw new InvalidDataException("Invalid MSF super block.");

        var directoryBlockCount = BlockCount(directorySize, blockSize);
        stream.Position = (long)blockMapAddress * blockSize;
        var directoryBlocks = new uint[directoryBlockCount];
        for (var i = 0; i < directoryBlocks.Length; i++)
            directoryBlocks[i] = reader.ReadUInt32();

        var directory = new BinaryReader(new MemoryStream(ReadBlocks(stream, directoryBlocks, blockSize, directorySize)));
        var streamCount = directory.ReadUInt32();
        if (streamCount > directorySize / 4)
            throw new InvalidDataException("Invalid MSF stream directory.");

        var sizes = new uint[streamCount];
        for (var i = 0; i < sizes.Length; i++)
        {
            sizes[i] = directory.ReadUInt32();
            if (sizes[i] == uint.MaxValue)
                sizes[i] = 0;
        }

        var blocks = new uint[streamCount][];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new uint[BlockCount(sizes[i], blockSize)];
            for (var j = 0; j < blocks[i].Length; j++)
                blocks[i][j] = directory.ReadUInt32();
        }

        var pdb = new PdbFile(stream, blockSize, sizes, blocks);
        pdb.ReadInfo();
        return pdb;
    }

    public byte[]? ReadStream(int index) =>
        index >= 0 && index < _streamSizes.Length
            ? ReadBlocks(_stream, _streamBlocks[index], _blockSize, _streamSizes[index])
            : null;

    public byte[]? ReadNamedStream(string name) =>
        NamedStreams.TryGetValue(name, out var index) ? ReadStream(index) : null;

    private IReadOnlyDictionary<string, int> NamedStreams => _namedStreams ??= ReadNamedStreams();

    private void ReadInfo()
    {
        var info = ReadStream(InfoStream);
        if (info == null || info.Length < 28)
            throw new InvalidDataException("PDB info stream is missing.");

        Age = BitConverter.ToInt32(info, 8);
        Signature = new Guid(info.AsSpan(12, 16));
        var dbi = ReadStream(DbiStream);
        DbiAge = dbi is { Length: >= 12 } && BitConverter.ToInt32(dbi, 0) == -1 ? BitConverter.ToInt32(dbi, 8) : Age;
    }

    private Dictionary<string, int> ReadNamedStreams()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var info = ReadStream(InfoStream);
        if (info == null || info.Length < 32)
            return result;

        using var reader = new BinaryReader(new MemoryStream(info, 28, info.Length - 28));
        try
        {
            var namesLength = reader.ReadInt32();
            var names = reader.ReadBytes(namesLength);
            reader.ReadUInt32();
            var capacity = reader.ReadUInt32();
            var present = ReadBitVector(reader);
            ReadBitVector(reader);
            for (var i = 0; i < capacity; i++)
            {
                if (!IsSet(present, i))
                    continue;

                var nameOffset = reader.ReadInt32();
                var streamIndex = reader.ReadInt32();
                if (nameOffset < 0 || nameOffset >= names.Length)
                    continue;

                var end = Array.IndexOf(names, (byte)0, nameOffset);
                var name = Encoding.UTF8.GetString(names, nameOffset, (end < 0 ? names.Length : end) - nameOffset);
                result.TryAdd(name, streamIndex);
            }
        }
        catch (EndOfStreamException)
        {
        }

        return result;
    }

    private static uint[] ReadBitVector(BinaryReader reader)
    {
        var words = new uint[reader.ReadUInt32()];
        for (var i = 0; i < words.Length; i++)
            words[i] = reader.ReadUInt32();
        return words;
    }

    private static bool IsSet(uint[] words, int bit) =>
        bit / 32 < words.Length && (words[bit / 32] & (1u << (bit % 32))) != 0;

    private static int BlockCount(uint size, int blockSize) => (int)((size + (uint)blockSize - 1) / (uint)blockSize);

    private static byte[] ReadBlocks(Stream stream, uint[] blocks, int blockSize, uint size)
    {
        var result = new byte[size];
        var offset = 0;
        foreach (var block in blocks)
        {
            var count = (int)Math.Min(blockSize, size - offset);
            if (count <= 0)
                break;
            stream.Position = (long)block * blockSize;
            stream.ReadExactly(result, offset, count);
            offset += count;
        }

        return result;
    }
}
