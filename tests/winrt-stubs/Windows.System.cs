using System;
using System.Collections.Generic;

namespace Windows.UI.Core
{
    public enum CoreDispatcherPriority { Low, Normal, High }

    public sealed class CoreDispatcher
    {
        public Windows.Foundation.IAsyncAction RunAsync(CoreDispatcherPriority priority, Action handler) { return null; }
        public bool HasThreadAccess { get { return true; } }
    }

    public sealed class CharacterReceivedEventArgs : EventArgs
    {
        public uint KeyCode { get { return 0; } }
        public bool Handled { get; set; }
    }

    public sealed class KeyEventArgs : EventArgs
    {
        public Windows.System.VirtualKey VirtualKey { get { return Windows.System.VirtualKey.None; } }
        public bool Handled { get; set; }
    }

    public sealed class CoreWindow
    {
        public static CoreWindow GetForCurrentThread() { return null; }
        public event Windows.Foundation.TypedEventHandler<CoreWindow, CharacterReceivedEventArgs> CharacterReceived;
        public event Windows.Foundation.TypedEventHandler<CoreWindow, KeyEventArgs> KeyDown;
        public event Windows.Foundation.TypedEventHandler<CoreWindow, KeyEventArgs> KeyUp;
    }
}

namespace Windows.System
{
    public enum VirtualKey
    {
        None = 0,
        Back = 8,
        Tab = 9,
        Enter = 13,
        Shift = 16,
        Control = 17,
        Escape = 27,
        Space = 32,
        Left = 37,
        Up = 38,
        Right = 39,
        Down = 40,
        Delete = 46,
        F1 = 112,
        F5 = 116,
        F11 = 122
    }
}

namespace Windows.System.Display
{
    public sealed class DisplayRequest
    {
        public void RequestActive() { }
        public void RequestRelease() { }
    }
}

namespace Windows.ApplicationModel
{
    public sealed class SuspendingEventArgs : EventArgs
    {
        public SuspendingOperation SuspendingOperation { get { return null; } }
    }

    public sealed class SuspendingOperation
    {
        public SuspendingDeferral GetDeferral() { return null; }
    }

    public sealed class SuspendingDeferral
    {
        public void Complete() { }
    }

}

namespace Windows.ApplicationModel.Activation
{
    public sealed class LaunchActivatedEventArgs : EventArgs
    {
        public string Arguments { get { return string.Empty; } }
    }
}

namespace Windows.ApplicationModel.DataTransfer
{
    public sealed class DataPackage
    {
        public void SetText(string value) { }
    }

    public sealed class DataPackageView
    {
        public bool Contains(string formatId) { return false; }
        public global::System.Threading.Tasks.Task<string> GetTextAsync() { return null; }
    }

    public static class StandardDataFormats
    {
        public static string Text { get { return "Text"; } }
    }

    public static class Clipboard
    {
        public static void SetContent(DataPackage content) { }
        public static DataPackageView GetContent() { return null; }
    }
}

namespace Windows.Storage
{
    public sealed class ApplicationDataContainer
    {
        public IDictionary<string, object> Values { get { return null; } }
    }

    public sealed class ApplicationData
    {
        public static ApplicationData Current { get { return null; } }
        public ApplicationDataContainer LocalSettings { get { return null; } }
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class ApplicationView
    {
        public static ApplicationView GetForCurrentView() { return null; }
        public bool IsFullScreenMode { get { return false; } }
        public bool TryEnterFullScreenMode() { return true; }
        public void ExitFullScreenMode() { }
    }
}

namespace Windows.Networking
{
    public sealed class HostName
    {
        public HostName(string hostName) { }
        public string CanonicalName { get { return string.Empty; } }
    }
}

namespace Windows.Networking.Sockets
{
    public sealed class StreamSocketControl
    {
        public bool NoDelay { get; set; }
    }

    public sealed class StreamSocket : IDisposable
    {
        public StreamSocketControl Control { get { return null; } }
        public object InputStream { get { return null; } }
        public object OutputStream { get { return null; } }
        public global::System.Threading.Tasks.Task ConnectAsync(Windows.Networking.HostName hostName, string serviceName) { return null; }
        public void Dispose() { }
    }
}
