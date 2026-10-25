using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// The RFB "PIXEL_FORMAT" structure (RFC 6143, 7.4).  It describes how every pixel of
    /// the framebuffer is laid out on the wire.
    /// </summary>
    public struct PixelFormat : IEquatable<PixelFormat>
    {
        public byte BitsPerPixel;
        public byte Depth;
        public bool BigEndianFlag;
        public bool TrueColourFlag;
        public ushort RedMax;
        public ushort GreenMax;
        public ushort BlueMax;
        public byte RedShift;
        public byte GreenShift;
        public byte BlueShift;

        /// <summary>
        /// 32 bpp / depth 24 / little endian / 8-8-8 true colour.  On the wire this is
        /// byte order B,G,R,X which is exactly what a WriteableBitmap (BGRA8) wants.
        /// </summary>
        public static PixelFormat DefaultBgra8
        {
            get
            {
                PixelFormat f = new PixelFormat();
                f.BitsPerPixel = 32;
                f.Depth = 24;
                f.BigEndianFlag = false;
                f.TrueColourFlag = true;
                f.RedMax = 255;
                f.GreenMax = 255;
                f.BlueMax = 255;
                f.RedShift = 16;
                f.GreenShift = 8;
                f.BlueShift = 0;
                return f;
            }
        }

        /// <summary>Number of bytes one pixel occupies in the stream.</summary>
        public int BytesPerPixel
        {
            get { return (BitsPerPixel + 7) / 8; }
        }

        /// <summary>
        /// True when the wire bytes are already B,G,R,X with X ignored, i.e. when the
        /// fast copy path in <see cref="PixelConverter"/> can be used.
        /// </summary>
        public bool IsWireCompatibleWithBgra8
        {
            get
            {
                return BitsPerPixel == 32
                    && !BigEndianFlag
                    && TrueColourFlag
                    && RedMax == 255 && GreenMax == 255 && BlueMax == 255
                    && RedShift == 16 && GreenShift == 8 && BlueShift == 0;
            }
        }

        public bool IsValid
        {
            get
            {
                return BitsPerPixel == 8 || BitsPerPixel == 16
                    || BitsPerPixel == 24 || BitsPerPixel == 32;
            }
        }

        public bool Equals(PixelFormat other)
        {
            return BitsPerPixel == other.BitsPerPixel
                && Depth == other.Depth
                && BigEndianFlag == other.BigEndianFlag
                && TrueColourFlag == other.TrueColourFlag
                && RedMax == other.RedMax
                && GreenMax == other.GreenMax
                && BlueMax == other.BlueMax
                && RedShift == other.RedShift
                && GreenShift == other.GreenShift
                && BlueShift == other.BlueShift;
        }

        public override bool Equals(object obj)
        {
            return obj is PixelFormat && Equals((PixelFormat)obj);
        }

        public override int GetHashCode()
        {
            return BitsPerPixel | (Depth << 8) | (RedShift << 16) | (GreenShift << 20) | (BlueShift << 24);
        }

        public override string ToString()
        {
            return string.Format(
                "{0}bpp depth {1} {2} rmax {3} gmax {4} bmax {5} shifts {6}/{7}/{8}",
                BitsPerPixel, Depth, BigEndianFlag ? "big endian" : "little endian",
                RedMax, GreenMax, BlueMax, RedShift, GreenShift, BlueShift);
        }

        /// <summary>Writes the 16 byte wire representation of this pixel format.</summary>
        public void WriteTo(byte[] buffer, int offset)
        {
            buffer[offset + 0] = BitsPerPixel;
            buffer[offset + 1] = Depth;
            buffer[offset + 2] = (byte)(BigEndianFlag ? 1 : 0);
            buffer[offset + 3] = (byte)(TrueColourFlag ? 1 : 0);
            buffer[offset + 4] = (byte)(RedMax >> 8);
            buffer[offset + 5] = (byte)(RedMax & 0xFF);
            buffer[offset + 6] = (byte)(GreenMax >> 8);
            buffer[offset + 7] = (byte)(GreenMax & 0xFF);
            buffer[offset + 8] = (byte)(BlueMax >> 8);
            buffer[offset + 9] = (byte)(BlueMax & 0xFF);
            buffer[offset + 10] = RedShift;
            buffer[offset + 11] = GreenShift;
            buffer[offset + 12] = BlueShift;
            buffer[offset + 13] = 0;
            buffer[offset + 14] = 0;
            buffer[offset + 15] = 0;
        }

        /// <summary>Reads the 16 byte wire representation of a pixel format.</summary>
        public static PixelFormat ReadFrom(byte[] buffer, int offset)
        {
            PixelFormat f = new PixelFormat();
            f.BitsPerPixel = buffer[offset + 0];
            f.Depth = buffer[offset + 1];
            f.BigEndianFlag = buffer[offset + 2] != 0;
            f.TrueColourFlag = buffer[offset + 3] != 0;
            f.RedMax = (ushort)((buffer[offset + 4] << 8) | buffer[offset + 5]);
            f.GreenMax = (ushort)((buffer[offset + 6] << 8) | buffer[offset + 7]);
            f.BlueMax = (ushort)((buffer[offset + 8] << 8) | buffer[offset + 9]);
            f.RedShift = buffer[offset + 10];
            f.GreenShift = buffer[offset + 11];
            f.BlueShift = buffer[offset + 12];
            return f;
        }
    }
}
