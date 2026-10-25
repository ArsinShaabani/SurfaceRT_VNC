using System;

namespace SurfaceVnc.Core
{
    /// <summary>Everything needed to open a connection to a VNC server.</summary>
    public sealed class ConnectionSettings
    {
        public const int DefaultPort = 5900;

        private string _host = string.Empty;
        private int _port = DefaultPort;

        public ConnectionSettings() { }

        public ConnectionSettings(string host, int port, string password)
        {
            Host = host;
            Port = port;
            Password = password;
        }

        /// <summary>Host name or IP address of the machine running the VNC server.</summary>
        public string Host
        {
            get { return _host; }
            set { _host = value == null ? string.Empty : value.Trim(); }
        }

        public int Port
        {
            get { return _port; }
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException("Port");
                _port = value;
            }
        }

        /// <summary>Password for VNC authentication (may be null/empty for servers without a password).</summary>
        public string Password { get; set; }

        /// <summary>When false the server is asked for an exclusive (non shared) session.</summary>
        public bool Shared { get; set; }

        /// <summary>When true the client never sends pointer/keyboard events.</summary>
        public bool ViewOnly { get; set; }

        /// <summary>Pixel format we ask the server to encode with.</summary>
        public PixelFormat RequestedPixelFormat { get; set; }

        /// <summary>Encodings we advertise, most preferred first.</summary>
        public RfbEncoding[] Encodings { get; set; }

        public static ConnectionSettings CreateDefault(string host, int port, string password)
        {
            ConnectionSettings s = new ConnectionSettings();
            s.Host = host;
            s.Port = port <= 0 ? DefaultPort : port;
            s.Password = password;
            s.Shared = true;
            s.ViewOnly = false;
            s.RequestedPixelFormat = PixelFormat.DefaultBgra8;
            s.Encodings = RfbProtocol.PreferredEncodings;
            return s;
        }

        /// <summary>Human readable description used in log/status messages.</summary>
        public string Describe()
        {
            return Host + ":" + Port;
        }
    }
}
