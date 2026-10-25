using System;
using System.Collections.Generic;

namespace Windows.UI.Xaml
{
    public class DependencyObject
    {
        public Windows.UI.Core.CoreDispatcher Dispatcher { get { return null; } }
        public void ClearValue(DependencyProperty property) { }
    }

    public class DependencyProperty
    {
    }

    public class RoutedEventArgs : EventArgs
    {
        public object OriginalSource { get; set; }
    }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

    public class SizeChangedEventArgs : RoutedEventArgs
    {
        public Windows.Foundation.Size NewSize { get; set; }
        public Windows.Foundation.Size PreviousSize { get; set; }
    }

    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);

    public enum Visibility { Visible, Collapsed }

    public enum FocusState { Unfocused, Pointer, Keyboard, Programmatic }

    public enum TextWrapping { NoWrap, Wrap, WrapWholeWords }

    public enum HorizontalAlignment { Left, Center, Right, Stretch }

    public enum VerticalAlignment { Top, Center, Bottom, Stretch }

    public struct GridLength
    {
        public GridLength(double value) { Value = value; }
        public double Value;
        public static GridLength Auto { get { return new GridLength(); } }
    }

    public class Thickness
    {
        public Thickness(double uniform) { }
        public Thickness(double left, double top, double right, double bottom) { }
    }

    public struct CornerRadius
    {
        public CornerRadius(double uniform) { }
    }

    public class Style
    {
    }

    public class Geometry
    {
    }

    public class RectangleGeometry : Geometry
    {
        public Windows.Foundation.Rect Rect { get; set; }
    }

    public class UIElement : DependencyObject
    {
        public Visibility Visibility { get; set; }
        public Geometry Clip { get; set; }
        public bool IsHitTestVisible { get; set; }
        public bool CapturePointer(object pointer) { return true; }
        public void ReleasePointerCapture(object pointer) { }
    }

    public class FrameworkElement : UIElement
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double ActualWidth { get { return 0; } }
        public double ActualHeight { get { return 0; } }
        public string Name { get; set; }
        public object Tag { get; set; }
        public Style Style { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public bool IsEnabled { get; set; }
        public event SizeChangedEventHandler SizeChanged;
        public event RoutedEventHandler Loaded;
        public event RoutedEventHandler Unloaded;
    }

    public class Application
    {
        public static Application Current { get { return null; } }
        public event EventHandler<Windows.ApplicationModel.SuspendingEventArgs> Suspending;
        protected virtual void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args) { }
        public void Exit() { }
    }

    public sealed class Window
    {
        public static Window Current { get { return null; } }
        public object Content { get; set; }
        public Windows.UI.Core.CoreWindow CoreWindow { get { return null; } }
        public void Activate() { }
    }

    public sealed class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler<object> Tick;
        public void Start() { }
        public void Stop() { }
    }
}

namespace Windows.UI.Xaml.Media
{
    public class Brush
    {
    }

    public class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Windows.UI.Color color) { }
        public Windows.UI.Color Color { get; set; }
    }

    public class ImageSource
    {
    }
}

namespace Windows.UI.Xaml.Media.Imaging
{
    public class WriteableBitmap : Windows.UI.Xaml.Media.ImageSource
    {
        public WriteableBitmap(int pixelWidth, int pixelHeight) { }
        public Windows.Storage.Streams.IBuffer PixelBuffer { get { return null; } }
        public int PixelWidth { get { return 0; } }
        public int PixelHeight { get { return 0; } }
        public void Invalidate() { }
    }

    public class BitmapImage : Windows.UI.Xaml.Media.ImageSource
    {
    }
}

namespace Windows.UI.Xaml.Input
{
    public class Pointer
    {
        public uint PointerId { get { return 0; } }
    }

    public class PointerPoint
    {
        public Windows.Foundation.Point Position { get { return new Windows.Foundation.Point(); } }
    }

    public class PointerRoutedEventArgs : Windows.UI.Xaml.RoutedEventArgs
    {
        public Pointer Pointer { get { return null; } }
        public PointerPoint GetCurrentPoint(object relativeTo) { return null; }
        public bool Handled { get; set; }
    }
}

namespace Windows.UI.Xaml
{
    public class Frame : Windows.UI.Xaml.Controls.ContentControl
    {
        public bool Navigate(Type sourcePageType, object parameter) { return true; }
    }
}
