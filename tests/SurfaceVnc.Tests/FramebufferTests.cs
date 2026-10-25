using System;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Framebuffer tests.</summary>
    public static class FramebufferTests
    {
        public static void Register()
        {
            TestRunner.Add("Framebuffer clips writes at the edges", Clipping);
            TestRunner.Add("Framebuffer CopyRect handles overlapping scrolls", CopyRectOverlap);
            TestRunner.Add("Framebuffer CopyRegionTo honours the destination stride", CopyRegionTo);
            TestRunner.Add("Framebuffer resize clears and notifies", Resize);
            TestRunner.Add("Rect union and clip", RectMath);
        }

        private static void Clipping()
        {
            Framebuffer framebuffer = new Framebuffer();
            framebuffer.Resize(4, 4);
            framebuffer.FillBgra8(0, 0, 4, 4, 0, 0, 0);

            byte[] pixels = WireImage.Create(3, 3, PixelFormat.DefaultBgra8, (x, y) => WireImage.Bgra(0xFF, 0xFF, 0xFF));
            framebuffer.WritePixels(2, 2, 3, 3, pixels, 0, PixelFormat.DefaultBgra8);

            Check.Pixel(framebuffer, 0, 0, 0, 0, 0, "outside the write must stay black");
            Check.Pixel(framebuffer, 1, 1, 0, 0, 0, "outside the write must stay black");
            Check.Pixel(framebuffer, 2, 2, 255, 255, 255, "inside the write");
            Check.Pixel(framebuffer, 3, 3, 255, 255, 255, "inside the write");

            // A rectangle entirely outside the framebuffer must be ignored without throwing.
            framebuffer.WritePixels(-10, -10, 2, 2, pixels, 0, PixelFormat.DefaultBgra8);
            Check.Pixel(framebuffer, 0, 0, 0, 0, 0, "fully clipped write must be ignored");
        }

        private static void CopyRectOverlap()
        {
            Framebuffer framebuffer = new Framebuffer();
            framebuffer.Resize(4, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    framebuffer.FillBgra8(x, y, 1, 1, (byte)(y * 40), (byte)(x * 40), 0);

            // Scroll the top three rows down by one row (blue channel encodes y).
            framebuffer.CopyRect(0, 0, 4, 3, 0, 1);
            Check.Pixel(framebuffer, 0, 1, 0, 0, 0, "row 0 copied to row 1");
            Check.Pixel(framebuffer, 3, 1, 0, 120, 0, "row 0 copied to row 1 (right edge)");
            Check.Pixel(framebuffer, 0, 3, 0, 0, 80, "row 2 copied to row 3");

            // And scroll it back up (source and destination overlap the other way).
            framebuffer.CopyRect(0, 1, 4, 3, 0, 0);
            Check.Pixel(framebuffer, 0, 0, 0, 0, 0, "row 1 (black) copied to row 0");
            Check.Pixel(framebuffer, 0, 1, 0, 0, 40, "row 2 copied to row 1");
            Check.Pixel(framebuffer, 0, 2, 0, 0, 80, "row 3 copied to row 2");
        }

        private static void CopyRegionTo()
        {
            Framebuffer framebuffer = new Framebuffer();
            framebuffer.Resize(4, 4);
            framebuffer.FillBgra8(0, 0, 4, 4, 0x10, 0x20, 0x30);
            framebuffer.FillBgra8(2, 2, 1, 1, 0xAA, 0xBB, 0xCC);

            // The destination is addressed relative to the region: 2x2 pixels, stride 12 bytes.
            byte[] destination = new byte[2 * 12];
            framebuffer.CopyRegionTo(new Rect(1, 1, 2, 2), destination, 0, 12);

            Check.Equal(0x10, destination[0], "region pixel blue");
            Check.Equal(0x20, destination[1], "region pixel green");
            Check.Equal(0x30, destination[2], "region pixel red");
            Check.Equal(0xFF, destination[3], "region pixel alpha");

            // Row 1 of the region starts at byte 12 and its second pixel is framebuffer (2,2).
            Check.Equal(0x10, destination[12], "second row, first pixel");
            Check.Equal(0xAA, destination[16], "second row, second pixel");

            // A destination offset shifts the whole region.
            byte[] offsetDestination = new byte[2 * 12 + 8];
            framebuffer.CopyRegionTo(new Rect(1, 1, 2, 2), offsetDestination, 8, 12);
            Check.Equal(0x10, offsetDestination[8], "region with an offset");
            Check.Equal(0, offsetDestination[0], "bytes before the offset must be untouched");
        }

        private static void Resize()
        {
            Framebuffer framebuffer = new Framebuffer();
            int notifications = 0;
            framebuffer.SizeChanged += (sender, args) => notifications++;

            framebuffer.Resize(10, 10);
            Check.Equal(10, framebuffer.Width, "width after resize");
            Check.Equal(40, framebuffer.Stride, "stride after resize");
            Check.Equal(1, notifications, "one notification for the first resize");

            framebuffer.FillBgra8(0, 0, 10, 10, 1, 2, 3);
            framebuffer.Resize(10, 10);
            Check.Pixel(framebuffer, 0, 0, 0, 0, 0, "resizing clears the framebuffer");
            Check.Equal(1, notifications, "resizing to the same size must not report a size change");
        }

        private static void RectMath()
        {
            Rect union = new Rect(1, 2, 3, 4).Union(new Rect(5, 6, 2, 2));
            Check.Equal(1, union.X, "union x");
            Check.Equal(2, union.Y, "union y");
            Check.Equal(6, union.Width, "union width");
            Check.Equal(6, union.Height, "union height");

            Rect clipped = new Rect(-2, -2, 5, 5).Clip(4, 4);
            Check.Equal(0, clipped.X, "clipped x");
            Check.Equal(3, clipped.Width, "clipped width");

            Check.True(new Rect(0, 0, 0, 5).IsEmpty, "zero width is empty");
            Check.True(Rect.Empty.Union(new Rect(2, 2, 1, 1)).Equals(new Rect(2, 2, 1, 1)), "union with empty");
        }
    }
}
