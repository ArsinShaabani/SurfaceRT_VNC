using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Point = Windows.Foundation.Point;
using Rect = SurfaceVnc.Core.Rect;

namespace SurfaceVnc.Store
{
    /// <summary>
    /// The whole UI of the app: connection settings, the remote desktop view and all the touch
    /// gestures that turn the tablet into a remote desktop terminal.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const double WheelNotchDistance = 40.0;
        private const double LongPressMilliseconds = 600.0;
        private const double DragTolerancePixels = 16.0;

        private readonly ViewportMapper _mapper = new ViewportMapper();
        private readonly FramebufferRenderer _renderer = new FramebufferRenderer();
        private readonly Dictionary<uint, Point> _pointers = new Dictionary<uint, Point>();
        private readonly Dictionary<uint, Button> _activeModifiers = new Dictionary<uint, Button>();

        private readonly object _pendingSync = new object();
        private Rect _pendingRegion = Rect.Empty;
        private bool _renderScheduled;

        private StreamSocketTransport _transport;
        private RfbClient _client;
        private CancellationTokenSource _cancellation;
        private RemoteKeyboardBridge _keyboard;
        private DisplayRequest _displayRequest;

        private bool _connected;
        private bool _leftButtonDown;
        private int _lastRemoteX;
        private int _lastRemoteY;

        private DispatcherTimer _longPressTimer;
        private Point _pressPosition;
        private bool _hasCentroid;
        private Point _lastCentroid;
        private double _wheelAccumulator;

        public MainPage()
        {
            InitializeComponent();

            // -1 means "the user has not touched the remote desktop yet", which lets the toolbar
            // buttons fall back to clicking in the middle of the screen.
            _lastRemoteX = -1;
            _lastRemoteY = -1;

            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            Application.Current.Suspending += OnSuspending;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            HostBox.Text = ConnectionSettingsStore.LoadHost();
            PortBox.Text = ConnectionSettingsStore.LoadPort().ToString();
            KeepAwakeCheckBox.IsChecked = ConnectionSettingsStore.LoadKeepScreenAwake();
            UpdatePlacement();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Disconnect("Closed");
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            // The socket would be torn down by the system anyway; close it cleanly so the VNC
            // server notices immediately and the session is not left half open.
            Disconnect("Suspended");
        }

        // ---------------------------------------------------------------------------------
        // Connect / disconnect
        // ---------------------------------------------------------------------------------

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_connected)
            {
                Disconnect("Disconnected");
                return;
            }

            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            string host = HostBox.Text == null ? string.Empty : HostBox.Text.Trim();
            int port = ConnectionSettings.DefaultPort;
            int parsedPort;
            if (int.TryParse(PortBox.Text == null ? string.Empty : PortBox.Text.Trim(), out parsedPort)
                && parsedPort > 0 && parsedPort < 65536)
            {
                port = parsedPort;
            }

            if (host.Length == 0)
            {
                SettingsErrorText.Text = "Enter the address of the PC that runs the VNC server.";
                return;
            }

            SettingsErrorText.Text = string.Empty;
            StatusText.Text = "Connecting to " + host + ":" + port + " ...";
            ConnectButton.IsEnabled = false;

            try
            {
                _transport = await StreamSocketTransport.ConnectAsync(host, port, CancellationToken.None);

                _client = new RfbClient(_transport);
                _client.FramebufferUpdated += OnFramebufferUpdated;
                _client.ClipboardReceived += OnClipboardReceived;
                _client.DesktopSizeChanged += OnDesktopSizeChanged;
                _client.Bell += OnBell;

                ConnectionSettings settings = ConnectionSettings.CreateDefault(host, port, PasswordBox.Password);
                settings.ViewOnly = ViewOnlyCheckBox.IsChecked == true;
                await _client.ConnectAsync(settings, CancellationToken.None);

                _cancellation = new CancellationTokenSource();
                RfbClient client = _client;
                CancellationToken token = _cancellation.Token;
                // The message loop runs on a background thread; it reports failures through
                // OnSessionEnded (which marshals back to the UI thread).
                Task messageLoop = Task.Run(async () =>
                {
                    try
                    {
                        await client.RunAsync(token);
                    }
                    catch (Exception error)
                    {
                        OnSessionEnded(error);
                    }
                });
                GC.KeepAlive(messageLoop);

                _renderer.Attach(_client.Framebuffer);
                RemoteImage.Source = null;
                _keyboard = new RemoteKeyboardBridge(_client, KeyboardProxy, Window.Current.CoreWindow);

                _connected = true;
                ConnectionSettingsStore.Save(host, port);
                SettingsPanel.Visibility = Visibility.Collapsed;
                ConnectButton.Content = "Disconnect";
                UpdatePlacement();
                RequestKeepAwake();
                UpdateStatus();
            }
            catch (Exception error)
            {
                SettingsErrorText.Text = DescribeError(error);
                SettingsPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Connection failed";
                Disconnect("Connection failed");
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private static string DescribeError(Exception error)
        {
            if (error == null) return "Unknown error";
            if (error is RfbAuthenticationException) return "Wrong VNC password. " + error.Message;
            if (error is RfbException) return error.Message;

            string message = error.Message;
            if (string.IsNullOrEmpty(message) && error.InnerException != null) message = error.InnerException.Message;
            return "Could not connect: " + (string.IsNullOrEmpty(message) ? error.GetType().Name : message)
                + " - check that the VNC server is running and allows connections from this device.";
        }

        private void Disconnect(string reason)
        {
            if (_keyboard != null)
            {
                _keyboard.Dispose();
                _keyboard = null;
            }

            if (_client != null)
            {
                _client.FramebufferUpdated -= OnFramebufferUpdated;
                _client.ClipboardReceived -= OnClipboardReceived;
                _client.DesktopSizeChanged -= OnDesktopSizeChanged;
                _client.Bell -= OnBell;
            }

            if (_cancellation != null)
            {
                try { _cancellation.Cancel(); }
                catch (Exception) { }
                _cancellation = null;
            }

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            _transport = null;
            _leftButtonDown = false;
            _pointers.Clear();
            _hasCentroid = false;
            CancelLongPress();

            if (_connected)
            {
                _connected = false;
                SettingsPanel.Visibility = Visibility.Visible;
                ConnectButton.Content = "Connect";
                StatusText.Text = reason;
            }

            ReleaseKeepAwake();
        }

        private void RequestKeepAwake()
        {
            if (KeepAwakeCheckBox.IsChecked != true) return;
            try
            {
                if (_displayRequest == null) _displayRequest = new DisplayRequest();
                _displayRequest.RequestActive();
            }
            catch (Exception)
            {
            }
        }

        private void ReleaseKeepAwake()
        {
            try
            {
                if (_displayRequest != null) _displayRequest.RequestRelease();
            }
            catch (Exception)
            {
            }
        }

        private void UpdateStatus()
        {
            if (_client == null) return;
            string name = string.IsNullOrEmpty(_client.ServerName) ? "VNC server" : _client.ServerName;
            StatusText.Text = name + " - " + _client.RemoteWidth + "x" + _client.RemoteHeight
                + " (" + (_mapper.Mode == ViewportScaleMode.Fit ? "fit" : "1:1") + ")";
        }

        // ---------------------------------------------------------------------------------
        // Server events (raised on the network thread)
        // ---------------------------------------------------------------------------------

        private void OnFramebufferUpdated(object sender, FramebufferUpdateEventArgs e)
        {
            QueueRender(e.Region);
        }

        private void OnClipboardReceived(object sender, string text)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    DataPackage package = new DataPackage();
                    package.SetText(text == null ? string.Empty : text);
                    Clipboard.SetContent(package);
                    StatusText.Text = "Clipboard received from the PC";
                }
                catch (Exception)
                {
                    // The clipboard is only available while the app is in the foreground.
                }
            });
        }

        private void OnDesktopSizeChanged(object sender, DesktopSizeEventArgs e)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                RemoteImage.Source = null;
                UpdatePlacement();
                UpdateStatus();
            });
        }

        private void OnBell(object sender, EventArgs e)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusText.Text = "Bell from the PC");
        }

        private void OnSessionEnded(Exception error)
        {
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_connected) return;
                string reason = error is RfbConnectionClosedException
                    ? "The PC closed the connection"
                    : "Session ended: " + (error == null ? "unknown error" : error.Message);
                Disconnect(reason);
                SettingsErrorText.Text = reason;
            });
        }

        // ---------------------------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Coalesces the damage regions of a burst of updates into a single repaint, so a busy
        /// remote desktop cannot flood the UI thread with render requests.
        /// </summary>
        private void QueueRender(Rect region)
        {
            lock (_pendingSync)
            {
                _pendingRegion = _pendingRegion.Union(region);
                if (_renderScheduled) return;
                _renderScheduled = true;
            }

            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RenderPending);
        }

        private void RenderPending()
        {
            Rect region;
            lock (_pendingSync)
            {
                region = _pendingRegion;
                _pendingRegion = Rect.Empty;
                _renderScheduled = false;
            }

            RenderRegion(region);
        }

        private void RenderRegion(Rect region)
        {
            if (_renderer.EnsureSize())
            {
                RemoteImage.Source = _renderer.Bitmap;
                UpdatePlacement();
            }

            if (!_renderer.HasBitmap) return;
            if (_renderer.Render(region)) return;

            // The framebuffer was busy decoding; try again shortly instead of dropping the frame.
            RetryRenderAsync(region);
        }

        private async void RetryRenderAsync(Rect region)
        {
            await Task.Delay(30);
            QueueRender(region);
        }

        // ---------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // A Grid does not clip its children, so panning the 1:1 view needs an explicit clip.
            Viewport.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, Viewport.ActualWidth, Viewport.ActualHeight)
            };

            UpdatePlacement();

            if (_client != null && _renderer.HasBitmap)
                QueueRender(new Rect(0, 0, _client.Framebuffer.Width, _client.Framebuffer.Height));
        }

        private void UpdatePlacement()
        {
            if (_client == null) return;

            int remoteWidth = _client.Framebuffer.Width;
            int remoteHeight = _client.Framebuffer.Height;
            if (remoteWidth <= 0 || remoteHeight <= 0) return;

            _mapper.Update(remoteWidth, remoteHeight, Viewport.ActualWidth, Viewport.ActualHeight);

            RemoteImage.Width = remoteWidth * _mapper.ScaleX;
            RemoteImage.Height = remoteHeight * _mapper.ScaleY;
            Canvas.SetLeft(RemoteImage, _mapper.OffsetX);
            Canvas.SetTop(RemoteImage, _mapper.OffsetY);
        }

        // ---------------------------------------------------------------------------------
        // Touch / mouse input
        //
        //   one finger   -> left mouse button (tap = click, drag = drag)
        //   long press   -> right click
        //   two fingers  -> scroll wheel, or pan the view when the desktop is larger than the
        //                   screen (1:1 mode)
        // ---------------------------------------------------------------------------------

        private void RemoteCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_connected) return;

            Point position = e.GetCurrentPoint(RemoteCanvas).Position;
            _pointers[e.Pointer.PointerId] = position;
            RemoteCanvas.CapturePointer(e.Pointer);

            if (_pointers.Count == 1)
            {
                _pressPosition = position;
                _hasCentroid = false;
                _wheelAccumulator = 0;

                int remoteX, remoteY;
                if (_mapper.TryMapToRemote(position.X, position.Y, out remoteX, out remoteY))
                {
                    _lastRemoteX = remoteX;
                    _lastRemoteY = remoteY;
                    SendPointer(RfbProtocol.ButtonLeft, remoteX, remoteY);
                    _leftButtonDown = true;
                    StartLongPressTimer();
                }
            }
            else
            {
                // A second finger turns the gesture into scrolling or panning: drop the click.
                CancelLongPress();
                if (_leftButtonDown)
                {
                    SendPointer(0, _lastRemoteX, _lastRemoteY);
                    _leftButtonDown = false;
                }

                Point centroid;
                if (TryGetCentroid(out centroid))
                {
                    _lastCentroid = centroid;
                    _hasCentroid = true;
                }
                _wheelAccumulator = 0;
            }
        }

        private void RemoteCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_connected) return;
            if (!_pointers.ContainsKey(e.Pointer.PointerId)) return;

            Point position = e.GetCurrentPoint(RemoteCanvas).Position;
            _pointers[e.Pointer.PointerId] = position;

            if (_pointers.Count >= 2)
            {
                Point centroid;
                if (!TryGetCentroid(out centroid)) return;

                if (!_hasCentroid)
                {
                    _lastCentroid = centroid;
                    _hasCentroid = true;
                    return;
                }

                double deltaX = centroid.X - _lastCentroid.X;
                double deltaY = centroid.Y - _lastCentroid.Y;
                _lastCentroid = centroid;

                if (_mapper.CanPan)
                {
                    // The desktop is bigger than the screen: two fingers move the view.
                    _mapper.PanBy(deltaX, deltaY);
                    UpdatePlacement();
                }
                else
                {
                    _wheelAccumulator += deltaY;
                    while (_wheelAccumulator <= -WheelNotchDistance)
                    {
                        _wheelAccumulator += WheelNotchDistance;
                        SendWheelNotch(true);
                    }
                    while (_wheelAccumulator >= WheelNotchDistance)
                    {
                        _wheelAccumulator -= WheelNotchDistance;
                        SendWheelNotch(false);
                    }
                }
                return;
            }

            if (!_leftButtonDown) return;

            // Dragging with one finger keeps the left button down, exactly like a mouse drag.
            double distanceX = position.X - _pressPosition.X;
            double distanceY = position.Y - _pressPosition.Y;
            if (distanceX * distanceX + distanceY * distanceY > DragTolerancePixels * DragTolerancePixels)
                CancelLongPress();

            int remoteX, remoteY;
            if (!_mapper.TryMapToRemote(position.X, position.Y, out remoteX, out remoteY)) return;
            if (remoteX == _lastRemoteX && remoteY == _lastRemoteY) return;

            _lastRemoteX = remoteX;
            _lastRemoteY = remoteY;
            SendPointer(RfbProtocol.ButtonLeft, remoteX, remoteY);
        }

        private void RemoteCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_pointers.ContainsKey(e.Pointer.PointerId))
                _pointers.Remove(e.Pointer.PointerId);
            RemoteCanvas.ReleasePointerCapture(e.Pointer);

            CancelLongPress();

            if (_pointers.Count == 0 && _leftButtonDown)
            {
                SendPointer(0, _lastRemoteX, _lastRemoteY);
                _leftButtonDown = false;
            }

            _hasCentroid = false;
            _wheelAccumulator = 0;
        }

        private bool TryGetCentroid(out Point centroid)
        {
            centroid = new Point(0, 0);
            if (_pointers.Count < 2) return false;

            double sumX = 0;
            double sumY = 0;
            foreach (Point point in _pointers.Values)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            centroid = new Point(sumX / _pointers.Count, sumY / _pointers.Count);
            return true;
        }

        private void StartLongPressTimer()
        {
            CancelLongPress();

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(LongPressMilliseconds);
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                if (!_leftButtonDown || _pointers.Count != 1) return;

                // Long press = right click: release the left button first so the PC does not
                // treat it as a drag, then click with the right button.
                SendPointer(0, _lastRemoteX, _lastRemoteY);
                _leftButtonDown = false;
                SendPointer(RfbProtocol.ButtonRight, _lastRemoteX, _lastRemoteY);
                SendPointer(0, _lastRemoteX, _lastRemoteY);
                StatusText.Text = "Right click";
            };
            timer.Start();

            _longPressTimer = timer;
        }

        private void CancelLongPress()
        {
            if (_longPressTimer == null) return;
            try
            {
                _longPressTimer.Stop();
            }
            catch (Exception)
            {
            }
            _longPressTimer = null;
        }

        private void SendPointer(byte buttonMask, int x, int y)
        {
            RfbClient client = _client;
            if (client == null) return;

            Task task = Task.Run(async () =>
            {
                try
                {
                    await client.SendPointerEventAsync(buttonMask, x, y, CancellationToken.None);
                }
                catch (Exception)
                {
                }
            });
            GC.KeepAlive(task);
        }

        private void SendWheelNotch(bool up)
        {
            byte mask = up ? RfbProtocol.ButtonWheelUp : RfbProtocol.ButtonWheelDown;
            SendPointer(mask, _lastRemoteX, _lastRemoteY);
            SendPointer(0, _lastRemoteX, _lastRemoteY);
        }

        // ---------------------------------------------------------------------------------
        // Toolbar commands
        // ---------------------------------------------------------------------------------

        private void Scale_Click(object sender, RoutedEventArgs e)
        {
            if (_mapper.Mode == ViewportScaleMode.Fit)
            {
                _mapper.SetMode(ViewportScaleMode.ActualSize);
                ScaleButton.Content = "1:1";
            }
            else
            {
                _mapper.SetMode(ViewportScaleMode.Fit);
                ScaleButton.Content = "Fit";
            }

            UpdatePlacement();
            UpdateStatus();

            if (_client != null && _renderer.HasBitmap)
                QueueRender(new Rect(0, 0, _client.Framebuffer.Width, _client.Framebuffer.Height));
        }

        private void Keys_Click(object sender, RoutedEventArgs e)
        {
            KeysPanel.Visibility = KeysPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void RightClick_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) return;

            int x, y;
            if (!TryGetTargetPoint(out x, out y)) return;

            SendPointer(RfbProtocol.ButtonRight, x, y);
            SendPointer(0, x, y);
            StatusText.Text = "Right click";
        }

        private void WheelUp_Click(object sender, RoutedEventArgs e)
        {
            int x, y;
            if (!TryGetTargetPoint(out x, out y)) return;
            _lastRemoteX = x;
            _lastRemoteY = y;
            SendWheelNotch(true);
        }

        private void WheelDown_Click(object sender, RoutedEventArgs e)
        {
            int x, y;
            if (!TryGetTargetPoint(out x, out y)) return;
            _lastRemoteX = x;
            _lastRemoteY = y;
            SendWheelNotch(false);
        }

        /// <summary>Where a toolbar initiated click should land: the last touch, or the centre.</summary>
        private bool TryGetTargetPoint(out int x, out int y)
        {
            x = _lastRemoteX;
            y = _lastRemoteY;

            if (!_connected) return false;
            if (x >= 0 && y >= 0) return true;

            if (_client == null) return false;
            x = _client.RemoteWidth / 2;
            y = _client.RemoteHeight / 2;
            return true;
        }

        private void Key_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            SendKeyPress(KeysymFromTag(button.Tag as string));
        }

        private void Modifier_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !_connected) return;

            uint keysym = KeysymFromTag(button.Tag as string);
            if (keysym == 0) return;

            if (_activeModifiers.ContainsKey(keysym))
            {
                SendKey(keysym, false);
                _activeModifiers.Remove(keysym);
                button.ClearValue(Control.BackgroundProperty);
            }
            else
            {
                SendKey(keysym, true);
                _activeModifiers[keysym] = button;
                button.Background = new SolidColorBrush(Windows.UI.Colors.Orange);
            }
        }

        private void CtrlAltDel_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) return;

            SendKey(RemoteKeys.ControlLeft, true);
            SendKey(RemoteKeys.AltLeft, true);
            SendKeyPress(RemoteKeys.Delete);
            SendKey(RemoteKeys.AltLeft, false);
            SendKey(RemoteKeys.ControlLeft, false);
            StatusText.Text = "Ctrl+Alt+Del sent";
        }

        private async void Paste_Click(object sender, RoutedEventArgs e)
        {
            RfbClient client = _client;
            if (client == null) return;

            try
            {
                DataPackageView view = Clipboard.GetContent();
                if (!view.Contains(StandardDataFormats.Text))
                {
                    StatusText.Text = "The clipboard has no text";
                    return;
                }

                string text = await view.GetTextAsync();
                await client.SendClientCutTextAsync(text, CancellationToken.None);
                StatusText.Text = "Sent " + (text == null ? 0 : text.Length) + " characters to the PC clipboard";
            }
            catch (Exception error)
            {
                StatusText.Text = "Clipboard error: " + error.Message;
            }
        }

        private async void TypeText_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) return;

            ContentDialog dialog = new ContentDialog();
            dialog.Title = "Type text on the PC";

            TextBox input = new TextBox();
            input.AcceptsReturn = false;
            input.TextWrapping = TextWrapping.Wrap;
            input.Height = 120;
            dialog.Content = input;
            dialog.PrimaryButtonText = "Send";
            dialog.SecondaryButtonText = "Send + Enter";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = ContentDialogButton.Primary;

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return;

            RemoteKeyboardBridge keyboard = _keyboard;
            RfbClient client = _client;
            if (keyboard == null) return;

            await keyboard.SendTextAsync(input.Text);
            if (result == ContentDialogResult.Secondary && client != null)
                await client.SendKeyPressAsync(RemoteKeys.Return, CancellationToken.None);

            StatusText.Text = "Text sent";
        }

        private bool _fullScreen;

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            _fullScreen = !_fullScreen;

            try
            {
                ApplicationView view = ApplicationView.GetForCurrentView();
                if (_fullScreen) view.TryEnterFullScreenMode();
                else view.ExitFullScreenMode();
            }
            catch (Exception)
            {
            }

            // Hide the chrome while in full screen so the remote desktop uses the whole screen.
            RootGrid.RowDefinitions[0].Height = _fullScreen ? new GridLength(0) : GridLength.Auto;
            RootGrid.RowDefinitions[1].Height = new GridLength(0);
            RootGrid.RowDefinitions[3].Height = _fullScreen ? new GridLength(0) : GridLength.Auto;
            KeysPanel.Visibility = Visibility.Collapsed;
            FullScreenButton.Content = _fullScreen ? "Windowed" : "Full screen";

            UpdatePlacement();
        }

        private void SendKeyPress(uint keysym)
        {
            SendKey(keysym, true);
            SendKey(keysym, false);
        }

        private void SendKey(uint keysym, bool down)
        {
            RfbClient client = _client;
            if (client == null || keysym == 0) return;

            Task task = Task.Run(async () =>
            {
                try
                {
                    await client.SendKeyEventAsync(keysym, down, CancellationToken.None);
                }
                catch (Exception)
                {
                }
            });
            GC.KeepAlive(task);
        }

        /// <summary>Maps the Tag of a key button to an X11 keysym.</summary>
        private static uint KeysymFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 0;

            switch (tag)
            {
                case "Escape": return RemoteKeys.Escape;
                case "Tab": return RemoteKeys.Tab;
                case "Return": return RemoteKeys.Return;
                case "BackSpace": return RemoteKeys.BackSpace;
                case "Delete": return RemoteKeys.Delete;
                case "Insert": return RemoteKeys.Insert;
                case "Up": return RemoteKeys.Up;
                case "Down": return RemoteKeys.Down;
                case "Left": return RemoteKeys.Left;
                case "Right": return RemoteKeys.Right;
                case "Home": return RemoteKeys.Home;
                case "End": return RemoteKeys.End;
                case "PageUp": return RemoteKeys.PageUp;
                case "PageDown": return RemoteKeys.PageDown;
                case "ControlLeft": return RemoteKeys.ControlLeft;
                case "AltLeft": return RemoteKeys.AltLeft;
                case "ShiftLeft": return RemoteKeys.ShiftLeft;
                case "SuperLeft": return RemoteKeys.SuperLeft;
                case "F1": return RemoteKeys.F1;
                case "F2": return RemoteKeys.F2;
                case "F3": return RemoteKeys.F3;
                case "F4": return RemoteKeys.F4;
                case "F5": return RemoteKeys.F5;
                case "F6": return RemoteKeys.F6;
                case "F7": return RemoteKeys.F7;
                case "F8": return RemoteKeys.F8;
                case "F9": return RemoteKeys.F9;
                case "F10": return RemoteKeys.F10;
                case "F11": return RemoteKeys.F11;
                case "F12": return RemoteKeys.F12;
            }

            return 0;
        }
    }
}
