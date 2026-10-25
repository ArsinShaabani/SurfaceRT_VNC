using System;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Viewport mapping tests.</summary>
    public static class ViewportTests
    {
        public static void Register()
        {
            TestRunner.Add("Fit mode maps touches onto the whole remote desktop", FitMode);
            TestRunner.Add("1:1 mode pans the viewport", ActualSizeMode);
            TestRunner.Add("Stretch mode fills the screen", StretchMode);
            TestRunner.Add("Fit mode never upscales beyond the limit", UpscaleLimit);
            TestRunner.Add("Touches outside the desktop are rejected", OutsideDesktop);
        }

        private static void FitMode()
        {
            ViewportMapper mapper = new ViewportMapper();
            mapper.Update(1920, 1080, 1366, 768);

            Check.Equal((int)ViewportScaleMode.Fit, (int)mapper.Mode, "default mode is Fit");
            Check.Equal(768.0 / 1080.0, mapper.ScaleX, 0.0001, "fit scale is the smaller of the two ratios");

            int x, y;
            Check.True(mapper.TryMapToRemote(683, 384, out x, out y), "the middle of the screen must map");
            Check.Equal(960, x, "middle x");
            Check.Equal(540, y, "middle y");

            // The whole desktop is visible, so it is centred.
            Check.Equal(0.0, mapper.OffsetX, 0.4, "horizontal centring");
            Check.Equal(0.0, mapper.OffsetY, 0.4, "vertical centring");
        }

        private static void ActualSizeMode()
        {
            ViewportMapper mapper = new ViewportMapper();
            mapper.SetMode(ViewportScaleMode.ActualSize);
            mapper.Update(1920, 1080, 1366, 768);

            Check.Equal(1.0, mapper.ScaleX, 0.0001, "1:1 scale");
            Check.True(mapper.CanPan, "a 1920x1080 desktop cannot fit in 1366x768 at 1:1");

            int x, y;
            Check.True(mapper.TryMapToRemote(0, 0, out x, out y), "top left must map");
            Check.Equal(0, x, "top left x");

            mapper.PanBy(-100, -50);
            Check.True(mapper.TryMapToRemote(0, 0, out x, out y), "after panning, the view origin must map");
            Check.Equal(100, x, "panned x");
            Check.Equal(50, y, "panned y");

            // Panning must be clamped so the desktop never leaves a gap at the top left.
            mapper.PanBy(500, 500);
            Check.Equal(0.0, mapper.OffsetX, 0.0001, "pan clamped at the top left");
        }

        private static void StretchMode()
        {
            ViewportMapper mapper = new ViewportMapper();
            mapper.SetMode(ViewportScaleMode.Stretch);
            mapper.Update(1920, 1080, 1366, 768);

            Check.Equal(1366.0 / 1920.0, mapper.ScaleX, 0.0001, "stretch scale x");
            Check.Equal(768.0 / 1080.0, mapper.ScaleY, 0.0001, "stretch scale y");

            int x, y;
            Check.True(mapper.TryMapToRemote(683, 384, out x, out y), "the middle must map");
            Check.Equal(960, x, "stretched middle x");
            Check.Equal(540, y, "stretched middle y");

            Rect visible = mapper.VisibleRemoteRect;
            Check.Equal(1920, visible.Width, "the whole width is visible");
            Check.Equal(1080, visible.Height, "the whole height is visible");
        }

        private static void UpscaleLimit()
        {
            ViewportMapper mapper = new ViewportMapper();
            mapper.Update(100, 100, 1000, 1000);
            Check.Equal(4.0, mapper.ScaleX, 0.0001, "upscale must be capped at MaxUpscale");
        }

        private static void OutsideDesktop()
        {
            ViewportMapper mapper = new ViewportMapper();
            mapper.SetMode(ViewportScaleMode.ActualSize);
            mapper.Update(100, 100, 500, 500);

            int x, y;
            Check.False(mapper.TryMapToRemote(600, 10, out x, out y), "a touch right of the desktop must be rejected");
            Check.False(mapper.TryMapToRemote(10, 600, out x, out y), "a touch below the desktop must be rejected");
            Check.True(mapper.TryMapToRemote(250, 250, out x, out y), "the centred desktop must accept touches");
            Check.Equal(50, x, "the centre of the view is the centre of the 100x100 desktop");
            Check.Equal(50, y, "the centre of the view is the centre of the 100x100 desktop");

            // The desktop is centred, so the view edges just outside it must be rejected too.
            Check.False(mapper.TryMapToRemote(199, 250, out x, out y), "just left of the desktop");
            Check.True(mapper.TryMapToRemote(200, 250, out x, out y), "the first desktop column");
            Check.Equal(0, x, "first column");
        }
    }
}
