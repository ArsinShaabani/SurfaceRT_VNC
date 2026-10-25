using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SurfaceVnc.Tests
{
    public sealed class CheckFailedException : Exception
    {
        public CheckFailedException(string message) : base(message) { }
    }

    /// <summary>Tiny assertion helper (no test framework dependency on purpose).</summary>
    public static class Check
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new CheckFailedException(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new CheckFailedException(message);
        }

        public static void Equal(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new CheckFailedException(message + " (expected " + expected + ", got " + actual + ")");
        }

        public static void Equal(uint expected, uint actual, string message)
        {
            if (expected != actual)
                throw new CheckFailedException(message + " (expected 0x" + expected.ToString("X") + ", got 0x" + actual.ToString("X") + ")");
        }

        public static void Equal(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new CheckFailedException(message + " (expected '" + expected + "', got '" + actual + "')");
        }

        public static void Equal(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new CheckFailedException(message + " (expected " + expected + ", got " + actual + ")");
        }

        public static void Bytes(byte[] expected, byte[] actual, string message)
        {
            if (expected == null || actual == null)
                throw new CheckFailedException(message + " (null buffer)");
            if (expected.Length != actual.Length)
                throw new CheckFailedException(message + " (expected " + expected.Length + " bytes, got " + actual.Length + ": " + Hex(actual) + ")");
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                    throw new CheckFailedException(message + " (byte " + i + ": expected 0x" + expected[i].ToString("X2") + ", got 0x" + actual[i].ToString("X2") + ")");
            }
        }

        public static void Pixel(Core.Framebuffer framebuffer, int x, int y, byte r, byte g, byte b, string message)
        {
            byte actualB, actualG, actualR, actualA;
            framebuffer.GetBgra8(x, y, out actualB, out actualG, out actualR, out actualA);
            if (actualR != r || actualG != g || actualB != b || actualA != 255)
            {
                throw new CheckFailedException(message + " at (" + x + "," + y + "): expected rgb(" + r + "," + g + "," + b + ",a=255) got rgb("
                    + actualR + "," + actualG + "," + actualB + ",a=" + actualA + ")");
            }
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception other)
            {
                throw new CheckFailedException(message + " (threw " + other.GetType().Name + " instead of " + typeof(TException).Name + ")");
            }
            throw new CheckFailedException(message + " (no exception was thrown, expected " + typeof(TException).Name + ")");
        }

        public static async Task ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception other)
            {
                throw new CheckFailedException(message + " (threw " + other.GetType().Name + ": " + other.Message + " instead of " + typeof(TException).Name + ")");
            }
            throw new CheckFailedException(message + " (no exception was thrown, expected " + typeof(TException).Name + ")");
        }

        public static string Hex(byte[] data)
        {
            if (data == null) return "<null>";
            System.Text.StringBuilder builder = new System.Text.StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) builder.Append(data[i].ToString("X2"));
            return builder.ToString();
        }
    }
}
