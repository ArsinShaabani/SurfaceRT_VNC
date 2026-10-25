using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace SurfaceVnc.Store
{
    /// <summary>
    /// <see cref="IRfbTransport"/> over a WinRT <see cref="StreamSocket"/>.  Store apps may open
    /// TCP sockets to any port, but they need the "privateNetworkClientServer" capability in
    /// Package.appxmanifest to reach the local network.
    /// </summary>
    public sealed class StreamSocketTransport : IRfbTransport
    {
        private readonly StreamSocket _socket;
        private readonly Stream _input;
        private readonly Stream _output;
        private bool _disposed;

        private StreamSocketTransport(StreamSocket socket)
        {
            _socket = socket;
            _input = socket.InputStream.AsStreamForRead();
            _output = socket.OutputStream.AsStreamForWrite();
        }

        /// <summary>Opens a TCP connection to <paramref name="host"/>:<paramref name="port"/>.</summary>
        public static async Task<StreamSocketTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            StreamSocket socket = new StreamSocket();
            EnableNoDelay(socket.Control);

            try
            {
                await socket.ConnectAsync(new HostName(host), port.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                socket.Dispose();
                throw;
            }

            return new StreamSocketTransport(socket);
        }

        /// <summary>
        /// Turns on TCP_NODELAY (the RFB protocol sends one small message per mouse move).  The
        /// property is looked up through reflection so the app still compiles against SDK builds
        /// that do not expose it.
        /// </summary>
        private static void EnableNoDelay(StreamSocketControl control)
        {
            try
            {
                System.Reflection.PropertyInfo property = typeof(StreamSocketControl).GetProperty("NoDelay");
                if (property != null && property.CanWrite) property.SetValue(control, true, null);
            }
            catch (Exception)
            {
                // Not fatal: without it the connection is just slightly more chatty.
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _input.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _output.WriteAsync(buffer, offset, count, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _input.Dispose(); }
            catch (Exception) { }
            try { _output.Dispose(); }
            catch (Exception) { }
            try { _socket.Dispose(); }
            catch (Exception) { }
        }
    }
}
