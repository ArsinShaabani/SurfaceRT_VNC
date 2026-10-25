using System;
using System.Threading.Tasks;

namespace SurfaceVnc.Tests
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;

            Console.WriteLine("SurfaceVnc.Core test suite");
            if (!string.IsNullOrEmpty(filter)) Console.WriteLine("filter: " + filter);
            Console.WriteLine();

            AuthTests.Register();
            PixelTests.Register();
            FramebufferTests.Register();
            ViewportTests.Register();
            InputTests.Register();
            ProtocolTests.Register();
            ServerMessageTests.Register();
            DecoderTests.Register();

            return await TestRunner.RunAllAsync(filter);
        }
    }
}
