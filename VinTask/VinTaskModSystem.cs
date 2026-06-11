using System;
using Cysharp.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VinTask
{
    public class VinTaskModSystem : ModSystem
    {
        Action<Exception>? _unobservedExceptionLogger;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            PlayerLoopHelper.Initialize(capi);
            HookUnobservedExceptionLogging(capi.Logger);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            PlayerLoopHelper.Initialize(sapi);
            HookUnobservedExceptionLogging(sapi.Logger);
        }

        /// <summary>
        /// Routes exceptions from forgotten/unawaited UniTasks into the game log so mod
        /// authors can see failures that would otherwise only reach the console.
        /// </summary>
        void HookUnobservedExceptionLogging(ILogger logger)
        {
            _unobservedExceptionLogger = ex => logger.Error("[VinTask] Unobserved UniTask exception: {0}", ex);
            UniTaskScheduler.UnobservedTaskException += _unobservedExceptionLogger;
        }

        public override void Dispose()
        {
            if (_unobservedExceptionLogger != null)
            {
                UniTaskScheduler.UnobservedTaskException -= _unobservedExceptionLogger;
                _unobservedExceptionLogger = null;
            }

            base.Dispose();
        }
    }
}
