using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core.Decoders;

namespace SurfaceVnc.Core
{
    /// <summary>Raised after a complete FramebufferUpdate message has been decoded.</summary>
    public sealed class FramebufferUpdateEventArgs : EventArgs
    {
        public FramebufferUpdateEventArgs(Rect region, int rectangleCount)
        {
            Region = region;
            RectangleCount = rectangleCount;
        }

        /// <summary>Union of all rectangles of the update, in remote coordinates.</summary>
        public Rect Region { get; private set; }

        public int RectangleCount { get; private set; }
    }

    /// <summary>Raised when the server announces a new desktop size.</summary>
    public sealed class DesktopSizeEventArgs : EventArgs
    {
        public DesktopSizeEventArgs(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
    }

    /// <summary>
    /// The RFB client: performs the handshake, runs the message loop and writes pointer /
    /// keyboard / clipboard events back to the server.
    ///
    /// Everything here is transport agnostic, which is what lets the same code run on
    /// Windows RT (StreamSocket) and inside the automated tests (TCP socket).
    /// </summary>
    public sealed class RfbClient : IDisposable
    {
        private readonly IRfbTransport _transport;
        private readonly RfbByteReader _reader;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly Framebuffer _framebuffer = new Framebuffer();
        private readonly Dictionary<RfbEncoding, IRfbDecoder> _decoders = DecoderRegistry.CreateDefault();
        private readonly RfbDecodeContext _decodeContext = new RfbDecodeContext();

        private ConnectionSettings _settings;
        private PixelFormat _wireFormat = PixelFormat.DefaultBgra8;
        private bool _disposed;

        public RfbClient(IRfbTransport transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            _transport = transport;
            _reader = new RfbByteReader(transport);
        }

        /// <summary>Raised on the network thread for every decoded framebuffer update.</summary>
        public event EventHandler<FramebufferUpdateEventArgs> FramebufferUpdated;

        /// <summary>Raised when the server sends new clipboard text (ServerCutText).</summary>
        public event EventHandler<string> ClipboardReceived;

        /// <summary>Raised when the server resizes the desktop (DesktopSize pseudo encoding).</summary>
        public event EventHandler<DesktopSizeEventArgs> DesktopSizeChanged;

        /// <summary>Raised when the server rings the bell.</summary>
        public event EventHandler Bell;

        /// <summary>Local copy of the remote framebuffer, already converted to BGRA8.</summary>
        public Framebuffer Framebuffer { get { return _framebuffer; } }

        /// <summary>Pixel format actually used on the wire (what we asked the server for).</summary>
        public PixelFormat WirePixelFormat { get { return _wireFormat; } }

        /// <summary>Name reported by the server ("ServerInit" name field).</summary>
        public string ServerName { get; private set; }

        /// <summary>Pixel format the server announced for its own framebuffer.</summary>
        public PixelFormat ServerPixelFormat { get; private set; }

        public int RemoteWidth { get; private set; }

        public int RemoteHeight { get; private set; }

        public ConnectionSettings Settings { get { return _settings; } }

        /// <summary>
        /// When true (default) the client automatically asks for the next incremental update
        /// after every FramebufferUpdate, which is what makes the picture follow the remote
        /// desktop without the UI having to poll.
        /// </summary>
        public bool AutoRequestUpdates { get; set; }

        // ---------------------------------------------------------------------------------
        // Handshake
        // ---------------------------------------------------------------------------------

        /// <summary>Performs the whole RFB handshake (version, security, ServerInit, formats).</summary>
        public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (string.IsNullOrEmpty(settings.Host)) throw new RfbException("no host name was supplied");

            _settings = settings;
            AutoRequestUpdates = true;

            string serverVersion = await ReadProtocolVersionAsync(cancellationToken).ConfigureAwait(false);
            int minorVersion = ParseMinorVersion(serverVersion);

            string clientVersion = minorVersion >= 8 ? RfbProtocol.VersionString38 : RfbProtocol.VersionString33;
            await WriteRawAsync(Encoding.ASCII.GetBytes(clientVersion), cancellationToken).ConfigureAwait(false);

            if (minorVersion >= 7)
                await NegotiateSecurity38Async(minorVersion, cancellationToken).ConfigureAwait(false);
            else
                await NegotiateSecurity33Async(cancellationToken).ConfigureAwait(false);

            // ClientInit: 1 = keep existing clients connected (shared session).
            byte[] clientInit = new byte[1];
            clientInit[0] = (byte)(settings.Shared ? 1 : 0);
            await WriteRawAsync(clientInit, cancellationToken).ConfigureAwait(false);

            await ReadServerInitAsync(cancellationToken).ConfigureAwait(false);
            await SendSetPixelFormatAsync(settings, cancellationToken).ConfigureAwait(false);
            await SendSetEncodingsAsync(settings, cancellationToken).ConfigureAwait(false);
            await RequestFramebufferUpdateAsync(false, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ReadProtocolVersionAsync(CancellationToken cancellationToken)
        {
            byte[] version = new byte[RfbProtocol.ProtocolVersionLength];
            await _reader.ReadExactAsync(version, 0, version.Length, cancellationToken).ConfigureAwait(false);
            string text = Encoding.ASCII.GetString(version, 0, version.Length);
            if (!text.StartsWith("RFB ", StringComparison.Ordinal))
                throw new RfbException("the server did not answer with an RFB banner (got: '" + Sanitize(text) + "'); is that really a VNC server port?");
            return text;
        }

        /// <summary>Extracts the minor version out of a banner such as "RFB 003.008\n".</summary>
        public static int ParseMinorVersion(string version)
        {
            if (version == null || version.Length < 11) return 3;
            string[] parts = version.Substring(4, 7).Split('.');
            int major;
            int minor;
            if (parts.Length != 2 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor))
                return 3;
            return minor;
        }

        private static string Sanitize(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char c in text)
                builder.Append(c < 32 || c > 126 ? '.' : c);
            return builder.ToString();
        }

        // ---------------------------------------------------------------------------------
        // Security
        // ---------------------------------------------------------------------------------

        /// <summary>RFB 3.7/3.8 security handshake: the server offers a list of security types.</summary>
        private async Task NegotiateSecurity38Async(int minorVersion, CancellationToken cancellationToken)
        {
            int count = await _reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                uint length = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
                string reason = await _reader.ReadStringAsync((int)length, cancellationToken).ConfigureAwait(false);
                throw new RfbException("the VNC server refused the connection: " + reason);
            }

            byte[] offered = await _reader.ReadByteArrayAsync(count, cancellationToken).ConfigureAwait(false);
            byte chosen = ChooseSecurityType(offered);

            byte[] choice = new byte[1];
            choice[0] = chosen;
            await WriteRawAsync(choice, cancellationToken).ConfigureAwait(false);

            if (chosen == RfbProtocol.SecurityVncAuthentication)
            {
                await PerformVncAuthenticationAsync(cancellationToken).ConfigureAwait(false);

                uint result = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
                if (result != 0)
                {
                    string reason = "the password was rejected";
                    if (minorVersion >= 8)
                    {
                        uint length = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
                        if (length > 0 && length < 4096)
                            reason = await _reader.ReadStringAsync((int)length, cancellationToken).ConfigureAwait(false);
                    }
                    throw new RfbAuthenticationException("VNC authentication failed: " + reason);
                }
            }
        }

        /// <summary>RFB 3.3 security handshake: the server sends a single 32 bit security type.</summary>
        private async Task NegotiateSecurity33Async(CancellationToken cancellationToken)
        {
            uint securityType = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
            if (securityType == 0)
            {
                uint length = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
                string reason = await _reader.ReadStringAsync((int)length, cancellationToken).ConfigureAwait(false);
                throw new RfbException("the VNC server refused the connection: " + reason);
            }
            if (securityType == RfbProtocol.SecurityVncAuthentication)
                await PerformVncAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            else if (securityType != RfbProtocol.SecurityNone)
                throw new RfbException("unsupported RFB 3.3 security type: " + securityType);
        }

        private byte ChooseSecurityType(byte[] offered)
        {
            bool hasVncAuth = false;
            bool hasNone = false;
            for (int i = 0; i < offered.Length; i++)
            {
                if (offered[i] == RfbProtocol.SecurityVncAuthentication) hasVncAuth = true;
                if (offered[i] == RfbProtocol.SecurityNone) hasNone = true;
            }

            string password = _settings == null ? null : _settings.Password;
            if (hasVncAuth && !string.IsNullOrEmpty(password)) return RfbProtocol.SecurityVncAuthentication;
            if (hasNone) return RfbProtocol.SecurityNone;
            if (hasVncAuth) return RfbProtocol.SecurityVncAuthentication;

            StringBuilder names = new StringBuilder();
            for (int i = 0; i < offered.Length; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(offered[i]);
            }
            throw new RfbException("the VNC server offers no security type this client understands (" + names + ")");
        }

        private async Task PerformVncAuthenticationAsync(CancellationToken cancellationToken)
        {
            byte[] challenge = await _reader.ReadByteArrayAsync(VncAuthentication.ChallengeLength, cancellationToken).ConfigureAwait(false);
            byte[] response = VncAuthentication.ComputeResponse(_settings.Password, challenge);
            await WriteRawAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------------------
        // ServerInit / client settings
        // ---------------------------------------------------------------------------------

        private async Task ReadServerInitAsync(CancellationToken cancellationToken)
        {
            int width = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
            int height = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

            byte[] pixelFormat = new byte[16];
            await _reader.ReadExactAsync(pixelFormat, 0, 16, cancellationToken).ConfigureAwait(false);
            ServerPixelFormat = PixelFormat.ReadFrom(pixelFormat, 0);

            uint nameLength = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
            ServerName = await _reader.ReadStringAsync((int)nameLength, cancellationToken).ConfigureAwait(false);

            RemoteWidth = width;
            RemoteHeight = height;
            _framebuffer.Resize(width, height);
        }

        private async Task SendSetPixelFormatAsync(ConnectionSettings settings, CancellationToken cancellationToken)
        {
            _wireFormat = settings.RequestedPixelFormat;
            if (!_wireFormat.IsValid || !_wireFormat.TrueColourFlag)
                _wireFormat = PixelFormat.DefaultBgra8;

            byte[] message = new byte[20];
            message[0] = RfbProtocol.MsgSetPixelFormat;
            _wireFormat.WriteTo(message, 4);
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendSetEncodingsAsync(ConnectionSettings settings, CancellationToken cancellationToken)
        {
            RfbEncoding[] encodings = settings.Encodings;
            if (encodings == null || encodings.Length == 0) encodings = RfbProtocol.PreferredEncodings;

            byte[] message = new byte[4 + encodings.Length * 4];
            message[0] = RfbProtocol.MsgSetEncodings;
            message[2] = (byte)(encodings.Length >> 8);
            message[3] = (byte)(encodings.Length & 0xFF);
            for (int i = 0; i < encodings.Length; i++)
            {
                int value = (int)encodings[i];
                message[4 + i * 4 + 0] = (byte)(value >> 24);
                message[4 + i * 4 + 1] = (byte)(value >> 16);
                message[4 + i * 4 + 2] = (byte)(value >> 8);
                message[4 + i * 4 + 3] = (byte)value;
            }
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------------------
        // Message loop
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Reads and dispatches server messages until the connection dies or the token is
        /// cancelled.  Runs forever: give it a background thread/task.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte messageType = await _reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                switch (messageType)
                {
                    case RfbProtocol.MsgFramebufferUpdate:
                        await HandleFramebufferUpdateAsync(cancellationToken).ConfigureAwait(false);
                        break;

                    case RfbProtocol.MsgSetColourMapEntries:
                        await HandleSetColourMapEntriesAsync(cancellationToken).ConfigureAwait(false);
                        break;

                    case RfbProtocol.MsgBell:
                        RaiseBell();
                        break;

                    case RfbProtocol.MsgServerCutText:
                        await HandleServerCutTextAsync(cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        throw new RfbException("unknown RFB server message type " + messageType + " (protocol desynchronised)");
                }
            }
        }

        private async Task HandleFramebufferUpdateAsync(CancellationToken cancellationToken)
        {
            await _reader.SkipAsync(1, cancellationToken).ConfigureAwait(false);
            int rectangleCount = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

            Rect region = Rect.Empty;
            int painted = 0;

            // The gate is held for the whole message so the UI never sees a half decoded frame.
            await _framebuffer.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < rectangleCount; i++)
                {
                    int x = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                    int y = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                    int width = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                    int height = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                    RfbEncoding encoding = (RfbEncoding)await _reader.ReadInt32Async(cancellationToken).ConfigureAwait(false);

                    if (encoding == RfbEncoding.LastRect)
                    {
                        // Only sent in response to a "LastRect" request; nothing to draw.
                        continue;
                    }

                    if (encoding == RfbEncoding.DesktopSize)
                    {
                        _framebuffer.Resize(width, height);
                        RemoteWidth = width;
                        RemoteHeight = height;
                        RaiseDesktopSize(width, height);
                        continue;
                    }

                    IRfbDecoder decoder;
                    if (!_decoders.TryGetValue(encoding, out decoder))
                        throw new RfbException("the VNC server used encoding " + (int)encoding + ", which this client does not implement");

                    _decodeContext.Framebuffer = _framebuffer;
                    _decodeContext.PixelFormat = _wireFormat;
                    _decodeContext.Reader = _reader;
                    _decodeContext.X = x;
                    _decodeContext.Y = y;
                    _decodeContext.Width = width;
                    _decodeContext.Height = height;
                    _decodeContext.SourceX = 0;
                    _decodeContext.SourceY = 0;

                    if (encoding == RfbEncoding.CopyRect)
                    {
                        _decodeContext.SourceX = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                        _decodeContext.SourceY = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                    }

                    await decoder.DecodeAsync(_decodeContext, cancellationToken).ConfigureAwait(false);

                    region = region.Union(new Rect(x, y, width, height));
                    painted++;
                }
            }
            finally
            {
                _framebuffer.Gate.Release();
            }

            if (painted > 0)
            {
                EventHandler<FramebufferUpdateEventArgs> handler = FramebufferUpdated;
                if (handler != null)
                    handler(this, new FramebufferUpdateEventArgs(region, painted));
            }

            if (AutoRequestUpdates)
                await RequestFramebufferUpdateAsync(true, cancellationToken).ConfigureAwait(false);
        }


        private async Task HandleSetColourMapEntriesAsync(CancellationToken cancellationToken)
        {
            // We always ask for true colour, so a colour map should never arrive.  It must still
            // be consumed byte for byte, otherwise the stream would desynchronise.
            await _reader.SkipAsync(1, cancellationToken).ConfigureAwait(false);
            await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
            int count = await _reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
            await _reader.SkipAsync(count * 6, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleServerCutTextAsync(CancellationToken cancellationToken)
        {
            await _reader.SkipAsync(3, cancellationToken).ConfigureAwait(false);
            uint length = await _reader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
            if (length > 4 * 1024 * 1024) throw new RfbException("absurd ServerCutText length: " + length);

            string text = await _reader.ReadStringAsync((int)length, cancellationToken).ConfigureAwait(false);
            EventHandler<string> handler = ClipboardReceived;
            if (handler != null) handler(this, text);
        }

        private void RaiseBell()
        {
            EventHandler handler = Bell;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseDesktopSize(int width, int height)
        {
            EventHandler<DesktopSizeEventArgs> handler = DesktopSizeChanged;
            if (handler != null) handler(this, new DesktopSizeEventArgs(width, height));
        }

        // ---------------------------------------------------------------------------------
        // Client -> server messages
        // ---------------------------------------------------------------------------------

        /// <summary>Asks the server for a framebuffer update (incremental or full).</summary>
        public async Task RequestFramebufferUpdateAsync(bool incremental, CancellationToken cancellationToken)
        {
            byte[] message = new byte[10];
            message[0] = RfbProtocol.MsgFramebufferUpdateRequest;
            message[1] = (byte)(incremental ? 1 : 0);
            // Whole framebuffer.
            message[6] = (byte)(RemoteHeight >> 8);
            message[7] = (byte)(RemoteHeight & 0xFF);
            message[8] = (byte)(RemoteWidth >> 8);
            message[9] = (byte)(RemoteWidth & 0xFF);
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends a pointer (mouse) event; <paramref name="buttonMask"/> uses the RFB bits.</summary>
        public async Task SendPointerEventAsync(byte buttonMask, int x, int y, CancellationToken cancellationToken)
        {
            if (IsViewOnly()) return;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x > 65535) x = 65535;
            if (y > 65535) y = 65535;

            byte[] message = new byte[6];
            message[0] = RfbProtocol.MsgPointerEvent;
            message[1] = buttonMask;
            message[2] = (byte)(x >> 8);
            message[3] = (byte)(x & 0xFF);
            message[4] = (byte)(y >> 8);
            message[5] = (byte)(y & 0xFF);
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends a key event.  <paramref name="keysym"/> is an X11 keysym (see <see cref="RemoteKeys"/>).</summary>
        public async Task SendKeyEventAsync(uint keysym, bool down, CancellationToken cancellationToken)
        {
            if (IsViewOnly()) return;

            byte[] message = new byte[8];
            message[0] = RfbProtocol.MsgKeyEvent;
            message[1] = (byte)(down ? 1 : 0);
            message[4] = (byte)(keysym >> 24);
            message[5] = (byte)(keysym >> 16);
            message[6] = (byte)(keysym >> 8);
            message[7] = (byte)keysym;
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends a complete key press (down then up).</summary>
        public async Task SendKeyPressAsync(uint keysym, CancellationToken cancellationToken)
        {
            await SendKeyEventAsync(keysym, true, cancellationToken).ConfigureAwait(false);
            await SendKeyEventAsync(keysym, false, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends a mouse wheel event (one notch up or down).</summary>
        public async Task SendWheelAsync(bool up, int x, int y, CancellationToken cancellationToken)
        {
            byte button = up ? RfbProtocol.ButtonWheelUp : RfbProtocol.ButtonWheelDown;
            await SendPointerEventAsync(button, x, y, cancellationToken).ConfigureAwait(false);
            await SendPointerEventAsync(0, x, y, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends text to the remote clipboard (ClientCutText).</summary>
        public async Task SendClientCutTextAsync(string text, CancellationToken cancellationToken)
        {
            if (IsViewOnly()) return;
            if (text == null) text = string.Empty;

            byte[] payload = Latin1.GetBytes(text);
            byte[] message = new byte[8 + payload.Length];
            message[0] = RfbProtocol.MsgClientCutText;
            message[4] = (byte)(payload.Length >> 24);
            message[5] = (byte)(payload.Length >> 16);
            message[6] = (byte)(payload.Length >> 8);
            message[7] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, message, 8, payload.Length);
            await WriteRawAsync(message, cancellationToken).ConfigureAwait(false);
        }

        private bool IsViewOnly()
        {
            return _settings != null && _settings.ViewOnly;
        }

        private async Task WriteRawAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException("RfbClient");

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _transport.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _transport.Dispose(); }
            catch (Exception) { }
        }
    }
}
