using EssSimulator.Core;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.DataExchange.Effects
{
    /// <summary>EMU 控制点变更后立即驱动 PCS / 单元断路器逻辑。</summary>
    public sealed class EmuPcsControlEffect : IControlEffect
    {
        private readonly Action? _refreshTelemetry;

        public EmuPcsControlEffect(Action? refreshTelemetry = null) =>
            _refreshTelemetry = refreshTelemetry;

        public ControlEffectId Id => ControlEffectId.PcsApplyCommands;

        public void OnControlChanged(ControlEffectContext context)
        {
            if (!TryResolveEmuUnit(context, out int unit1Based))
                return;

            EmuCommandPipeline.TryApplyUnit(unit1Based, _refreshTelemetry);
        }

        /// <summary>
        /// 解析控制点作用的机组号：优先绑定根路径 emu{n}（一台 PCS 一路时 simEmu 号与机组号不必 1:1）；
        /// 无绑定时再从 simEmu{n} 设备号回退。
        /// </summary>
        internal static bool TryResolveEmuUnit(ControlEffectContext context, out int unit1Based)
        {
            if (TryParseEmuRoot(context.Binding.Target.RootKey, out unit1Based))
                return true;

            return TryParseEmuUnit(context.ServerName, out unit1Based);
        }

        internal static bool TryParseEmuRoot(string? rootKey, out int unit1Based)
        {
            unit1Based = 0;
            if (string.IsNullOrWhiteSpace(rootKey) ||
                !rootKey.StartsWith("emu", StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(rootKey.AsSpan(3), out unit1Based) && unit1Based >= 1;
        }

        internal static bool TryParseEmuUnit(string serverName, out int unit1Based)
        {
            unit1Based = 0;
            if (string.IsNullOrWhiteSpace(serverName) ||
                !serverName.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(serverName.AsSpan(6), out unit1Based) && unit1Based >= 1;
        }
    }

    /// <summary>EMU 单元高压断路器（Breaker.Closed / Emu.PowerOnOff → 电气网络单元断路器）。</summary>
    public sealed class EmuUnitBreakerEffect : IControlEffect
    {
        public ControlEffectId Id => ControlEffectId.UnitHighVoltageBreaker;

        public void OnControlChanged(ControlEffectContext context)
        {
            if (!EmuPcsControlEffect.TryResolveEmuUnit(context, out int unit1Based))
                return;

            var emu = SimulatorHost.Instance.TryGetEmu(unit1Based);
            if (emu == null)
                return;

            bool closed = IsEmuLevelBreakerClosed(context.Binding.Target.PropertyPath)
                ? emu.Breaker.Closed != 0
                : emu.Emu.PowerOnOff != 0;
            DeviceControlFacade.TrySetUnitBreaker(unit1Based, closed, out _);
        }

        internal static bool IsEmuLevelBreakerClosed(string? propertyPath) =>
            string.Equals(propertyPath, "Breaker.Closed", StringComparison.OrdinalIgnoreCase);
    }
}
