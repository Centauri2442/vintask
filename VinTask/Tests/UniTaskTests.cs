using Cysharp.Threading.Tasks.Internal;
using Cysharp.Threading.Tasks.Linq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// All integration test cases for VinTask. Call <see cref="Register"/> to populate a
    /// <see cref="TestRunner"/> with the desired category of tests.
    /// </summary>
    public static class UniTaskTests
    {
        private sealed class Box<T>
        {
            public T Value { get; set; }
        }

        #region Registration
        
        public static void Register(TestRunner runner, string filter = "all")
        {
            bool Match(string name)
            {
                if (string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string category = name.Contains('.') ? name.Substring(0, name.IndexOf('.')) : name;
                return string.Equals(category, filter, StringComparison.OrdinalIgnoreCase);
            }

            bool isClientSide = PlayerLoopHelper.IsClientTickThread;
            bool isServerSide = PlayerLoopHelper.IsServerTickThread;
            bool hasRender = PlayerLoopHelper.RenderThreadId != 0;

            RegisterYieldTests(runner, Match, isClientSide, isServerSide, hasRender);
            RegisterNextFrameTests(runner, Match);
            RegisterDelayTests(runner, Match);
            RegisterWaitUntilTests(runner, Match);
            RegisterCancellationTests(runner, Match);
            RegisterWhenAllAnyTests(runner, Match);
            RegisterCompletionSourceTests(runner, Match);
            RegisterLinqTests(runner, Match);
            RegisterChannelTests(runner, Match);
            RegisterThreadDiagnostics(runner, Match);
            RegisterSwitchTests(runner, Match);
        }

        #endregion

        #region Switch

        private static void RegisterSwitchTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("Switch.ThreadPoolAndBack"))
            {
                runner.Add("Switch.ThreadPoolAndBack", async () =>
                {
                    await UniTask.SwitchToThreadPool();
                    Assert.IsFalse(PlayerLoopHelper.IsMainThread, "Should be off the game thread after SwitchToThreadPool");

                    await UniTask.SwitchToMainThread();
                    Assert.IsTrue(PlayerLoopHelper.IsMainThread, "Should be back on a game thread after SwitchToMainThread");
                });
            }

            if (match("Switch.AlreadyOnThreadIsSynchronous"))
            {
                runner.Add("Switch.AlreadyOnThreadIsSynchronous", async () =>
                {
                    // Already on a game thread, so the switch must not advance a tick.
                    int before = PlayerLoopHelper.CurrentTickId;
                    await UniTask.SwitchToMainThread();
                    int after = PlayerLoopHelper.CurrentTickId;
                    Assert.AreEqual(before, after, "SwitchToMainThread should be synchronous when already on the game thread");
                });
            }
        }

        #endregion

        #region Threads

        private static void RegisterThreadDiagnostics(TestRunner runner, Func<string, bool> match)
        {
            if (match("Threads.Report"))
            {
                runner.Add("Threads.Report", async () =>
                {
                    await UniTask.Yield();
                    int render = PlayerLoopHelper.RenderThreadId;
                    int clientTick = PlayerLoopHelper.ClientTickThreadId;
                    int server = PlayerLoopHelper.ServerTickThreadId;
                    bool clientSharesRenderThread = render != 0 && render == clientTick;
                    runner.Log(
                        $"[Threads] render={render} clientTick={clientTick} server={server} " +
                        $"current={Environment.CurrentManagedThreadId} clientSharesRenderThread={clientSharesRenderThread}");
                });
            }
        }

        #endregion

        #region Yield

        private static void RegisterYieldTests(TestRunner runner, Func<string, bool> match, bool isClientSide, bool isServerSide, bool hasRender)
        {
            if (match("Yield.Default.ResumesOnMainThread"))
            {
                runner.Add("Yield.Default.ResumesOnMainThread", async () =>
                {
                    await UniTask.Yield();
                    Assert.IsTrue(PlayerLoopHelper.IsMainThread, "IsMainThread must be true after default Yield");
                });
            }

            if (match("Yield.Default.TickIdStable"))
            {
                runner.Add("Yield.Default.TickIdStable", async () =>
                {
                    int tickBefore = PlayerLoopHelper.CurrentTickId;
                    await UniTask.Yield();
                    int tickAfter = PlayerLoopHelper.CurrentTickId;
                    Assert.IsTrue(tickAfter >= tickBefore, $"Tick must not go backwards: {tickBefore} -> {tickAfter}");
                });
            }

            if (hasRender && match("Yield.Render.PreRender"))
            {
                runner.Add("Yield.Render.PreRender", async () =>
                {
                    await UniTask.Yield(RenderLoopTiming.PreRender);
                    Assert.IsTrue(PlayerLoopHelper.IsRenderThread, "Must be on render thread after Yield(PreRender)");
                });
            }

            if (hasRender && match("Yield.Render.PostRender"))
            {
                runner.Add("Yield.Render.PostRender", async () =>
                {
                    await UniTask.Yield(RenderLoopTiming.PostRender);
                    Assert.IsTrue(PlayerLoopHelper.IsRenderThread, "Must be on render thread after Yield(PostRender)");
                });
            }

            if (hasRender && match("Yield.Render.WaitForPostRender"))
            {
                runner.Add("Yield.Render.WaitForPostRender", async () =>
                {
                    await UniTask.WaitForPostRender();
                    Assert.IsTrue(PlayerLoopHelper.IsRenderThread, "Must be on render thread after WaitForPostRender");
                });
            }

            if (hasRender && match("Yield.Render.RenderTickAdvances"))
            {
                runner.Add("Yield.Render.RenderTickAdvances", async () =>
                {
                    int tickBefore = PlayerLoopHelper.RenderTickId;
                    await UniTask.NextFrame(RenderLoopTiming.PreRender);
                    int tickAfter = PlayerLoopHelper.RenderTickId;
                    Assert.IsTrue(tickAfter > tickBefore, $"RenderTickId must advance: {tickBefore} -> {tickAfter}");
                });
            }

            if (isClientSide && match("Yield.Client.OnClientTick"))
            {
                runner.Add("Yield.Client.OnClientTick", async () =>
                {
                    await UniTask.Yield(ClientLoopTiming.OnClientTick);
                    Assert.IsTrue(PlayerLoopHelper.IsClientTickThread, "Must be on client tick thread after Yield(OnClientTick)");
                });
            }

            if (isClientSide && match("Yield.Client.LateClientTick"))
            {
                runner.Add("Yield.Client.LateClientTick", async () =>
                {
                    await UniTask.Yield(ClientLoopTiming.LateClientTick);
                    Assert.IsTrue(PlayerLoopHelper.IsClientTickThread, "Must be on client tick thread after Yield(LateClientTick)");
                });
            }

            if (isClientSide && match("Yield.Client.ClientTickAdvances"))
            {
                runner.Add("Yield.Client.ClientTickAdvances", async () =>
                {
                    int tickBefore = PlayerLoopHelper.ClientTickId;
                    await UniTask.NextFrame(ClientLoopTiming.OnClientTick);
                    int tickAfter = PlayerLoopHelper.ClientTickId;
                    Assert.IsTrue(tickAfter > tickBefore, $"ClientTickId must advance: {tickBefore} -> {tickAfter}");
                });
            }

            if (isServerSide && match("Yield.Server.OnServerTick"))
            {
                runner.Add("Yield.Server.OnServerTick", async () =>
                {
                    await UniTask.Yield(ServerLoopTiming.OnServerTick);
                    Assert.IsTrue(PlayerLoopHelper.IsServerTickThread, "Must be on server tick thread after Yield(OnServerTick)");
                });
            }

            if (isServerSide && match("Yield.Server.LateServerTick"))
            {
                runner.Add("Yield.Server.LateServerTick", async () =>
                {
                    await UniTask.Yield(ServerLoopTiming.LateServerTick);
                    Assert.IsTrue(PlayerLoopHelper.IsServerTickThread, "Must be on server tick thread after Yield(LateServerTick)");
                });
            }

            if (isServerSide && match("Yield.Server.ServerTickAdvances"))
            {
                runner.Add("Yield.Server.ServerTickAdvances", async () =>
                {
                    int tickBefore = PlayerLoopHelper.ServerTickId;
                    await UniTask.NextFrame(ServerLoopTiming.OnServerTick);
                    int tickAfter = PlayerLoopHelper.ServerTickId;
                    Assert.IsTrue(tickAfter > tickBefore, $"ServerTickId must advance: {tickBefore} -> {tickAfter}");
                });
            }
        }

        #endregion

        #region NextFrame

        private static void RegisterNextFrameTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("NextFrame.AdvancesTick"))
            {
                runner.Add("NextFrame.AdvancesTick", async () =>
                {
                    int tickBefore = PlayerLoopHelper.CurrentTickId;
                    await UniTask.NextFrame();
                    int tickAfter = PlayerLoopHelper.CurrentTickId;
                    Assert.IsTrue(tickAfter > tickBefore, $"CurrentTickId must advance: {tickBefore} -> {tickAfter}");
                });
            }
        }

        #endregion

        #region Delay

        private static void RegisterDelayTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("Delay.500ms"))
            {
                runner.Add("Delay.500ms", async () =>
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    await UniTask.Delay(500);
                    long elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds;
                    Assert.IsInRange(elapsedMilliseconds, 400, 3000, "Delay(500)");
                    Assert.IsTrue(PlayerLoopHelper.IsMainThread, "Must resume on main thread after Delay");
                });
            }

            if (match("Delay.TimeSpan200ms"))
            {
                runner.Add("Delay.TimeSpan200ms", async () =>
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    await UniTask.Delay(TimeSpan.FromMilliseconds(200));
                    long elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds;
                    Assert.IsInRange(elapsedMilliseconds, 150, 2000, "Delay(TimeSpan 200ms)");
                });
            }

            if (match("Delay.WaitForSeconds1"))
            {
                runner.Add("Delay.WaitForSeconds1", async () =>
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    await UniTask.WaitForSeconds(1f);
                    long elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds;
                    Assert.IsInRange(elapsedMilliseconds, 800, 5000, "WaitForSeconds(1)");
                });
            }

            if (match("DelayFrame.5"))
            {
                runner.Add("DelayFrame.5", async () =>
                {
                    int tickBefore = PlayerLoopHelper.CurrentTickId;
                    await UniTask.DelayFrame(5);
                    int tickAfter = PlayerLoopHelper.CurrentTickId;
                    Assert.IsTrue(tickAfter - tickBefore >= 5, $"Expected ≥5 tick advance, got {tickAfter - tickBefore}");
                });
            }

            if (match("DelayFrame.0"))
            {
                runner.Add("DelayFrame.0", async () =>
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    await UniTask.DelayFrame(0);
                    long elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds;
                    Assert.IsTrue(elapsedMilliseconds < 2000, $"DelayFrame(0) took too long: {elapsedMilliseconds}ms");
                });
            }
        }

        #endregion

        #region WaitUntil

        private static void RegisterWaitUntilTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("WaitUntil.Flag"))
            {
                runner.Add("WaitUntil.Flag", async () =>
                {
                    bool flag = false;

                    async UniTask SetFlagAfterDelay()
                    {
                        await UniTask.DelayFrame(3);
                        flag = true;
                    }

                    SetFlagAfterDelay().Forget();
                    await UniTask.WaitUntil(() => flag);
                    Assert.IsTrue(flag, "Flag must be true after WaitUntil");
                });
            }

            if (match("WaitWhile.Flag"))
            {
                runner.Add("WaitWhile.Flag", async () =>
                {
                    bool flag = true;

                    async UniTask ClearFlagAfterDelay()
                    {
                        await UniTask.DelayFrame(3);
                        flag = false;
                    }

                    ClearFlagAfterDelay().Forget();
                    await UniTask.WaitWhile(() => flag);
                    Assert.IsFalse(flag, "Flag must be false after WaitWhile");
                });
            }

            if (match("WaitUntil.ValueChanged"))
            {
                runner.Add("WaitUntil.ValueChanged", async () =>
                {
                    var box = new Box<int> { Value = 0 };

                    async UniTask ChangeValueAfterDelay()
                    {
                        await UniTask.DelayFrame(3);
                        box.Value = 42;
                    }

                    ChangeValueAfterDelay().Forget();
                    int result = await UniTask.WaitUntilValueChanged(box, boxed => boxed.Value);
                    Assert.AreEqual(42, result, "WaitUntilValueChanged must return new value");
                });
            }

            if (match("WaitUntil.Canceled"))
            {
                runner.Add("WaitUntil.Canceled", async () =>
                {
                    using var cts = new CancellationTokenSource();

                    async UniTask CancelAfterDelay()
                    {
                        await UniTask.DelayFrame(2);
                        cts.Cancel();
                    }

                    CancelAfterDelay().Forget();
                    await UniTask.WaitUntilCanceled(cts.Token);
                    Assert.IsTrue(cts.Token.IsCancellationRequested, "Token must be cancelled after WaitUntilCanceled");
                });
            }
        }

        #endregion

        #region Cancellation

        private static void RegisterCancellationTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("Cancellation.PreCancelled"))
            {
                runner.Add("Cancellation.PreCancelled", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();

                    bool threw = false;
                    try
                    {
                        await UniTask.Delay(1000, cancellationToken: cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "Pre-cancelled token must throw OperationCanceledException");
                });
            }

            if (match("Cancellation.CancelDuringDelay"))
            {
                runner.Add("Cancellation.CancelDuringDelay", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    var stopwatch = ValueStopwatch.StartNew();
                    var task = UniTask.Delay(5000, cancellationToken: cts.Token);
                    await UniTask.Delay(200);
                    cts.Cancel();

                    bool threw = false;
                    try
                    {
                        await task;
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "Expected OperationCanceledException");
                    Assert.IsInRange((long)stopwatch.Elapsed.TotalMilliseconds, 0, 3000, "Cancel reaction time");
                });
            }

            if (match("Cancellation.WaitUntilCancel"))
            {
                runner.Add("Cancellation.WaitUntilCancel", async () =>
                {
                    using var cts = new CancellationTokenSource();

                    async UniTask CancelAfterDelay()
                    {
                        await UniTask.DelayFrame(3);
                        cts.Cancel();
                    }

                    CancelAfterDelay().Forget();

                    bool threw = false;
                    try
                    {
                        await UniTask.WaitUntil(() => false, cancellationToken: cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "WaitUntil(() => false) must throw when cancelled");
                });
            }

            if (match("Cancellation.YieldCancel"))
            {
                runner.Add("Cancellation.YieldCancel", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();

                    bool threw = false;
                    try
                    {
                        await UniTask.Yield(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "Yield with cancelled token must throw OperationCanceledException");
                });
            }
        }

        #endregion

        #region WhenAll / WhenAny

        private static void RegisterWhenAllAnyTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("WhenAll.Completes"))
            {
                runner.Add("WhenAll.Completes", async () =>
                {
                    await UniTask.WhenAll(
                        UniTask.Delay(50),
                        UniTask.Delay(75),
                        UniTask.Delay(100));
                });
            }

            if (match("WhenAll.ReturnsValues"))
            {
                runner.Add("WhenAll.ReturnsValues", async () =>
                {
                    var tcs1 = new UniTaskCompletionSource<int>();
                    var tcs2 = new UniTaskCompletionSource<int>();

                    async UniTask SetValues()
                    {
                        await UniTask.DelayFrame(2);
                        tcs1.TrySetResult(10);
                        tcs2.TrySetResult(20);
                    }

                    SetValues().Forget();
                    var (result1, result2) = await UniTask.WhenAll(tcs1.Task, tcs2.Task);
                    Assert.AreEqual(10, result1, "WhenAll value 1");
                    Assert.AreEqual(20, result2, "WhenAll value 2");
                });
            }

            if (match("WhenAny.ReturnsFastest"))
            {
                runner.Add("WhenAny.ReturnsFastest", async () =>
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    int winner = await UniTask.WhenAny(UniTask.Delay(100), UniTask.Delay(500));
                    long elapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds;
                    Assert.AreEqual(0, winner, "First (faster) task should win WhenAny");
                    Assert.IsInRange(elapsedMilliseconds, 50, 400, "WhenAny timing");
                });
            }

            if (match("WhenAll.PropagatesException"))
            {
                runner.Add("WhenAll.PropagatesException", async () =>
                {
                    async UniTask FailAfterDelay()
                    {
                        await UniTask.DelayFrame(1);
                        throw new InvalidOperationException("intentional test failure");
                    }

                    bool threw = false;
                    try
                    {
                        await UniTask.WhenAll(UniTask.DelayFrame(2), FailAfterDelay());
                    }
                    catch (Exception)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "WhenAll must propagate exceptions from child tasks");
                });
            }
        }

        #endregion

        #region CompletionSource

        private static void RegisterCompletionSourceTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("CompletionSource.SetResult"))
            {
                runner.Add("CompletionSource.SetResult", async () =>
                {
                    var tcs = new UniTaskCompletionSource<int>();

                    async UniTask SetResult()
                    {
                        await UniTask.DelayFrame(2);
                        tcs.TrySetResult(42);
                    }

                    SetResult().Forget();
                    int result = await tcs.Task;
                    Assert.AreEqual(42, result, "CompletionSource must return correct result");
                });
            }

            if (match("CompletionSource.SetException"))
            {
                runner.Add("CompletionSource.SetException", async () =>
                {
                    var tcs = new UniTaskCompletionSource<int>();

                    async UniTask SetException()
                    {
                        await UniTask.DelayFrame(2);
                        tcs.TrySetException(new InvalidOperationException("test exception"));
                    }

                    SetException().Forget();

                    bool threw = false;
                    string exMessage = null;
                    try
                    {
                        await tcs.Task;
                    }
                    catch (InvalidOperationException ex)
                    {
                        threw = true;
                        exMessage = ex.Message;
                    }

                    Assert.IsTrue(threw, "CompletionSource.SetException must propagate exception");
                    Assert.AreEqual("test exception", exMessage, "Exception message must match");
                });
            }

            if (match("CompletionSource.SetCanceled"))
            {
                runner.Add("CompletionSource.SetCanceled", async () =>
                {
                    var tcs = new UniTaskCompletionSource<int>();

                    async UniTask SetCanceled()
                    {
                        await UniTask.DelayFrame(2);
                        tcs.TrySetCanceled();
                    }

                    SetCanceled().Forget();

                    bool threw = false;
                    try
                    {
                        await tcs.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    Assert.IsTrue(threw, "CompletionSource.SetCanceled must throw OperationCanceledException");
                });
            }
        }

        #endregion

        #region Linq

        private static void RegisterLinqTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("Linq.Range"))
            {
                runner.Add("Linq.Range", async () =>
                {
                    int[] result = await UniTaskAsyncEnumerable.Range(0, 5).ToArrayAsync();
                    Assert.AreEqual(5, result.Length, "Range(0,5) must have 5 elements");
                    for (int elementIndex = 0; elementIndex < 5; elementIndex++)
                    {
                        Assert.AreEqual(elementIndex, result[elementIndex], $"Range element {elementIndex}");
                    }
                });
            }

            if (match("Linq.Where"))
            {
                runner.Add("Linq.Where", async () =>
                {
                    int[] result = await UniTaskAsyncEnumerable.Range(0, 10)
                        .Where(x => x % 2 == 0)
                        .ToArrayAsync();
                    int[] expected = { 0, 2, 4, 6, 8 };
                    Assert.AreEqual(expected.Length, result.Length, "Where result length");
                    for (int elementIndex = 0; elementIndex < expected.Length; elementIndex++)
                    {
                        Assert.AreEqual(expected[elementIndex], result[elementIndex], $"Where element {elementIndex}");
                    }
                });
            }

            if (match("Linq.Select"))
            {
                runner.Add("Linq.Select", async () =>
                {
                    int[] result = await UniTaskAsyncEnumerable.Range(1, 3)
                        .Select(x => x * x)
                        .ToArrayAsync();
                    int[] expected = { 1, 4, 9 };
                    Assert.AreEqual(expected.Length, result.Length, "Select result length");
                    for (int elementIndex = 0; elementIndex < expected.Length; elementIndex++)
                    {
                        Assert.AreEqual(expected[elementIndex], result[elementIndex], $"Select element {elementIndex}");
                    }
                });
            }
        }

        #endregion

        #region Channel

        private static void RegisterChannelTests(TestRunner runner, Func<string, bool> match)
        {
            if (match("Channel.WriteRead"))
            {
                runner.Add("Channel.WriteRead", async () =>
                {
                    var channel = Channel.CreateSingleConsumerUnbounded<int>();
                    channel.Writer.TryWrite(1);
                    channel.Writer.TryWrite(2);
                    channel.Writer.TryWrite(3);

                    int v1 = await channel.Reader.ReadAsync();
                    int v2 = await channel.Reader.ReadAsync();
                    int v3 = await channel.Reader.ReadAsync();

                    Assert.AreEqual(1, v1, "Channel read 1");
                    Assert.AreEqual(2, v2, "Channel read 2");
                    Assert.AreEqual(3, v3, "Channel read 3");
                });
            }

            if (match("Channel.AsyncEnumerable"))
            {
                runner.Add("Channel.AsyncEnumerable", async () =>
                {
                    var channel = Channel.CreateSingleConsumerUnbounded<int>();
                    channel.Writer.TryWrite(10);
                    channel.Writer.TryWrite(20);
                    channel.Writer.TryWrite(30);
                    channel.Writer.TryComplete();

                    var results = new List<int>();
                    await foreach (int item in channel.Reader.ReadAllAsync())
                    {
                        results.Add(item);
                    }

                    Assert.AreEqual(3, results.Count, "Channel async enumerable count");
                    Assert.AreEqual(10, results[0], "Channel item 0");
                    Assert.AreEqual(20, results[1], "Channel item 1");
                    Assert.AreEqual(30, results[2], "Channel item 2");
                });
            }
        }

        #endregion
    }
}
