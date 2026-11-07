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

        public static TranscriptCursor FromOffset(string path, long offset)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return new TranscriptCursor(path, Math.Max(0, offset));
        }

        public static TranscriptCursor FromTime(string path, DateTime sinceUtc)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return new TranscriptCursor(path, OffsetBefore(path, sinceUtc));
        }

        public long Offset
        {
            get { return offset - partial.Length; }
        }

        private static long OffsetBefore(string path, DateTime sinceUtc)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return 0;
                if (info.LastWriteTimeUtc < sinceUtc) return info.Length;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long low = 0;
                    long high = stream.Length;
                    for (int step = 0; step < 40 && high - low > SeekMargin; step++)
                    {
                        long middle = low + (high - low) / 2;
                        DateTime? time = TimeAfter(stream, middle);
                        if (time.HasValue && time.Value < sinceUtc) low = middle;
                        else high = middle;
                    }
                    return Math.Max(0, LineStart(stream, Math.Max(0, low - SeekMargin)));
                }
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
            catch (ArgumentException)
            {
                return 0;
            }
            catch (NotSupportedException)
            {
                return 0;
            }
        }

        private const int SeekMargin = 1024 * 1024;
        private const int ProbeLines = 8;
        private const int ProbeBytes = 4 * 1024 * 1024;

        private static long LineStart(Stream stream, long position)
        {
            if (position == 0) return 0;
            stream.Seek(position, SeekOrigin.Begin);
            int value;
            long current = position;
            while ((value = stream.ReadByte()) >= 0)
            {
                current++;
                if (value == '\n') return current;
            }
            return current;
        }

        private static DateTime? TimeAfter(Stream stream, long position)
        {
            long start = LineStart(stream, position);
            stream.Seek(start, SeekOrigin.Begin);
            var line = new List<byte>();
            int read = 0;
            int lines = 0;
            int value;
            while (lines < ProbeLines && read < ProbeBytes && (value = stream.ReadByte()) >= 0)
            {
                read++;
                if (value != '\n')
                {
                    line.Add((byte)value);
                    continue;
                }
                lines++;
                Dictionary<string, object> record = Json.TryParseObject(Encoding.UTF8.GetString(line.ToArray()));
                line.Clear();
                string stamp = Json.String(record, "timestamp");
                DateTime time;
                if (stamp != null && DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out time)) return time;
            }
            return null;
        }

        public string Path
        {
            get { return path; }
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
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length == offset && partial.Length == 0) return lines;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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
