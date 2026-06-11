using Cysharp.Threading.Tasks.Internal;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// Collects named UniTask test cases and runs them sequentially, reporting pass/fail to chat.
    /// </summary>
    public sealed class TestRunner
    {
        private readonly List<(string Name, Func<UniTask> Body)> _tests = new();
        private readonly Action<string> _output;

        public TestRunner(Action<string> output)
        {
            _output = output;
        }

        /// <summary>
        /// Registers a named test case.
        /// </summary>
        public void Add(string name, Func<UniTask> body)
        {
            _tests.Add((name, body));
        }

        /// <summary>
        /// Writes a diagnostic line to the test output channel.
        /// </summary>
        public void Log(string message)
        {
            _output(message);
        }

        /// <summary>
        /// Runs all registered tests sequentially and prints a summary.
        /// Each test is given an 8-second timeout.
        /// </summary>
        public async UniTask RunAll()
        {
            int passed = 0;
            var totalSw = ValueStopwatch.StartNew();

            _output($"Running {_tests.Count} test(s)...");

            foreach (var (name, body) in _tests)
            {
                var testSw = ValueStopwatch.StartNew();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await body().AttachExternalCancellation(cts.Token);
                    long ms = (long)testSw.Elapsed.TotalMilliseconds;
                    _output($"[PASS] {name} ({ms}ms)");
                    passed++;
                }
                catch (Exception ex)
                {
                    string reason = ex is OperationCanceledException
                        ? "timeout (>8s) or unexpected cancellation"
                        : ex.Message;
                    _output($"[FAIL] {name}: {reason}");
                }
            }

            long totalMs = (long)totalSw.Elapsed.TotalMilliseconds;
            _output($"--- {passed}/{_tests.Count} passed ({totalMs}ms total) ---");
        }
    }

    /// <summary>
    /// Assertion helpers for test cases. All methods throw <see cref="Exception"/> on failure.
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Throws if condition is false.
        /// </summary>
        public static void IsTrue(bool condition, string message = "Expected true")
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        /// <summary>
        /// Throws if condition is true.
        /// </summary>
        public static void IsFalse(bool condition, string message = "Expected false")
        {
            if (condition)
            {
                throw new Exception(message);
            }
        }

        /// <summary>
        /// Throws if expected and actual are not equal.
        /// </summary>
        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message ?? $"Expected <{expected}>, got <{actual}>");
            }
        }

        /// <summary>
        /// Throws if value is outside [min, max].
        /// </summary>
        public static void IsInRange(long value, long min, long max, string label)
        {
            if (value < min || value > max)
            {
                throw new Exception($"{label} = {value}ms, expected [{min}, {max}]ms");
            }
        }
    }
}
