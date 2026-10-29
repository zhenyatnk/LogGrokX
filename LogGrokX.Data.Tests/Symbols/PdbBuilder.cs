using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogGrokX.Data.Tests.Symbols;

internal static class PdbBuilder
{
    private const int BlockSize = 512;

    public static byte[] Build(Guid signature, int age, int? dbiAge = null, string? sourceServer = null)
    {
        var streams = new List<byte[]>
        {
            Array.Empty<byte>(),
            BuildInfo(signature, age, sourceServer != null),
            Array.Empty<byte>(),
            dbiAge is { } value ? BuildDbi(value) : Array.Empty<byte>()
        };
        if (sourceServer != null)
            streams.Add(Encoding.UTF8.GetBytes(sourceServer));

        var nextBlock = 5;
        var streamBlocks = new List<int[]>();
        foreach (var stream in streams)
        {
            var count = BlockCount(stream.Length);
            var blocks = new int[count];
            for (var i = 0; i < count; i++)
                blocks[i] = nextBlock++;
            streamBlocks.Add(blocks);
        }

        var directory = new MemoryStream();
        var directoryWriter = new BinaryWriter(directory);
        directoryWriter.Write(streams.Count);
        foreach (var stream in streams)
            directoryWriter.Write(stream.Length);
        foreach (var blocks in streamBlocks)
            foreach (var block in blocks)
                directoryWriter.Write(block);
        var directoryBytes = directory.ToArray();
        if (directoryBytes.Length > BlockSize)
            throw new InvalidOperationException("Directory does not fit into one block.");

        var file = new byte[nextBlock * BlockSize];
        var superBlock = new BinaryWriter(new MemoryStream(file));
        superBlock.Write(Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"));
        superBlock.Write(BlockSize);
        superBlock.Write(1);
        superBlock.Write(nextBlock);
        superBlock.Write(directoryBytes.Length);
        superBlock.Write(0);
        superBlock.Write(3);
        BitConverter.GetBytes(4).CopyTo(file, 3 * BlockSize);
        directoryBytes.CopyTo(file, 4 * BlockSize);
        for (var i = 0; i < streams.Count; i++)
        {
            for (var j = 0; j < streamBlocks[i].Length; j++)
            {
                var offset = j * BlockSize;
                Array.Copy(streams[i], offset, file, streamBlocks[i][j] * BlockSize,
                    Math.Min(BlockSize, streams[i].Length - offset));
            }
        }

        return file;
    }

    private static byte[] BuildInfo(Guid signature, int age, bool hasSourceServer)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(20000404);
        writer.Write(0x12345678);
        writer.Write(age);
        writer.Write(signature.ToByteArray());
        var names = Encoding.ASCII.GetBytes("/names\0srcsrv\0");
        writer.Write(names.Length);
        writer.Write(names);
        writer.Write(hasSourceServer ? 1 : 0);
        writer.Write(4);
        writer.Write(1);
        writer.Write(hasSourceServer ? 0b100u : 0u);
        writer.Write(0);
        if (hasSourceServer)
        {
            writer.Write(7);
            writer.Write(4);
        }

        writer.Write(0);
        return stream.ToArray();
    }

    private static byte[] BuildDbi(int age)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(-1);
        writer.Write(19990903);
        writer.Write(age);
        writer.Write(new byte[52]);
        return stream.ToArray();
    }

    private static int BlockCount(int size) => (size + BlockSize - 1) / BlockSize;
}
