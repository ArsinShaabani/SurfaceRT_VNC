using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core.Decoders
{
    /// <summary>Everything a decoder needs: the target framebuffer, the wire pixel format and the stream.</summary>
    public sealed class RfbDecodeContext
    {
        public Framebuffer Framebuffer;
        public PixelFormat PixelFormat;
        public RfbByteReader Reader;

        /// <summary>Rectangle of this update, in remote (framebuffer) coordinates.</summary>
        public int X;
        public int Y;
        public int Width;
        public int Height;

        /// <summary>Source position of a CopyRect rectangle.</summary>
        public int SourceX;
        public int SourceY;
    }

    /// <summary>Decodes one rectangle of a FramebufferUpdate message.</summary>
    public interface IRfbDecoder
    {
        RfbEncoding Encoding { get; }

        Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken);
    }

    /// <summary>Maps RFB encoding numbers to decoders.</summary>
    public static class DecoderRegistry
    {
        public static Dictionary<RfbEncoding, IRfbDecoder> CreateDefault()
        {
            Dictionary<RfbEncoding, IRfbDecoder> decoders = new Dictionary<RfbEncoding, IRfbDecoder>();
            Add(decoders, new RawDecoder());
            Add(decoders, new CopyRectDecoder());
            Add(decoders, new RreDecoder());
            Add(decoders, new CoRreDecoder());
            Add(decoders, new HextileDecoder());
            return decoders;
        }

        private static void Add(Dictionary<RfbEncoding, IRfbDecoder> decoders, IRfbDecoder decoder)
        {
            decoders[decoder.Encoding] = decoder;
        }
    }
}
