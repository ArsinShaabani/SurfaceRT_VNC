using System;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>DES and VNC authentication tests, checked against externally produced vectors.</summary>
    public static class AuthTests
    {
        public static void Register()
        {
            TestRunner.Add("DES matches the FIPS 46-3 test vectors", DesMatchesFipsVectors);
            TestRunner.Add("VNC authentication matches the OpenSSL reference vector", VncAuthMatchesOpenSslVector);
            TestRunner.Add("VNC authentication key derivation", VncAuthKeyDerivation);
            TestRunner.Add("VNC authentication rejects a wrong password", VncAuthWrongPassword);
            TestRunner.Add("Bit reversal is its own inverse", BitReversalRoundTrip);
        }

        private static byte[] ParseHex(string text)
        {
            byte[] result = new byte[text.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return result;
        }

        private static void AssertDes(string keyHex, string plainHex, string cipherHex)
        {
            byte[] key = ParseHex(keyHex);
            byte[] plain = ParseHex(plainHex);
            byte[] expected = ParseHex(cipherHex);
            byte[] actual = Des.EncryptEcb(key, plain);
            Check.Bytes(expected, actual, "DES(" + keyHex + ", " + plainHex + ")");
        }

        private static void DesMatchesFipsVectors()
        {
            // Published FIPS 46-3 / classic DES known answer vectors.
            AssertDes("133457799BBCDFF1", "0123456789ABCDEF", "85E813540F0AB405");
            AssertDes("0123456789ABCDEF", "4E6F772069732074", "3FA40E8A984D4815");
            AssertDes("0E329232EA6D0D73", "8787878787878787", "0000000000000000");
        }

        /// <summary>
        /// The expected response was produced outside this code base with
        /// `openssl enc -des-ecb -K 0e86ceceeef64e26 -nopad` on the bit reversed challenge,
        /// with the bits of the result reversed (which is what RFB VNC authentication does).
        /// </summary>
        private static void VncAuthMatchesOpenSslVector()
        {
            byte[] challenge = ParseHex("00112233445566778899AABBCCDDEEFF");
            byte[] response = VncAuthentication.ComputeResponse("password", challenge);

            Check.Bytes(ParseHex("87A725DBE9F34E9BAA26250220E2C981"), response, "VNC auth response");
            Check.Bytes(ParseHex("0E86CECEEEF64E26"), VncAuthentication.BuildKey("password"), "VNC auth DES key");
        }

        private static void VncAuthKeyDerivation()
        {
            // Passwords longer than 8 bytes are truncated, shorter ones are NUL padded.
            Check.Bytes(ParseHex("0E86CECEEEF64E26"), VncAuthentication.BuildKey("password-too-long"), "truncated key");
            Check.Bytes(ParseHex("0E00000000000000"), VncAuthentication.BuildKey("p"), "padded key");
            Check.Bytes(ParseHex("0000000000000000"), VncAuthentication.BuildKey(null), "null password key");
        }

        private static void VncAuthWrongPassword()
        {
            byte[] challenge = ParseHex("00112233445566778899AABBCCDDEEFF");
            byte[] right = VncAuthentication.ComputeResponse("password", challenge);
            byte[] wrong = VncAuthentication.ComputeResponse("Password", challenge);
            Check.False(Check.Hex(right) == Check.Hex(wrong), "a different password must produce a different response");

            byte[] shortChallenge = new byte[8];
            Check.Throws<ArgumentException>(
                () => VncAuthentication.ComputeResponse("password", shortChallenge),
                "a challenge that is not 16 bytes must be rejected");
        }

        private static void BitReversalRoundTrip()
        {
            for (int value = 0; value < 256; value++)
            {
                byte reversed = VncAuthentication.ReverseBits((byte)value);
                byte again = VncAuthentication.ReverseBits(reversed);
                if (again != value)
                    throw new CheckFailedException("bit reversal is not an involution for " + value);
            }
            Check.Equal(0x0E, (int)VncAuthentication.ReverseBits(0x70), "0x70 reversed");
        }
    }
}
