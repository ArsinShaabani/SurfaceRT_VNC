using System;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core.Decoders
{
    /// <summary>Encoding 0 (Raw): the rectangle is sent as plain pixels, row by row.</summary>
    public sealed class RawDecoder : IRfbDecoder
    {
        public RfbEncoding Encoding { get { return RfbEncoding.Raw; } }

        public async Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken)
        {
            int bpp = context.PixelFormat.BytesPerPixel;
            byte[] row = new byte[context.Width * bpp];

            for (int y = 0; y < context.Height; y++)
            {
                await context.Reader.ReadExactAsync(row, 0, row.Length, cancellationToken).ConfigureAwait(false);
                context.Framebuffer.WritePixels(context.X, context.Y + y, context.Width, 1, row, 0, context.PixelFormat);
            }
        }
    }

    /// <summary>
    /// Encoding 1 (CopyRect): "the pixels are already on the client", the rectangle is only
    /// moved to a new position.  Two 16 bit coordinates follow the rectangle header.
    /// </summary>
    public sealed class CopyRectDecoder : IRfbDecoder
    {
        public RfbEncoding Encoding { get { return RfbEncoding.CopyRect; } }

        public Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken)
        {
            context.Framebuffer.CopyRect(
                context.SourceX, context.SourceY, context.Width, context.Height, context.X, context.Y);
            return Task.FromResult(true);
        }
    }
}
