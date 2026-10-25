using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using SurfaceVnc.Core;
using Windows.UI.Xaml.Media.Imaging;

namespace SurfaceVnc.Store
{
    /// <summary>
    /// Pushes the decoded framebuffer into a <see cref="WriteableBitmap"/>.
    ///
    /// The framebuffer already stores BGRA8 with an opaque alpha, which is exactly the layout of
    /// a WriteableBitmap pixel buffer, so refreshing a region is a plain row copy.  All methods
    /// here must be called on the UI thread.
    /// </summary>
    public sealed class FramebufferRenderer
    {
        private WriteableBitmap _bitmap;
        private Framebuffer _framebuffer;
        private int _width;
        private int _height;
        private byte[] _scratch = new byte[0];
        private bool _seekSupported = true;

        /// <summary>The bitmap to assign to the Image control (may be null before the first frame).</summary>
        public WriteableBitmap Bitmap { get { return _bitmap; } }

        /// <summary>True once a bitmap of the remote size exists.</summary>
        public bool HasBitmap { get { return _bitmap != null; } }

        /// <summary>Called once the client is connected; the framebuffer may still be empty.</summary>
        public void Attach(Framebuffer framebuffer)
        {
            _framebuffer = framebuffer;
            _bitmap = null;
            _width = 0;
            _height = 0;
        }

        /// <summary>Creates (or recreates) the bitmap when the remote desktop size changed.</summary>
        public bool EnsureSize()
        {
            if (_framebuffer == null || _framebuffer.IsEmpty) return false;
            if (_bitmap != null && _width == _framebuffer.Width && _height == _framebuffer.Height) return false;

            _width = _framebuffer.Width;
            _height = _framebuffer.Height;
            _bitmap = new WriteableBitmap(_width, _height);
            return true;
        }

        /// <summary>Repaints the whole bitmap from the framebuffer.</summary>
        public void RenderAll()
        {
            Render(new Rect(0, 0, _framebuffer == null ? 0 : _framebuffer.Width, _framebuffer == null ? 0 : _framebuffer.Height));
        }

        /// <summary>
        /// Repaints <paramref name="region"/> (in remote coordinates).  Returns false when the
        /// framebuffer was busy and the caller should try again on the next update.
        /// </summary>
        public bool Render(Rect region)
        {
            if (_framebuffer == null) return false;
            EnsureSize();
            if (_bitmap == null) return false;

            Rect clipped = region.Clip(_width, _height);
            if (clipped.IsEmpty) return false;

            // The network thread holds this gate while it decodes a whole update message, so
            // waiting here guarantees we never paint a torn frame.  A short timeout keeps the UI
            // responsive if a very large frame is still being decoded.
            if (!_framebuffer.Gate.Wait(250)) return false;
            try
            {
                if (_seekSupported)
                {
                    try
                    {
                        WriteRegion(clipped);
                    }
                    catch (NotSupportedException)
                    {
                        // This SDK build returns a write-only stream over the pixel buffer; fall
                        // back to pushing the whole frame from offset zero on every update.
                        _seekSupported = false;
                    }
                }

                if (!_seekSupported) WriteWholeFrame();
            }
            finally
            {
                _framebuffer.Gate.Release();
            }

            Refresh();
            return true;
        }

        /// <summary>Writes just the damaged rows, seeking inside the pixel buffer.</summary>
        private void WriteRegion(Rect clipped)
        {
            int stride = _width * 4;
            using (Stream stream = _bitmap.PixelBuffer.AsStream())
            {
                if (clipped.Width == _width)
                {
                    // Whole rows are contiguous in both buffers: one write does the job.
                    int first = clipped.Y * stride;
                    stream.Seek(first, SeekOrigin.Begin);
                    stream.Write(_framebuffer.Pixels, first, clipped.Height * stride);
                }
                else
                {
                    int rowBytes = clipped.Width * 4;
                    int required = rowBytes * clipped.Height;
                    if (_scratch.Length < required) _scratch = new byte[required];

                    _framebuffer.CopyRegionTo(clipped, _scratch, 0, rowBytes);
                    for (int row = 0; row < clipped.Height; row++)
                    {
                        stream.Seek((clipped.Y + row) * stride + clipped.X * 4, SeekOrigin.Begin);
                        stream.Write(_scratch, row * rowBytes, rowBytes);
                    }
                }
                stream.Flush();
            }
        }

        /// <summary>Writes the complete framebuffer from offset zero (no seeking needed).</summary>
        private void WriteWholeFrame()
        {
            byte[] pixels = _framebuffer.Pixels;
            using (Stream stream = _bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
                stream.Flush();
            }
        }

        /// <summary>
        /// Asks the bitmap to repaint.  Some Windows 8.1 SDK versions expose
        /// <c>WriteableBitmap.Invalidate()</c> and some do not, so it is called through
        /// reflection: writing to the pixel buffer is enough when the method is absent.
        /// </summary>
        private void Refresh()
        {
            if (_bitmap == null) return;
            try
            {
                if (_invalidate == null) return;
                _invalidate.Invoke(_bitmap, null);
            }
            catch (Exception)
            {
                // Never let a cosmetic refresh failure break the session.
            }
        }

        private static readonly System.Reflection.MethodInfo _invalidate =
            typeof(WriteableBitmap).GetMethod("Invalidate", Type.EmptyTypes);
    }
}
