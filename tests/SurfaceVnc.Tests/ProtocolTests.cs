using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Handshake and input tests, all over a real TCP connection.</summary>
    public static class ProtocolTests
    {
        public static void Register()
        {
            TestRunner.Add("Handshake negotiates the requested pixel format and encodings", Handshake);
            TestRunner.Add("VNC authentication with the right password succeeds", VncAuthSuccess);
            TestRunner.Add("VNC authentication with a wrong password fails", VncAuthFailure);
            TestRunner.Add("A server refusal is reported with its reason", ServerRefusal);
            TestRunner.Add("RFB 3.3 servers are supported", LegacyServer);
            TestRunner.Add("Pointer, key and clipboard events reach the server", InputEvents);
            TestRunner.Add("View only mode sends no input events", ViewOnly);
        }

        internal static byte[] ParseHex(string text)
        {
            byte[] result = new byte[text.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return result;
        }

        private static async Task Handshake()
        {
            using (FakeRfbServer server = new FakeRfbServer(800, 600))
            {
                server.ServerName = "workshop-pc";
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");
                    Check.Equal(800, harness.Client.RemoteWidth, "remote width");
                    Check.Equal(600, harness.Client.RemoteHeight, "remote height");
                    Check.Equal("workshop-pc", harness.Client.ServerName, "server name");
                    Check.Equal("RFB 003.008\n", server.ClientVersionString, "client banner");
                    Check.True(server.ClientInitShared, "the client must ask for a shared session");
                    Check.True(server.ClientPixelFormatSeen, "the client must send SetPixelFormat");
                    Check.True(harness.Client.WirePixelFormat.Equals(PixelFormat.DefaultBgra8), "negotiated pixel format");

                    Check.Equal(5, server.ClientEncodings.Count, "encoding count");
                    Check.Equal((int)RfbEncoding.CopyRect, server.ClientEncodings[0], "first encoding");
                    Check.Equal((int)RfbEncoding.Hextile, server.ClientEncodings[1], "second encoding");
                    Check.Equal((int)RfbEncoding.Raw, server.ClientEncodings[4], "raw must be the last resort");

                    Check.True(await Wait.UntilAsync(() => server.UpdateRequestCount >= 1, 5000), "the client must request an update");
                    Check.False(server.LastRequestIncremental, "the first update request must be a full one");
                }
            }
        }

        private static async Task VncAuthSuccess()
        {
            using (FakeRfbServer server = new FakeRfbServer(320, 240))
            {
                server.SecurityType = RfbProtocol.SecurityVncAuthentication;
                server.Password = "password";

                using (ClientHarness harness = await ClientHarness.StartAsync(server, "password"))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");
                    Check.True(server.VncAuthAccepted, "the server must accept the password");
                    // Cross checked with OpenSSL, see AuthTests.
                    Check.Bytes(ParseHex("87A725DBE9F34E9BAA26250220E2C981"), server.VncAuthResponse, "response bytes on the wire");
                    Check.Equal(320, harness.Client.RemoteWidth, "the client must be connected");
                }
            }
        }

        private static async Task VncAuthFailure()
        {
            using (FakeRfbServer server = new FakeRfbServer(320, 240))
            {
                server.SecurityType = RfbProtocol.SecurityVncAuthentication;
                server.Password = "password";

                try
                {
                    await ClientHarness.StartAsync(server, "not-the-password");
                    throw new CheckFailedException("connecting with a wrong password must fail");
                }
                catch (RfbAuthenticationException error)
                {
                    Check.True(error.Message.IndexOf("Authentication failure", StringComparison.Ordinal) >= 0,
                        "the server reason must be reported, got: " + error.Message);
                }
            }
        }

        private static async Task ServerRefusal()
        {
            using (FakeRfbServer server = new FakeRfbServer(320, 240))
            {
                server.RejectReason = "Too many connections";

                try
                {
                    await ClientHarness.StartAsync(server, null);
                    throw new CheckFailedException("a refused connection must fail");
                }
                catch (RfbException error)
                {
                    Check.True(error.Message.IndexOf("Too many connections", StringComparison.Ordinal) >= 0,
                        "the refusal reason must be reported, got: " + error.Message);
                }
            }
        }

        private static async Task LegacyServer()
        {
            using (FakeRfbServer server = new FakeRfbServer(640, 480))
            {
                server.ProtocolVersion = RfbProtocol.VersionString33;

                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");
                    Check.Equal("RFB 003.003\n", server.ClientVersionString, "the client must answer with 3.3");
                    Check.Equal(640, harness.Client.RemoteWidth, "the client must be connected");
                }
            }
        }

        private static async Task InputEvents()
        {
            using (FakeRfbServer server = new FakeRfbServer(800, 600))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");

                    CancellationToken token = CancellationToken.None;
                    await harness.Client.SendPointerEventAsync(RfbProtocol.ButtonLeft, 123, 456, token);
                    await harness.Client.SendPointerEventAsync(0, 124, 457, token);
                    await harness.Client.SendKeyPressAsync(RemoteKeys.Return, token);
                    await harness.Client.SendKeyEventAsync(RemoteKeys.ControlLeft, true, token);
                    await harness.Client.SendWheelAsync(true, 10, 20, token);
                    await harness.Client.SendClientCutTextAsync("hello", token);

                    Check.True(await Wait.UntilAsync(() => server.CutTexts.Count > 0, 5000), "input events did not arrive");

                    Check.Equal(4, server.PointerEvents.Count, "pointer event count");
                    Check.Equal(1, server.PointerEvents[0].ButtonMask, "left button down");
                    Check.Equal(123, server.PointerEvents[0].X, "pointer x");
                    Check.Equal(456, server.PointerEvents[0].Y, "pointer y");
                    Check.Equal(0, server.PointerEvents[1].ButtonMask, "left button up");
                    Check.Equal(RfbProtocol.ButtonWheelUp, server.PointerEvents[2].ButtonMask, "wheel up");
                    Check.Equal(0, server.PointerEvents[3].ButtonMask, "wheel release");

                    Check.Equal(3, server.KeyEvents.Count, "key event count");
                    Check.True(server.KeyEvents[0].Down && server.KeyEvents[0].KeySym == RemoteKeys.Return, "return down");
                    Check.False(server.KeyEvents[1].Down, "return up");
                    Check.True(server.KeyEvents[2].Down && server.KeyEvents[2].KeySym == RemoteKeys.ControlLeft, "control down");

                    Check.Equal("hello", server.CutTexts[0], "clipboard text");
                }
            }
        }

        private static async Task ViewOnly()
        {
            using (FakeRfbServer server = new FakeRfbServer(800, 600))
            {
                using (ClientHarness harness = await ClientHarness.StartAsync(server, null, PixelFormat.DefaultBgra8, true))
                {
                    Check.True(server.WaitForHandshake(5000), "the handshake did not finish");
                    await harness.Client.SendPointerEventAsync(RfbProtocol.ButtonLeft, 5, 5, CancellationToken.None);
                    await harness.Client.SendKeyPressAsync(RemoteKeys.Return, CancellationToken.None);
                    await harness.Client.SendClientCutTextAsync("nope", CancellationToken.None);
                    await Wait.ForAsync(200);

                    Check.Equal(0, server.PointerEvents.Count, "view only clients must not move the pointer");
                    Check.Equal(0, server.KeyEvents.Count, "view only clients must not type");
                    Check.Equal(0, server.CutTexts.Count, "view only clients must not set the clipboard");
                }
            }
        }
    }
}
