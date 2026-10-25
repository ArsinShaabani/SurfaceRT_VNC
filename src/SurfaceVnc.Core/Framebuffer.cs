using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// The local copy of the remote framebuffer, always stored as 32 bpp BGRA8 with an
    /// opaque alpha channel so it can be pushed into a WinRT WriteableBitmap directly.
    ///
    /// All decoders write into this class; the UI copies regions out of it under
    /// <see cref="SyncRoot"/> while the network thread is filling it.
    /// </summary>
    public sealed class Framebuffer
    {
        public const int BytesPerPixel = 4;

        private int _width;
        private int _height;
        private byte[] _pixels = new byte[0];
        private byte[] _copyScratch = new byte[0];

        /// <summary>
        /// Held by the network thread while a whole framebuffer update is being decoded, and by
        /// the UI thread while pixels are copied out of <see cref="Pixels"/>.  It is a semaphore
        /// (not a plain lock) because decoders await socket reads while they hold it.
        /// </summary>
        public readonly System.Threading.SemaphoreSlim Gate = new System.Threading.SemaphoreSlim(1, 1);

        public int Width { get { return _width; } }

        public int Height { get { return _height; } }

        /// <summary>Raw BGRA8 pixels, <c>Width * Height * 4</c> bytes, top-left origin.</summary>
        public byte[] Pixels { get { return _pixels; } }

        public int Stride { get { return _width * BytesPerPixel; } }

        public bool IsEmpty { get { return _width <= 0 || _height <= 0; } }

        /// <summary>Raised (on the network thread) when the desktop size changed.</summary>
        public event EventHandler SizeChanged;

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width", "framebuffer size must be positive");
            if (width > 8192 || height > 8192) throw new ArgumentOutOfRangeException("width", "framebuffer size is not plausible");

            bool changed = width != _width || height != _height;
            _width = width;
            _height = height;
            int required = width * height * BytesPerPixel;
            if (_pixels.Length != required)
            {
                _pixels = new byte[required];
                changed = true;
            }
            else
            {
                Array.Clear(_pixels, 0, _pixels.Length);
            }

            if (changed)
            {
                EventHandler handler = SizeChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void Clear()
        {
            Array.Clear(_pixels, 0, _pixels.Length);
        }

        /// <summary>
        /// Converts and blits <paramref name="width"/> x <paramref name="height"/> pixels taken from
        /// <paramref name="src"/> (in <paramref name="format"/>, tightly packed) to (x,y).
        /// Out of bounds pixels are clipped away.
        /// </summary>
        public void WritePixels(int x, int y, int width, int height, byte[] src, int srcOffset, PixelFormat format)
        {
            if (src == null) throw new ArgumentNullException("src");
            if (width <= 0 || height <= 0) return;

            int bpp = format.BytesPerPixel;
            Rect target = new Rect(x, y, width, height).Clip(_width, _height);
            if (target.IsEmpty) return;

            for (int row = 0; row < target.Height; row++)
            {
                int srcRow = (target.Y - y) + row;
                int srcX = target.X - x;
                int srcPixelOffset = srcOffset + (srcRow * width + srcX) * bpp;
                int dstOffset = (target.Y + row) * Stride + target.X * BytesPerPixel;
                PixelConverter.ToBgra8(src, srcPixelOffset, target.Width, format, _pixels, dstOffset);
            }
        }

        /// <summary>Blits a run of already converted BGRA8 pixels (used by unit tests and CopyRect).</summary>
        public void WriteBgra8(int x, int y, int width, int height, byte[] src, int srcOffset)
        {
            if (src == null) throw new ArgumentNullException("src");
            Rect target = new Rect(x, y, width, height).Clip(_width, _height);
            if (target.IsEmpty) return;

            for (int row = 0; row < target.Height; row++)
            {
                int srcRow = (target.Y - y) + row;
                int srcX = target.X - x;
                int srcPixelOffset = srcOffset + (srcRow * width + srcX) * BytesPerPixel;
                int dstOffset = (target.Y + row) * Stride + target.X * BytesPerPixel;
                Buffer.BlockCopy(src, srcPixelOffset, _pixels, dstOffset, target.Width * BytesPerPixel);
            }
        }

        /// <summary>Fills a rectangle with a single BGRA8 colour.</summary>
        public void FillBgra8(int x, int y, int width, int height, byte b, byte g, byte r)
        {
            Rect target = new Rect(x, y, width, height).Clip(_width, _height);
            if (target.IsEmpty) return;

            for (int row = 0; row < target.Height; row++)
            {
                int dstOffset = (target.Y + row) * Stride + target.X * BytesPerPixel;
                for (int col = 0; col < target.Width; col++)
                {
                    PixelConverter.WriteBgra8(_pixels, dstOffset, b, g, r);
                    dstOffset += BytesPerPixel;
                }
            }
        }
        /// <summary>Fills a rectangle with one raw pixel given in <paramref name="format"/>.</summary>
        public void FillPixel(int x, int y, int width, int height, byte[] pixel, int pixelOffset, PixelFormat format)
        {
            if (pixel == null) throw new ArgumentNullException("pixel");
            byte[] bgra = new byte[4];
            PixelConverter.ToBgra8(pixel, pixelOffset, 1, format, bgra, 0);
            FillBgra8(x, y, width, height, bgra[0], bgra[1], bgra[2]);
        }

        /// <summary>Copies a region inside the framebuffer (RFB "CopyRect" pseudo encoding).</summary>
        public void CopyRect(int srcX, int srcY, int width, int height, int dstX, int dstY)
        {
            if (width <= 0 || height <= 0) return;

            Rect src = new Rect(srcX, srcY, width, height).Clip(_width, _height);
            if (src.IsEmpty) return;

            // Source and destination may overlap (scrolling), so take a snapshot first.
            int scratchLength = src.Width * src.Height * BytesPerPixel;
            if (_copyScratch.Length < scratchLength) _copyScratch = new byte[scratchLength];

            int rowBytes = src.Width * BytesPerPixel;
            for (int row = 0; row < src.Height; row++)
            {
                int srcOffset = (src.Y + row) * Stride + src.X * BytesPerPixel;
                Buffer.BlockCopy(_pixels, srcOffset, _copyScratch, row * rowBytes, rowBytes);
            }

            int targetX = dstX + (src.X - srcX);
            int targetY = dstY + (src.Y - srcY);
            WriteBgra8(targetX, targetY, src.Width, src.Height, _copyScratch, 0);
        }

        /// <summary>
        /// Copies a region into a destination buffer with an arbitrary stride.  Used by the UI
        /// layer to refresh just the damaged part of the WriteableBitmap.
        /// </summary>
        public void CopyRegionTo(Rect region, byte[] destination, int destinationOffset, int destinationStride)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            Rect source = region.Clip(_width, _height);
            if (source.IsEmpty) return;

            int byteCount = source.Width * BytesPerPixel;
            for (int row = 0; row < source.Height; row++)
            {
                int srcOffset = (source.Y + row) * Stride + source.X * BytesPerPixel;
                int dstOffset = destinationOffset + row * destinationStride + source.X * BytesPerPixel;
                if (dstOffset + byteCount > destination.Length) break;
                Buffer.BlockCopy(_pixels, srcOffset, destination, dstOffset, byteCount);
            }
        }

        /// <summary>Reads one pixel out of the framebuffer (test helper).</summary>
        public void GetBgra8(int x, int y, out byte b, out byte g, out byte r, out byte a)
        {
            int offset = y * Stride + x * BytesPerPixel;
            b = _pixels[offset + 0];
            g = _pixels[offset + 1];
            r = _pixels[offset + 2];
            a = _pixels[offset + 3];
        }
    }
}
