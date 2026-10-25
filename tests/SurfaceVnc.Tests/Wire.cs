using System;
using System.Collections.Generic;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Growable big-endian byte builder used to compose RFB messages.</summary>
    public sealed class PayloadWriter
    {
        private byte[] _buffer = new byte[256];
        private int _length;

        public int Length { get { return _length; } }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;
            int size = _buffer.Length;
            while (size < _length + extra) size *= 2;
            Array.Resize(ref _buffer, size);
        }

        public void WriteByte(int value)
        {
            Ensure(1);
            _buffer[_length++] = (byte)value;
        }

        public void WriteUInt16(int value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)value;
        }

        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        public void WriteUInt32(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)(value >> 24);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)value;
        }

        public void WriteBytes(byte[] data)
        {
            WriteBytes(data, 0, data.Length);
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;
        }

        public void WriteAscii(string text)
        {
            for (int i = 0; i < text.Length; i++) WriteByte(text[i]);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }
    }

    /// <summary>Builds a complete FramebufferUpdate message out of rectangles.</summary>
    public sealed class FramebufferUpdateBuilder
    {
        private readonly PayloadWriter _rectangles = new PayloadWriter();
        private int _count;

        public FramebufferUpdateBuilder(PixelFormat pixelFormat)
        {
            PixelFormat = pixelFormat;
        }

        public PixelFormat PixelFormat { get; private set; }

        public int RectangleCount { get { return _count; } }

        public void AddRectangle(int x, int y, int width, int height, RfbEncoding encoding, Action<PayloadWriter> payload)
        {
            _rectangles.WriteUInt16(x);
            _rectangles.WriteUInt16(y);
            _rectangles.WriteUInt16(width);
            _rectangles.WriteUInt16(height);
            _rectangles.WriteInt32((int)encoding);
            if (payload != null) payload(_rectangles);
            _count++;
        }

        /// <summary>Serialises "FramebufferUpdate" (message type 0) including the header.</summary>
        public byte[] Build()
        {
            PayloadWriter message = new PayloadWriter();
            message.WriteByte(0);           // message-type = FramebufferUpdate
            message.WriteByte(0);           // padding
            message.WriteUInt16(_count);
            message.WriteBytes(_rectangles.ToArray());
            return message.ToArray();
        }
    }
}
