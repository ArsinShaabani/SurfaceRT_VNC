using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    public sealed class PointerEventRecord
    {
        public byte ButtonMask;
        public int X;
        public int Y;

        public override string ToString()
        {
            return "mask=" + ButtonMask + " at " + X + "," + Y;
        }
    }

    public sealed class KeyEventRecord
    {
        public bool Down;
        public uint KeySym;

        public override string ToString()
        {
            return (Down ? "down " : "up   ") + "0x" + KeySym.ToString("X");
        }
    }

    /// <summary>
    /// A real RFB 3.3/3.8 server implementation on 127.0.0.1, used to drive the production
    /// client core over an actual TCP connection.  It records everything the client sends so
    /// tests can assert on exact bytes, and it can be scripted with framebuffer updates.
    /// </summary>
    public sealed class FakeRfbServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly object _sync = new object();
        private readonly Queue<byte[]> _pendingFrames = new Queue<byte[]>();
        private readonly ManualResetEventSlim _clientConnected = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _handshakeDone = new ManualResetEventSlim(false);

        private TcpClient _client;
        private NetworkStream _stream;
        private volatile bool _stop;
        private bool _updatePending;

        public FakeRfbServer(int width, int height)
        {
            Width = width;
            Height = height;
            ProtocolVersion = RfbProtocol.VersionString38;
            SecurityType = RfbProtocol.SecurityNone;
            ServerName = "SurfaceVnc test server";
            Challenge = new byte[16];
            for (int i = 0; i < 16; i++) Challenge[i] = (byte)(i * 0x11);

            ServerPixelFormat = PixelFormat.DefaultBgra8;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "FakeRfbServer";
            _thread.Start();
        }

        // ---- configuration -------------------------------------------------------------
        public int Port { get; private set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ProtocolVersion { get; set; }
        public byte SecurityType { get; set; }
        public string Password { get; set; }
        public string ServerName { get; set; }
        public byte[] Challenge { get; private set; }
        public PixelFormat ServerPixelFormat { get; set; }
        public string RejectReason { get; set; }

        // ---- observations --------------------------------------------------------------
        public Exception ServerError { get; private set; }
        public string ClientVersionString { get; private set; }
        public byte ClientSecurityChoice { get; private set; }
        public bool ClientInitShared { get; private set; }
        public bool ClientPixelFormatSeen { get; private set; }
        public PixelFormat ClientPixelFormat { get; private set; }
        public List<int> ClientEncodings = new List<int>();
        public List<PointerEventRecord> PointerEvents = new List<PointerEventRecord>();
        public List<KeyEventRecord> KeyEvents = new List<KeyEventRecord>();
        public List<string> CutTexts = new List<string>();
        public List<string> Log = new List<string>();
        public int UpdateRequestCount { get; private set; }
        public bool LastRequestIncremental { get; private set; }
        public byte[] VncAuthResponse { get; private set; }
        public bool VncAuthAccepted { get; private set; }

        public bool WaitForHandshake(int timeoutMilliseconds)
        {
            return _handshakeDone.Wait(timeoutMilliseconds);
        }

        public bool WaitForClient(int timeoutMilliseconds)
        {
            return _clientConnected.Wait(timeoutMilliseconds);
        }

        /// <summary>Queues a FramebufferUpdate; it is sent when the client asks for an update.</summary>
        public void QueueFrame(byte[] message)
        {
            lock (_sync)
            {
                _pendingFrames.Enqueue(message);
                TryFlushLocked();
            }
        }

        /// <summary>Writes bytes to the client immediately (server initiated message).</summary>
        public void SendNow(byte[] message)
        {
            lock (_sync) { WriteLocked(message); }
        }

        public void SendBell()
        {
            SendNow(new byte[] { 2 });
        }

        public void CloseConnection()
        {
            lock (_sync)
            {
                try { if (_stream != null) _stream.Close(); }
                catch (Exception) { }
                try { if (_client != null) _client.Close(); }
                catch (Exception) { }
            }
        }

        private void TryFlushLocked()
        {
            if (!_updatePending) return;
            if (_pendingFrames.Count == 0) return;

            _updatePending = false;
            WriteLocked(_pendingFrames.Dequeue());
        }

        private void WriteLocked(byte[] message)
        {
            if (_stream == null) return;
            _stream.Write(message, 0, message.Length);
            _stream.Flush();
        }

        public void SendServerCutText(string text)
        {
            byte[] payload = Latin1.GetBytes(text);
            PayloadWriter writer = new PayloadWriter();
            writer.WriteByte(3);           // ServerCutText
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteUInt32((uint)payload.Length);
            writer.WriteBytes(payload);
            SendNow(writer.ToArray());
        }

        public void SendColourMapEntries(int first, byte[][] colours)
        {
            PayloadWriter writer = new PayloadWriter();
            writer.WriteByte(1);           // SetColourMapEntries
            writer.WriteByte(0);
            writer.WriteUInt16(first);
            writer.WriteUInt16(colours.Length);
            foreach (byte[] colour in colours)
            {
                writer.WriteUInt16(colour[0]);
                writer.WriteUInt16(colour[1]);
                writer.WriteUInt16(colour[2]);
            }
            SendNow(writer.ToArray());
        }

        // ---- server thread -------------------------------------------------------------

        private void Run()
        {
            try
            {
                _client = _listener.AcceptTcpClient();
                _client.NoDelay = true;
                _stream = _client.GetStream();
                _clientConnected.Set();

                Handshake();
                _handshakeDone.Set();
                MessageLoop();
            }
            catch (Exception error)
            {
                if (!_stop) ServerError = error;
            }
        }

        private void Handshake()
        {
            WriteRaw(Encoding.ASCII.GetBytes(ProtocolVersion));
            ClientVersionString = Encoding.ASCII.GetString(ReadExact(12));
            Log.Add("client banner: " + ClientVersionString.Replace("\n", "\\n"));

            bool modern = ProtocolVersion.IndexOf("003.003", StringComparison.Ordinal) < 0;

            if (modern)
            {
                if (RejectReason != null)
                {
                    byte[] reason = Encoding.ASCII.GetBytes(RejectReason);
                    WriteByte(0);
                    WriteUInt32((uint)reason.Length);
                    WriteRaw(reason);
                    return;
                }

                WriteByte(1);
                WriteByte(SecurityType);
                ClientSecurityChoice = (byte)ReadByte();
                if (ClientSecurityChoice != SecurityType)
                    throw new InvalidOperationException("client chose security type " + ClientSecurityChoice + " but the server offered " + SecurityType);
                if (SecurityType == RfbProtocol.SecurityVncAuthentication) PerformVncAuthentication();
            }
            else
            {
                WriteUInt32(SecurityType);
                if (SecurityType == RfbProtocol.SecurityVncAuthentication) PerformVncAuthentication();
            }

            ClientInitShared = ReadByte() != 0;

            PayloadWriter serverInit = new PayloadWriter();
            serverInit.WriteUInt16(Width);
            serverInit.WriteUInt16(Height);
            byte[] pixelFormat = new byte[16];
            ServerPixelFormat.WriteTo(pixelFormat, 0);
            serverInit.WriteBytes(pixelFormat);
            byte[] name = Encoding.ASCII.GetBytes(ServerName);
            serverInit.WriteUInt32((uint)name.Length);
            serverInit.WriteBytes(name);
            WriteRaw(serverInit.ToArray());
        }

        private void PerformVncAuthentication()
        {
            WriteRaw(Challenge);
            VncAuthResponse = ReadExact(16);

            byte[] expected = VncAuthentication.ComputeResponse(Password, Challenge);
            VncAuthAccepted = true;
            for (int i = 0; i < 16; i++)
            {
                if (expected[i] != VncAuthResponse[i]) VncAuthAccepted = false;
            }

            WriteUInt32(VncAuthAccepted ? 0u : 1u);
            if (!VncAuthAccepted)
            {
                byte[] reason = Encoding.ASCII.GetBytes("Authentication failure");
                WriteUInt32((uint)reason.Length);
                WriteRaw(reason);
            }
        }

        private void MessageLoop()
        {
            while (!_stop)
            {
                int type = _stream.ReadByte();
                if (type < 0)
                {
                    Log.Add("client closed the connection");
                    return;
                }

                switch (type)
                {
                    case 0:     // SetPixelFormat
                        ReadExact(3);
                        ClientPixelFormat = PixelFormat.ReadFrom(ReadExact(16), 0);
                        ClientPixelFormatSeen = true;
                        break;

                    case 2:     // SetEncodings
                    {
                        ReadExact(1);
                        int count = ReadUInt16();
                        List<int> encodings = new List<int>();
                        for (int i = 0; i < count; i++) encodings.Add(ReadInt32());
                        lock (_sync) { ClientEncodings = encodings; }
                        break;
                    }

                    case 3:     // FramebufferUpdateRequest
                    {
                        int incremental = ReadByte();
                        ReadUInt16();
                        ReadUInt16();
                        ReadUInt16();
                        ReadUInt16();
                        lock (_sync)
                        {
                            UpdateRequestCount++;
                            LastRequestIncremental = incremental != 0;
                            _updatePending = true;
                            TryFlushLocked();
                        }
                        break;
                    }

                    case 4:     // KeyEvent
                    {
                        int down = ReadByte();
                        ReadExact(2);
                        uint keysym = ReadUInt32();
                        lock (_sync) { KeyEvents.Add(new KeyEventRecord { Down = down != 0, KeySym = keysym }); }
                        break;
                    }

                    case 5:     // PointerEvent
                    {
                        int mask = ReadByte();
                        int x = ReadUInt16();
                        int y = ReadUInt16();
                        lock (_sync) { PointerEvents.Add(new PointerEventRecord { ButtonMask = (byte)mask, X = x, Y = y }); }
                        break;
                    }

                    case 6:     // ClientCutText
                    {
                        ReadExact(3);
                        uint length = ReadUInt32();
                        byte[] text = ReadExact((int)length);
                        lock (_sync) { CutTexts.Add(Latin1.GetString(text)); }
                        break;
                    }

                    default:
                        Log.Add("unexpected client message type " + type);
                        return;
                }
            }
        }

        // ---- raw stream helpers --------------------------------------------------------

        private void WriteByte(int value)
        {
            WriteRaw(new byte[] { (byte)value });
        }

        private void WriteUInt32(uint value)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(value >> 24);
            bytes[1] = (byte)(value >> 16);
            bytes[2] = (byte)(value >> 8);
            bytes[3] = (byte)value;
            WriteRaw(bytes);
        }

        private void WriteRaw(byte[] bytes)
        {
            lock (_sync)
            {
                if (_stream == null) return;
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
        }

        private int ReadByte()
        {
            return _stream.ReadByte();
        }

        private int ReadUInt16()
        {
            byte[] bytes = ReadExact(2);
            return (bytes[0] << 8) | bytes[1];
        }

        private int ReadInt32()
        {
            byte[] bytes = ReadExact(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private uint ReadUInt32()
        {
            byte[] bytes = ReadExact(4);
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = _stream.Read(buffer, total, count - total);
                if (read <= 0) throw new System.IO.EndOfStreamException("the client closed the connection while " + count + " bytes were expected");
                total += read;
            }
            return buffer;
        }

        public void Dispose()
        {
            _stop = true;
            CloseConnection();
            try { _listener.Stop(); }
            catch (Exception) { }
            try { _thread.Join(2000); }
            catch (Exception) { }
        }
    }
}
