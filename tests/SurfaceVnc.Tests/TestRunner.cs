using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SurfaceVnc.Tests
{
    /// <summary>Collects tests and runs them, printing a summary and returning an exit code.</summary>
    public static class TestRunner
    {
        private static readonly List<KeyValuePair<string, Func<Task>>> Tests = new List<KeyValuePair<string, Func<Task>>>();

        public static void Add(string name, Func<Task> test)
        {
            Tests.Add(new KeyValuePair<string, Func<Task>>(name, test));
        }

        public static void Add(string name, Action test)
        {
            Tests.Add(new KeyValuePair<string, Func<Task>>(name, () => { test(); return Task.CompletedTask; }));
        }

        public static async Task<int> RunAllAsync(string filter)
        {
            int passed = 0;
            int failed = 0;
            List<string> failures = new List<string>();

            foreach (KeyValuePair<string, Func<Task>> test in Tests)
            {
                if (!string.IsNullOrEmpty(filter) && test.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    Task task = test.Value();
                    Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
                    if (completed != task)
                        throw new CheckFailedException("test timed out after 30 seconds");
                    await task;

                    passed++;
                    Console.WriteLine("  PASS  " + test.Key);
                }
                catch (Exception error)
                {
                    failed++;
                    failures.Add(test.Key + ": " + error.Message);
                    Console.WriteLine("  FAIL  " + test.Key);
                    Console.WriteLine("          " + error.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("passed: " + passed + ", failed: " + failed);
            if (failures.Count > 0)
            {
                Console.WriteLine();
                foreach (string failure in failures) Console.WriteLine("FAILED  " + failure);
            }
            return failed == 0 ? 0 : 1;
        }
    }

    /// <summary>Polling helpers for the network tests.</summary>
    public static class Wait
    {
        /// <summary>Waits until <paramref name="condition"/> becomes true (or the timeout expires).</summary>
        public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds)
        {
            int waited = 0;
            while (waited < timeoutMilliseconds)
            {
                if (condition()) return true;
                await Task.Delay(10);
                waited += 10;
            }
            return condition();
        }

        public static async Task ForAsync(int milliseconds)
        {
            await Task.Delay(milliseconds);
        }
    }
}
