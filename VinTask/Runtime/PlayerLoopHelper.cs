using System;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// Timings driven by the render/OpenGL thread on the client. Each value corresponds to a
    /// <see cref="EnumRenderStage"/> and runs on the render thread each frame.
    /// </summary>
    public enum RenderLoopTiming
    {
        /// <summary>
        /// Start of the render frame, before any geometry is drawn.
        /// </summary>
        PreRender = 0,

        /// <summary>
        /// After 3D rendering and post-processing effects, before the GUI pass.
        /// </summary>
        AfterPostProcessing = 1,

        /// <summary>
        /// During the 2D orthographic pass (HUD, GUI, overlays).
        /// </summary>
        OnRenderGUI = 2,

        /// <summary>
        /// End of the render frame; all rendering complete, buffers not yet swapped.
        /// </summary>
        PostRender = 3,
    }

    /// <summary>
    /// Timings driven by the client game-tick listener. Note: in Vintage Story the client
    /// game tick and rendering typically run on the same main thread, so these timings are
    /// distinct points within a frame rather than a separate thread. Prefer render timings
    /// for anything that must align with a specific <see cref="EnumRenderStage"/>.
    /// </summary>
    public enum ClientLoopTiming
    {
        /// <summary>
        /// Very start of each client game tick
        /// </summary>
        PreClientTick = 0,

        /// <summary>
        /// Main client game tick
        /// </summary>
        OnClientTick = 1,

        /// <summary>
        /// After main game tick
        /// </summary>
        LateClientTick = 2,

        /// <summary>
        /// Very end of the client game tick
        /// </summary>
        PostLateClientTick = 3,
    }

    /// <summary>
    /// Timings driven by the server game tick. Runs once per server tick on the server thread.
    /// </summary>
    public enum ServerLoopTiming
    {
        /// <summary>
        /// Very start of each server tick, before game systems run.
        /// </summary>
        PreServerTick = 0,

        /// <summary>
        /// Main server tick
        /// </summary>
        OnServerTick = 1,

        /// <summary>
        /// After main server tick
        /// </summary>
        LateServerTick = 2,

        /// <summary>
        /// Very end of the server tick
        /// </summary>
        PostLateServerTick = 3,
    }

    /// <summary>
    /// Item registered with a timing's runner; MoveNext is called once per tick.
    /// Returns true to keep running, false to remove.
    /// </summary>
    public interface IPlayerLoopItem
    {
        bool MoveNext();
    }

    /// <summary>
    /// Drives UniTask scheduling against Vintage Story's render and game-tick systems.
    /// Call <see cref="Initialize(ICoreClientAPI)"/> from <c>StartClientSide</c> and
    /// <see cref="Initialize(ICoreServerAPI)"/> from <c>StartServerSide</c>.
    /// </summary>
    public static class PlayerLoopHelper
    {
        const int RENDER_TIMING_COUNT = 4;
        const int CLIENT_TIMING_COUNT = 4;
        const int SERVER_TIMING_COUNT = 4;

        static int _renderGeneration;
        static int _clientGeneration;
        static int _serverGeneration;

        static int _renderThreadId;
        static int _clientTickThreadId;
        static int _serverTickThreadId;

        // Internal: read by UniTaskRenderer
        internal static int RenderGeneration => _renderGeneration;
        internal static int ClientGeneration => _clientGeneration;
        internal static int ServerGeneration => _serverGeneration;
        internal static int RenderThreadIdInternal => _renderThreadId;

        internal static ContinuationQueue[] _renderYielders = null!;
        internal static PlayerLoopRunner[] _renderRunners = null!;
        internal static ContinuationQueue[] _clientYielders = null!;
        internal static PlayerLoopRunner[] _clientRunners = null!;
        internal static ContinuationQueue[] _serverYielders = null!;
        internal static PlayerLoopRunner[] _serverRunners = null!;

        /// <summary
        /// >Gets the managed thread ID of the render/OpenGL thread (client only).
        /// </summary>
        public static int RenderThreadId => _renderThreadId;

        /// <summary>
        /// Gets the managed thread ID of the client game-logic tick thread.
        /// </summary>
        public static int ClientTickThreadId => _clientTickThreadId;

        /// <summary>
        /// Gets the managed thread ID of the server tick thread.
        /// </summary>
        public static int ServerTickThreadId => _serverTickThreadId;

        /// <summary>
        /// Gets whether the calling code is on the render/OpenGL thread.
        /// </summary>
        public static bool IsRenderThread => Environment.CurrentManagedThreadId == _renderThreadId && _renderThreadId != 0;

        /// <summary>
        /// Gets whether the calling code is on the client game-logic tick thread.
        /// </summary>
        public static bool IsClientTickThread => Environment.CurrentManagedThreadId == _clientTickThreadId && _clientTickThreadId != 0;

        /// <summary>
        /// Gets whether the calling code is on the server tick thread.
        /// </summary>
        public static bool IsServerTickThread => Environment.CurrentManagedThreadId == _serverTickThreadId && _serverTickThreadId != 0;

        /// <summary>
        /// Gets whether the calling code is on any recognized game thread.
        /// </summary>
        public static bool IsMainThread => IsRenderThread || IsClientTickThread || IsServerTickThread;

        /// <summary>
        /// Render frame counter, incremented once per frame on the render thread.
        /// </summary>
        public static int RenderTickId { get; internal set; }

        /// <summary>
        /// Client game tick counter, incremented once per client tick.
        /// </summary>
        public static int ClientTickId { get; internal set; }

        /// <summary>
        /// Server tick counter, incremented once per server tick.
        /// </summary>
        public static int ServerTickId { get; internal set; }

        /// <summary>
        /// Returns the tick counter relevant to the calling thread:
        /// <see cref="RenderTickId"/> on the render thread, <see cref="ClientTickId"/> on the
        /// client tick thread, and <see cref="ServerTickId"/> otherwise.
        /// </summary>
        public static int CurrentTickId =>
            IsRenderThread ? RenderTickId :
            IsClientTickThread ? ClientTickId :
            ServerTickId;

        #region Initialize

        /// <summary>
        /// Initializes render-thread and client-tick runners for the client side.
        /// Call from <c>StartClientSide(ICoreClientAPI)</c>.
        /// </summary>
        public static void Initialize(ICoreClientAPI capi)
        {
            int renderGen = Interlocked.Increment(ref _renderGeneration);
            _renderThreadId = 0;
            _renderYielders = new ContinuationQueue[RENDER_TIMING_COUNT];
            _renderRunners = new PlayerLoopRunner[RENDER_TIMING_COUNT];
            for (int i = 0; i < RENDER_TIMING_COUNT; i++)
            {
                _renderYielders[i] = new ContinuationQueue();
                _renderRunners[i] = new PlayerLoopRunner();
            }

            capi.Event.RegisterRenderer(
                new UniTaskRenderer(renderGen, (int)RenderLoopTiming.PreRender, incrementTick: true),
                EnumRenderStage.Before, "VinTask.PreRender");
            capi.Event.RegisterRenderer(
                new UniTaskRenderer(renderGen, (int)RenderLoopTiming.AfterPostProcessing, incrementTick: false),
                EnumRenderStage.AfterPostProcessing, "VinTask.AfterPostProcessing");
            capi.Event.RegisterRenderer(
                new UniTaskRenderer(renderGen, (int)RenderLoopTiming.OnRenderGUI, incrementTick: false),
                EnumRenderStage.Ortho, "VinTask.OnRenderGUI");
            capi.Event.RegisterRenderer(
                new UniTaskRenderer(renderGen, (int)RenderLoopTiming.PostRender, incrementTick: false),
                EnumRenderStage.Done, "VinTask.PostRender");

            int clientGen = Interlocked.Increment(ref _clientGeneration);
            _clientTickThreadId = 0;
            _clientYielders = new ContinuationQueue[CLIENT_TIMING_COUNT];
            _clientRunners = new PlayerLoopRunner[CLIENT_TIMING_COUNT];
            for (int i = 0; i < CLIENT_TIMING_COUNT; i++)
            {
                _clientYielders[i] = new ContinuationQueue();
                _clientRunners[i] = new PlayerLoopRunner();
            }

            capi.Event.RegisterGameTickListener(dt =>
            {
                if (_clientGeneration != clientGen)
                {
                    return;
                }

                if (_clientTickThreadId == 0)
                {
                    _clientTickThreadId = Environment.CurrentManagedThreadId;
                    
                    if (!IsLiveContext(SynchronizationContext.Current))
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new VintageStoryLoopSynchronizationContext(
                                _clientYielders[(int)ClientLoopTiming.OnClientTick]));
                    }
                }

                // PreClientTick runs first, then the tick advances, then On/Late/PostLate follow.
                _clientYielders[(int)ClientLoopTiming.PreClientTick].Run();
                _clientRunners[(int)ClientLoopTiming.PreClientTick].Run();
                ClientTickId++;
                for (int i = (int)ClientLoopTiming.OnClientTick; i < CLIENT_TIMING_COUNT; i++)
                {
                    _clientYielders[i].Run();
                    _clientRunners[i].Run();
                }
            }, 1);
        }

        /// <summary>
        /// Initializes server-tick runners for the server side.
        /// Call from <c>StartServerSide(ICoreServerAPI)</c>.
        /// </summary>
        public static void Initialize(ICoreServerAPI sapi)
        {
            int serverGen = Interlocked.Increment(ref _serverGeneration);
            _serverTickThreadId = Environment.CurrentManagedThreadId;
            _serverYielders = new ContinuationQueue[SERVER_TIMING_COUNT];
            _serverRunners = new PlayerLoopRunner[SERVER_TIMING_COUNT];
            for (int i = 0; i < SERVER_TIMING_COUNT; i++)
            {
                _serverYielders[i] = new ContinuationQueue();
                _serverRunners[i] = new PlayerLoopRunner();
            }

            SynchronizationContext.SetSynchronizationContext(
                new VintageStoryLoopSynchronizationContext(
                    _serverYielders[(int)ServerLoopTiming.OnServerTick]));

            sapi.Event.RegisterGameTickListener(dt =>
            {
                if (_serverGeneration != serverGen)
                {
                    return;
                }

                // PreServerTick runs first, then the tick advances, then On/Late/PostLate follow.
                _serverYielders[(int)ServerLoopTiming.PreServerTick].Run();
                _serverRunners[(int)ServerLoopTiming.PreServerTick].Run();
                ServerTickId++;
                for (int i = (int)ServerLoopTiming.OnServerTick; i < SERVER_TIMING_COUNT; i++)
                {
                    _serverYielders[i].Run();
                    _serverRunners[i].Run();
                }
            }, 1);
        }

        #endregion

        #region Internal

        internal static void CaptureRenderThread()
        {
            _renderThreadId = Environment.CurrentManagedThreadId;
            
            if (!IsLiveContext(SynchronizationContext.Current))
            {
                SynchronizationContext.SetSynchronizationContext(
                    new VintageStoryLoopSynchronizationContext(
                        _renderYielders[(int)RenderLoopTiming.PreRender]));
            }
        }

        /// <summary>
        /// Returns true only if the context is a VinTask context whose queue is still being
        /// drained by the current generation's runners.
        /// </summary>
        internal static bool IsLiveContext(SynchronizationContext? context)
        {
            if (!(context is VintageStoryLoopSynchronizationContext vsContext))
            {
                return false;
            }

            return ContainsQueue(_renderYielders, vsContext.Queue)
                || ContainsQueue(_clientYielders, vsContext.Queue)
                || ContainsQueue(_serverYielders, vsContext.Queue);
        }

        static bool ContainsQueue(ContinuationQueue[]? queues, ContinuationQueue queue)
        {
            if (queues == null)
            {
                return false;
            }

            for (int i = 0; i < queues.Length; i++)
            {
                if (ReferenceEquals(queues[i], queue))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Scheduling

        /// <summary>
        /// Registers a one-shot continuation to run at the given render timing.
        /// </summary>
        public static bool TryAddContinuation(RenderLoopTiming timing, Action continuation)
        {
            var queue = _renderYielders?[(int)timing];
            if (queue == null)
            {
                return false;
            }

            queue.Enqueue(continuation);
            return true;
        }

        /// <summary>
        /// Registers a persistent loop item to be ticked at the given render timing.
        /// </summary>
        public static void AddAction(RenderLoopTiming timing, IPlayerLoopItem? action)
        {
            var runner = _renderRunners?[(int)timing];
            if (runner == null)
            {
                throw new InvalidOperationException("PlayerLoopHelper is not initialized for the render thread.");
            }

            runner.AddAction(action);
        }

        /// <summary>Registers a one-shot continuation to run at the given client tick timing.</summary>
        public static bool TryAddContinuation(ClientLoopTiming timing, Action continuation)
        {
            var queue = _clientYielders?[(int)timing];
            if (queue == null)
            {
                return false;
            }

            queue.Enqueue(continuation);
            return true;
        }

        /// <summary>Registers a persistent loop item to be ticked at the given client tick timing.</summary>
        public static void AddAction(ClientLoopTiming timing, IPlayerLoopItem? action)
        {
            var runner = _clientRunners?[(int)timing];
            if (runner == null)
            {
                throw new InvalidOperationException("PlayerLoopHelper is not initialized for the client tick thread.");
            }

            runner.AddAction(action);
        }

        /// <summary>Registers a one-shot continuation to run at the given server tick timing.</summary>
        public static bool TryAddContinuation(ServerLoopTiming timing, Action continuation)
        {
            var queue = _serverYielders?[(int)timing];
            if (queue == null)
            {
                return false;
            }

            queue.Enqueue(continuation);
            return true;
        }

        /// <summary>Registers a persistent loop item to be ticked at the given server timing.</summary>
        public static void AddAction(ServerLoopTiming timing, IPlayerLoopItem? action)
        {
            var runner = _serverRunners?[(int)timing];
            if (runner == null)
            {
                throw new InvalidOperationException("PlayerLoopHelper is not initialized for the server tick thread.");
            }

            runner.AddAction(action);
        }

        #endregion

        #region Default Routing

        /// <summary>
        /// Enqueues a continuation to the default queue for the current thread.
        /// Routes to the render thread's PreRender queue, the client tick's OnClientTick queue,
        /// or the server's OnServerTick queue; whichever matches the calling thread.
        /// WARNING: when called from a threadpool thread the side is unknown, so this falls
        /// back to render → client → server in that order. In single-player that means
        /// server-side code resuming after a threadpool hop lands on the client render
        /// thread; use an explicit timing overload (e.g. Yield(ServerLoopTiming.OnServerTick))
        /// after SwitchToThreadPool when the side matters.
        /// </summary>
        internal static bool TryAddContinuationDefault(Action continuation)
        {
            if (IsRenderThread)
            {
                _renderYielders[(int)RenderLoopTiming.PreRender].Enqueue(continuation);
                return true;
            }

            if (IsClientTickThread)
            {
                _clientYielders[(int)ClientLoopTiming.OnClientTick].Enqueue(continuation);
                return true;
            }

            if (IsServerTickThread)
            {
                _serverYielders[(int)ServerLoopTiming.OnServerTick].Enqueue(continuation);
                return true;
            }

            // Threadpool fallback: prefer render → client tick → server tick
            if (_renderYielders != null)
            {
                _renderYielders[(int)RenderLoopTiming.PreRender].Enqueue(continuation);
                return true;
            }

            if (_clientYielders != null)
            {
                _clientYielders[(int)ClientLoopTiming.OnClientTick].Enqueue(continuation);
                return true;
            }

            if (_serverYielders != null)
            {
                _serverYielders[(int)ServerLoopTiming.OnServerTick].Enqueue(continuation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Registers a persistent loop item in the default runner for the current thread.
        /// Same routing logic as <see cref="TryAddContinuationDefault"/>.
        /// </summary>
        internal static void AddActionDefault(IPlayerLoopItem? action)
        {
            if (IsRenderThread)
            {
                _renderRunners[(int)RenderLoopTiming.PreRender].AddAction(action);
                return;
            }

            if (IsClientTickThread)
            {
                _clientRunners[(int)ClientLoopTiming.OnClientTick].AddAction(action);
                return;
            }

            if (IsServerTickThread)
            {
                _serverRunners[(int)ServerLoopTiming.OnServerTick].AddAction(action);
                return;
            }

            // Threadpool fallback
            if (_renderRunners != null)
            {
                _renderRunners[(int)RenderLoopTiming.PreRender].AddAction(action);
                return;
            }

            if (_clientRunners != null)
            {
                _clientRunners[(int)ClientLoopTiming.OnClientTick].AddAction(action);
                return;
            }

            if (_serverRunners != null)
            {
                _serverRunners[(int)ServerLoopTiming.OnServerTick].AddAction(action);
                return;
            }

            throw new InvalidOperationException("PlayerLoopHelper is not initialized. Call Initialize from StartClientSide or StartServerSide.");
        }

        #endregion
    }

    /// <summary>
    /// <see cref="IRenderer"/> implementation that drains one timing slot's queues each frame.
    /// Registered once per <see cref="EnumRenderStage"/>; uses a generation counter for zombie protection.
    /// </summary>
    internal sealed class UniTaskRenderer : IRenderer
    {
        readonly int _expectedGeneration;
        readonly int _timingIndex;
        readonly bool _incrementTick;

        internal UniTaskRenderer(int generation, int timingIndex, bool incrementTick)
        {
            _expectedGeneration = generation;
            _timingIndex = timingIndex;
            _incrementTick = incrementTick;
        }

        public double RenderOrder => 0.01;
        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (PlayerLoopHelper.RenderGeneration != _expectedGeneration)
            {
                return;
            }

            if (PlayerLoopHelper.RenderThreadIdInternal == 0)
            {
                PlayerLoopHelper.CaptureRenderThread();
            }

            if (_incrementTick)
            {
                PlayerLoopHelper.RenderTickId++;
            }

            PlayerLoopHelper._renderYielders[_timingIndex].Run();
            PlayerLoopHelper._renderRunners[_timingIndex].Run();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// <see cref="SynchronizationContext"/> that queues <see cref="SynchronizationContext.Post"/>
    /// callbacks into a <see cref="ContinuationQueue"/>, ensuring continuations resume on the
    /// thread that drains that queue.
    /// </summary>
    internal sealed class VintageStoryLoopSynchronizationContext : SynchronizationContext
    {
        readonly ContinuationQueue _queue;

        internal ContinuationQueue Queue => _queue;

        public VintageStoryLoopSynchronizationContext(ContinuationQueue queue)
        {
            _queue = queue;
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _queue.Enqueue(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            d(state);
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }
    }
}
