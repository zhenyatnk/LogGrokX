using System;
using System.IO;

namespace LogGrokX.Data
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads until the buffer is full or the end of stream is reached.
        /// <see cref="Stream.Read(Span{byte})"/> is allowed to return fewer bytes
        /// than requested at any time, so a single call must never be treated as
        /// "the rest of the stream".
        /// </summary>
        public static int ReadFull(this Stream stream, Span<byte> buffer)
        {
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = stream.Read(buffer.Slice(totalRead));
                if (read == 0)
                    break;
                totalRead += read;
            }

            return totalRead;
        }
    }
}
