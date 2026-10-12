using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using System;

namespace EssSimulator.Display
{
    public class BreakerCommand : ICommand
    {
        public string Name => "breaker";
        public string Description => "breaker 操控命令 (set)";

        public CommandResult Execute(string[] args)
        {
            if (args.Length != 2)
                return CommandResult.Fail("用法: breaker <operation> <state>");

            if (!ExternalControlGate.TryAllow(out var blocked))
                return CommandResult.Fail(blocked);

            if (!bool.TryParse(args[1], out var flag))
                return CommandResult.Fail("请输入有效的布尔值 (true/false)");

            var objectsCollect = SimulatorHost.Instance;
            EnergyStorageSystem ess = objectsCollect.Get<EnergyStorageSystem>("ess");
            if (ess == null)
                return CommandResult.Fail("找不到对应的模型");

            if (args[0] == "set")
            {
                ess.SetMainBreakerClosed(flag);
                UiSnapshotNotifier.RequestImmediatePush();
                return CommandResult.Ok($"执行成功: 主断路器 {(flag ? "合闸" : "分闸")}");
            }

            return CommandResult.Fail("操作命令参数不正确，仅支持 set");
        }
    }
}
