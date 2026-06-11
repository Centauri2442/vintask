using System;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        /// <summary>
        /// Identifies which game loop the caller was on, so work can resume there afterwards.
        /// </summary>
        enum CallerLoopKind
        {
            None,
            Render,
            ClientTick,
            ServerTick,
        }

        static CallerLoopKind CaptureCallerLoop()
        {
            if (PlayerLoopHelper.IsRenderThread) return CallerLoopKind.Render;
            if (PlayerLoopHelper.IsClientTickThread) return CallerLoopKind.ClientTick;
            if (PlayerLoopHelper.IsServerTickThread) return CallerLoopKind.ServerTick;
            return CallerLoopKind.None;
        }

        static async UniTask ReturnToCallerLoop(CallerLoopKind kind)
        {
            switch (kind)
            {
                case CallerLoopKind.Render:
                    await SwitchToMainThread(RenderLoopTiming.PreRender);
                    break;
                case CallerLoopKind.ClientTick:
                    await SwitchToMainThread(ClientLoopTiming.OnClientTick);
                    break;
                case CallerLoopKind.ServerTick:
                    await SwitchToMainThread(ServerLoopTiming.OnServerTick);
                    break;
                default:
                    // Caller was not on a game thread; stay on the pool.
                    break;
            }
        }

        /// <summary>
        /// Runs an action on the ThreadPool. When <paramref name="configureAwait"/> is true,
        /// resumes on the game loop the caller was on; otherwise stays on the ThreadPool.
        /// </summary>
        public static async UniTask Run(Action action, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    action();
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                }
            }
            else
            {
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                action();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Runs an action that takes a state argument on the ThreadPool without a closure allocation.
        /// </summary>
        public static async UniTask Run(Action<object> action, object state, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    action(state);
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                }
            }
            else
            {
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                action(state);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Runs a function on the ThreadPool and returns its result. When
        /// <paramref name="configureAwait"/> is true, resumes on the game loop the caller was on.
        /// </summary>
        public static async UniTask<T> Run<T>(Func<T> func, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return func();
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            return func();
        }

        /// <summary>
        /// Runs a function that takes a state argument on the ThreadPool without a closure allocation.
        /// </summary>
        public static async UniTask<T> Run<T>(Func<object, T> func, object state, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return func(state);
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            return func(state);
        }

        /// <summary>
        /// Runs an async function on the ThreadPool. When <paramref name="configureAwait"/> is
        /// true, resumes on the game loop the caller was on.
        /// </summary>
        public static async UniTask Run(Func<UniTask> asyncAction, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await asyncAction();
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                }
            }
            else
            {
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                await asyncAction();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Runs an async function that returns a value on the ThreadPool. When
        /// <paramref name="configureAwait"/> is true, resumes on the game loop the caller was on.
        /// </summary>
        public static async UniTask<T> Run<T>(Func<UniTask<T>> asyncFunc, bool configureAwait = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (configureAwait)
            {
                var callerLoop = CaptureCallerLoop();
                await SwitchToThreadPool();
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await asyncFunc();
                }
                finally
                {
                    await ReturnToCallerLoop(callerLoop);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            return await asyncFunc();
        }
    }
}
