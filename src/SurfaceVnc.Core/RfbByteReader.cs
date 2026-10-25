using System;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// Buffered big-endian reader over an <see cref="IRfbTransport"/>.  All RFB messages are
    /// big-endian and made of many small fields (Hextile especially), so reading through a
    /// buffer avoids one socket round trip per field.
    /// </summary>
    public sealed class RfbByteReader
    {
        private readonly IRfbTransport _transport;
        private readonly byte[] _buffer;
        private int _position;
        private int _length;

        public RfbByteReader(IRfbTransport transport) : this(transport, 8192) { }

        public RfbByteReader(IRfbTransport transport, int bufferSize)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (bufferSize < 16) bufferSize = 16;
            _transport = transport;
            _buffer = new byte[bufferSize];
        }

        /// <summary>Bytes currently sitting in the internal buffer (diagnostics/tests).</summary>
        public int BufferedBytes { get { return _length - _position; } }

        public async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_position == _length) await FillAsync(cancellationToken).ConfigureAwait(false);
            return _buffer[_position++];
        }

        public async Task<ushort> ReadUInt16Async(CancellationToken cancellationToken)
        {
            await EnsureAsync(2, cancellationToken).ConfigureAwait(false);
            ushort value = (ushort)((_buffer[_position] << 8) | _buffer[_position + 1]);
            _position += 2;
            return value;
        }

        public async Task<uint> ReadUInt32Async(CancellationToken cancellationToken)
        {
            await EnsureAsync(4, cancellationToken).ConfigureAwait(false);
            uint value = ((uint)_buffer[_position] << 24)
                       | ((uint)_buffer[_position + 1] << 16)
                       | ((uint)_buffer[_position + 2] << 8)
                       | _buffer[_position + 3];
            _position += 4;
            return value;
        }

        public async Task<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            return unchecked((int)await ReadUInt32Async(cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes into <paramref name="destination"/>.</summary>
        public async Task ReadExactAsync(byte[] destination, int offset, int count, CancellationToken cancellationToken)
        {
            if (destination == null) throw new ArgumentNullException("destination");

            while (count > 0)
            {
                if (_position == _length) await FillAsync(cancellationToken).ConfigureAwait(false);

                int available = _length - _position;
                int take = available < count ? available : count;
                Buffer.BlockCopy(_buffer, _position, destination, offset, take);
                _position += take;
                offset += take;
                count -= take;
            }
        }

        /// <summary>Reads and discards <paramref name="count"/> bytes.</summary>
        public async Task SkipAsync(int count, CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                if (_position == _length) await FillAsync(cancellationToken).ConfigureAwait(false);
                int available = _length - _position;
                int take = available < count ? available : count;
                _position += take;
                count -= take;
            }
        }

        /// <summary>Reads a fixed length Latin-1 string (RFB server name / cut text).</summary>
        public async Task<string> ReadStringAsync(int byteCount, CancellationToken cancellationToken)
        {
            if (byteCount <= 0) return string.Empty;
            byte[] temp = new byte[byteCount];
            await ReadExactAsync(temp, 0, byteCount, cancellationToken).ConfigureAwait(false);
            return SurfaceVnc.Core.Latin1.GetString(temp, 0, temp.Length);
        }

        /// <summary>Reads <paramref name="count"/> bytes into a freshly allocated array.</summary>
        public async Task<byte[]> ReadByteArrayAsync(int count, CancellationToken cancellationToken)
        {
            byte[] result = new byte[count];
            await ReadExactAsync(result, 0, count, cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>Ensures at least <paramref name="count"/> bytes are buffered.</summary>
        private async Task EnsureAsync(int count, CancellationToken cancellationToken)
        {
            if (_length - _position >= count) return;

            int remaining = _length - _position;
            if (remaining > 0 && _position > 0)
                Buffer.BlockCopy(_buffer, _position, _buffer, 0, remaining);
            _position = 0;
            _length = remaining;

            if (count > _buffer.Length)
                throw new RfbException("RFB field is larger than the internal read buffer (" + _buffer.Length + " bytes)");

            while (_length < count)
            {
                int read = await _transport.ReadAsync(_buffer, _length, _buffer.Length - _length, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new RfbConnectionClosedException("the VNC server closed the connection while " + count + " bytes were expected");
                _length += read;
            }
        }

        private async Task FillAsync(CancellationToken cancellationToken)
        {
            _position = 0;
            _length = 0;
            int read = await _transport.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new RfbConnectionClosedException("the VNC server closed the connection");
            _length = read;
        }
    }
}
