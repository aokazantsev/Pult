using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class TranscriptCursor
    {
        private const int MaxReadBytes = 8 * 1024 * 1024;

        private readonly string path;
        private long offset;
        private byte[] partial = new byte[0];

        private TranscriptCursor(string path, long offset)
        {
            this.path = path;
            this.offset = offset;
        }

        public static TranscriptCursor AtEnd(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            long length = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) length = info.Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            return new TranscriptCursor(path, length);
        }

        public List<string> ReadNew()
        {
            var lines = new List<string>();
            byte[] chunk;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = stream.Length;
                    if (length < offset)
                    {
                        offset = 0;
                        partial = new byte[0];
                    }
                    long available = Math.Min(length - offset, MaxReadBytes);
                    if (available <= 0) return lines;
                    chunk = new byte[available];
                    stream.Seek(offset, SeekOrigin.Begin);
                    int total = 0;
                    while (total < chunk.Length)
                    {
                        int read = stream.Read(chunk, total, chunk.Length - total);
                        if (read <= 0) break;
                        total += read;
                    }
                    if (total < chunk.Length) Array.Resize(ref chunk, total);
                    offset += total;
                }
            }
            catch (IOException)
            {
                return lines;
            }
            catch (UnauthorizedAccessException)
            {
                return lines;
            }
            byte[] data = Concat(partial, chunk);
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != (byte)'\n') continue;
                int end = i > start && data[i - 1] == (byte)'\r' ? i - 1 : i;
                if (end > start) lines.Add(Encoding.UTF8.GetString(data, start, end - start));
                start = i + 1;
            }
            partial = new byte[data.Length - start];
            Array.Copy(data, start, partial, 0, partial.Length);
            return lines;
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            if (first.Length == 0) return second;
            var result = new byte[first.Length + second.Length];
            Array.Copy(first, result, first.Length);
            Array.Copy(second, 0, result, first.Length, second.Length);
            return result;
        }
    }
}
