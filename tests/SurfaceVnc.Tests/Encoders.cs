using System;
using System.Collections.Generic;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>
    /// Server side reference encoders.  They are written straight from RFC 6143 so that the
    /// decoders in the client core are checked against an independent implementation.
    /// </summary>
    public static class Encoders
    {
        public static void Raw(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format)
        {
            writer.WriteBytes(image);
        }

        /// <summary>
        /// Hextile with coloured sub-rectangles (and raw tiles when <paramref name="forceRawTiles"/>
        /// is set, so the raw tile path is covered too).
        /// </summary>
        public static void Hextile(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format, bool forceRawTiles)
        {
            for (int tileY = 0; tileY < height; tileY += 16)
            {
                int tileHeight = Math.Min(16, height - tileY);
                for (int tileX = 0; tileX < width; tileX += 16)
                {
                    int tileWidth = Math.Min(16, width - tileX);
                    byte[] background = WireImage.PixelBytes(image, width, tileX, tileY, format);

                    List<int> differing = new List<int>();
                    for (int dy = 0; dy < tileHeight; dy++)
                    {
                        for (int dx = 0; dx < tileWidth; dx++)
                        {
                            if (!WireImage.SamePixel(image, width, tileX + dx, tileY + dy, background))
                                differing.Add(dy * tileWidth + dx);
                        }
                    }

                    if (differing.Count == 0)
                    {
                        writer.WriteByte(2);                       // background specified only
                        writer.WriteBytes(background);
                        continue;
                    }

                    if (forceRawTiles)
                    {
                        writer.WriteByte(1);                       // raw tile
                        for (int dy = 0; dy < tileHeight; dy++)
                            for (int dx = 0; dx < tileWidth; dx++)
                                writer.WriteBytes(WireImage.PixelBytes(image, width, tileX + dx, tileY + dy, format));
                        continue;
                    }

                    writer.WriteByte(2 | 8 | 16);                  // background + subrects + coloured
                    writer.WriteBytes(background);
                    writer.WriteUInt16(differing.Count);
                    foreach (int index in differing)
                    {
                        int dx = index % tileWidth;
                        int dy = index / tileWidth;
                        writer.WriteBytes(WireImage.PixelBytes(image, width, tileX + dx, tileY + dy, format));
                        writer.WriteByte((dx << 4) | dy);
                        writer.WriteByte(0);                       // width  - 1
                        writer.WriteByte(0);                       // height - 1
                    }
                }
            }
        }

        /// <summary>
        /// Hextile using the foreground pixel (no per-sub-rectangle colour).  Every tile must
        /// contain at most one colour besides its background pixel, exactly like a real server.
        /// </summary>
        public static void HextileForeground(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format)
        {
            for (int tileY = 0; tileY < height; tileY += 16)
            {
                int tileHeight = Math.Min(16, height - tileY);
                for (int tileX = 0; tileX < width; tileX += 16)
                {
                    int tileWidth = Math.Min(16, width - tileX);
                    byte[] background = WireImage.PixelBytes(image, width, tileX, tileY, format);

                    byte[] foreground = null;
                    List<int> matching = new List<int>();
                    for (int dy = 0; dy < tileHeight; dy++)
                    {
                        for (int dx = 0; dx < tileWidth; dx++)
                        {
                            if (WireImage.SamePixel(image, width, tileX + dx, tileY + dy, background)) continue;
                            if (foreground == null) foreground = WireImage.PixelBytes(image, width, tileX + dx, tileY + dy, format);
                            matching.Add(dy * tileWidth + dx);
                        }
                    }

                    if (foreground == null)
                    {
                        writer.WriteByte(2);
                        writer.WriteBytes(background);
                        continue;
                    }

                    writer.WriteByte(2 | 4 | 8);                   // background + foreground + subrects
                    writer.WriteBytes(background);
                    writer.WriteBytes(foreground);
                    writer.WriteUInt16(matching.Count);
                    foreach (int index in matching)
                    {
                        int dx = index % tileWidth;
                        int dy = index / tileWidth;
                        writer.WriteByte((dx << 4) | dy);
                        writer.WriteByte(0);
                        writer.WriteByte(0);
                    }
                }
            }
        }
        /// <summary>
        /// Hextile that only sends a background pixel when it changes and then relies on the
        /// inherited one, which is what real servers do: a tile that is entirely background is
        /// sent as a bare "0" sub-encoding with no payload at all.
        /// </summary>
        public static void HextileInherited(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format)
        {
            byte[] knownBackground = null;

            for (int tileY = 0; tileY < height; tileY += 16)
            {
                int tileHeight = Math.Min(16, height - tileY);
                for (int tileX = 0; tileX < width; tileX += 16)
                {
                    int tileWidth = Math.Min(16, width - tileX);
                    byte[] background = WireImage.PixelBytes(image, width, tileX, tileY, format);

                    List<int> differing = new List<int>();
                    for (int dy = 0; dy < tileHeight; dy++)
                    {
                        for (int dx = 0; dx < tileWidth; dx++)
                        {
                            if (!WireImage.SamePixel(image, width, tileX + dx, tileY + dy, background))
                                differing.Add(dy * tileWidth + dx);
                        }
                    }

                    bool backgroundKnown = knownBackground != null && SameBytes(knownBackground, background);

                    if (differing.Count == 0)
                    {
                        if (backgroundKnown)
                        {
                            writer.WriteByte(0);                   // the inherited background covers the tile
                        }
                        else
                        {
                            writer.WriteByte(2);                   // background specified only
                            writer.WriteBytes(background);
                            knownBackground = background;
                        }
                        continue;
                    }

                    if (backgroundKnown)
                    {
                        writer.WriteByte(8 | 16);                  // subrects, coloured, inherited background
                    }
                    else
                    {
                        writer.WriteByte(2 | 8 | 16);              // background + subrects + coloured
                        writer.WriteBytes(background);
                        knownBackground = background;
                    }

                    writer.WriteUInt16(differing.Count);
                    foreach (int index in differing)
                    {
                        int dx = index % tileWidth;
                        int dy = index / tileWidth;
                        writer.WriteBytes(WireImage.PixelBytes(image, width, tileX + dx, tileY + dy, format));
                        writer.WriteByte((dx << 4) | dy);
                        writer.WriteByte(0);
                        writer.WriteByte(0);
                    }
                }
            }
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }


        public static void Rre(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format)
        {
            byte[] background = WireImage.PixelBytes(image, width, 0, 0, format);
            List<Run> runs = FindRuns(image, width, height, format, background);

            writer.WriteBytes(background);
            writer.WriteUInt16(runs.Count);
            foreach (Run run in runs)
            {
                writer.WriteBytes(WireImage.PixelBytes(image, width, run.X, run.Y, format));
                writer.WriteUInt16(run.X);
                writer.WriteUInt16(run.Y);
                writer.WriteUInt16(run.Width);
                writer.WriteUInt16(run.Height);
            }
        }

        public static void CoRre(PayloadWriter writer, byte[] image, int width, int height, PixelFormat format)
        {
            byte[] background = WireImage.PixelBytes(image, width, 0, 0, format);
            List<Run> runs = FindRuns(image, width, height, format, background);

            writer.WriteBytes(background);
            writer.WriteUInt16(runs.Count);
            foreach (Run run in runs)
            {
                writer.WriteBytes(WireImage.PixelBytes(image, width, run.X, run.Y, format));
                writer.WriteByte(run.X);
                writer.WriteByte(run.Y);
                writer.WriteByte(run.Width);
                writer.WriteByte(run.Height);
            }
        }

        public sealed class Run
        {
            public int X, Y, Width, Height;
        }

        /// <summary>Finds maximal horizontal runs of non background pixels (used by RRE/CoRRE).</summary>
        public static List<Run> FindRuns(byte[] image, int width, int height, PixelFormat format, byte[] background)
        {
            List<Run> runs = new List<Run>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    if (WireImage.SamePixel(image, width, x, y, background)) { x++; continue; }

                    byte[] pixel = WireImage.PixelBytes(image, width, x, y, format);
                    int start = x;
                    while (x < width && WireImage.SamePixel(image, width, x, y, pixel)) x++;

                    Run run = new Run();
                    run.X = start;
                    run.Y = y;
                    run.Width = x - start;
                    run.Height = 1;
                    runs.Add(run);
                }
            }
            return runs;
        }
    }
}
