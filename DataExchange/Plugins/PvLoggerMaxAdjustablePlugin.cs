using System;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.DataExchange.Adapters;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.DataExchange.Plugins
{
    /// <summary>
    /// Logger 最大可调有功（kW）：读取时按“单机上限优先、否则取辐照可发”汇总。
    ///
    /// 为什么不在点表里直接绑 <c>Logger.MaxAdjustableActivePowerKw</c>：
    /// 模型每个仿真步都会在 <c>PvLogger.Refresh</c> 里按辐照重写该属性，
    /// 门面只能在步末（<c>AfterPlantStep</c>）再按上限覆盖；Modbus 遥测线程
    /// 若在两者之间采样，就会读到辐照值（例如上限 100×4 时读到 1280）。
    /// 插件在每次读点瞬间计算，保证 yc20 与当前上限一致。
    ///
    /// 点表绑定：<c>model=plugin|arg1=PvLoggerMaxAdjustableKw|arg2=pvDeviceId</c>。
    /// </summary>
    public sealed class PvLoggerMaxAdjustablePlugin : ITelemetryPlugin
    {
        public const string WordKey = "PvLoggerMaxAdjustableKw";

        public bool CanHandle(string wordKey) =>
            string.Equals(wordKey, WordKey, StringComparison.OrdinalIgnoreCase);

        public object? Compute(string wordKey, string deviceRoot, ISimulationDataAdapter simulation)
        {
            if (string.IsNullOrWhiteSpace(deviceRoot))
                return null;

            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null)
                return null;

            string root = deviceRoot.Trim();
            if (!root.StartsWith("pv", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(root.AsSpan(2), out int pv1Based) ||
                pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                return null;

            var unit = ess.PvUnits[pv1Based - 1];
            double sum = 0;
            for (int i = 0; i < unit.Inverters.Count; i++)
            {
                var inv = unit.Inverters[i];
                if (!inv.IsExternalRunCommand || inv.GetCurrentState().Mode != OperationMode.Normal)
                    continue;

                string reason = inv.LimitReason;
                if (reason == "电网断开" || reason == "停机")
                    continue;

                double basis = inv.AvailableAcPowerKw;
                if (DeviceControlFacade.TryReadPvInverterMaxDischarge(pv1Based, i, out double capKw))
                    basis = capKw;

                sum += Math.Min(inv.RatedPowerKw, Math.Max(0, basis));
            }

            return double.IsFinite(sum) ? sum : null;
        }
    }
}
