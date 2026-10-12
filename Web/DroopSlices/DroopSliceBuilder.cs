using EssSimulator.Core;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Effects;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.Web.DroopSlices
{
    /// <summary>从当前仿真态构建白盒切片。</summary>
    public static class DroopSliceBuilder
    {
        public static DroopSlice Build(
            string serverName,
            PointBinding binding,
            object appliedValue,
            object? previousValue,
            long sequence)
        {
            ParsePcsTarget(binding.Target.PropertyPath, out int unitIndex0, out int slotInUnit, out bool isActive);
            if (EmuPcsControlEffect.TryParseEmuRoot(binding.Target.RootKey, out int unit1Based))
                unitIndex0 = unit1Based - 1;
            else
                ApplyUnitFromServer(serverName, ref unitIndex0);
            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            int channelIndex = (ess?.PcsBaseIndexOfUnit(unitIndex0) ?? unitIndex0 * 2) + slotInUnit;
            double applied = ToDouble(appliedValue);
            double? previous = previousValue == null ? null : ToDouble(previousValue);

            var emu = SimulatorHost.Instance.Get<EnergyManagementData>($"emu{unitIndex0 + 1}");
            var bms = SimulatorHost.Instance.Get<BatteryManagementSystemData>($"bms{channelIndex + 1}");

            var pcsData = emu?.PcsList != null && slotInUnit < emu.PcsList.Count
                ? emu.PcsList[slotInUnit]
                : null;
            var stack = bms?.BatteryStacks is { Count: > 0 } ? bms.BatteryStacks[0] : null;

            double pcsActualP = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{channelIndex}]._currentState.ActivePower");
            double pcsActualQ = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{channelIndex}]._currentState.ReactivePower");
            double pcsLineV = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{channelIndex}]._currentState.AcVoltage");
            double pcsFreq = GuiSimDataAccess.SafeGetDouble(
                $"ess._pcsList[{channelIndex}]._currentState.Frequency", 50);
            string mode = GuiSimDataAccess.SafeGetString(
                $"ess._pcsList[{channelIndex}]._currentState.Mode", "");

            bool unitBreakerClosed = ess != null && ess.IsUnitBreakerClosed(unitIndex0);

            return new DroopSlice
            {
                Id = Guid.NewGuid(),
                Sequence = sequence,
                TimestampUtc = DateTimeOffset.UtcNow,
                Trigger = new DroopSliceTrigger
                {
                    ServerName = serverName,
                    ParamName = binding.ParamName,
                    Kind = isActive ? "activePowerSetting" : "reactivePowerSetting",
                    TargetPath = binding.Target.FullPath,
                    EngineeringValue = applied,
                    PreviousEngineeringValue = previous,
                    Unit = isActive ? "kW" : "kvar"
                },
                Grid = new DroopSliceGrid
                {
                    NominalLineVoltageV = GuiSimDataAccess.SafeGetDouble(
                        "ess.ElectricalNetwork.Grid.NominalLineVoltageV", 220000),
                    PccLineVoltageV = GuiSimDataAccess.SafeGetDouble("ess.PccLineVoltageV"),
                    StationBus35LineVoltageV = GuiSimDataAccess.SafeGetDouble("ess.StationBus35LineVoltageV"),
                    SystemFrequencyHz = GuiSimDataAccess.SafeGetDouble(
                        "ess.ElectricalNetwork.SystemFrequencyHz", 50),
                    MainBreakerClosed = GuiSimDataAccess.SafeGetBool("ess._breaker.IsClosed")
                },
                Meter = new DroopSliceMeter
                {
                    LineVoltageAB = GuiSimDataAccess.SafeGetDouble("em.LineVoltageAB"),
                    LineVoltageBC = GuiSimDataAccess.SafeGetDouble("em.LineVoltageBC"),
                    LineVoltageCA = GuiSimDataAccess.SafeGetDouble("em.LineVoltageCA"),
                    PhaseACurrent = GuiSimDataAccess.SafeGetDouble("em.PhaseACurrent"),
                    PhaseBCurrent = GuiSimDataAccess.SafeGetDouble("em.PhaseBCurrent"),
                    PhaseCCurrent = GuiSimDataAccess.SafeGetDouble("em.PhaseCCurrent"),
                    TotalActivePowerKw = GuiSimDataAccess.SafeGetDouble("em.TotalActivePower"),
                    TotalReactivePowerKvar = GuiSimDataAccess.SafeGetDouble("em.TotalReactivePower"),
                    TotalApparentPowerKva = GuiSimDataAccess.SafeGetDouble("em.TotalApparentPower"),
                    PowerFactor = GuiSimDataAccess.SafeGetDouble("em.PowerFactor"),
                    FrequencyHz = GuiSimDataAccess.SafeGetDouble("em.Frequency")
                },
                Pcs = new DroopSlicePcs
                {
                    UnitIndex = unitIndex0,
                    SlotInUnit = slotInUnit,
                    ChannelIndex = channelIndex,
                    PcsActivePowerSettingKw = pcsData?.PCSActivePowerSetting ?? 0,
                    PcsReactivePowerSettingKvar = pcsData?.PCSReactivePowerSetting ?? 0,
                    ActivePowerKw = pcsActualP,
                    ReactivePowerKvar = pcsActualQ,
                    LineVoltageV = pcsLineV,
                    FrequencyHz = pcsFreq,
                    OperationStatus = pcsData?.OperationStatus ?? 0,
                    PcsOnOffSwitch = pcsData?.pcsOnOffSwitch ?? false,
                    SimulatorMode = mode,
                    BlackStartEnabled = pcsData?.BlackStartEnabled ?? false
                },
                Bms = new DroopSliceBms
                {
                    BmsIndex = channelIndex,
                    IsPcsLinked = stack?.IsPcsLinked ?? false,
                    GridConnectStatus = stack?.GridConnectStatus ?? 0,
                    SocPercent = (stack?.SOC ?? 0) * 100.0,
                    TotalVoltageV = stack?.TotalVoltage ?? 0,
                    CurrentA = stack?.Current ?? 0,
                    PowerKw = stack?.Power ?? 0,
                    OperationStatus = stack?.OperationStatus,
                    MaxChargePowerKw = stack?.MaxChargePower,
                    MaxDischargePowerKw = stack?.MaxDischargePower
                },
                Topology = new DroopSliceTopology
                {
                    UnitBreakerClosed = unitBreakerClosed,
                    PropagationEnabled = GuiSimDataAccess.TryGetObject("ess.RadialGraph") != null
                }
            };
        }

        private static void ParsePcsTarget(
            string propertyPath,
            out int unitIndex0,
            out int slotInUnit,
            out bool isActive)
        {
            unitIndex0 = 0;
            slotInUnit = 0;
            isActive = propertyPath.Contains("PCSActivePowerSetting", StringComparison.Ordinal);

            // emu1.PcsList[0].PCSActivePowerSetting 或 PcsList[1]...
            int listIdx = propertyPath.IndexOf("PcsList[", StringComparison.Ordinal);
            if (listIdx >= 0)
            {
                int start = listIdx + "PcsList[".Length;
                int end = propertyPath.IndexOf(']', start);
                if (end > start && int.TryParse(propertyPath.AsSpan(start, end - start), out var slot))
                    slotInUnit = slot;
            }

            // FullPath root is emu{N}; PropertyPath alone may not have unit — caller uses serverName
        }

        internal static void ApplyUnitFromServer(string serverName, ref int unitIndex0)
        {
            if (serverName.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(serverName.AsSpan(6), out var unit1Based)
                && unit1Based >= 1)
            {
                unitIndex0 = unit1Based - 1;
            }
        }

        private static double ToDouble(object value) =>
            value switch
            {
                null => 0,
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal m => (double)m,
                string s when double.TryParse(s, out var dv) => dv,
                _ => Convert.ToDouble(value)
            };
    }
}
