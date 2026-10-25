#if !NETFX_CORE
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// <see cref="IRfbTransport"/> over a plain TCP socket.  Not used by the Windows Store
    /// app (WinRT code must use <c>Windows.Networking.Sockets.StreamSocket</c>), but it keeps
    /// the core usable from any desktop build and it is what the automated tests drive.
    /// </summary>
    public sealed class SocketRfbTransport : IRfbTransport
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private bool _disposed;

        public SocketRfbTransport(TcpClient client)
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;
            _stream = client.GetStream();
        }

        /// <summary>Connects to <paramref name="host"/>:<paramref name="port"/> and returns a transport.</summary>
        public static async Task<SocketRfbTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            TcpClient client = new TcpClient();
            client.NoDelay = true;
            try
            {
                await client.ConnectAsync(host, port).ConfigureAwait(false);
            }
            catch
            {
                client.Close();
                throw;
            }
            return new SocketRfbTransport(client);
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Dispose(); }
            catch (Exception) { }
            try { _client.Close(); }
            catch (Exception) { }
        }
    }
}
#endif
