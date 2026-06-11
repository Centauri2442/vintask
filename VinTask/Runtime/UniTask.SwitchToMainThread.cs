using System;
using System.Runtime.CompilerServices;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        /// <summary>
        /// Switches back onto a game loop thread, the symmetric counterpart to
        /// <see cref="SwitchToThreadPool"/>. The no-argument overload routes to the default
        /// timing for the calling thread's side (render to PreRender, client to OnClientTick,
        /// server to OnServerTick); when called from a ThreadPool worker it falls back to
        /// render then client then server, like the default <see cref="Yield()"/>.
        /// <para>
        /// Unlike <see cref="Yield()"/>, this completes synchronously and costs no tick when the
        /// caller is already on a game thread. The timing only decides the re-entry queue used
        /// when a switch is actually needed; it is not a guarantee of resuming at that exact
        /// stage. Use <see cref="Yield(RenderLoopTiming)"/> when you need a specific stage.
        /// </para>
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread()
        {
            return new SwitchToMainThreadAwaitable();
        }

        /// <summary>
        /// Switches onto the render thread, re-entering at the given timing if a switch is needed.
        /// Completes synchronously when already on the render thread.
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(RenderLoopTiming timing)
        {
            return new SwitchToMainThreadAwaitable(timing);
        }

        /// <summary>
        /// Switches onto the client tick thread, re-entering at the given timing if a switch is needed.
        /// Completes synchronously when already on the client tick thread.
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(ClientLoopTiming timing)
        {
            return new SwitchToMainThreadAwaitable(timing);
        }

        /// <summary>
        /// Switches onto the server tick thread, re-entering at the given timing if a switch is needed.
        /// Completes synchronously when already on the server tick thread.
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(ServerLoopTiming timing)
        {
            return new SwitchToMainThreadAwaitable(timing);
        }

        #region SwitchToMainThreadAwaitable

        /// <summary>
        /// Awaitable that enqueues its continuation into a loop queue, or completes synchronously
        /// when the caller is already on the requested side's thread.
        /// </summary>
        public readonly struct SwitchToMainThreadAwaitable
        {
            // Sentinel values for which timing type is stored; internal so Awaiter can reference them.
            internal const int KIND_DEFAULT = 0;
            internal const int KIND_RENDER = 1;
            internal const int KIND_CLIENT = 2;
            internal const int KIND_SERVER = 3;

            readonly int _kind;
            readonly int _index;

            internal SwitchToMainThreadAwaitable(RenderLoopTiming timing)
            {
                _kind = KIND_RENDER;
                _index = (int)timing;
            }

            internal SwitchToMainThreadAwaitable(ClientLoopTiming timing)
            {
                _kind = KIND_CLIENT;
                _index = (int)timing;
            }

            internal SwitchToMainThreadAwaitable(ServerLoopTiming timing)
            {
                _kind = KIND_SERVER;
                _index = (int)timing;
            }
            
            public SwitchToMainThreadAwaitable()
            {
                _kind = KIND_DEFAULT;
                _index = 0;
            }

            public Awaiter GetAwaiter()
            {
                return new Awaiter(_kind, _index);
            }

            public readonly struct Awaiter : ICriticalNotifyCompletion
            {
                readonly int _kind;
                readonly int _index;

                internal Awaiter(int kind, int index)
                {
                    _kind = kind;
                    _index = index;
                }

                public bool IsCompleted
                {
                    get
                    {
                        switch (_kind)
                        {
                            case KIND_RENDER:
                                return PlayerLoopHelper.IsRenderThread;
                            case KIND_CLIENT:
                                return PlayerLoopHelper.IsClientTickThread;
                            case KIND_SERVER:
                                return PlayerLoopHelper.IsServerTickThread;
                            default:
                                return PlayerLoopHelper.IsMainThread;
                        }
                    }
                }

                public void GetResult()
                {
                }

                public void OnCompleted(Action continuation)
                {
                    UnsafeOnCompleted(continuation);
                }

                public void UnsafeOnCompleted(Action continuation)
                {
                    bool added;
                    switch (_kind)
                    {
                        case KIND_RENDER:
                            added = PlayerLoopHelper.TryAddContinuation((RenderLoopTiming)_index, continuation);
                            break;
                        case KIND_CLIENT:
                            added = PlayerLoopHelper.TryAddContinuation((ClientLoopTiming)_index, continuation);
                            break;
                        case KIND_SERVER:
                            added = PlayerLoopHelper.TryAddContinuation((ServerLoopTiming)_index, continuation);
                            break;
                        default:
                            added = PlayerLoopHelper.TryAddContinuationDefault(continuation);
                            break;
                    }

                    if (!added)
                    {
                        throw new InvalidOperationException(
                            "PlayerLoopHelper is not initialized for the requested loop timing. " +
                            "Render timings only exist on the client; call Initialize from StartClientSide/StartServerSide.");
                    }
                }
            }
        }

        #endregion
    }
}
