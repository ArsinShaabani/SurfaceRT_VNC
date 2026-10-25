using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// Converts raw RFB pixels (any of the 8/16/24/32 bpp layouts) into the BGRA8 layout
    /// used by our framebuffer and by the WinRT WriteableBitmap.
    ///
    /// The alpha byte is always forced to 0xFF because WriteableBitmap treats its buffer
    /// as *premultiplied* BGRA, so a zero alpha byte would make the pixel invisible.
    /// </summary>
    public static class PixelConverter
    {
        /// <summary>Converts <paramref name="pixelCount"/> pixels from <paramref name="src"/> to BGRA8.</summary>
        public static void ToBgra8(byte[] src, int srcOffset, int pixelCount, PixelFormat format, byte[] dst, int dstOffset)
        {
            if (src == null) throw new ArgumentNullException("src");
            if (dst == null) throw new ArgumentNullException("dst");
            if (pixelCount <= 0) return;

            int bpp = format.BytesPerPixel;
            if (srcOffset < 0 || srcOffset + pixelCount * bpp > src.Length)
                throw new ArgumentOutOfRangeException("src", "source buffer is too small for the requested pixels");
            if (dstOffset < 0 || dstOffset + pixelCount * 4 > dst.Length)
                throw new ArgumentOutOfRangeException("dst", "destination buffer is too small");

            if (format.IsWireCompatibleWithBgra8)
            {
                Buffer.BlockCopy(src, srcOffset, dst, dstOffset, pixelCount * 4);
                int end = dstOffset + pixelCount * 4;
                for (int i = dstOffset + 3; i < end; i += 4)
                    dst[i] = 0xFF;
                return;
            }

            if (!format.TrueColourFlag)
                throw new RfbException("colour-map (non true colour) pixel formats are not supported; the client always requests true colour");

            uint rMask = format.RedMax;
            uint gMask = format.GreenMax;
            uint bMask = format.BlueMax;

            for (int i = 0; i < pixelCount; i++)
            {
                uint value = ReadPixel(src, srcOffset + i * bpp, format);
                int d = dstOffset + i * 4;
                dst[d + 0] = ScaleToByte((value >> format.BlueShift) & bMask, bMask);
                dst[d + 1] = ScaleToByte((value >> format.GreenShift) & gMask, gMask);
                dst[d + 2] = ScaleToByte((value >> format.RedShift) & rMask, rMask);
                dst[d + 3] = 0xFF;
            }
        }

        /// <summary>Reads one raw pixel as a host order unsigned integer.</summary>
        public static uint ReadPixel(byte[] src, int offset, PixelFormat format)
        {
            switch (format.BitsPerPixel)
            {
                case 8:
                    return src[offset];
                case 16:
                    if (format.BigEndianFlag)
                        return (uint)((src[offset] << 8) | src[offset + 1]);
                    return (uint)(src[offset] | (src[offset + 1] << 8));
                case 24:
                    if (format.BigEndianFlag)
                        return (uint)((src[offset] << 16) | (src[offset + 1] << 8) | src[offset + 2]);
                    return (uint)(src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16));
                case 32:
                    if (format.BigEndianFlag)
                        return ((uint)src[offset] << 24) | ((uint)src[offset + 1] << 16) | ((uint)src[offset + 2] << 8) | src[offset + 3];
                    return (uint)src[offset] | ((uint)src[offset + 1] << 8) | ((uint)src[offset + 2] << 16) | ((uint)src[offset + 3] << 24);
                default:
                    throw new RfbException("unsupported bits-per-pixel: " + format.BitsPerPixel);
            }
        }

        /// <summary>Writes one BGRA8 pixel into <paramref name="dst"/>.</summary>
        public static void WriteBgra8(byte[] dst, int offset, byte b, byte g, byte r)
        {
            dst[offset + 0] = b;
            dst[offset + 1] = g;
            dst[offset + 2] = r;
            dst[offset + 3] = 0xFF;
        }

        /// <summary>Scales a channel value from its native maximum down to 0..255.</summary>
        public static byte ScaleToByte(uint value, uint max)
        {
            if (max == 0) return 0;
            if (max == 255) return (byte)value;
            uint scaled = (value * 255u + (max >> 1)) / max;
            if (scaled > 255u) scaled = 255u;
            return (byte)scaled;
        }
    }
}
