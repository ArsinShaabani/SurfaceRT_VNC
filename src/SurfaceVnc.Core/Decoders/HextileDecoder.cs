using System;
using System.Threading;
using System.Threading.Tasks;

namespace SurfaceVnc.Core.Decoders
{
    /// <summary>
    /// Encoding 5 (Hextile): the rectangle is split into 16x16 tiles.  Every tile starts with a
    /// sub-encoding byte that tells which of the following parts are present (RFC 6143 7.7.4):
    ///
    ///   bit 0 (1)  raw pixels follow
    ///   bit 1 (2)  a background pixel follows
    ///   bit 2 (4)  a foreground pixel follows
    ///   bit 3 (8)  a sub-rectangle count follows
    ///   bit 4 (16) every sub-rectangle carries its own pixel (otherwise the foreground is used)
    ///
    /// Background/foreground are remembered across tiles, which is why they are tracked here.
    /// </summary>
    public sealed class HextileDecoder : IRfbDecoder
    {
        public const int TileSize = 16;

        public RfbEncoding Encoding { get { return RfbEncoding.Hextile; } }

        public async Task DecodeAsync(RfbDecodeContext context, CancellationToken cancellationToken)
        {
            PixelFormat format = context.PixelFormat;
            int bpp = format.BytesPerPixel;

            byte[] background = new byte[bpp];
            byte[] foreground = new byte[bpp];
            byte[] pixel = new byte[bpp];
            byte[] rawTile = new byte[TileSize * TileSize * bpp];
            bool haveBackground = false;
            bool haveForeground = false;

            for (int tileY = 0; tileY < context.Height; tileY += TileSize)
            {
                int tileHeight = Math.Min(TileSize, context.Height - tileY);

                for (int tileX = 0; tileX < context.Width; tileX += TileSize)
                {
                    int tileWidth = Math.Min(TileSize, context.Width - tileX);
                    byte subEncoding = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);

                    if ((subEncoding & 1) != 0)
                    {
                        int byteCount = tileWidth * tileHeight * bpp;
                        await context.Reader.ReadExactAsync(rawTile, 0, byteCount, cancellationToken).ConfigureAwait(false);
                        context.Framebuffer.WritePixels(
                            context.X + tileX, context.Y + tileY, tileWidth, tileHeight, rawTile, 0, format);
                        continue;
                    }

                    if ((subEncoding & 2) != 0)
                    {
                        await context.Reader.ReadExactAsync(background, 0, bpp, cancellationToken).ConfigureAwait(false);
                        haveBackground = true;
                    }

                    if ((subEncoding & 4) != 0)
                    {
                        await context.Reader.ReadExactAsync(foreground, 0, bpp, cancellationToken).ConfigureAwait(false);
                        haveForeground = true;
                    }

                    if (!haveBackground)
                        throw new RfbException("hextile tile #" + (tileY / TileSize) + "," + (tileX / TileSize) + " has no background colour");

                    context.Framebuffer.FillPixel(
                        context.X + tileX, context.Y + tileY, tileWidth, tileHeight, background, 0, format);

                    if ((subEncoding & 8) == 0) continue;

                    bool colouredSubRectangles = (subEncoding & 16) != 0;
                    int subRectangleCount = await context.Reader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);

                    for (int i = 0; i < subRectangleCount; i++)
                    {
                        byte[] fill = foreground;
                        if (colouredSubRectangles)
                        {
                            await context.Reader.ReadExactAsync(pixel, 0, bpp, cancellationToken).ConfigureAwait(false);
                            fill = pixel;
                        }
                        else if (!haveForeground)
                        {
                            // Tolerate servers that forget to send the foreground pixel.
                            fill = background;
                        }

                        int xy = await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                        int x = (xy >> 4) & 0x0F;
                        int y = xy & 0x0F;
                        int width = (await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false)) + 1;
                        int height = (await context.Reader.ReadByteAsync(cancellationToken).ConfigureAwait(false)) + 1;

                        context.Framebuffer.FillPixel(
                            context.X + tileX + x, context.Y + tileY + y, width, height, fill, 0, format);
                    }
                }
            }
        }
    }
}
