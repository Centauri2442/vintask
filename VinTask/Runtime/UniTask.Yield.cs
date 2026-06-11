using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        /// <summary>
        /// Yields to the next occurrence of the default timing for the current thread.
        /// On the render thread: <see cref="RenderLoopTiming.PreRender"/>.
        /// On the client tick thread: <see cref="ClientLoopTiming.OnClientTick"/>.
        /// On the server tick thread: <see cref="ServerLoopTiming.OnServerTick"/>.
        /// </summary>
        public static YieldAwaitable Yield()
        {
            return new YieldAwaitable();
        }

        /// <summary>
        /// Yields to the next occurrence of the given render timing.
        /// </summary>
        public static YieldAwaitable Yield(RenderLoopTiming timing)
        {
            return new YieldAwaitable(timing);
        }

        /// <summary>
        /// Yields to the next occurrence of the given client tick timing.
        /// </summary>
        public static YieldAwaitable Yield(ClientLoopTiming timing)
        {
            return new YieldAwaitable(timing);
        }

        /// <summary>
        /// Yields to the next occurrence of the given server tick timing.
        /// </summary>
        public static YieldAwaitable Yield(ServerLoopTiming timing)
        {
            return new YieldAwaitable(timing);
        }

        /// <summary>
        /// Yields with cancellation support, resuming at the default timing for the current thread.
        /// </summary>
        public static UniTask Yield(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Yields with cancellation support, resuming at the given render timing.
        /// </summary>
        public static UniTask Yield(RenderLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(timing, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Yields with cancellation support, resuming at the given client tick timing.
        /// </summary>
        public static UniTask Yield(ClientLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(timing, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Yields with cancellation support, resuming at the given server tick timing.
        /// </summary>
        public static UniTask Yield(ServerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(timing, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Waits until the next game tick (guaranteed to resume on the following tick, not the current one).
        /// </summary>
        public static UniTask NextFrame()
        {
            return new UniTask(NextFramePromise.CreateDefault(CancellationToken.None, false, out var token), token);
        }

        /// <summary>
        /// Waits until the next render frame.
        /// </summary>
        public static UniTask NextFrame(RenderLoopTiming timing)
        {
            return new UniTask(NextFramePromise.Create(timing, CancellationToken.None, false, out var token), token);
        }

        /// <summary>
        /// Waits until the next client tick.
        /// </summary>
        public static UniTask NextFrame(ClientLoopTiming timing)
        {
            return new UniTask(NextFramePromise.Create(timing, CancellationToken.None, false, out var token), token);
        }

        /// <summary>
        /// Waits until the next server tick.
        /// </summary>
        public static UniTask NextFrame(ServerLoopTiming timing)
        {
            return new UniTask(NextFramePromise.Create(timing, CancellationToken.None, false, out var token), token);
        }

        /// <summary>
        /// Waits until the next game tick with cancellation support.
        /// </summary>
        public static UniTask NextFrame(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(NextFramePromise.CreateDefault(cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Yields to the end of the render frame (PostRender timing).
        /// </summary>
        public static YieldAwaitable WaitForPostRender()
        {
            return Yield(RenderLoopTiming.PostRender);
        }

        /// <summary>
        /// Yields to the end of the render frame with cancellation support.
        /// </summary>
        public static UniTask WaitForPostRender(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return Yield(RenderLoopTiming.PostRender, cancellationToken, cancelImmediately);
        }

        #region YieldAwaitable

        /// <summary>
        /// Awaitable that enqueues a continuation into the appropriate loop queue.
        /// Constructed with no timing arg for the default (thread-routed) queue.
        /// </summary>
        public readonly struct YieldAwaitable
        {
            // Sentinel values for which timing type is stored; internal so Awaiter can reference them.
            internal const int KIND_DEFAULT = 0;
            internal const int KIND_RENDER = 1;
            internal const int KIND_CLIENT = 2;
            internal const int KIND_SERVER = 3;

            readonly int _kind;
            readonly int _index;

            internal YieldAwaitable(RenderLoopTiming timing)
            {
                _kind = KIND_RENDER;
                _index = (int)timing;
            }

            internal YieldAwaitable(ClientLoopTiming timing)
            {
                _kind = KIND_CLIENT;
                _index = (int)timing;
            }

            internal YieldAwaitable(ServerLoopTiming timing)
            {
                _kind = KIND_SERVER;
                _index = (int)timing;
            }

            // No-arg public ctor: _kind defaults to KIND_DEFAULT (0), _index defaults to 0.
            // The parameterless struct ctor must be public per C# spec; callers use Yield() factory.
            public YieldAwaitable()
            {
                _kind = KIND_DEFAULT;
                _index = 0;
            }

            public Awaiter GetAwaiter()
            {
                return new Awaiter(_kind, _index);
            }

            public UniTask ToUniTask()
            {
                return Yield(CancellationToken.None);
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

                public bool IsCompleted => false;

                public void GetResult()
                {
                }

                public void OnCompleted(Action continuation)
                {
                    UnsafeOnCompleted(continuation);
                }

                public void UnsafeOnCompleted(Action continuation)
                {
                    // A dropped continuation would hang the awaiting method forever
                    // (e.g. awaiting a render timing on a dedicated server), so a
                    // missing loop must fail loudly instead.
                    bool added;
                    switch (_kind)
                    {
                        case YieldAwaitable.KIND_RENDER:
                            added = PlayerLoopHelper.TryAddContinuation((RenderLoopTiming)_index, continuation);
                            break;
                        case YieldAwaitable.KIND_CLIENT:
                            added = PlayerLoopHelper.TryAddContinuation((ClientLoopTiming)_index, continuation);
                            break;
                        case YieldAwaitable.KIND_SERVER:
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

        #region YieldPromise

        sealed class YieldPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<YieldPromise>
        {
            static TaskPool<YieldPromise> _pool;
            YieldPromise _nextNode;
            public ref YieldPromise NextNode => ref _nextNode;

            static YieldPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(YieldPromise), () => _pool.Size);
            }

            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;
            UniTaskCompletionSourceCore<object> _core;

            YieldPromise()
            {
            }

            // Default-routed factory
            public static IUniTaskSource Create(CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new YieldPromise();
                }

                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (YieldPromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddActionDefault(result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(RenderLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new YieldPromise();
                }

                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (YieldPromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(ClientLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new YieldPromise();
                }

                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (YieldPromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(ServerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new YieldPromise();
                }

                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (YieldPromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public void GetResult(short token)
            {
                try
                {
                    _core.GetResult(token);
                }
                finally
                {
                    if (!(_cancelImmediately && _cancellationToken.IsCancellationRequested))
                    {
                        TryReturn();
                    }
                    else
                    {
                        TaskTracker.RemoveTracking(this);
                    }
                }
            }

            public UniTaskStatus GetStatus(short token) => _core.GetStatus(token);
            public UniTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();
            public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

            public bool MoveNext()
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _core.TrySetCanceled(_cancellationToken);
                    return false;
                }

                _core.TrySetResult(null);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion

        #region NextFramePromise

        sealed class NextFramePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<NextFramePromise>
        {
            static TaskPool<NextFramePromise> _pool;
            NextFramePromise _nextNode;
            public ref NextFramePromise NextNode => ref _nextNode;

            static NextFramePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(NextFramePromise), () => _pool.Size);
            }

            // -1 means "registered from a non-main thread";  always skips the initial tick check.
            int _registrationTickId;
            UniTaskCompletionSourceCore<AsyncUnit> _core;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            NextFramePromise()
            {
            }

            static NextFramePromise Rent(CancellationToken cancellationToken, bool cancelImmediately)
            {
                if (!_pool.TryPop(out var result))
                {
                    result = new NextFramePromise();
                }

                result._registrationTickId = PlayerLoopHelper.IsMainThread ? PlayerLoopHelper.CurrentTickId : -1;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (NextFramePromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                return result;
            }

            public static IUniTaskSource CreateDefault(CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                var result = Rent(cancellationToken, cancelImmediately);
                PlayerLoopHelper.AddActionDefault(result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(RenderLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                var result = Rent(cancellationToken, cancelImmediately);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(ClientLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                var result = Rent(cancellationToken, cancelImmediately);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public static IUniTaskSource Create(ServerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                var result = Rent(cancellationToken, cancelImmediately);
                PlayerLoopHelper.AddAction(timing, result);
                token = result._core.Version;
                return result;
            }

            public void GetResult(short token)
            {
                try
                {
                    _core.GetResult(token);
                }
                finally
                {
                    if (!(_cancelImmediately && _cancellationToken.IsCancellationRequested))
                    {
                        TryReturn();
                    }
                    else
                    {
                        TaskTracker.RemoveTracking(this);
                    }
                }
            }

            public UniTaskStatus GetStatus(short token) => _core.GetStatus(token);
            public UniTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();
            public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

            public bool MoveNext()
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _core.TrySetCanceled(_cancellationToken);
                    return false;
                }

                // Skip completion on the same tick we were registered.
                if (_registrationTickId == PlayerLoopHelper.CurrentTickId)
                {
                    return true;
                }

                _core.TrySetResult(AsyncUnit.Default);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion
    }
}
