using System;

namespace SurfaceVnc.Core
{
    /// <summary>A simple integer rectangle used for framebuffer damage tracking.</summary>
    public struct Rect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public Rect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool IsEmpty
        {
            get { return Width <= 0 || Height <= 0; }
        }

        public static Rect Empty
        {
            get { return new Rect(0, 0, 0, 0); }
        }

        /// <summary>Smallest rectangle containing both inputs (ignores empty rectangles).</summary>
        public Rect Union(Rect other)
        {
            if (IsEmpty) return other;
            if (other.IsEmpty) return this;
            int x1 = Math.Min(X, other.X);
            int y1 = Math.Min(Y, other.Y);
            int x2 = Math.Max(X + Width, other.X + other.Width);
            int y2 = Math.Max(Y + Height, other.Y + other.Height);
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        /// <summary>Clips this rectangle to the given width/height (assumes origin 0,0).</summary>
        public Rect Clip(int width, int height)
        {
            int x1 = Math.Max(0, X);
            int y1 = Math.Max(0, Y);
            int x2 = Math.Min(width, X + Width);
            int y2 = Math.Min(height, Y + Height);
            if (x2 <= x1 || y2 <= y1) return Empty;
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        public override string ToString()
        {
            return string.Format("{0},{1} {2}x{3}", X, Y, Width, Height);
        }
    }
}
