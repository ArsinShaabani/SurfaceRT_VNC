using System;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// Byte stream abstraction so the protocol code is independent of the socket API.
    /// The Windows Store app implements this on top of
    /// <c>Windows.Networking.Sockets.StreamSocket</c>, the tests on top of
    /// <c>System.Net.Sockets.TcpClient</c>.
    /// </summary>
    public interface IRfbTransport : IDisposable
    {
        /// <summary>
        /// Reads *up to* <paramref name="count"/> bytes and returns how many were stored in
        /// <paramref name="buffer"/> (0 means the peer closed the connection).  A partial read
        /// is normal: callers that need an exact number of bytes use
        /// <see cref="RfbTransportHelper.ReadExactAsync"/>.
        /// </summary>
        Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);

        /// <summary>Writes <paramref name="count"/> bytes, flushing them to the peer.</summary>
        Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);
    }

    /// <summary>Helpers that build exact reads on top of the partial read primitive.</summary>
    public static class RfbTransportHelper
    {
        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes.  Throws
        /// <see cref="RfbConnectionClosedException"/> when the peer closes the connection.
        /// </summary>
        public static async Task ReadExactAsync(this IRfbTransport transport, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (buffer == null) throw new ArgumentNullException("buffer");

            int total = 0;
            while (total < count)
            {
                int read = await transport.ReadAsync(buffer, offset + total, count - total, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new RfbConnectionClosedException("the VNC server closed the connection after " + total + " of " + count + " expected bytes");
                total += read;
            }
        }
    }
}
