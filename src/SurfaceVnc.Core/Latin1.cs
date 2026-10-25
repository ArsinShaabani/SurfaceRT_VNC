using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// RFB strings (server name, cut text) are Latin-1, i.e. one byte == one character.
    /// Implemented by hand so the code does not depend on code page support being available
    /// (Windows Store apps and invariant-globalization hosts are both happier this way).
    /// </summary>
    public static class Latin1
    {
        public static string GetString(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (count <= 0) return string.Empty;

            char[] characters = new char[count];
            for (int i = 0; i < count; i++)
                characters[i] = (char)data[offset + i];
            return new string(characters);
        }

        public static string GetString(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            return GetString(data, 0, data.Length);
        }

        public static byte[] GetBytes(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];

            byte[] bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bytes[i] = c <= 0xFF ? (byte)c : (byte)'?';
            }
            return bytes;
        }
    }
}
