using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SurfaceVnc.Core;

namespace SurfaceVnc.Tests
{
    /// <summary>Owns a connected <see cref="RfbClient"/> plus its message loop task.</summary>
    public sealed class ClientHarness : IDisposable
    {
        public RfbClient Client { get; private set; }
        public CancellationTokenSource Cancellation { get; private set; }
        public Task RunTask { get; private set; }
        public Exception RunError { get; private set; }

        public static async Task<ClientHarness> StartAsync(FakeRfbServer server, string password)
        {
            return await StartAsync(server, password, PixelFormat.DefaultBgra8, false);
        }

        public static async Task<ClientHarness> StartAsync(FakeRfbServer server, string password, PixelFormat pixelFormat, bool viewOnly)
        {
            SocketRfbTransport transport = await SocketRfbTransport.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

            RfbClient client = new RfbClient(transport);
            ConnectionSettings settings = ConnectionSettings.CreateDefault("127.0.0.1", server.Port, password);
            settings.RequestedPixelFormat = pixelFormat;
            settings.ViewOnly = viewOnly;
            await client.ConnectAsync(settings, CancellationToken.None);

            ClientHarness harness = new ClientHarness();
            harness.Client = client;
            harness.Cancellation = new CancellationTokenSource();
            harness.RunTask = Task.Run(async () =>
            {
                try
                {
                    await client.RunAsync(harness.Cancellation.Token);
                }
                catch (Exception error)
                {
                    harness.RunError = error;
                }
            });
            return harness;
        }

        /// <summary>Waits for the message loop to die and returns the error it produced.</summary>
        public async Task<Exception> WaitForRunErrorAsync(int timeoutMilliseconds)
        {
            Task completed = await Task.WhenAny(RunTask, Task.Delay(timeoutMilliseconds));
            if (completed != RunTask) return null;
            return RunError;
        }

        public void Dispose()
        {
            try { Cancellation.Cancel(); }
            catch (Exception) { }
            try { Client.Dispose(); }
            catch (Exception) { }
        }
    }
}
