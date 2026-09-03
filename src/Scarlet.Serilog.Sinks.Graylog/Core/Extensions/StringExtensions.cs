using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Extensions
{
    internal static class StringExtensions
    {
        /// <summary>
        /// UTF-8 encodes and gzip-compresses a GELF payload.
        /// </summary>
        /// <remarks>
        /// The uncompressed bytes go through a pooled buffer rather than a fresh array. This runs on
        /// every UDP event, and the uncompressed payload is the largest thing the send allocates -
        /// renting it cuts the garbage per event by three to five times, though it does not make the
        /// call faster: gzip dominates the time completely.
        /// <para>
        /// The buffer is returned without clearing. It holds log content that is about to go out over
        /// the wire regardless, the pool is process-local, and clearing a megabyte on every event
        /// would cost more than the rent saves.
        /// </para>
        /// </remarks>
        public static byte[] ToGzip(this string source)
        {
            int byteCount = Encoding.UTF8.GetByteCount(source);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                Encoding.UTF8.GetBytes(source, 0, source.Length, buffer, 0);

                using var resultStream = new MemoryStream();

                // leaveOpen, so each stream is owned and disposed exactly once. Without it the gzip
                // stream closes the MemoryStream underneath it, and the ToArray below reads from a
                // disposed stream - which MemoryStream happens to allow, but nothing here should rely
                // on that.
                using (var gzipStream = new GZipStream(resultStream, CompressionMode.Compress, leaveOpen: true))
                {
                    gzipStream.Write(buffer, 0, byteCount);
                }

                return resultStream.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static byte[] ToByteArray(this string source) => Encoding.UTF8.GetBytes(source);

        /// <summary>
        /// Truncates the specified maximum length.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="maxLength">The maximum length.</param>
        /// <returns>The source, cut to <paramref name="maxLength"/> characters.</returns>
        public static string Truncate(this string source, int maxLength)
        {
            return source.Length > maxLength ? source.Substring(0, maxLength) : source;
        }

        public static string Expand(this string source)
        {
            return Environment.ExpandEnvironmentVariables(source);
        }
    }
}
