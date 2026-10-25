using System;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core.Decoders
{
    /// <summary>
    /// Encoding 2 (RRE): one background pixel for the whole rectangle followed by a list of
    /// sub-rectangles that are filled with a single pixel each.
    /// </summary>
    public sealed class RreDecoder : IRfbDecoder
    {
        public RfbEncoding Encoding { get { return RfbEncoding.Rre; } }

        public async Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken)
        {
            int bpp = context.PixelFormat.BytesPerPixel;
            byte[] background = new byte[bpp];
            byte[] pixel = new byte[bpp];

            await context.Reader.ReadExactAsync(background, 0, bpp, cancellationToken).ConfigureAwait(false);
            int subRectangleCount = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

            context.Framebuffer.FillPixel(context.X, context.Y, context.Width, context.Height, background, 0, context.PixelFormat);

            for (int i = 0; i < subRectangleCount; i++)
            {
                await context.Reader.ReadExactAsync(pixel, 0, bpp, cancellationToken).ConfigureAwait(false);
                int x = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                int y = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                int width = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                int height = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

                context.Framebuffer.FillPixel(context.X + x, context.Y + y, width, height, pixel, 0, context.PixelFormat);
            }
        }
    }

    /// <summary>
    /// Encoding 4 (CoRRE): identical to RRE but the sub-rectangle geometry uses single bytes,
    /// which caps sub-rectangles to 255x255 pixels.
    /// </summary>
    public sealed class CoRreDecoder : IRfbDecoder
    {
        public RfbEncoding Encoding { get { return RfbEncoding.CoRre; } }

        public async Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken)
        {
            int bpp = context.PixelFormat.BytesPerPixel;
            byte[] background = new byte[bpp];
            byte[] pixel = new byte[bpp];

            await context.Reader.ReadExactAsync(background, 0, bpp, cancellationToken).ConfigureAwait(false);
            int subRectangleCount = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

            context.Framebuffer.FillPixel(context.X, context.Y, context.Width, context.Height, background, 0, context.PixelFormat);

            for (int i = 0; i < subRectangleCount; i++)
            {
                await context.Reader.ReadExactAsync(pixel, 0, bpp, cancellationToken).ConfigureAwait(false);
                int x = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                int y = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                int width = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                int height = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);

                context.Framebuffer.FillPixel(context.X + x, context.Y + y, width, height, pixel, 0, context.PixelFormat);
            }
        }
    }
}
