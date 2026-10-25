using System;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Key mapping and buffered reader tests.</summary>
    public static class InputTests
    {
        public static void Register()
        {
            TestRunner.Add("Virtual keys and unicode characters map to keysyms", KeyMapping);
            TestRunner.Add("Buffered reader copes with one byte per read", DripFeedReader);
            TestRunner.Add("Buffered reader reports a closed connection", ReaderDetectsClose);
        }

        private static void KeyMapping()
        {
            Check.Equal(RemoteKeys.Return, RemoteKeys.FromVirtualKey(0x0D), "VK_RETURN");
            Check.Equal(RemoteKeys.Left, RemoteKeys.FromVirtualKey(0x25), "VK_LEFT");
            Check.Equal(RemoteKeys.BackSpace, RemoteKeys.FromVirtualKey(0x08), "VK_BACK");
            Check.Equal(RemoteKeys.F5, RemoteKeys.FromVirtualKey(0x74), "VK_F5");
            Check.Equal(RemoteKeys.F12, RemoteKeys.FromVirtualKey(0x7B), "VK_F12");
            Check.Equal(RemoteKeys.SuperLeft, RemoteKeys.FromVirtualKey(0x5B), "VK_LWIN");
            Check.Equal(0u, RemoteKeys.FromVirtualKey(0x41), "letters are sent as characters, not virtual keys");

            Check.Equal(0x41u, RemoteKeys.FromUnicode('A'), "ASCII");
            Check.Equal(0x20u, RemoteKeys.FromUnicode(' '), "space");
            Check.Equal(0xE9u, RemoteKeys.FromUnicode(0xE9), "Latin-1 characters keep their code point as keysym");
            Check.Equal(0x01000641u, RemoteKeys.FromUnicode(0x0641), "Persian FEH uses a unicode keysym");
            Check.Equal(0x01000627u, RemoteKeys.FromUnicode(0x0627), "Persian ALEF uses a unicode keysym");
            Check.Equal(0x010020ACu, RemoteKeys.FromUnicode(0x20AC), "euro sign uses a unicode keysym");
            Check.Equal(0x0101F600u, RemoteKeys.FromUnicode(0x1F600), "astral plane characters are supported");
        }

        private static void DripFeedReader()
        {
            byte[] payload = new byte[100];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;

            DripTransport transport = new DripTransport(payload);
            RfbByteReader reader = new RfbByteReader(transport, 16);

            Check.Equal(0x00, reader.ReadByteAsync(CancellationToken.None).Result, "first byte");
            Check.Equal(0x0102, reader.ReadUInt16Async(CancellationToken.None).Result, "big endian u16");
            Check.Equal(0x03040506u, reader.ReadUInt32Async(CancellationToken.None).Result, "big endian u32");

            byte[] rest = reader.ReadByteArrayAsync(93, CancellationToken.None).Result;
            Check.Equal(0x07, rest[0], "continuation byte");
            Check.Equal(0x62, rest[91], "byte 98");
            Check.Equal(0x63, rest[92], "last byte");
        }

        private static void ReaderDetectsClose()
        {
            DripTransport transport = new DripTransport(new byte[] { 1, 2, 3 });
            RfbByteReader reader = new RfbByteReader(transport, 16);
            reader.ReadByteArrayAsync(3, CancellationToken.None).Wait();

            try
            {
                reader.ReadByteAsync(CancellationToken.None).Wait();
                throw new CheckFailedException("reading past the end of the stream must throw");
            }
            catch (AggregateException error)
            {
                if (!(error.InnerException is RfbConnectionClosedException))
                    throw new CheckFailedException("expected RfbConnectionClosedException, got " + error.InnerException);
            }
        }

        /// <summary>A transport that hands out at most one byte per read, like a slow network.</summary>
        private sealed class DripTransport : IRfbTransport
        {
            private readonly byte[] _data;
            private int _position;

            public DripTransport(byte[] data)
            {
                _data = data;
            }

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_position >= _data.Length) return Task.FromResult(0);
                buffer[offset] = _data[_position++];
                return Task.FromResult(1);
            }

            public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public void Dispose()
            {
            }
        }
    }
}
