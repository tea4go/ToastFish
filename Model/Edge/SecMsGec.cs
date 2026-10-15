using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToastFish.Model.Edge
{
    /// <summary>
    /// 生成 Edge 朗读服务要求的 Sec-MS-GEC 令牌（v2 格式：CS64-SHA256）。
    /// 算法是微软刻意混淆过的自定义摘要，移植自 edge-tts 的 drm 模块；
    /// 服务端一旦改动算法，这里会直接失效（表现为握手 403）。
    /// </summary>
    internal static class SecMsGec
    {
        const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        const double WIN_EPOCH = 11644473600.0;
        const double NS_PER_TICK_UNIT = 1e7;

        static readonly uint[] SHA256_INITIAL =
        {
            0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
            0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
        };

        static readonly uint[] SHA256_STATE =
        {
            0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A,
            0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2,
        };

        static readonly uint[] SHA256_SCHEDULE =
        {
            0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA,
            0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6,
            0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33,
            0xD258B060, 0x72766564,
        };

        static readonly uint[] SHA256_ROUND_CONSTANTS =
        {
            0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
            0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
            0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
            0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
            0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
            0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
            0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
            0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
            0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
            0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
            0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
            0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
            0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
            0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
            0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
            0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
        };

        const uint SHA256_ADJUSTMENT = 0xC9DC06A7;
        const uint SHA256_SIGMA0_ADJUSTMENT = 0x059C0B80;
        const uint SHA256_SIGMA1_ADJUSTMENT = 0x000B92C0;

        static readonly uint[] SHA256_BLOCK_CONSTANTS = BuildBlockConstants();

        static readonly uint[,] CS64_COEFFICIENTS =
        {
            { 0xE82E91B1, 0xB32700D0 },
            { 0x44DE24C6, 0x88DFF51D },
            { 0x3BEB65BC, 0x29514038 },
            { 0xC89C5933, 0xB328CBDF },
            { 0x8D1EC211, 0xD405B4D5 },
            { 0x314EF5D5, 0x26E55F3C },
            { 0xE92491B1, 0x607801C6 },
            { 0xF0A5B9D5, 0x82B2C1FB },
            { 0x06064CF3, 0xFA51A3DA },
            { 0xDA79FA60, 0x207CC464 },
            { 0xBED20560, 0x3C3C74F3 },
            { 0x89F9C9F8, 0xD2D14B61 },
            { 0x35FA38C6, 0xBB04A2C1 },
            { 0xCB49E3C6, 0xC5626167 },
        };

        const uint CS64_FACTOR = 0x82893933;
        const uint CS64_ADJUSTMENT0 = 0xF2F90B1A;
        const uint CS64_ADJUSTMENT1 = 0x0B1A0000;

        public static string Generate()
        {
            double seconds = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return Generate(seconds);
        }

        internal static string Generate(double unixTimestamp)
        {
            double ticks = unixTimestamp + WIN_EPOCH;
            ticks -= ticks % 300.0;
            ticks *= NS_PER_TICK_UNIT;

            byte[] data = Encoding.ASCII.GetBytes(
                ((long)ticks).ToString(CultureInfo.InvariantCulture) + TRUSTED_CLIENT_TOKEN);

            byte[] digest = Sha256Digest(data);
            uint[] cs64 = Cs64Digest(data, digest);
            return LeHex(cs64[0]) + LeHex(cs64[1]) + "-" + HexUpper(digest);
        }

        static uint[] BuildBlockConstants()
        {
            var result = new uint[64];
            Array.Copy(SHA256_ROUND_CONSTANTS, result, 14);
            result[14] = SHA256_ADJUSTMENT;
            Array.Copy(SHA256_ROUND_CONSTANTS, 15, result, 15, 49);
            return result;
        }

        static uint Rol16(uint value)
        {
            return (value << 16) | (value >> 16);
        }

        static uint Rotr(uint value, int bits)
        {
            return (value >> bits) | (value << (32 - bits));
        }

        static uint BigSigma0(uint x) { return Rotr(x, 2) ^ Rotr(x, 13) ^ Rotr(x, 22); }
        static uint BigSigma1(uint x) { return Rotr(x, 6) ^ Rotr(x, 11) ^ Rotr(x, 25); }
        static uint SmallSigma0(uint x) { return Rotr(x, 7) ^ Rotr(x, 18) ^ (x >> 3); }
        static uint SmallSigma1(uint x) { return Rotr(x, 17) ^ Rotr(x, 19) ^ (x >> 10); }
        static uint Ch(uint e, uint f, uint g) { return g ^ (e & (f ^ g)); }
        static uint Maj(uint a, uint b, uint c) { return (a & b) | (c & (a | b)); }

        static uint[] Sha256Rounds(uint[] state, uint[] words, int firstRound)
        {
            uint[] constants = firstRound == 14 ? SHA256_BLOCK_CONSTANTS : SHA256_ROUND_CONSTANTS;
            var r = (uint[])state.Clone();
            for (int index = firstRound; index < 64; index++)
            {
                uint t1 = unchecked(r[7] + BigSigma1(r[4]) + Ch(r[4], r[5], r[6]) + constants[index] + words[index]);
                uint t2 = unchecked(BigSigma0(r[0]) + Maj(r[0], r[1], r[2]));
                r[3] = unchecked(r[3] + t1);
                r[7] = unchecked(t1 + t2);
                uint rotated = r[7];
                for (int i = 7; i > 0; i--) r[i] = r[i - 1];
                r[0] = rotated;
            }
            return r;
        }

        static void ExpandSchedule(List<uint> words, int firstWord)
        {
            for (int index = firstWord; index < 64; index++)
            {
                uint x = words[index - 2];
                uint y = words[index - 15];
                words.Add(unchecked(SmallSigma1(x) + words[index - 7] + SmallSigma0(y) + words[index - 16]));
            }
        }

        static uint[] Sha256Init(byte[] data)
        {
            var words = new List<uint>(64);
            for (int i = 0; i < 14; i++) words.Add(0);
            words.Add((uint)((data[0] << 16) | (data[1] << 8) | data[2]));
            words.Add((uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]));

            for (int index = 16; index < 30; index++)
            {
                uint value = SHA256_SCHEDULE[index - 16];
                if (index - 2 >= 14)
                {
                    uint sigma1 = SmallSigma1(words[index - 2]);
                    value = unchecked(value + (index == 16 ? sigma1 ^ SHA256_SIGMA1_ADJUSTMENT : sigma1));
                }
                if (index - 7 >= 14)
                {
                    value = unchecked(value + words[index - 7]);
                }
                if (index - 15 >= 14)
                {
                    uint sigma0 = SmallSigma0(words[index - 15]);
                    value = unchecked(value + (index == 29 ? sigma0 ^ SHA256_SIGMA0_ADJUSTMENT : sigma0));
                }
                words.Add(value);
            }

            words.Add(unchecked(
                SmallSigma1(words[28]) + words[23] + SmallSigma0(words[15]) + words[14]
                + SHA256_ADJUSTMENT - SHA256_ROUND_CONSTANTS[14]));

            ExpandSchedule(words, 31);

            uint[] working = Sha256Rounds(SHA256_STATE, words.ToArray(), 14);
            var result = new uint[8];
            for (int i = 0; i < 8; i++) result[i] = unchecked(SHA256_INITIAL[i] + working[i]);
            return result;
        }

        static byte[] Sha256Digest(byte[] data)
        {
            int length = 57 + data.Length;
            int zeroCount = ((55 - length) % 64 + 64) % 64;

            var padded = new List<byte>(data.Length + 72);
            for (int i = 7; i < data.Length; i++) padded.Add(data[i]);
            padded.Add(0x80);
            for (int i = 0; i < zeroCount; i++) padded.Add(0);
            ulong bitLength = (ulong)length * 8;
            for (int i = 7; i >= 0; i--) padded.Add((byte)(bitLength >> (8 * i)));
            byte[] block = padded.ToArray();

            uint[] state = Sha256Init(data);
            for (int offset = 0; offset < block.Length; offset += 64)
            {
                var words = new List<uint>(64);
                for (int i = 0; i < 16; i++)
                {
                    int o = offset + i * 4;
                    words.Add((uint)((block[o] << 24) | (block[o + 1] << 16) | (block[o + 2] << 8) | block[o + 3]));
                }
                ExpandSchedule(words, 16);
                uint[] working = Sha256Rounds(state, words.ToArray(), 0);
                for (int i = 0; i < 8; i++) state[i] = unchecked(state[i] + working[i]);
            }

            var digest = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                digest[i * 4] = (byte)(state[i] >> 24);
                digest[i * 4 + 1] = (byte)(state[i] >> 16);
                digest[i * 4 + 2] = (byte)(state[i] >> 8);
                digest[i * 4 + 3] = (byte)state[i];
            }
            return digest;
        }

        static uint[] WordsLe4Even(byte[] data)
        {
            int pad = ((-data.Length) % 4 + 4) % 4;
            int total = data.Length + pad;
            if (total == 0) return new uint[0];

            var bytes = new byte[total];
            Array.Copy(data, bytes, data.Length);

            var words = new List<uint>(total / 4 + 1);
            for (int i = 0; i < total; i += 4)
            {
                words.Add((uint)(bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16) | (bytes[i + 3] << 24)));
            }
            if (words.Count % 2 != 0) words.Add(0);
            return words.ToArray();
        }

        static uint[] Cs64Digest(byte[] data, byte[] digest)
        {
            uint keyA = BitConverter.ToUInt32(digest, 0) | 1;
            uint keyB = BitConverter.ToUInt32(digest, 4) | 1;
            uint[] factors = { unchecked(keyA * CS64_FACTOR), unchecked(keyB * CS64_FACTOR) };

            var terms = new List<uint>(64);
            var offsets = new List<uint>(64);
            for (int i = 0; i < CS64_COEFFICIENTS.GetLength(0); i++)
            {
                terms.Add(unchecked(CS64_COEFFICIENTS[i, 0] * factors[i % 2]));
                offsets.Add(CS64_COEFFICIENTS[i, 1]);
            }

            var prefixed = new byte[data.Length + 1];
            Array.Copy(data, 0, prefixed, 1, data.Length);
            uint[] words = WordsLe4Even(prefixed);
            for (int index = 0; index < words.Length; index++)
            {
                uint word = words[index];
                bool even = index % 2 == 0;
                uint term = unchecked(word * (even ? keyA : keyB));
                uint offset = unchecked(Rol16(word) * (even ? 0xEF0569FB : 0xC31713DB));
                if (index == 0)
                {
                    term = unchecked(term + CS64_ADJUSTMENT0 * factors[0]);
                    offset = unchecked(offset + CS64_ADJUSTMENT1);
                }
                terms.Add(term);
                offsets.Add(offset);
            }

            uint swapAcc = 0, swapLast = 0, state = 0, acc = 0;
            for (int index = 0; index < terms.Count; index += 2)
            {
                uint x = terms[index], y = terms[index + 1];

                uint t = Rol16(unchecked(offsets[index] + x));
                uint q = unchecked(t * 0x76F50F34);
                swapAcc = unchecked(swapAcc + (unchecked(Rol16(q) * 0xC3EFEA97) + q));

                t = Rol16(unchecked(offsets[index + 1] + y));
                q = unchecked(t * 0x1881CE3C);
                swapLast = unchecked(Rol16(q) * 0x35BD1EC9 + q);
                swapAcc = unchecked(swapAcc + swapLast);

                state = unchecked(state * keyA + x);
                state = unchecked(Rol16(state) * 0xCF98B111);
                state = unchecked(Rol16(state) * 0x87085B9F);
                state = unchecked(Rol16(state) * 0x12CEB96D);
                state = unchecked(Rol16(state) * 0x257E1D83);
                acc = unchecked(acc + state);

                t = unchecked(state * keyB + y);
                t = unchecked(Rol16(t) * 0xA27416F5);
                t = unchecked(Rol16(t) * 0xD38396FF);
                state = unchecked(Rol16(unchecked(Rol16(t) * 0x7C932B89)) * 0xBFA49F69);
                acc = unchecked(acc + state);
            }

            return new[] { unchecked(swapLast ^ state), unchecked(swapAcc ^ acc) };
        }

        static string LeHex(uint value)
        {
            var sb = new StringBuilder(8);
            sb.Append(((byte)value).ToString("X2"));
            sb.Append(((byte)(value >> 8)).ToString("X2"));
            sb.Append(((byte)(value >> 16)).ToString("X2"));
            sb.Append(((byte)(value >> 24)).ToString("X2"));
            return sb.ToString();
        }

        static string HexUpper(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
