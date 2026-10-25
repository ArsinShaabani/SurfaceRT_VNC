using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>
    /// Framebuffer encoding tests: a real server encodes an image with every encoding this
    /// client advertises and the decoded framebuffer is compared against the expected image.
    /// </summary>
    public static class DecoderTests
    {
        public static void Register()
        {
            TestRunner.Add("Raw rectangles decode over TCP", RawEndToEnd);
            TestRunner.Add("Hextile rectangles decode over TCP (coloured sub-rectangles)", HextileColoured);
            TestRunner.Add("Hextile raw tiles decode over TCP", HextileRawTiles);
            TestRunner.Add("Hextile foreground sub-rectangles decode over TCP", HextileForeground);
            TestRunner.Add("Hextile follows the RFC byte layout", HextileHandCraftedVector);
            TestRunner.Add("RRE rectangles decode over TCP", RreEndToEnd);
            TestRunner.Add("CoRRE rectangles decode over TCP", CoRreEndToEnd);
            TestRunner.Add("CopyRect scrolls the framebuffer", CopyRectEndToEnd);
            TestRunner.Add("16bpp pixel formats decode over TCP", SixteenBitEndToEnd);
            TestRunner.Add("Multiple rectangles are reported as one damaged region", DamageRegion);
        }

        /// <summary>Distinct colour per pixel so off-by-one and row/column mix ups cannot hide.</summary>
        internal static uint PatternRgb(int x, int y)
        {
            uint r = (uint)((x * 8) & 0xFF);
            uint g = (uint)((y * 8) & 0xFF);
            uint b = (uint)(((x ^ y) * 8) & 0xFF);
            return (r << 16) | (g << 8) | b;
        }

        /// <summary>Builds a wire image for the default 32bpp BGRA8 format.</summary>
        internal static byte[] BuildImage(int width, int height, Func<int, int, uint> rgb)
        {
            return WireImage.Create(width, height, PixelFormat.DefaultBgra8, (x, y) =>
            {
                uint value = rgb(x, y);
                return WireImage.Bgra((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            });
        }

        internal static void AssertColours(Framebuffer framebuffer, int width, int height, Func<int, int, uint> rgb, string label)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint value = rgb(x, y);
                    Check.Pixel(framebuffer, x, y, (byte)(value >> 16), (byte)(value >> 8), (byte)value, label);
                }
            }
        }

        internal static async Task<bool> WaitForUpdateAsync(ClientHarness harness, FakeRfbServer server, byte[] frame)
        {
            int updates = 0;
            EventHandler<FramebufferUpdateEventArgs> handler = (sender, args) => Interlocked.Increment(ref updates);
            harness.Client.FramebufferUpdated += handler;
            try
            {
                server.QueueFrame(frame);
                return await Wait.UntilAsync(() => updates > 0, 5000);
            }
            finally
            {
                harness.Client.FramebufferUpdated -= handler;
            }
        }

        private static async Task RawEndToEnd()
        {
            const int width = 200;
            const int height = 150;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Raw,
                        writer => Encoders.Raw(writer, image, width, height, PixelFormat.DefaultBgra8));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    Check.Equal(width, harness.Client.Framebuffer.Width, "framebuffer width");
                    AssertColours(harness.Client.Framebuffer, width, height, PatternRgb, "raw");
                }
            }
        }

        private static async Task HextileColoured()
        {
            const int width = 40;
            const int height = 24;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Hextile,
                        writer => Encoders.Hextile(writer, image, width, height, PixelFormat.DefaultBgra8, false));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, PatternRgb, "hextile");
                }
            }
        }

        private static async Task HextileRawTiles()
        {
            const int width = 40;
            const int height = 24;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Hextile,
                        writer => Encoders.Hextile(writer, image, width, height, PixelFormat.DefaultBgra8, true));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, PatternRgb, "hextile raw tiles");
                }
            }
        }

        private static async Task HextileForeground()
        {
            const int width = 40;
            const int height = 24;

            // The foreground path can only express one colour per tile besides the background,
            // so use a black image with two white blocks in different tiles.
            Func<int, int, uint> blocks = (x, y) =>
            {
                bool first = x >= 5 && x < 9 && y >= 4 && y < 7;
                bool second = x >= 20 && x < 22 && y >= 18 && y < 20;
                return (first || second) ? 0xFFFFFFu : 0x000000u;
            };

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, blocks);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Hextile,
                        writer => Encoders.HextileForeground(writer, image, width, height, PixelFormat.DefaultBgra8));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, blocks, "hextile foreground");
                }
            }
        }

        /// <summary>
        /// Hand written Hextile bytes for a 2x2 pixel rectangle: a 4 byte background pixel
        /// followed by one sub-rectangle.  This pins down the exact byte layout, including the
        /// "coloured pixel before the position byte" rule and the "width/height minus one" rule.
        /// </summary>
        private static async Task HextileHandCraftedVector()
        {
            using (FakeRfbServer server = new FakeRfbServer(2, 2))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    // background = dark blue (r=0x20, g=0x00, b=0x40), sub-rectangle = red
                    byte[] background = { 0x40, 0x00, 0x20, 0x00 };
                    byte[] red = { 0x00, 0x00, 0xFF, 0x00 };

                    PayloadWriter tile = new PayloadWriter();
                    tile.WriteByte(2 | 8 | 16);      // background specified, subrects, coloured
                    tile.WriteBytes(background);
                    tile.WriteUInt16(1);             // one sub-rectangle
                    tile.WriteBytes(red);
                    tile.WriteByte(0x11);            // x = 1, y = 1 within the tile
                    tile.WriteByte(0);               // width  = 0 + 1 = 1
                    tile.WriteByte(0);               // height = 0 + 1 = 1

                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, 2, 2, RfbEncoding.Hextile, writer => writer.WriteBytes(tile.ToArray()));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    Check.Pixel(harness.Client.Framebuffer, 0, 0, 0x20, 0x00, 0x40, "background pixel");
                    Check.Pixel(harness.Client.Framebuffer, 1, 0, 0x20, 0x00, 0x40, "background pixel");
                    Check.Pixel(harness.Client.Framebuffer, 0, 1, 0x20, 0x00, 0x40, "background pixel");
                    Check.Pixel(harness.Client.Framebuffer, 1, 1, 0xFF, 0x00, 0x00, "sub-rectangle pixel");
                }
            }
        }

        private static async Task RreEndToEnd()
        {
            const int width = 48;
            const int height = 32;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Rre,
                        writer => Encoders.Rre(writer, image, width, height, PixelFormat.DefaultBgra8));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, PatternRgb, "rre");
                }
            }
        }

        private static async Task CoRreEndToEnd()
        {
            const int width = 48;
            const int height = 32;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.CoRre,
                        writer => Encoders.CoRre(writer, image, width, height, PixelFormat.DefaultBgra8));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, PatternRgb, "corre");
                }
            }
        }

        private static async Task CopyRectEndToEnd()
        {
            const int width = 32;
            const int height = 16;

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(width, height, PatternRgb);
                    FramebufferUpdateBuilder first = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    first.AddRectangle(0, 0, width, height, RfbEncoding.Raw,
                        writer => Encoders.Raw(writer, image, width, height, PixelFormat.DefaultBgra8));
                    Check.True(await WaitForUpdateAsync(harness, server, first.Build()), "the initial update was not decoded");

                    // Scroll rows 0..14 down by one row, exactly like a window scroll.
                    FramebufferUpdateBuilder scroll = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    scroll.AddRectangle(0, 1, width, 15, RfbEncoding.CopyRect, writer =>
                    {
                        writer.WriteUInt16(0);      // source x
                        writer.WriteUInt16(0);      // source y
                    });
                    Check.True(await WaitForUpdateAsync(harness, server, scroll.Build()), "the CopyRect update was not decoded");

                    Func<int, int, uint> expected = (x, y) => y == 0 ? PatternRgb(x, 0) : PatternRgb(x, y - 1);
                    AssertColours(harness.Client.Framebuffer, width, height, expected, "copyrect scroll");
                }
            }
        }

        private static uint Rgb565Pattern(int x, int y)
        {
            switch (((x / 4) + (y / 4)) % 4)
            {
                case 0: return 0xFF0000u;
                case 1: return 0x00FF00u;
                case 2: return 0x0000FFu;
                default: return 0x000000u;
            }
        }

        private static async Task SixteenBitEndToEnd()
        {
            const int width = 32;
            const int height = 16;
            PixelFormat format = PixelTests.Rgb565();

            using (FakeRfbServer server = new FakeRfbServer(width, height))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null, format, false))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");
                    Check.True(server.ClientPixelFormatSeen, "the client must send SetPixelFormat");
                    Check.True(server.ClientPixelFormat.Equals(format), "the client must ask for 16bpp");
                    Check.Equal(2, server.ClientPixelFormat.BytesPerPixel, "two bytes per pixel on the wire");

                    byte[] image = WireImage.Create(width, height, format, (x, y) =>
                    {
                        uint rgb = Rgb565Pattern(x, y);
                        uint r = (rgb >> 16) & 0xFF;
                        uint g = (rgb >> 8) & 0xFF;
                        uint b = rgb & 0xFF;
                        return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
                    });

                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(format);
                    builder.AddRectangle(0, 0, width, height, RfbEncoding.Raw,
                        writer => Encoders.Raw(writer, image, width, height, format));

                    Check.True(await WaitForUpdateAsync(harness, server, builder.Build()), "no update was decoded");
                    AssertColours(harness.Client.Framebuffer, width, height, Rgb565Pattern, "16bpp");
                }
            }
        }

        private static async Task DamageRegion()
        {
            using (FakeRfbServer server = new FakeRfbServer(64, 48))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    byte[] image = BuildImage(8, 8, PatternRgb);
                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, 8, 8, RfbEncoding.Raw,
                        writer => Encoders.Raw(writer, image, 8, 8, PixelFormat.DefaultBgra8));
                    builder.AddRectangle(24, 16, 8, 8, RfbEncoding.Raw,
                        writer => Encoders.Raw(writer, image, 8, 8, PixelFormat.DefaultBgra8));

                    Rect region = Rect.Empty;
                    int count = 0;
                    EventHandler<FramebufferUpdateEventArgs> handler = (sender, args) =>
                    {
                        region = args.Region;
                        count = args.RectangleCount;
                    };
                    harness.Client.FramebufferUpdated += handler;
                    try
                    {
                        server.QueueFrame(builder.Build());
                        Check.True(await Wait.UntilAsync(() => count > 0, 5000), "no update was decoded");
                    }
                    finally
                    {
                        harness.Client.FramebufferUpdated -= handler;
                    }

                    Check.Equal(2, count, "rectangle count");
                    Check.Equal(0, region.X, "damage region x");
                    Check.Equal(0, region.Y, "damage region y");
                    Check.Equal(32, region.Width, "damage region width");
                    Check.Equal(24, region.Height, "damage region height");
                }
            }
        }
    }
}
