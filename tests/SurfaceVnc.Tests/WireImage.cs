using System;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Helpers to build images that are already in the RFB wire pixel format.</summary>
    public static class WireImage
    {
        public static byte[] Create(int width, int height, PixelFormat format, Func<int, int, uint> colourAt)
        {
            byte[] image = new byte[width * height * format.BytesPerPixel];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    SetValue(image, width, x, y, format, colourAt(x, y));
            return image;
        }

        public static void SetValue(byte[] image, int width, int x, int y, PixelFormat format, uint value)
        {
            int bpp = format.BytesPerPixel;
            int offset = (y * width + x) * bpp;
            switch (bpp)
            {
                case 1:
                    image[offset] = (byte)value;
                    break;
                case 2:
                    if (format.BigEndianFlag) { image[offset] = (byte)(value >> 8); image[offset + 1] = (byte)value; }
                    else { image[offset] = (byte)value; image[offset + 1] = (byte)(value >> 8); }
                    break;
                case 3:
                    if (format.BigEndianFlag)
                    {
                        image[offset] = (byte)(value >> 16); image[offset + 1] = (byte)(value >> 8); image[offset + 2] = (byte)value;
                    }
                    else
                    {
                        image[offset] = (byte)value; image[offset + 1] = (byte)(value >> 8); image[offset + 2] = (byte)(value >> 16);
                    }
                    break;
                default:
                    if (format.BigEndianFlag)
                    {
                        image[offset] = (byte)(value >> 24); image[offset + 1] = (byte)(value >> 16);
                        image[offset + 2] = (byte)(value >> 8); image[offset + 3] = (byte)value;
                    }
                    else
                    {
                        image[offset] = (byte)value; image[offset + 1] = (byte)(value >> 8);
                        image[offset + 2] = (byte)(value >> 16); image[offset + 3] = (byte)(value >> 24);
                    }
                    break;
            }
        }

        public static uint GetValue(byte[] image, int width, int x, int y, PixelFormat format)
        {
            int bpp = format.BytesPerPixel;
            int offset = (y * width + x) * bpp;
            uint result = 0;
            if (format.BigEndianFlag)
            {
                for (int i = 0; i < bpp; i++) result = (result << 8) | image[offset + i];
            }
            else
            {
                for (int i = bpp - 1; i >= 0; i--) result = (result << 8) | image[offset + i];
            }
            return result;
        }

        public static byte[] PixelBytes(byte[] image, int width, int x, int y, PixelFormat format)
        {
            int bpp = format.BytesPerPixel;
            byte[] result = new byte[bpp];
            Buffer.BlockCopy(image, (y * width + x) * bpp, result, 0, bpp);
            return result;
        }

        /// <summary>Builds the wire value of a 32bpp BGRA8 pixel (little endian byte order B,G,R,X).</summary>
        public static uint Bgra(byte r, byte g, byte b)
        {
            return (uint)(b | (g << 8) | (r << 16));
        }

        public static bool SamePixel(byte[] image, int width, int x, int y, byte[] pixel)
        {
            int bpp = pixel.Length;
            int offset = (y * width + x) * bpp;
            for (int i = 0; i < bpp; i++)
            {
                if (image[offset + i] != pixel[i]) return false;
            }
            return true;
        }
    }
}
