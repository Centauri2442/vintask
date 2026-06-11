using System;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        /// <summary>
        /// Delays for the given number of milliseconds, measured by wall-clock time.
        /// Resumes on the same thread type that called this method.
        /// </summary>
        public static UniTask Delay(int millisecondsDelay, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Delays for the given duration, measured by wall-clock time.
        /// Resumes on the same thread type that called this method.
        /// </summary>
        public static UniTask Delay(TimeSpan delayTimeSpan, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (delayTimeSpan < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTimeSpan), "Delay does not allow a negative duration.");
            }

            return new UniTask(DelayRealtimePromise.Create(delayTimeSpan, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Delays for the given number of seconds.
        /// </summary>
        public static UniTask WaitForSeconds(float duration, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromSeconds(duration), cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Delays for the given number of seconds.
        /// </summary>
        public static UniTask WaitForSeconds(int duration, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromSeconds(duration), cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Delays for the given number of game ticks on the calling thread's loop.
        /// </summary>
        public static UniTask DelayFrame(int delayFrameCount, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (delayFrameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayFrameCount), "DelayFrame does not allow a negative frame count.");
            }

            return new UniTask(DelayFramePromise.Create(delayFrameCount, cancellationToken, cancelImmediately, out var token), token);
        }

        #region Promise Implementations

        sealed class DelayRealtimePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayRealtimePromise>
        {
            static TaskPool<DelayRealtimePromise> _pool;
            DelayRealtimePromise _nextNode;
            public ref DelayRealtimePromise NextNode => ref _nextNode;

            static DelayRealtimePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(DelayRealtimePromise), () => _pool.Size);
            }

            long _delayTimeSpanTicks;
            ValueStopwatch _stopwatch;
            int _registrationTickId;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<AsyncUnit> _core;

            DelayRealtimePromise() { }

            public static IUniTaskSource Create(TimeSpan delayTimeSpan, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new DelayRealtimePromise();
                }

                result._stopwatch = ValueStopwatch.StartNew();
                result._delayTimeSpanTicks = delayTimeSpan.Ticks;
                result._registrationTickId = PlayerLoopHelper.IsMainThread ? PlayerLoopHelper.CurrentTickId : -1;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (DelayRealtimePromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddActionDefault(result);
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

                if (_stopwatch.ElapsedTicks < _delayTimeSpanTicks)
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
                _stopwatch = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        sealed class DelayFramePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayFramePromise>
        {
            static TaskPool<DelayFramePromise> _pool;
            DelayFramePromise _nextNode;
            public ref DelayFramePromise NextNode => ref _nextNode;

            static DelayFramePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(DelayFramePromise), () => _pool.Size);
            }

            int _registrationTickId;
            int _delayFrameCount;
            int _currentFrameCount;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<AsyncUnit> _core;

            DelayFramePromise()
            {
            }

            public static IUniTaskSource Create(int delayFrameCount, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new DelayFramePromise();
                }

                result._delayFrameCount = delayFrameCount;
                result._currentFrameCount = 0;
                result._registrationTickId = PlayerLoopHelper.IsMainThread ? PlayerLoopHelper.CurrentTickId : -1;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (DelayFramePromise)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddActionDefault(result);
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

                if (_currentFrameCount == 0)
                {
                    if (_delayFrameCount == 0)
                    {
                        _core.TrySetResult(AsyncUnit.Default);
                        return false;
                    }

                    // Stay on the tick we were registered to ensure at least one full tick passes.
                    if (_registrationTickId == PlayerLoopHelper.CurrentTickId)
                    {
                        return true;
                    }
                }

                if (++_currentFrameCount >= _delayFrameCount)
                {
                    _core.TrySetResult(AsyncUnit.Default);
                    return false;
                }

                return true;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _currentFrameCount = default;
                _delayFrameCount = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion
    }
}
