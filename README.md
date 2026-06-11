# VinTask
A port of [Cysharp's UniTask](https://github.com/Cysharp/UniTask) to [Vintage Story](https://www.vintagestory.at/), allowing mod authors to use allocation-free async/await that runs on the game's own loops instead of the .NET ThreadPool.

# License Notice

VinTask is released under the MIT License. The core task machinery is from [UniTask](https://github.com/Cysharp/UniTask) by Yoshifumi Kawai (neuecc) / Cysharp, Inc., also MIT. Vintage Story integration and port by STUDIO Violet.

# Overview
VinTask ports UniTask to Vintage Story, allowing async/await code that respects VS's own render and tick loops. Your code resumes on the thread it started on, at a timing you control.

VinTask is designed for temporary operations that run and complete within a play session. It has no persistence mechanism, so in-flight tasks are lost on world unload. Anything that needs to survive a save/load cycle (Crop growth, cooldowns, timed events) should use block entity data and _RegisterGameTickListener_ instead.

# How To Use
VinTask initializes itself automatically, so the only setup required is on mods wishing to use the system.

How to add VinTask to your mod
- Add the dependency to your _modinfo.json_:
```json
"dependencies": {
    "game": "1.21.0",
    "vintask": "1.0.0"
}
```
- Reference _VinTask.dll_ in your csproj, with `<Private>false</Private>` so it isn't copied to your output (It already ships with the VinTask mod).
- Add `using Cysharp.Threading.Tasks;` to your scripts.

# How it works
The system can be broken down into three schedulable surfaces, each with its own timing enum:
- **RenderLoopTiming**
  - Driven by the client render stages (_EnumRenderStage_).
  - PreRender, AfterPostProcessing, OnRenderGUI, PostRender.
- **ClientLoopTiming**
  - Driven by the client game tick listener.
  - PreClientTick, OnClientTick, LateClientTick, PostLateClientTick.
- **ServerLoopTiming**
  - Driven by the server game tick, on the server thread.
  - PreServerTick, OnServerTick, LateServerTick, PostLateServerTick.

On the client, game ticks and rendering run on the same main thread. The render/client timing split controls *when within a frame* you resume, not which thread. Anything awaited on the client main thread is render-safe.

The core awaiters:

```csharp
await UniTask.Yield();                                  // Next default timing for the thread you're on
await UniTask.Yield(RenderLoopTiming.OnRenderGUI);      // Resume during the GUI render pass
await UniTask.NextFrame();                              // Guaranteed next tick/frame, never the current one
await UniTask.Delay(500, cancellationToken: ct);        // Wall-clock delay
await UniTask.DelayFrame(10);                           // Wait 10 ticks/frames
await UniTask.WaitUntil(() => player.Entity.Alive);     // Poll a predicate each tick
```

# ThreadPool Workers
Heavy computation should be kept off the main thread, but the game API can only be safely touched from it. VinTask handles hopping between the two:

```csharp
// One-liner: runs on the pool, resumes on the loop you called from
var mesh = await UniTask.Run(() => BuildMeshData(snapshot));
capi.Render.UploadMesh(mesh); // Back on the main thread, safe

// Manual hops for finer control
var input = CollectInput(capi);                                  // 1. Read game state on the main thread
await UniTask.SwitchToThreadPool();                              // 2. Hop to a worker
var result = Crunch(input);                                      // 4. Heavy work here
await UniTask.SwitchToMainThread(ClientLoopTiming.OnClientTick); // 5. Hop back
capi.ShowChatMessage(result);                                    // 6. Touch the API again
```

`SwitchToMainThread` is the counterpart to `SwitchToThreadPool`. Unlike `Yield`, it completes synchronously and costs no tick when you're already on a game thread, so it's safe to call defensively. The timing argument only decides the re-entry queue used when a switch is actually needed, and is not a guarantee of resuming at that exact stage. Use `Yield(timing)` when you need a specific render stage.

Rules of thumb when using workers:
- Snapshot game state before the hop, apply results after hopping back. World data is not safe to read from worker threads (Chunks can unload under you).
- In single-player, be explicit after a pool hop. A bare `UniTask.SwitchToMainThread()` from a worker thread can't know which side you came from and prefers the client. Pass the side explicitly (`UniTask.SwitchToMainThread(ServerLoopTiming.OnServerTick)`) when it matters, or use `UniTask.Run`, which captures and restores your loop automatically.
- For streaming progress out of a long job, use `Channel.CreateSingleConsumerUnbounded<T>()`. Write from the worker, then `await foreach` on the main thread.

# Included UniTask Features
The full upstream toolkit is included and works as the [UniTask documentation](https://github.com/Cysharp/UniTask) describes:
- **WhenAll / WhenAny / WhenEach**
- **UniTaskCompletionSource, AsyncLazy, AsyncReactiveProperty**
- **UniTaskAsyncEnumerable**
  - Full async LINQ (Where, Select, Merge, Buffer, etc.)
- **Cancellation**
  - CancellationToken integration on every awaiter, `cancelImmediately` overloads, `SuppressCancellationThrow`.
- **Task/ValueTask interop**
  - `AsUniTask()`, `AsTask()`, `AsValueTask()`.
- **Fire-and-forget**
  - `.Forget()` for unawaited tasks. Unhandled exceptions are reported through `UniTaskScheduler.UnobservedTaskException` and logged to the client/server game log with a `[VinTask]` prefix.

# Limitations
VinTask installs a SynchronizationContext on the client main thread and the server tick thread, which will make plain `Task` awaits resume on the game thread instead of the pool. This comes with two consequences:
- Never block the main thread on a task. `task.Wait()` or `task.Result` on the main thread, where that task internally awaits, will deadlock (The continuation gets queued to the very thread that's blocked). This was always an anti-pattern, but with VinTask installed it freezes the game instead of limping along. Use await all the way down.
- Continuations land on the main thread. Code that did `await Task.Delay(...)` followed by heavy work was running that work on a pool thread before. Now it runs on the main thread, so if the work is genuinely heavy, wrap it in `UniTask.Run` or do manual hops.

# Testing
Debug builds register an in-game test suite:
```
.vttest all      (client chat)
/vttest all      (server chat)
```
Categories: `yield`, `nextframe`, `delay`, `waituntil`, `cancellation`, `whenall`, `completionsource`, `linq`, `channel`, `threads` (Prints the thread-ID layout of your setup), `switch` (Verifies SwitchToMainThread round-trips and the synchronous fast-path).
