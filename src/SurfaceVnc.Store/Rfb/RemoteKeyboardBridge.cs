using System;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SurfaceVnc.Store
{
    /// <summary>
    /// Forwards what the user types to the remote desktop.
    ///
    /// Two sources are wired up:
    ///   * a hidden TextBox that keeps the focus so the Windows on-screen keyboard pops up.  The
    ///     keyboard (and a physical keyboard) insert text into it, which raises TextChanged - that
    ///     is the authoritative character source while the box has focus;
    ///   * CoreWindow.CharacterReceived, used when no text box has focus, plus CoreWindow.KeyDown
    ///     for the keys that have no character at all (arrows, F keys, ctrl/alt/...).
    /// </summary>
    public sealed class RemoteKeyboardBridge : IDisposable
    {
        private readonly RfbClient _client;
        private readonly TextBox _proxy;
        private readonly CoreWindow _window;

        private bool _suppressTextChanged;
        private string _pending = string.Empty;
        private bool _disposed;

        public RemoteKeyboardBridge(RfbClient client, TextBox proxy, CoreWindow window)
        {
            if (client == null) throw new ArgumentNullException("client");
            if (proxy == null) throw new ArgumentNullException("proxy");
            if (window == null) throw new ArgumentNullException("window");

            _client = client;
            _proxy = proxy;
            _window = window;

            _proxy.TextChanged += Proxy_TextChanged;
            _window.CharacterReceived += Window_CharacterReceived;
            _window.KeyDown += Window_KeyDown;
            _window.KeyUp += Window_KeyUp;
        }

        /// <summary>Makes the on-screen keyboard appear (the proxy text box takes the focus).</summary>
        public void Focus()
        {
            _proxy.Focus(FocusState.Programmatic);
        }

        public void Unfocus()
        {
            _suppressTextChanged = true;
            try { _proxy.Text = string.Empty; }
            finally { _suppressTextChanged = false; }
        }

        /// <summary>Types a whole string into the remote session (used by the "send text" dialog).</summary>
        public async Task SendTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\r') continue;
                if (character == '\n')
                {
                    await _client.SendKeyPressAsync(RemoteKeys.Return, CancellationToken.None);
                    continue;
                }

                uint keysym = RemoteKeys.FromChar(character);
                if (keysym == 0) continue;
                await _client.SendKeyPressAsync(keysym, CancellationToken.None);
            }
        }

        // ---- character sources ---------------------------------------------------------

        private void Proxy_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged || _disposed) return;

            string current = _proxy.Text;
            if (string.IsNullOrEmpty(current)) return;

            // Only the newly added characters are interesting; the proxy is cleared again so it
            // never grows (it is just a funnel for the on-screen keyboard).
            string added = current;
            if (current.StartsWith(_pending, StringComparison.Ordinal))
                added = current.Substring(_pending.Length);

            _pending = string.Empty;
            _suppressTextChanged = true;
            try { _proxy.Text = string.Empty; }
            finally { _suppressTextChanged = false; }

            if (string.IsNullOrEmpty(added)) return;

            for (int i = 0; i < added.Length; i++)
            {
                char character = added[i];
                if (character == '\r') continue;
                if (character == '\n')
                {
                    Send(RemoteKeys.Return, false);
                    continue;
                }

                uint keysym = RemoteKeys.FromChar(character);
                if (keysym != 0) Send(keysym, false);
            }
        }

        private void Window_CharacterReceived(CoreWindow sender, CharacterReceivedEventArgs args)
        {
            if (_disposed) return;
            // While the proxy has the focus the text box already reports the character.
            if (_proxy.FocusState != FocusState.Unfocused) return;

            uint keysym = RemoteKeys.FromUnicode((int)args.KeyCode);
            if (keysym == 0) return;

            Send(keysym, true);
            args.Handled = true;
        }

        // ---- special keys -------------------------------------------------------------

        private void Window_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            if (_disposed) return;

            uint keysym = RemoteKeys.FromVirtualKey((int)args.VirtualKey);
            if (keysym == 0) return;

            Send(keysym, true);
            args.Handled = true;
        }

        private void Window_KeyUp(CoreWindow sender, KeyEventArgs args)
        {
            if (_disposed) return;

            uint keysym = RemoteKeys.FromVirtualKey((int)args.VirtualKey);
            if (keysym == 0) return;

            Send(keysym, false);
            args.Handled = true;
        }

        /// <summary>
        /// Fire and forget key sending: the UI thread must never wait for the socket.  Failures
        /// are ignored because a dead session is reported by the message loop anyway.
        /// </summary>
        private void Send(uint keysym, bool down)
        {
            RfbClient client = _client;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _proxy.TextChanged -= Proxy_TextChanged;
            _window.CharacterReceived -= Window_CharacterReceived;
            _window.KeyDown -= Window_KeyDown;
            _window.KeyUp -= Window_KeyUp;
        }
    }
}
