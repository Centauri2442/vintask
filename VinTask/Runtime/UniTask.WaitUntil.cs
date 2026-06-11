using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        /// <summary>
        /// Waits until the predicate returns true, polling once per tick on the calling thread's loop.
        /// </summary>
        public static UniTask WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return new UniTask(WaitUntilPromise.Create(predicate, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Waits until the predicate returns true, using an explicit state argument to avoid allocations.
        /// </summary>
        public static UniTask WaitUntil<T>(T state, Func<T, bool> predicate, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return new UniTask(WaitUntilPromise<T>.Create(state, predicate, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Waits while the predicate returns true, polling once per tick on the calling thread's loop.
        /// </summary>
        public static UniTask WaitWhile(Func<bool> predicate, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return new UniTask(WaitWhilePromise.Create(predicate, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Waits while the predicate returns true, using an explicit state argument to avoid allocations.
        /// </summary>
        public static UniTask WaitWhile<T>(T state, Func<T, bool> predicate, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return new UniTask(WaitWhilePromise<T>.Create(state, predicate, cancellationToken, cancelImmediately, out var token), token);
        }

        /// <summary>
        /// Completes successfully when the cancellation token is cancelled.
        /// </summary>
        public static UniTask WaitUntilCanceled(CancellationToken cancellationToken, bool completeImmediately = false)
        {
            return new UniTask(WaitUntilCanceledPromise.Create(cancellationToken, completeImmediately, out var token), token);
        }

        /// <summary>
        /// Polls a selector on the target object each tick and completes with the new value when it changes.
        /// Uses a WeakReference so GC'd targets cancel the wait.
        /// </summary>
        public static UniTask<U> WaitUntilValueChanged<T, U>(
            T target,
            Func<T, U> monitorFunction,
            IEqualityComparer<U> equalityComparer = null,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
            where T : class
        {
            return new UniTask<U>(
                WaitUntilValueChangedPromise<T, U>.Create(target, monitorFunction, equalityComparer, cancellationToken, cancelImmediately, out var token),
                token);
        }

        #region WaitUntilPromise

        sealed class WaitUntilPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitUntilPromise>
        {
            static TaskPool<WaitUntilPromise> _pool;
            WaitUntilPromise _nextNode;
            public ref WaitUntilPromise NextNode => ref _nextNode;

            static WaitUntilPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitUntilPromise), () => _pool.Size);
            }

            Func<bool> _predicate;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<object> _core;

            WaitUntilPromise()
            {
            }

            public static IUniTaskSource Create(Func<bool> predicate, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitUntilPromise();
                }

                result._predicate = predicate;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitUntilPromise)state;
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

                try
                {
                    if (!_predicate())
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _core.TrySetException(ex);
                    return false;
                }

                _core.TrySetResult(null);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _predicate = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion

        #region WaitUntilPromise<T>

        sealed class WaitUntilPromise<T> : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitUntilPromise<T>>
        {
            static TaskPool<WaitUntilPromise<T>> _pool;
            WaitUntilPromise<T> _nextNode;
            public ref WaitUntilPromise<T> NextNode => ref _nextNode;

            static WaitUntilPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitUntilPromise<T>), () => _pool.Size);
            }

            Func<T, bool> _predicate;
            T _argument;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<object> _core;

            WaitUntilPromise()
            {
            }

            public static IUniTaskSource Create(T argument, Func<T, bool> predicate, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitUntilPromise<T>();
                }

                result._predicate = predicate;
                result._argument = argument;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitUntilPromise<T>)state;
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

                try
                {
                    if (!_predicate(_argument))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _core.TrySetException(ex);
                    return false;
                }

                _core.TrySetResult(null);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _predicate = default;
                _argument = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion

        #region WaitWhilePromise

        sealed class WaitWhilePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitWhilePromise>
        {
            static TaskPool<WaitWhilePromise> _pool;
            WaitWhilePromise _nextNode;
            public ref WaitWhilePromise NextNode => ref _nextNode;

            static WaitWhilePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitWhilePromise), () => _pool.Size);
            }

            Func<bool> _predicate;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<object> _core;

            WaitWhilePromise()
            {
            }

            public static IUniTaskSource Create(Func<bool> predicate, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitWhilePromise();
                }

                result._predicate = predicate;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitWhilePromise)state;
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

                try
                {
                    if (_predicate())
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _core.TrySetException(ex);
                    return false;
                }

                _core.TrySetResult(null);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _predicate = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion

        #region WaitWhilePromise<T>

        sealed class WaitWhilePromise<T> : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitWhilePromise<T>>
        {
            static TaskPool<WaitWhilePromise<T>> _pool;
            WaitWhilePromise<T> _nextNode;
            public ref WaitWhilePromise<T> NextNode => ref _nextNode;

            static WaitWhilePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitWhilePromise<T>), () => _pool.Size);
            }

            Func<T, bool> _predicate;
            T _argument;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<object> _core;

            WaitWhilePromise()
            {
            }

            public static IUniTaskSource Create(T argument, Func<T, bool> predicate, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitWhilePromise<T>();
                }

                result._predicate = predicate;
                result._argument = argument;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitWhilePromise<T>)state;
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

                try
                {
                    if (_predicate(_argument))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _core.TrySetException(ex);
                    return false;
                }

                _core.TrySetResult(null);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _predicate = default;
                _argument = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion

        #region WaitUntilCanceledPromise

        sealed class WaitUntilCanceledPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitUntilCanceledPromise>
        {
            static TaskPool<WaitUntilCanceledPromise> _pool;
            WaitUntilCanceledPromise _nextNode;
            public ref WaitUntilCanceledPromise NextNode => ref _nextNode;

            static WaitUntilCanceledPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitUntilCanceledPromise), () => _pool.Size);
            }

            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<object> _core;

            WaitUntilCanceledPromise()
            {
            }

            public static IUniTaskSource Create(CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitUntilCanceledPromise();
                }

                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitUntilCanceledPromise)state;
                        promise._core.TrySetResult(null);
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
                    _core.TrySetResult(null);
                    return false;
                }

                return true;
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

        #region WaitUntilValueChangedPromise<T, U>

        sealed class WaitUntilValueChangedPromise<T, U> : IUniTaskSource<U>, IPlayerLoopItem, ITaskPoolNode<WaitUntilValueChangedPromise<T, U>>
            where T : class
        {
            static TaskPool<WaitUntilValueChangedPromise<T, U>> _pool;
            WaitUntilValueChangedPromise<T, U> _nextNode;
            public ref WaitUntilValueChangedPromise<T, U> NextNode => ref _nextNode;

            static WaitUntilValueChangedPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(WaitUntilValueChangedPromise<T, U>), () => _pool.Size);
            }

            WeakReference<T> _target;
            U _currentValue;
            Func<T, U> _monitorFunction;
            IEqualityComparer<U> _equalityComparer;
            CancellationToken _cancellationToken;
            CancellationTokenRegistration _cancellationTokenRegistration;
            bool _cancelImmediately;

            UniTaskCompletionSourceCore<U> _core;

            WaitUntilValueChangedPromise()
            {
            }

            public static IUniTaskSource<U> Create(T target, Func<T, U> monitorFunction, IEqualityComparer<U> equalityComparer, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource<U>.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new WaitUntilValueChangedPromise<T, U>();
                }

                result._target = new WeakReference<T>(target, false);
                result._monitorFunction = monitorFunction;
                result._currentValue = monitorFunction(target);
                result._equalityComparer = equalityComparer ?? EqualityComparer<U>.Default;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                    {
                        var promise = (WaitUntilValueChangedPromise<T, U>)state;
                        promise._core.TrySetCanceled(promise._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddActionDefault(result);
                token = result._core.Version;
                return result;
            }

            public U GetResult(short token)
            {
                try
                {
                    return _core.GetResult(token);
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

            void IUniTaskSource.GetResult(short token)
            {
                GetResult(token);
            }

            public UniTaskStatus GetStatus(short token) => _core.GetStatus(token);
            public UniTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();
            public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

            public bool MoveNext()
            {
                if (_cancellationToken.IsCancellationRequested || !_target.TryGetTarget(out var t))
                {
                    _core.TrySetCanceled(_cancellationToken);
                    return false;
                }

                U nextValue = default;
                try
                {
                    nextValue = _monitorFunction(t);
                    if (_equalityComparer.Equals(_currentValue, nextValue))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _core.TrySetException(ex);
                    return false;
                }

                _core.TrySetResult(nextValue);
                return false;
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _target = default;
                _currentValue = default;
                _monitorFunction = default;
                _equalityComparer = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                _cancelImmediately = default;
                return _pool.TryPush(this);
            }
        }

        #endregion
    }
}
