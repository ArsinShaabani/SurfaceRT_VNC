using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// RFB "VNC authentication" (security type 2, RFC 6143 7.1.2).
    ///
    /// The password is truncated to 8 bytes and the bits of every byte are reversed to form
    /// the DES key.  The 16 byte challenge sent by the server has the bits of every byte
    /// reversed as well, the two halves are DES encrypted, and the bits of every byte of the
    /// result are reversed once more to produce the response.
    /// </summary>
    public static class VncAuthentication
    {
        public const int KeyLength = 8;
        public const int ChallengeLength = 16;
        public const int ResponseLength = 16;

        /// <summary>Builds the 8 byte DES key from a password (bit reversed, NUL padded).</summary>
        public static byte[] BuildKey(string password)
        {
            byte[] key = new byte[KeyLength];
            if (!string.IsNullOrEmpty(password))
            {
                int count = password.Length < KeyLength ? password.Length : KeyLength;
                for (int i = 0; i < count; i++)
                    key[i] = ReverseBits((byte)(password[i] & 0xFF));
            }
            return key;
        }

        /// <summary>Reverses the bit order of every byte.</summary>
        public static byte[] ReverseBits(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = ReverseBits(data[i]);
            return result;
        }

        /// <summary>Reverses the bit order of a single byte.</summary>
        public static byte ReverseBits(byte value)
        {
            int result = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((value & (1 << bit)) != 0) result |= 1 << (7 - bit);
            }
            return (byte)result;
        }

        /// <summary>Computes the 16 byte response for a 16 byte server challenge.</summary>
        public static byte[] ComputeResponse(string password, byte[] challenge)
        {
            if (challenge == null) throw new ArgumentNullException("challenge");
            if (challenge.Length != ChallengeLength)
                throw new ArgumentException("the VNC challenge must be exactly 16 bytes", "challenge");

            byte[] key = BuildKey(password);
            byte[] reversedChallenge = ReverseBits(challenge);
            byte[] encrypted = Des.EncryptEcb(key, reversedChallenge);
            return ReverseBits(encrypted);
        }
    }
}
