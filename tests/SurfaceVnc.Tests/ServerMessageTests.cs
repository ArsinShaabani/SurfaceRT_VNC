using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Server initiated messages and error handling.</summary>
    public static class ServerMessageTests
    {
        public static void Register()
        {
            TestRunner.Add("ServerCutText raises the clipboard event", ServerCutText);
            TestRunner.Add("Bell raises the bell event", Bell);
            TestRunner.Add("Colour map entries do not desynchronise the stream", ColourMap);
            TestRunner.Add("A closed connection surfaces as an error", ConnectionClosed);
            TestRunner.Add("Unsupported encodings are reported", UnsupportedEncoding);
            TestRunner.Add("The client keeps asking for updates", KeepsRequestingUpdates);
            TestRunner.Add("DesktopSize resizes the framebuffer", DesktopSize);
        }

        internal static byte[] SimpleFrame(int width, int height, RfbEncoding encoding)
        {
            FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
            byte[] image = WireImage.Create(width, height, PixelFormat.DefaultBgra8, (x, y) => WireImage.Bgra(0x10, 0x20, 0x30));
            builder.AddRectangle(0, 0, width, height, encoding, writer => Encoders.Raw(writer, image, width, height, PixelFormat.DefaultBgra8));
            return builder.Build();
        }

        private static async Task ServerCutText()
        {
            using (FakeRfbServer server = new FakeRfbServer(64, 64))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    List<string> received = new List<string>();
                    harness.Client.ClipboardReceived += (sender, text) => { lock (received) received.Add(text); };

                    server.SendServerCutText("hello from the workshop pc");
                    Check.True(await Wait.UntilAsync(() => { lock (received) return received.Count > 0; }, 5000), "no clipboard event arrived");
                    lock (received) Check.Equal("hello from the workshop pc", received[0], "clipboard text");
                }
            }
        }

        private static async Task Bell()
        {
            using (FakeRfbServer server = new FakeRfbServer(64, 64))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    int bells = 0;
                    harness.Client.Bell += (sender, args) => Interlocked.Increment(ref bells);

                    server.SendBell();
                    Check.True(await Wait.UntilAsync(() => bells > 0, 5000), "no bell event arrived");
                }
            }
        }

        private static async Task ColourMap()
        {
            using (FakeRfbServer server = new FakeRfbServer(8, 8))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    // The client asks for true colour, so a colour map is unexpected: it must be
                    // consumed without corrupting the stream.
                    server.SendColourMapEntries(0, new byte[][] { new byte[] { 0xFF, 0x00, 0x00 }, new byte[] { 0x00, 0xFF, 0x00 } });

                    int updates = 0;
                    harness.Client.FramebufferUpdated += (sender, args) => Interlocked.Increment(ref updates);
                    server.QueueFrame(SimpleFrame(8, 8, RfbEncoding.Raw));

                    Check.True(await Wait.UntilAsync(() => updates > 0, 5000), "the update after the colour map was not decoded");
                    Check.Pixel(harness.Client.Framebuffer, 3, 3, 0x10, 0x20, 0x30, "framebuffer content");
                }
            }
        }

        private static async Task ConnectionClosed()
        {
            using (FakeRfbServer server = new FakeRfbServer(64, 64))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    server.CloseConnection();
                    Exception error = await harness.WaitForRunErrorAsync(5000);
                    Check.True(error is RfbConnectionClosedException, "expected RfbConnectionClosedException, got " + error);
                }
            }
        }

        private static async Task UnsupportedEncoding()
        {
            using (FakeRfbServer server = new FakeRfbServer(16, 16))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, 16, 16, RfbEncoding.Tight, null);
                    server.QueueFrame(builder.Build());

                    Exception error = await harness.WaitForRunErrorAsync(5000);
                    Check.True(error is RfbException, "expected RfbException, got " + error);
                    Check.True(error != null && error.Message.IndexOf("7", StringComparison.Ordinal) >= 0,
                        "the error should name the unsupported encoding, got: " + error);
                }
            }
        }

        private static async Task KeepsRequestingUpdates()
        {
            using (FakeRfbServer server = new FakeRfbServer(32, 32))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    server.QueueFrame(SimpleFrame(32, 32, RfbEncoding.Raw));
                    Check.True(await Wait.UntilAsync(() => server.UpdateRequestCount >= 2, 5000),
                        "the client must ask for the next update after receiving one (requests seen: " + server.UpdateRequestCount + ")");
                    Check.True(server.LastRequestIncremental, "follow up requests must be incremental");
                }
            }
        }

        private static async Task DesktopSize()
        {
            using (FakeRfbServer server = new FakeRfbServer(64, 64))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    FramebufferUpdateBuilder builder = new FramebufferUpdateBuilder(PixelFormat.DefaultBgra8);
                    builder.AddRectangle(0, 0, 128, 96, RfbEncoding.DesktopSize, null);
                    server.QueueFrame(builder.Build());

                    Check.True(await Wait.UntilAsync(() => harness.Client.Framebuffer.Width == 128, 5000),
                        "the framebuffer must follow the announced desktop size");
                    Check.Equal(96, harness.Client.Framebuffer.Height, "new framebuffer height");
                }
            }
        }
    }
}
