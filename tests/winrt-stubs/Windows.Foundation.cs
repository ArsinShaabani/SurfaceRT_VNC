// ---------------------------------------------------------------------------------------------
// Compile time model of the small part of the WinRT API surface that SurfaceVnc.Store uses.
//
// The real Windows Store app for Windows RT 8.1 can only be compiled on Windows with the
// Windows 8.1 SDK.  These stubs let the *same source files* be compiled anywhere (dotnet build),
// which catches typos, wrong argument counts, missing members and C# language level mistakes in
// the app layer.  They are deliberately not a complete projection of WinRT: they only have to be
// faithful enough for the app sources to compile and for mistakes in them to fail.
//
// Nothing here is ever executed.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Windows.Foundation
{
    public struct Point
    {
        public Point(double x, double y) { X = x; Y = y; }
        public double X;
        public double Y;
    }

    public struct Size
    {
        public Size(double width, double height) { Width = width; Height = height; }
        public double Width;
        public double Height;
    }

    public struct Rect
    {
        public Rect(double x, double y, double width, double height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args);

    public interface IAsyncAction { }
    public interface IAsyncOperation<T> { }
}

namespace Windows.Storage.Streams
{
    public interface IBuffer
    {
        uint Length { get; }
    }
}

namespace System.IO
{
    /// <summary>System.IO.WindowsRuntimeStreamExtensions (the WinRT stream adapters).</summary>
    public static class WindowsRuntimeStreamExtensions
    {
        public static Stream AsStreamForRead(this object inputStream) { return null; }
        public static Stream AsStreamForWrite(this object outputStream) { return null; }
    }
}

namespace System.Runtime.InteropServices.WindowsRuntime
{
    /// <summary>System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.</summary>
    public static class WindowsRuntimeBufferExtensions
    {
        public static Stream AsStream(this global::Windows.Storage.Streams.IBuffer buffer) { return null; }
    }
}

namespace Windows.UI
{
    public struct Color
    {
    }

    public static class Colors
    {
        public static Color Orange { get { return new Color(); } }
    }
}
