using System;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.CrossCheck
{
    /// <summary>
    /// Connects the production <see cref="RfbClient"/> to the independent Python RFB server
    /// (tools/crosscheck-server.py) and checks that the three scripted updates arrive decoded
    /// exactly as that server sent them.
    ///
    /// The expected images below mirror the script in the Python server.  The two files are
    /// written independently on purpose: if they disagree, one of the two implementations is
    /// wrong about RFC 6143.
    ///
    /// Updates are requested one at a time (AutoRequestUpdates is turned off) so each stage can be
    /// verified before the next update is applied.
    /// </summary>
    public static class Program
    {
        private const int Width = 64;
        private const int Height = 48;

        private static readonly byte[] ColourA = { 0x11, 0x22, 0x33 };   // Hextile rect at (8,8) 32x16
        private static readonly byte[] ColourC = { 0xFF, 0x00, 0x00 };   // sub-rectangle at (41,25) 1x1
        private static readonly byte[] ColourD = { 0x00, 0xFF, 0x00 };   // sub-rectangle at (43,26) 2x2
        private static readonly Rect HextileRect = new Rect(8, 8, 32, 16);
        private static readonly Rect SubRectRect = new Rect(40, 24, 16, 16);
        private static readonly Rect CopySource = new Rect(0, 0, 32, 16);
        private const int CopyDestinationX = 32;
        private const int CopyDestinationY = 16;

        private static int _failures;

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine("  ok    " + message);
            }
            else
            {
                Console.WriteLine("  FAIL  " + message);
                _failures++;
            }
        }

        /// <summary>The image the Python server sends as a Raw rectangle, mirrored here.</summary>
        private static void PatternColour(int x, int y, out byte r, out byte g, out byte b)
        {
            r = (byte)((x * 4) & 0xFF);
            g = (byte)((y * 4) & 0xFF);
            b = (byte)(((x + y) * 2) & 0xFF);
        }

        private static void AssertPixel(Framebuffer framebuffer, int x, int y, byte r, byte g, byte b, string label)
        {
            byte actualB, actualG, actualR, actualA;
            framebuffer.GetBgra8(x, y, out actualB, out actualG, out actualR, out actualA);
            Check(actualR == r && actualG == g && actualB == b && actualA == 255,
                string.Format("{0} at ({1},{2}) is rgb({3},{4},{5}) [got rgb({6},{7},{8}), a={9}]",
                    label, x, y, r, g, b, actualR, actualG, actualB, actualA));
        }

        private static async Task<bool> WaitForUpdateAsync(SemaphoreSlim updates)
        {
            Task wait = updates.WaitAsync();
            Task completed = await Task.WhenAny(wait, Task.Delay(10000));
            return completed == wait;
        }

        /// <summary>Runs a verification while holding the framebuffer gate, like the UI does.</summary>
        private static void VerifyUnderGate(RfbClient client, Action verify)
        {
            if (!client.Framebuffer.Gate.Wait(2000))
            {
                Check(false, "could not take the framebuffer gate for verification");
                return;
            }

            try
            {
                verify();
            }
            finally
            {
                client.Framebuffer.Gate.Release();
            }
        }

        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: SurfaceVnc.CrossCheck <host> <port> <password>");
                return 2;
            }

            string host = args[0];
            int port = int.Parse(args[1]);
            string password = args[2];

            Console.WriteLine("C# client -> python RFB server at {0}:{1}", host, port);

            using (SocketRfbTransport transport = await SocketRfbTransport.ConnectAsync(host, port, CancellationToken.None))
            {
                RfbClient client = new RfbClient(transport);
                ConnectionSettings settings = ConnectionSettings.CreateDefault(host, port, password);

                await client.ConnectAsync(settings, CancellationToken.None);

                // Stage the updates ourselves: otherwise the client asks for the next update the
                // moment it has decoded one, and a verification could read the framebuffer after
                // the following update already landed.
                client.AutoRequestUpdates = false;

                Check(client.RemoteWidth == Width && client.RemoteHeight == Height,
                    string.Format("server reported {0}x{1}", client.RemoteWidth, client.RemoteHeight));
                Check(client.ServerName == "python-crosscheck", "server name was read: " + client.ServerName);
                Check(client.WirePixelFormat.Equals(PixelFormat.DefaultBgra8), "negotiated 32bpp BGRA8 on the wire");

                using (SemaphoreSlim updates = new SemaphoreSlim(0))
                {
                    client.FramebufferUpdated += (sender, e) => updates.Release();

                    Task run = Task.Run(async () =>
                    {
                        try { await client.RunAsync(CancellationToken.None); }
                        catch (Exception) { }
                    });

                    // ConnectAsync already asked for the first update, so this one arrives on its own.
                    Check(await WaitForUpdateAsync(updates), "update 1 (Raw) arrived");
                    VerifyUnderGate(client, () => VerifyRawUpdate(client));

                    await client.RequestFramebufferUpdateAsync(true, CancellationToken.None);
                    Check(await WaitForUpdateAsync(updates), "update 2 (Hextile) arrived");
                    VerifyUnderGate(client, () => VerifyHextileUpdate(client));

                    await client.RequestFramebufferUpdateAsync(true, CancellationToken.None);
                    Check(await WaitForUpdateAsync(updates), "update 3 (CopyRect) arrived");
                    VerifyUnderGate(client, () => VerifyCopyRectUpdate(client));

                    // ---- input: the python server checks these bytes ----------------------------
                    await client.SendPointerEventAsync(RfbProtocol.ButtonLeft, 100, 200, CancellationToken.None);
                    await client.SendPointerEventAsync(0, 101, 201, CancellationToken.None);
                    await client.SendKeyPressAsync(RemoteKeys.Return, CancellationToken.None);
                    await client.SendClientCutTextAsync("cross-check", CancellationToken.None);

                    await Task.Delay(400);
                    Check(!run.IsFaulted, "the message loop kept running");
                }
            }

            Console.WriteLine();
            if (_failures > 0)
            {
                Console.WriteLine("C# client: {0} check(s) FAILED", _failures);
                return 1;
            }

            Console.WriteLine("C# client: all checks passed");
            return 0;
        }

        /// <summary>Update 1: the whole screen, sent as one Raw rectangle.</summary>
        private static void VerifyRawUpdate(RfbClient client)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte r, g, b;
                    PatternColour(x, y, out r, out g, out b);

                    byte actualB, actualG, actualR, actualA;
                    client.Framebuffer.GetBgra8(x, y, out actualB, out actualG, out actualR, out actualA);
                    if (actualR != r || actualG != g || actualB != b || actualA != 255)
                    {
                        Check(false, string.Format("Raw pixel ({0},{1}) is rgb({2},{3},{4}) [got rgb({5},{6},{7})]",
                            x, y, r, g, b, actualR, actualG, actualB));
                        return;
                    }
                }
            }

            Check(true, "all " + (Width * Height) + " Raw pixels match the image the server sent");
        }

        /// <summary>
        /// Update 2: a Hextile rectangle whose second tile omits the background pixel (it must be
        /// inherited from the first tile), plus a tile with coloured sub-rectangles.
        /// </summary>
        private static void VerifyHextileUpdate(RfbClient client)
        {
            Framebuffer framebuffer = client.Framebuffer;

            AssertPixel(framebuffer, HextileRect.X, HextileRect.Y, ColourA[0], ColourA[1], ColourA[2],
                "Hextile first tile (background specified)");
            AssertPixel(framebuffer, HextileRect.X + 31, HextileRect.Y + 15, ColourA[0], ColourA[1], ColourA[2],
                "Hextile second tile (background inherited from the first)");
            AssertPixel(framebuffer, SubRectRect.X + 1, SubRectRect.Y + 1, ColourC[0], ColourC[1], ColourC[2],
                "Hextile coloured sub-rectangle 1x1");
            AssertPixel(framebuffer, SubRectRect.X + 3, SubRectRect.Y + 2, ColourD[0], ColourD[1], ColourD[2],
                "Hextile coloured sub-rectangle 2x2 (top left)");
            AssertPixel(framebuffer, SubRectRect.X + 4, SubRectRect.Y + 3, ColourD[0], ColourD[1], ColourD[2],
                "Hextile coloured sub-rectangle 2x2 (bottom right)");
            AssertPixel(framebuffer, SubRectRect.X + 8, SubRectRect.Y + 8, 0, 0, 0,
                "Hextile background outside the sub-rectangles");
            AssertPixel(framebuffer, 1, 1, PatternColourAt(1, 1, 0), PatternColourAt(1, 1, 1), PatternColourAt(1, 1, 2),
                "pixels outside the Hextile rectangles are untouched");
        }

        /// <summary>Update 3: CopyRect, which must reproduce the source region at the destination.</summary>
        private static void VerifyCopyRectUpdate(RfbClient client)
        {
            Framebuffer framebuffer = client.Framebuffer;

            for (int y = 0; y < CopySource.Height; y++)
            {
                for (int x = 0; x < CopySource.Width; x++)
                {
                    byte r, g, b;
                    if (x >= HextileRect.X && x < HextileRect.X + HextileRect.Width
                        && y >= HextileRect.Y && y < HextileRect.Y + HextileRect.Height)
                    {
                        r = ColourA[0]; g = ColourA[1]; b = ColourA[2];
                    }
                    else
                    {
                        PatternColour(x, y, out r, out g, out b);
                    }

                    byte actualB, actualG, actualR, actualA;
                    framebuffer.GetBgra8(CopyDestinationX + x, CopyDestinationY + y,
                        out actualB, out actualG, out actualR, out actualA);
                    if (actualR != r || actualG != g || actualB != b)
                    {
                        Check(false, string.Format("CopyRect pixel ({0},{1}) is rgb({2},{3},{4}) [got rgb({5},{6},{7})]",
                            CopyDestinationX + x, CopyDestinationY + y, r, g, b, actualR, actualG, actualB));
                        return;
                    }
                }
            }

            Check(true, "the CopyRect destination matches the source region");

            byte r0, g0, b0;
            PatternColour(0, 40, out r0, out g0, out b0);
            AssertPixel(framebuffer, 0, 40, r0, g0, b0, "pixels outside the copy are untouched");
        }

        private static byte PatternColourAt(int x, int y, int channel)
        {
            byte r, g, b;
            PatternColour(x, y, out r, out g, out b);
            if (channel == 0) return r;
            return channel == 1 ? g : b;
        }
    }
}
