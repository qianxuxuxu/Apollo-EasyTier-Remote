using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemoteControlUI
{
    /// <summary>
    /// Pure C# ISO/IEC 18004 QR Code encoder (Byte mode, Error Correction Level M, Versions 1..15).
    /// Zero external libraries or network access required.
    /// </summary>
    public static class QrCodeEncoder
    {
        private struct VersionSpec
        {
            public int Version;
            public int TotalCodewords;
            public int EcPerBlock;
            public int Group1Blocks;
            public int Group1DataCw;
            public int Group2Blocks;
            public int Group2DataCw;
            public int[] AlignPositions;

            public int TotalDataCodewords
            {
                get { return Group1Blocks * Group1DataCw + Group2Blocks * Group2DataCw; }
            }
        }

        private static readonly VersionSpec[] SpecsM = new VersionSpec[]
        {
            new VersionSpec { Version = 1,  TotalCodewords = 26,  EcPerBlock = 10, Group1Blocks = 1, Group1DataCw = 16, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[0] },
            new VersionSpec { Version = 2,  TotalCodewords = 44,  EcPerBlock = 16, Group1Blocks = 1, Group1DataCw = 28, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 18 } },
            new VersionSpec { Version = 3,  TotalCodewords = 70,  EcPerBlock = 26, Group1Blocks = 1, Group1DataCw = 44, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 22 } },
            new VersionSpec { Version = 4,  TotalCodewords = 100, EcPerBlock = 18, Group1Blocks = 2, Group1DataCw = 32, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 26 } },
            new VersionSpec { Version = 5,  TotalCodewords = 134, EcPerBlock = 24, Group1Blocks = 2, Group1DataCw = 43, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 30 } },
            new VersionSpec { Version = 6,  TotalCodewords = 172, EcPerBlock = 16, Group1Blocks = 4, Group1DataCw = 27, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 34 } },
            new VersionSpec { Version = 7,  TotalCodewords = 196, EcPerBlock = 18, Group1Blocks = 4, Group1DataCw = 31, Group2Blocks = 0, Group2DataCw = 0,  AlignPositions = new int[] { 6, 22, 38 } },
            new VersionSpec { Version = 8,  TotalCodewords = 242, EcPerBlock = 22, Group1Blocks = 2, Group1DataCw = 38, Group2Blocks = 2, Group2DataCw = 39, AlignPositions = new int[] { 6, 24, 42 } },
            new VersionSpec { Version = 9,  TotalCodewords = 292, EcPerBlock = 22, Group1Blocks = 3, Group1DataCw = 36, Group2Blocks = 2, Group2DataCw = 37, AlignPositions = new int[] { 6, 26, 46 } },
            new VersionSpec { Version = 10, TotalCodewords = 346, EcPerBlock = 26, Group1Blocks = 4, Group1DataCw = 43, Group2Blocks = 1, Group2DataCw = 44, AlignPositions = new int[] { 6, 28, 50 } },
            new VersionSpec { Version = 11, TotalCodewords = 404, EcPerBlock = 30, Group1Blocks = 1, Group1DataCw = 50, Group2Blocks = 4, Group2DataCw = 51, AlignPositions = new int[] { 6, 30, 54 } },
            new VersionSpec { Version = 12, TotalCodewords = 466, EcPerBlock = 22, Group1Blocks = 6, Group1DataCw = 36, Group2Blocks = 2, Group2DataCw = 37, AlignPositions = new int[] { 6, 32, 58 } },
            new VersionSpec { Version = 13, TotalCodewords = 532, EcPerBlock = 22, Group1Blocks = 8, Group1DataCw = 37, Group2Blocks = 1, Group2DataCw = 38, AlignPositions = new int[] { 6, 34, 62 } },
            new VersionSpec { Version = 14, TotalCodewords = 581, EcPerBlock = 24, Group1Blocks = 4, Group1DataCw = 40, Group2Blocks = 5, Group2DataCw = 41, AlignPositions = new int[] { 6, 26, 46, 66 } },
            new VersionSpec { Version = 15, TotalCodewords = 655, EcPerBlock = 24, Group1Blocks = 5, Group1DataCw = 41, Group2Blocks = 5, Group2DataCw = 42, AlignPositions = new int[] { 6, 26, 48, 70 } }
        };

        private static readonly int[] GfExp = new int[512];
        private static readonly int[] GfLog = new int[256];

        static QrCodeEncoder()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                GfExp[i] = x;
                GfLog[x] = i;
                x <<= 1;
                if ((x & 0x100) != 0)
                {
                    x ^= 0x11D;
                }
            }
            for (int i = 255; i < 512; i++)
            {
                GfExp[i] = GfExp[i - 255];
            }
        }

        private static int GfMul(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return GfExp[GfLog[a] + GfLog[b]];
        }

        private static byte[] ComputeRsGenerator(int degree)
        {
            byte[] poly = new byte[] { 1 };
            for (int i = 0; i < degree; i++)
            {
                byte[] next = new byte[poly.Length + 1];
                int factor = GfExp[i];
                for (int j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= (byte)GfMul(poly[j], factor);
                }
                poly = next;
            }
            return poly;
        }

        private static byte[] ComputeRsRemainder(byte[] data, int ecLen)
        {
            byte[] gen = ComputeRsGenerator(ecLen);
            byte[] rem = new byte[ecLen];
            for (int i = 0; i < data.Length; i++)
            {
                int factor = data[i] ^ rem[0];
                Array.Copy(rem, 1, rem, 0, ecLen - 1);
                rem[ecLen - 1] = 0;
                if (factor != 0)
                {
                    for (int j = 0; j < ecLen; j++)
                    {
                        rem[j] ^= (byte)GfMul(gen[j + 1], factor);
                    }
                }
            }
            return rem;
        }

        private static void AppendBits(List<bool> bits, int val, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                bits.Add(((val >> i) & 1) != 0);
            }
        }

        public static bool[,] Encode(string text)
        {
            if (text == null) text = "";
            byte[] payload = Encoding.UTF8.GetBytes(text);

            VersionSpec spec = default(VersionSpec);
            bool found = false;
            for (int i = 0; i < SpecsM.Length; i++)
            {
                int lenBits = SpecsM[i].Version <= 9 ? 8 : 16;
                int capacityBits = SpecsM[i].TotalDataCodewords * 8;
                if (4 + lenBits + payload.Length * 8 <= capacityBits)
                {
                    spec = SpecsM[i];
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                // Truncate safely on valid UTF-8 codepoint boundary to max V15 capacity
                spec = SpecsM[SpecsM.Length - 1];
                int maxBytes = spec.TotalDataCodewords - 3;
                int cut = Math.Min(maxBytes, payload.Length);
                if (cut < payload.Length && cut > 0)
                {
                    // Walk back over any UTF-8 continuation bytes (10xxxxxx)
                    while (cut > 0 && (payload[cut] & 0xC0) == 0x80)
                    {
                        cut--;
                    }
                }
                byte[] trimmed = new byte[cut];
                Array.Copy(payload, trimmed, cut);
                payload = trimmed;
            }

            // 1. Build data bitstream (Byte mode 0100)
            List<bool> bits = new List<bool>();
            AppendBits(bits, 0x4, 4);
            AppendBits(bits, payload.Length, spec.Version <= 9 ? 8 : 16);
            for (int i = 0; i < payload.Length; i++)
            {
                AppendBits(bits, payload[i], 8);
            }

            int totalDataBits = spec.TotalDataCodewords * 8;
            int terminator = Math.Min(4, totalDataBits - bits.Count);
            AppendBits(bits, 0, terminator);
            while (bits.Count % 8 != 0)
            {
                bits.Add(false);
            }
            int padIdx = 0;
            while (bits.Count < totalDataBits)
            {
                AppendBits(bits, (padIdx % 2 == 0) ? 0xEC : 0x11, 8);
                padIdx++;
            }

            byte[] dataCw = new byte[spec.TotalDataCodewords];
            for (int i = 0; i < dataCw.Length; i++)
            {
                int b = 0;
                for (int k = 0; k < 8; k++)
                {
                    b = (b << 1) | (bits[i * 8 + k] ? 1 : 0);
                }
                dataCw[i] = (byte)b;
            }

            // 2. Split into RS blocks and interleave
            List<byte[]> dataBlocks = new List<byte[]>();
            List<byte[]> ecBlocks = new List<byte[]>();
            int offset = 0;
            for (int i = 0; i < spec.Group1Blocks; i++)
            {
                byte[] blk = new byte[spec.Group1DataCw];
                Array.Copy(dataCw, offset, blk, 0, spec.Group1DataCw);
                offset += spec.Group1DataCw;
                dataBlocks.Add(blk);
                ecBlocks.Add(ComputeRsRemainder(blk, spec.EcPerBlock));
            }
            for (int i = 0; i < spec.Group2Blocks; i++)
            {
                byte[] blk = new byte[spec.Group2DataCw];
                Array.Copy(dataCw, offset, blk, 0, spec.Group2DataCw);
                offset += spec.Group2DataCw;
                dataBlocks.Add(blk);
                ecBlocks.Add(ComputeRsRemainder(blk, spec.EcPerBlock));
            }

            List<byte> interleaved = new List<byte>(spec.TotalCodewords);
            int maxDataLen = Math.Max(spec.Group1DataCw, spec.Group2DataCw);
            for (int col = 0; col < maxDataLen; col++)
            {
                for (int b = 0; b < dataBlocks.Count; b++)
                {
                    if (col < dataBlocks[b].Length)
                    {
                        interleaved.Add(dataBlocks[b][col]);
                    }
                }
            }
            for (int col = 0; col < spec.EcPerBlock; col++)
            {
                for (int b = 0; b < ecBlocks.Count; b++)
                {
                    interleaved.Add(ecBlocks[b][col]);
                }
            }

            // 3. Draw function patterns
            int size = spec.Version * 4 + 17;
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];

            DrawFinderPattern(modules, isFunction, 0, 0, size);
            DrawFinderPattern(modules, isFunction, size - 7, 0, size);
            DrawFinderPattern(modules, isFunction, 0, size - 7, size);

            // Timing patterns
            for (int i = 0; i < size; i++)
            {
                SetFunctionModule(modules, isFunction, 6, i, i % 2 == 0);
                SetFunctionModule(modules, isFunction, i, 6, i % 2 == 0);
            }

            // Alignment patterns
            int[] pos = spec.AlignPositions;
            for (int i = 0; i < pos.Length; i++)
            {
                for (int j = 0; j < pos.Length; j++)
                {
                    int cx = pos[i];
                    int cy = pos[j];
                    if ((cx == 6 && cy == 6) || (cx == 6 && cy == size - 7) || (cx == size - 7 && cy == 6))
                    {
                        continue;
                    }
                    DrawAlignmentPattern(modules, isFunction, cx, cy);
                }
            }

            // Reserve format info & dark module
            DrawFormatBits(modules, isFunction, size, 0);
            // Reserve & draw version info for V >= 7
            if (spec.Version >= 7)
            {
                DrawVersionBits(modules, isFunction, size, spec.Version);
            }

            // 4. Place data bits in zig-zag order
            int bitIdx = 0;
            int totalBits = interleaved.Count * 8;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                bool upward = (((right + 1) & 2) == 0);
                for (int vert = 0; vert < size; vert++)
                {
                    int y = upward ? (size - 1 - vert) : vert;
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int x = right - dx;
                        if (!isFunction[y, x])
                        {
                            bool bit = false;
                            if (bitIdx < totalBits)
                            {
                                bit = ((interleaved[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                                bitIdx++;
                            }
                            modules[y, x] = bit;
                        }
                    }
                }
            }

            // 5. Evaluate all 8 masks and pick the lowest penalty
            int bestMask = 0;
            int minPenalty = int.MaxValue;
            bool[,] bestModules = null;

            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] cand = (bool[,])modules.Clone();
                ApplyMask(cand, isFunction, size, mask);
                DrawFormatBits(cand, isFunction, size, mask);
                int penalty = CalculatePenalty(cand, size);
                if (penalty < minPenalty)
                {
                    minPenalty = penalty;
                    bestMask = mask;
                    bestModules = cand;
                }
            }

            return bestModules;
        }

        private static void SetFunctionModule(bool[,] m, bool[,] f, int x, int y, bool dark)
        {
            if (x < 0 || y < 0 || x >= m.GetLength(1) || y >= m.GetLength(0)) return;
            m[y, x] = dark;
            f[y, x] = true;
        }

        private static void DrawFinderPattern(bool[,] m, bool[,] f, int left, int top, int size)
        {
            for (int dy = -1; dy <= 7; dy++)
            {
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = left + dx;
                    int y = top + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    bool dark = (dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6) &&
                                (dx == 0 || dx == 6 || dy == 0 || dy == 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));
                    SetFunctionModule(m, f, x, y, dark);
                }
            }
        }

        private static void DrawAlignmentPattern(bool[,] m, bool[,] f, int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(m, f, cx + dx, cy + dy, dist != 1);
                }
            }
        }

        private static void DrawFormatBits(bool[,] m, bool[,] f, int size, int mask)
        {
            // Error Correction Level M format indicator bits = 00 (0)
            int data = (0 << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537);
            }
            int bits = ((data << 10) | rem) ^ 0x5412;

            // Top-left copy
            for (int i = 0; i <= 5; i++) SetFunctionModule(m, f, 8, i, ((bits >> i) & 1) != 0);
            SetFunctionModule(m, f, 8, 7, ((bits >> 6) & 1) != 0);
            SetFunctionModule(m, f, 8, 8, ((bits >> 7) & 1) != 0);
            SetFunctionModule(m, f, 7, 8, ((bits >> 8) & 1) != 0);
            for (int i = 9; i < 15; i++) SetFunctionModule(m, f, 14 - i, 8, ((bits >> i) & 1) != 0);

            // Second copy (top-right & bottom-left)
            for (int i = 0; i < 8; i++) SetFunctionModule(m, f, size - 1 - i, 8, ((bits >> i) & 1) != 0);
            for (int i = 8; i < 15; i++) SetFunctionModule(m, f, 8, size - 15 + i, ((bits >> i) & 1) != 0);

            // Dark module always at (8, size - 8)
            SetFunctionModule(m, f, 8, size - 8, true);
        }

        private static void DrawVersionBits(bool[,] m, bool[,] f, int size, int version)
        {
            int rem = version;
            for (int i = 0; i < 12; i++)
            {
                rem = (rem << 1) ^ (((rem >> 11) & 1) * 0x1F25);
            }
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + (i % 3);
                int b = i / 3;
                SetFunctionModule(m, f, a, b, bit);
                SetFunctionModule(m, f, b, a, bit);
            }
        }

        private static void ApplyMask(bool[,] m, bool[,] f, int size, int mask)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (f[y, x]) continue;
                    bool invert = false;
                    switch (mask)
                    {
                        case 0: invert = ((x + y) % 2 == 0); break;
                        case 1: invert = (y % 2 == 0); break;
                        case 2: invert = (x % 3 == 0); break;
                        case 3: invert = ((x + y) % 3 == 0); break;
                        case 4: invert = (((y / 2) + (x / 3)) % 2 == 0); break;
                        case 5: invert = ((x * y) % 2 + (x * y) % 3 == 0); break;
                        case 6: invert = ((((x * y) % 2) + ((x * y) % 3)) % 2 == 0); break;
                        case 7: invert = ((((x + y) % 2) + ((x * y) % 3)) % 2 == 0); break;
                    }
                    if (invert) m[y, x] = !m[y, x];
                }
            }
        }

        private static int CalculatePenalty(bool[,] m, int size)
        {
            int penalty = 0;
            // N1: adjacent same-color runs >= 5
            for (int y = 0; y < size; y++)
            {
                int runLen = 1;
                for (int x = 1; x < size; x++)
                {
                    if (m[y, x] == m[y, x - 1]) runLen++;
                    else { if (runLen >= 5) penalty += 3 + (runLen - 5); runLen = 1; }
                }
                if (runLen >= 5) penalty += 3 + (runLen - 5);
            }
            for (int x = 0; x < size; x++)
            {
                int runLen = 1;
                for (int y = 1; y < size; y++)
                {
                    if (m[y, x] == m[y - 1, x]) runLen++;
                    else { if (runLen >= 5) penalty += 3 + (runLen - 5); runLen = 1; }
                }
                if (runLen >= 5) penalty += 3 + (runLen - 5);
            }
            // N2: 2x2 blocks
            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    bool c = m[y, x];
                    if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) penalty += 3;
                }
            }
            // N3: finder-like patterns
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x <= size - 11; x++)
                {
                    if (IsFinderLikeRow(m, y, x)) penalty += 40;
                }
            }
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y <= size - 11; y++)
                {
                    if (IsFinderLikeCol(m, x, y)) penalty += 40;
                }
            }
            // N4: dark ratio balance
            int dark = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (m[y, x]) dark++;
            int total = size * size;
            int k = Math.Abs(dark * 20 - total * 10) / total;
            penalty += k * 10;
            return penalty;
        }

        private static bool IsFinderLikeRow(bool[,] m, int y, int x)
        {
            bool p1 = m[y, x] && !m[y, x + 1] && m[y, x + 2] && m[y, x + 3] && m[y, x + 4] && !m[y, x + 5] && m[y, x + 6] && !m[y, x + 7] && !m[y, x + 8] && !m[y, x + 9] && !m[y, x + 10];
            bool p2 = !m[y, x] && !m[y, x + 1] && !m[y, x + 2] && !m[y, x + 3] && m[y, x + 4] && !m[y, x + 5] && m[y, x + 6] && m[y, x + 7] && m[y, x + 8] && !m[y, x + 9] && m[y, x + 10];
            return p1 || p2;
        }

        private static bool IsFinderLikeCol(bool[,] m, int x, int y)
        {
            bool p1 = m[y, x] && !m[y + 1, x] && m[y + 2, x] && m[y + 3, x] && m[y + 4, x] && !m[y + 5, x] && m[y + 6, x] && !m[y + 7, x] && !m[y + 8, x] && !m[y + 9, x] && !m[y + 10, x];
            bool p2 = !m[y, x] && !m[y + 1, x] && !m[y + 2, x] && !m[y + 3, x] && m[y + 4, x] && !m[y + 5, x] && m[y + 6, x] && m[y + 7, x] && m[y + 8, x] && !m[y + 9, x] && m[y + 10, x];
            return p1 || p2;
        }

        public static BitmapSource RenderBitmap(string text, int pixelsPerModule, int quietZoneModules)
        {
            bool[,] matrix = Encode(text);
            int n = matrix.GetLength(0);
            int totalModules = n + quietZoneModules * 2;
            int pxSize = totalModules * pixelsPerModule;
            int stride = pxSize * 4;
            byte[] pixels = new byte[stride * pxSize];

            // Fill crisp white background (BGRA)
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = 255;
            }

            // Draw dark modules (#0F172A deep slate for crisp contrast)
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (!matrix[y, x]) continue;
                    int pxY0 = (y + quietZoneModules) * pixelsPerModule;
                    int pxX0 = (x + quietZoneModules) * pixelsPerModule;
                    for (int dy = 0; dy < pixelsPerModule; dy++)
                    {
                        int rowStart = (pxY0 + dy) * stride;
                        for (int dx = 0; dx < pixelsPerModule; dx++)
                        {
                            int idx = rowStart + (pxX0 + dx) * 4;
                            pixels[idx] = 0x2A;     // B
                            pixels[idx + 1] = 0x17; // G
                            pixels[idx + 2] = 0x0F; // R
                            pixels[idx + 3] = 0xFF; // A
                        }
                    }
                }
            }

            BitmapSource bmp = BitmapSource.Create(pxSize, pxSize, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        public static void SavePng(BitmapSource bmp, string path)
        {
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }
    }
}
