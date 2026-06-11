#pragma warning disable CS0618

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// Registers in-game chat commands for running the VinTask integration test suite.
    /// Only active in debug builds so the released mod does not ship test commands.
    /// Client command: .vttest [category|all]
    /// Server command: /vttest [category|all]
    /// </summary>
    public class VinTaskTestSystem : ModSystem
    {
        /// <summary>
        /// Registers the client-side .vttest chat command for running integration tests in-game.
        /// </summary>
        public override void StartClientSide(ICoreClientAPI capi)
        {
#if DEBUG
            capi.RegisterCommand(
                "vttest",
                "Run VinTask UniTask integration tests",
                ".vttest [all|yield|nextframe|delay|waituntil|cancellation|whenall|completionsource|linq|channel|threads|switch]",
                (int groupId, CmdArgs args) =>
                {
                    string filter = args.PopWord() ?? "all";

                    // ShowChatMessage requires the render thread; route all output there.
                    var runner = new TestRunner(msg =>
                        capi.Event.EnqueueMainThreadTask(
                            () => capi.ShowChatMessage(msg), "VinTask.TestOutput"));

                    UniTaskTests.Register(runner, filter);
                    runner.RunAll().Forget();
                });
#endif
        }

        /// <summary>
        /// Registers the server-side /vttest chat command for running integration tests in-game.
        /// </summary>
        public override void StartServerSide(ICoreServerAPI sapi)
        {
#if DEBUG
            sapi.RegisterCommand(
                "vttest",
                "Run VinTask UniTask integration tests",
                "/vttest [all|yield|nextframe|delay|waituntil|cancellation|whenall|completionsource|linq|channel|threads|switch]",
                (IServerPlayer player, int groupId, CmdArgs args) =>
                {
                    string filter = args.PopWord() ?? "all";
                    var runner = new TestRunner(msg =>
                        player.SendMessage(groupId, msg, EnumChatType.Notification, null));
                    UniTaskTests.Register(runner, filter);
                    runner.RunAll().Forget();
                },
                "chat");
#endif
        }
    }
}
