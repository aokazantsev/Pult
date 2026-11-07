using System;
using System.Collections.Generic;
using System.Drawing;

namespace Pult
{
    internal sealed class QrCode
    {
        private const int LevelM = 1;
        private static readonly int[] FormatBitsByLevel = { 1, 0, 3, 2 };
        private static readonly int[][] EccCodewordsPerBlock =
        {
            new[] { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            new[] { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            new[] { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            new[] { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
        };
        private static readonly int[][] ErrorCorrectionBlocks =
        {
            new[] { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            new[] { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            new[] { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            new[] { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }
        };

        public readonly int Version;
        public readonly int Size;
        public readonly int Mask;
        private readonly int level;
        private readonly bool[,] modules;
        private readonly bool[,] isFunction;

        private QrCode(int version, int level, byte[] codewords, int forcedMask)
        {
            Version = version;
            this.level = level;
            Size = version * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];
            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(codewords));
            int mask = forcedMask;
            if (mask < 0)
            {
                int best = int.MaxValue;
                for (int candidate = 0; candidate < 8; candidate++)
                {
                    ApplyMask(candidate);
                    DrawFormatBits(candidate);
                    int penalty = Penalty();
                    if (penalty < best)
                    {
                        mask = candidate;
                        best = penalty;
                    }
                    ApplyMask(candidate);
                }
            }
            Mask = mask;
            ApplyMask(mask);
            DrawFormatBits(mask);
        }

        public bool this[int x, int y]
        {
            get { return x >= 0 && y >= 0 && x < Size && y < Size && modules[y, x]; }
        }

        public static QrCode EncodeBytes(byte[] data)
        {
            return EncodeBytes(data, LevelM, -1);
        }

        public static QrCode EncodeBytes(byte[] data, int level, int forcedMask)
        {
            for (int version = 1; version <= 40; version++)
            {
                int capacityBits = DataCodewords(version, level) * 8;
                int countBits = version <= 9 ? 8 : 16;
                if (data.Length >= (1 << countBits)) continue;
                int usedBits = 4 + countBits + data.Length * 8;
                if (usedBits > capacityBits) continue;
                var bits = new List<bool>();
                AppendBits(bits, 4, 4);
                AppendBits(bits, data.Length, countBits);
                foreach (byte b in data) AppendBits(bits, b, 8);
                AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
                AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
                for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) AppendBits(bits, pad, 8);
                var codewords = new byte[bits.Count / 8];
                for (int i = 0; i < bits.Count; i++)
                {
                    if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                }
                return new QrCode(version, level, codewords, forcedMask);
            }
            throw new ArgumentException("data too long for a QR code");
        }

        public Bitmap Render(int moduleSize, int border)
        {
            int pixels = (Size + border * 2) * moduleSize;
            var bitmap = new Bitmap(pixels, pixels);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        if (modules[y, x]) g.FillRectangle(Brushes.Black, (x + border) * moduleSize, (y + border) * moduleSize, moduleSize, moduleSize);
                    }
                }
            }
            return bitmap;
        }

        private static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int alignments = version / 7 + 2;
                result -= (25 * alignments - 10) * alignments - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        private static int DataCodewords(int version, int level)
        {
            return RawDataModules(version) / 8 - EccCodewordsPerBlock[level][version] * ErrorCorrectionBlocks[level][version];
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);
            int[] positions = AlignmentPositions();
            int count = positions.Length;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    bool corner = (i == 0 && j == 0) || (i == 0 && j == count - 1) || (i == count - 1 && j == 0);
                    if (!corner) DrawAlignment(positions[i], positions[j]);
                }
            }
            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            int data = FormatBitsByLevel[level] << 3 | mask;
            int remainder = data;
            for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            int bits = (data << 10 | remainder) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int remainder = Version;
            for (int i = 0; i < 12; i++) remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
            int bits = Version << 12 | remainder;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3;
                int b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx;
                    int yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunction(xx, yy, distance != 2 && distance != 4);
                }
            }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++) SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        private void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int count = Version / 7 + 2;
            int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step) result[i] = position;
            return result;
        }

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int blocks = ErrorCorrectionBlocks[level][Version];
            int eccLength = EccCodewordsPerBlock[level][Version];
            int rawCodewords = RawDataModules(Version) / 8;
            int shortBlocks = blocks - rawCodewords % blocks;
            int shortLength = rawCodewords / blocks;
            byte[] divisor = ReedSolomonDivisor(eccLength);
            var all = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
                var block = new byte[shortLength + 1];
                Array.Copy(data, k, block, 0, length);
                k += length;
                var chunk = new byte[length];
                Array.Copy(data, k - length, chunk, 0, length);
                byte[] ecc = ReedSolomonRemainder(chunk, divisor);
                int dataSlots = shortLength - eccLength + 1;
                Array.Copy(ecc, 0, block, dataSlots, eccLength);
                all.Add(block);
            }
            var result = new byte[rawCodewords];
            int index = 0;
            for (int i = 0; i < shortLength + 1; i++)
            {
                for (int j = 0; j < blocks; j++)
                {
                    if (i != shortLength - eccLength || j >= shortBlocks) result[index++] = all[j][i];
                }
            }
            return result;
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vertical = 0; vertical < Size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vertical : vertical;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !isFunction[y, x]) modules[y, x] = !modules[y, x];
                }
            }
        }

        private int Penalty()
        {
            int result = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < Size; a++)
                {
                    bool runColor = false;
                    int run = 0;
                    var history = new int[7];
                    for (int b = 0; b < Size; b++)
                    {
                        bool color = pass == 0 ? modules[a, b] : modules[b, a];
                        if (color == runColor)
                        {
                            run++;
                            if (run == 5) result += 3;
                            else if (run > 5) result++;
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!runColor) result += CountFinderLike(history) * 40;
                            runColor = color;
                            run = 1;
                        }
                    }
                    if (runColor)
                    {
                        AddHistory(run, history);
                        run = 0;
                    }
                    run += Size;
                    AddHistory(run, history);
                    result += CountFinderLike(history) * 40;
                }
            }
            for (int y = 0; y < Size - 1; y++)
            {
                for (int x = 0; x < Size - 1; x++)
                {
                    bool color = modules[y, x];
                    if (color == modules[y, x + 1] && color == modules[y + 1, x] && color == modules[y + 1, x + 1]) result += 3;
                }
            }
            int dark = 0;
            foreach (bool module in modules)
            {
                if (module) dark++;
            }
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + k * 10;
        }

        private void AddHistory(int run, int[] history)
        {
            if (history[0] == 0) run += Size;
            Array.Copy(history, 0, history, 1, history.Length - 1);
            history[0] = run;
        }

        private static int CountFinderLike(int[] h)
        {
            int n = h[1];
            bool core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }

        private static bool Bit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }
    }
}
