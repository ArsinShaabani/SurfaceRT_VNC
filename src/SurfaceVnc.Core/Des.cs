using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// Plain DES (FIPS 46-3) ECB block cipher.  It is only here because RFB "VNC
    /// authentication" (security type 2) encrypts the 16 byte challenge with DES, using the
    /// password bytes with the bits of every byte reversed as the key and reversing the bits
    /// of every response byte (see <see cref="VncAuthentication"/>).
    ///
    /// DES parity bits are ignored, which is exactly what the VNC key derivation relies on.
    /// </summary>
    public static class Des
    {
        // Initial permutation.
        private static readonly byte[] Ip =
        {
            58, 50, 42, 34, 26, 18, 10, 2, 60, 52, 44, 36, 28, 20, 12, 4,
            62, 54, 46, 38, 30, 22, 14, 6, 64, 56, 48, 40, 32, 24, 16, 8,
            57, 49, 41, 33, 25, 17, 9, 1, 59, 51, 43, 35, 27, 19, 11, 3,
            61, 53, 45, 37, 29, 21, 13, 5, 63, 55, 47, 39, 31, 23, 15, 7
        };

        // Final permutation (inverse of Ip).
        private static readonly byte[] Fp =
        {
            40, 8, 48, 16, 56, 24, 64, 32, 39, 7, 47, 15, 55, 23, 63, 31,
            38, 6, 46, 14, 54, 22, 62, 30, 37, 5, 45, 13, 53, 21, 61, 29,
            36, 4, 44, 12, 52, 20, 60, 28, 35, 3, 43, 11, 51, 19, 59, 27,
            34, 2, 42, 10, 50, 18, 58, 26, 33, 1, 41, 9, 49, 17, 57, 25
        };

        // Expansion function E (32 -> 48 bits).
        private static readonly byte[] E =
        {
            32, 1, 2, 3, 4, 5, 4, 5, 6, 7, 8, 9, 8, 9, 10, 11,
            12, 13, 12, 13, 14, 15, 16, 17, 16, 17, 18, 19, 20, 21, 20, 21,
            22, 23, 24, 25, 24, 25, 26, 27, 28, 29, 28, 29, 30, 31, 32, 1
        };

        // Permutation P applied to the S-box output.
        private static readonly byte[] P =
        {
            16, 7, 20, 21, 29, 12, 28, 17, 1, 15, 23, 26, 5, 18, 31, 10,
            2, 8, 24, 14, 32, 27, 3, 9, 19, 13, 30, 6, 22, 11, 4, 25
        };

        // Key schedule: PC-1 (64 -> 56 bits) and PC-2 (56 -> 48 bits).
        private static readonly byte[] Pc1 =
        {
            57, 49, 41, 33, 25, 17, 9, 1, 58, 50, 42, 34, 26, 18,
            10, 2, 59, 51, 43, 35, 27, 19, 11, 3, 60, 52, 44, 36,
            63, 55, 47, 39, 31, 23, 15, 7, 62, 54, 46, 38, 30, 22,
            14, 6, 61, 53, 45, 37, 29, 21, 13, 5, 28, 20, 12, 4
        };

        private static readonly byte[] Pc2 =
        {
            14, 17, 11, 24, 1, 5, 3, 28, 15, 6, 21, 10,
            23, 19, 12, 4, 26, 8, 16, 7, 27, 20, 13, 2,
            41, 52, 31, 37, 47, 55, 30, 40, 51, 45, 33, 48,
            44, 49, 39, 56, 34, 53, 46, 42, 50, 36, 29, 32
        };

        // Left rotations of the two 28 bit key halves per round.
        private static readonly byte[] LeftRotations = { 1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1 };

        // The eight substitution boxes, flattened: box b occupies SBoxes[b*64 .. b*64+63].
        private static readonly byte[] SBoxes =
        {
            // S1
            14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7,
            0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8,
            4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0,
            15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13,
            // S2
            15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10,
            3, 13, 4, 7, 15, 2, 8, 14, 12, 0, 1, 10, 6, 9, 11, 5,
            0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15,
            13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9,
            // S3
            10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8,
            13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1,
            13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7,
            1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12,
            // S4
            7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15,
            13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9,
            10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4,
            3, 15, 0, 6, 10, 1, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14,
            // S5
            2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9,
            14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6,
            4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14,
            11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3,
            // S6
            12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11,
            10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8,
            9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6,
            4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13,
            // S7
            4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1,
            13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6,
            1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2,
            6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12,
            // S8
            13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7,
            1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2,
            7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8,
            2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11
        };

        /// <summary>Encrypts one 8 byte block from <paramref name="input"/> into <paramref name="output"/>.</summary>
        public static void EncryptBlock(byte[] key, byte[] input, int inputOffset, byte[] output, int outputOffset)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (input == null) throw new ArgumentNullException("input");
            if (output == null) throw new ArgumentNullException("output");
            if (key.Length < 8) throw new ArgumentException("DES needs an 8 byte key", "key");
            if (inputOffset < 0 || inputOffset + 8 > input.Length) throw new ArgumentOutOfRangeException("inputOffset");
            if (outputOffset < 0 || outputOffset + 8 > output.Length) throw new ArgumentOutOfRangeException("outputOffset");

            ulong[] subKeys = CreateSubKeys(key);
            FromUInt64(EncryptBlockRaw(ToUInt64(input, inputOffset), subKeys), output, outputOffset);
        }

        /// <summary>Encrypts <paramref name="data"/> (length must be a multiple of 8) in ECB mode.</summary>
        public static byte[] EncryptEcb(byte[] key, byte[] data)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (data == null) throw new ArgumentNullException("data");
            if (data.Length % 8 != 0) throw new ArgumentException("DES-ECB needs whole 8 byte blocks", "data");

            ulong[] subKeys = CreateSubKeys(key);
            byte[] result = new byte[data.Length];
            for (int offset = 0; offset < data.Length; offset += 8)
                FromUInt64(EncryptBlockRaw(ToUInt64(data, offset), subKeys), result, offset);
            return result;
        }

        private static ulong[] CreateSubKeys(byte[] key)
        {
            ulong permuted = Permute(ToUInt64(key, 0), Pc1, 64);
            uint c = (uint)(permuted >> 28) & 0x0FFFFFFFu;
            uint d = (uint)permuted & 0x0FFFFFFFu;

            ulong[] subKeys = new ulong[16];
            for (int round = 0; round < 16; round++)
            {
                c = RotateLeft28(c, LeftRotations[round]);
                d = RotateLeft28(d, LeftRotations[round]);
                subKeys[round] = Permute(((ulong)c << 28) | d, Pc2, 56);
            }
            return subKeys;
        }

        private static uint RotateLeft28(uint value, int shift)
        {
            return ((value << shift) | (value >> (28 - shift))) & 0x0FFFFFFFu;
        }

        private static ulong EncryptBlockRaw(ulong block, ulong[] subKeys)
        {
            ulong permuted = Permute(block, Ip, 64);
            uint left = (uint)(permuted >> 32);
            uint right = (uint)(permuted & 0xFFFFFFFFUL);

            for (int round = 0; round < 16; round++)
            {
                uint next = left ^ Feistel(right, subKeys[round]);
                left = right;
                right = next;
            }

            ulong preOutput = ((ulong)right << 32) | left;
            return Permute(preOutput, Fp, 64);
        }

        private static uint Feistel(uint right, ulong subKey)
        {
            ulong expanded = Permute(right, E, 32) ^ subKey;
            uint substituted = 0;
            for (int box = 0; box < 8; box++)
            {
                int sixBits = (int)((expanded >> (42 - 6 * box)) & 0x3FUL);
                int row = ((sixBits >> 4) & 0x02) | (sixBits & 0x01);
                int column = (sixBits >> 1) & 0x0F;
                substituted = (substituted << 4) | SBoxes[box * 64 + row * 16 + column];
            }
            return (uint)Permute(substituted, P, 32);
        }

        /// <summary>Generic bit permutation; <paramref name="table"/> holds 1 based bit numbers, MSB first.</summary>
        private static ulong Permute(ulong input, byte[] table, int inputBits)
        {
            ulong output = 0;
            for (int i = 0; i < table.Length; i++)
            {
                output <<= 1;
                int sourceBit = table[i];
                if (((input >> (inputBits - sourceBit)) & 1UL) != 0UL) output |= 1UL;
            }
            return output;
        }

        private static ulong ToUInt64(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value = (value << 8) | data[offset + i];
            return value;
        }

        private static void FromUInt64(ulong value, byte[] data, int offset)
        {
            for (int i = 7; i >= 0; i--)
            {
                data[offset + i] = (byte)(value & 0xFF);
                value >>= 8;
            }
        }
    }
}
