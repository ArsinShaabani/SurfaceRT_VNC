using System;

namespace SurfaceVnc.Core
{
    /// <summary>How the remote desktop is mapped onto the Surface screen.</summary>
    public enum ViewportScaleMode
    {
        /// <summary>Scale down (or up to <see cref="ViewportMapper.MaxUpscale"/>) so everything fits.</summary>
        Fit = 0,

        /// <summary>1:1 pixels; the view can be panned with two fingers.</summary>
        ActualSize = 1,

        /// <summary>Fill the screen, possibly with different horizontal/vertical scaling.</summary>
        Stretch = 2
    }

    /// <summary>
    /// Converts between Surface touch/pointer coordinates and remote desktop pixels.
    ///
    /// The Surface RT screen is 1366x768 while the remote desktop is often 1920x1080, so this
    /// mapping is what makes "touch the spot you want to click" work.
    /// </summary>
    public sealed class ViewportMapper
    {
        public ViewportMapper()
        {
            Mode = ViewportScaleMode.Fit;
            MaxUpscale = 4.0;
            ScaleX = 1.0;
            ScaleY = 1.0;
        }

        public ViewportScaleMode Mode { get; private set; }

        /// <summary>Upper bound for upscaling in <see cref="ViewportScaleMode.Fit"/>.</summary>
        public double MaxUpscale { get; set; }

        public double ScaleX { get; private set; }
        public double ScaleY { get; private set; }

        /// <summary>Position of the remote desktop's top-left corner inside the view.</summary>
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }

        public int RemoteWidth { get; private set; }
        public int RemoteHeight { get; private set; }
        public double ViewWidth { get; private set; }
        public double ViewHeight { get; private set; }

        /// <summary>True when the scaled desktop is larger than the view (panning makes sense).</summary>
        public bool CanPan
        {
            get { return RemoteWidth * ScaleX > ViewWidth + 0.5 || RemoteHeight * ScaleY > ViewHeight + 0.5; }
        }

        public void SetMode(ViewportScaleMode mode)
        {
            Mode = mode;
            Update(RemoteWidth, RemoteHeight, ViewWidth, ViewHeight);
        }

        /// <summary>Recomputes scale and offsets after a size change.</summary>
        public void Update(int remoteWidth, int remoteHeight, double viewWidth, double viewHeight)
        {
            RemoteWidth = remoteWidth < 0 ? 0 : remoteWidth;
            RemoteHeight = remoteHeight < 0 ? 0 : remoteHeight;
            ViewWidth = viewWidth < 0 ? 0 : viewWidth;
            ViewHeight = viewHeight < 0 ? 0 : viewHeight;

            if (RemoteWidth == 0 || RemoteHeight == 0 || ViewWidth <= 0 || ViewHeight <= 0)
            {
                ScaleX = 1.0;
                ScaleY = 1.0;
                OffsetX = 0;
                OffsetY = 0;
                return;
            }

            switch (Mode)
            {
                case ViewportScaleMode.Stretch:
                    ScaleX = ViewWidth / RemoteWidth;
                    ScaleY = ViewHeight / RemoteHeight;
                    break;

                case ViewportScaleMode.ActualSize:
                    ScaleX = 1.0;
                    ScaleY = 1.0;
                    break;

                default:
                    double scale = Math.Min(ViewWidth / RemoteWidth, ViewHeight / RemoteHeight);
                    if (scale > MaxUpscale) scale = MaxUpscale;
                    if (scale <= 0) scale = 1.0;
                    ScaleX = scale;
                    ScaleY = scale;
                    break;
            }

            ClampOffsets(OffsetX, OffsetY);
        }

        /// <summary>Moves the viewport by the given amount (touch panning) and clamps it.</summary>
        public void PanBy(double deltaX, double deltaY)
        {
            ClampOffsets(OffsetX + deltaX, OffsetY + deltaY);
        }

        public void Center()
        {
            ClampOffsets(0, 0);
        }

        private void ClampOffsets(double offsetX, double offsetY)
        {
            double contentWidth = RemoteWidth * ScaleX;
            double contentHeight = RemoteHeight * ScaleY;

            if (contentWidth <= ViewWidth) OffsetX = (ViewWidth - contentWidth) / 2.0;
            else OffsetX = Math.Min(0.0, Math.Max(ViewWidth - contentWidth, offsetX));

            if (contentHeight <= ViewHeight) OffsetY = (ViewHeight - contentHeight) / 2.0;
            else OffsetY = Math.Min(0.0, Math.Max(ViewHeight - contentHeight, offsetY));
        }

        /// <summary>
        /// Maps a point in view coordinates to remote pixels.  Returns false when the point is
        /// outside the remote desktop (the caller should ignore such touches).
        /// </summary>
        public bool TryMapToRemote(double viewX, double viewY, out int remoteX, out int remoteY)
        {
            remoteX = 0;
            remoteY = 0;
            if (RemoteWidth == 0 || RemoteHeight == 0) return false;
            if (ScaleX <= 0 || ScaleY <= 0) return false;

            double x = (viewX - OffsetX) / ScaleX;
            double y = (viewY - OffsetY) / ScaleY;

            if (x < 0 || y < 0 || x > RemoteWidth || y > RemoteHeight) return false;

            remoteX = (int)Math.Floor(x);
            remoteY = (int)Math.Floor(y);
            if (remoteX >= RemoteWidth) remoteX = RemoteWidth - 1;
            if (remoteY >= RemoteHeight) remoteY = RemoteHeight - 1;
            if (remoteX < 0) remoteX = 0;
            if (remoteY < 0) remoteY = 0;
            return true;
        }

        /// <summary>Maps a remote pixel back into view coordinates.</summary>
        public void MapToView(int remoteX, int remoteY, out double viewX, out double viewY)
        {
            viewX = OffsetX + remoteX * ScaleX;
            viewY = OffsetY + remoteY * ScaleY;
        }

        /// <summary>The part of the remote desktop that is currently visible.</summary>
        public Rect VisibleRemoteRect
        {
            get
            {
                if (RemoteWidth == 0 || RemoteHeight == 0 || ScaleX <= 0 || ScaleY <= 0) return Rect.Empty;

                int x1 = (int)Math.Floor(-OffsetX / ScaleX);
                int y1 = (int)Math.Floor(-OffsetY / ScaleY);
                int x2 = (int)Math.Ceiling((ViewWidth - OffsetX) / ScaleX);
                int y2 = (int)Math.Ceiling((ViewHeight - OffsetY) / ScaleY);
                return new Rect(x1, y1, x2 - x1, y2 - y1).Clip(RemoteWidth, RemoteHeight);
            }
        }
    }
}
