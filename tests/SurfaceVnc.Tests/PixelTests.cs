using System;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Pixel conversion tests.</summary>
    public static class PixelTests
    {
        public static void Register()
        {
            TestRunner.Add("PixelFormat round trips through its wire form", PixelFormatRoundTrip);
            TestRunner.Add("32bpp pixels are copied verbatim with an opaque alpha", Pixel32BppFastPath);
            TestRunner.Add("16bpp 5-6-5 pixels are scaled to 8 bits per channel", Pixel16Bpp565);
            TestRunner.Add("24bpp little and big endian pixels", Pixel24Bpp);
            TestRunner.Add("32bpp big endian and 8bpp pixels", Pixel32BigEndianAnd8Bpp);
            TestRunner.Add("colour map pixel formats are rejected", PixelColourMapRejected);
        }

        internal static byte[] Convert(byte[] source, PixelFormat format, int pixelCount)
        {
            byte[] destination = new byte[pixelCount * 4];
            PixelConverter.ToBgra8(source, 0, pixelCount, format, destination, 0);
            return destination;
        }

        internal static void AssertPixel(byte[] bgra, int index, byte r, byte g, byte b)
        {
            int offset = index * 4;
            if (bgra[offset + 0] != b || bgra[offset + 1] != g || bgra[offset + 2] != r || bgra[offset + 3] != 255)
            {
                throw new CheckFailedException("pixel " + index + ": expected bgra(" + b + "," + g + "," + r + ",255) got bgra("
                    + bgra[offset] + "," + bgra[offset + 1] + "," + bgra[offset + 2] + "," + bgra[offset + 3] + ")");
            }
        }

        private static void PixelFormatRoundTrip()
        {
            PixelFormat format = PixelFormat.DefaultBgra8;
            byte[] wire = new byte[16];
            format.WriteTo(wire, 0);
            PixelFormat parsed = PixelFormat.ReadFrom(wire, 0);

            Check.True(format.Equals(parsed), "pixel format did not survive a wire round trip");
            Check.Equal(4, format.BytesPerPixel, "32bpp is 4 bytes per pixel");
            Check.True(format.IsWireCompatibleWithBgra8, "the default format must be wire compatible with BGRA8");
            Check.False(Rgb565().IsWireCompatibleWithBgra8, "16bpp must not take the fast path");
        }

        internal static PixelFormat Rgb565()
        {
            PixelFormat format = new PixelFormat();
            format.BitsPerPixel = 16;
            format.Depth = 16;
            format.TrueColourFlag = true;
            format.RedMax = 31; format.GreenMax = 63; format.BlueMax = 31;
            format.RedShift = 11; format.GreenShift = 5; format.BlueShift = 0;
            return format;
        }

        internal static PixelFormat Rgb888()
        {
            PixelFormat format = new PixelFormat();
            format.BitsPerPixel = 24;
            format.Depth = 24;
            format.TrueColourFlag = true;
            format.RedMax = 255; format.GreenMax = 255; format.BlueMax = 255;
            format.RedShift = 16; format.GreenShift = 8; format.BlueShift = 0;
            return format;
        }

        private static void Pixel32BppFastPath()
        {
            // Wire order for 32bpp little endian BGRA8 is B,G,R,X.
            byte[] source = { 0x11, 0x22, 0x33, 0x00, 0x44, 0x55, 0x66, 0xFF };
            byte[] result = Convert(source, PixelFormat.DefaultBgra8, 2);

            Check.Bytes(new byte[] { 0x11, 0x22, 0x33, 0xFF, 0x44, 0x55, 0x66, 0xFF }, result, "32bpp conversion");
        }

        private static void Pixel16Bpp565()
        {
            PixelFormat format = Rgb565();

            byte[] source = new byte[8];
            WireImage.SetValue(source, 4, 0, 0, format, 0xF81F);   // magenta: r=31, g=0,  b=31
            WireImage.SetValue(source, 4, 1, 0, format, 0x07E0);   // green:   r=0,  g=63, b=0
            WireImage.SetValue(source, 4, 2, 0, format, 0x001F);   // blue:    b=31
            WireImage.SetValue(source, 4, 3, 0, format, 0x0000);   // black

            byte[] result = Convert(source, format, 4);
            AssertPixel(result, 0, 255, 0, 255);
            AssertPixel(result, 1, 0, 255, 0);
            AssertPixel(result, 2, 0, 0, 255);
            AssertPixel(result, 3, 0, 0, 0);
        }

        private static void Pixel24Bpp()
        {
            byte[] source = { 0x30, 0x20, 0x10 };
            AssertPixel(Convert(source, Rgb888(), 1), 0, 0x10, 0x20, 0x30);

            PixelFormat big = Rgb888();
            big.BigEndianFlag = true;
            byte[] bigEndianSource = { 0x10, 0x20, 0x30 };
            AssertPixel(Convert(bigEndianSource, big, 1), 0, 0x10, 0x20, 0x30);
        }

        private static void Pixel32BigEndianAnd8Bpp()
        {
            PixelFormat big = PixelFormat.DefaultBgra8;
            big.BigEndianFlag = true;
            byte[] source = { 0x00, 0x11, 0x22, 0x33 };
            AssertPixel(Convert(source, big, 1), 0, 0x11, 0x22, 0x33);

            PixelFormat eight = new PixelFormat();
            eight.BitsPerPixel = 8;
            eight.Depth = 8;
            eight.TrueColourFlag = true;
            eight.RedMax = 255; eight.GreenMax = 255; eight.BlueMax = 255;
            byte[] grey = { 0xAB };
            AssertPixel(Convert(grey, eight, 1), 0, 0xAB, 0xAB, 0xAB);
        }

        private static void PixelColourMapRejected()
        {
            PixelFormat format = new PixelFormat();
            format.BitsPerPixel = 8;
            format.Depth = 8;
            format.TrueColourFlag = false;
            byte[] source = { 0x00 };

            Check.Throws<RfbException>(() => Convert(source, format, 1), "a colour map pixel format must be rejected");
        }
    }
}
