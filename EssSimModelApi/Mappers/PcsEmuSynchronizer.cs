using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>ESS 物理模型 ↔ EMU/PCS DTO 单向同步（无 Modbus 依赖）。</summary>
    public static class PcsEmuSynchronizer
    {
        public static void SyncUnit(
            EnergyStorageSystem ess,
            EnergyManagementData emu,
            int unitIndex0,
            int pcsBaseIndex,
            double unitXfRatedKw = 0)
        {
            SyncPcsStateFromModel(ess, emu, pcsBaseIndex);

            int pcsCount = emu.PcsList.Count;
            var racks = new List<BatteryRackSimulator>();
            for (int i = 0; i < pcsCount; i++)
            {
                int rackIdx = pcsBaseIndex + i;
                if (rackIdx < ess._batteryRacks.Count)
                    racks.Add(ess._batteryRacks[rackIdx]);
            }
            PcsMapper.MapEmuState(emu, racks);
            PcsMapper.MapGroupState(emu);

            ApplyCommandsIfChanged(ess, emu, unitIndex0, pcsBaseIndex);

            SyncDeviceMirrors(ess, emu, unitIndex0, unitXfRatedKw);
        }

        /// <summary>
        /// 控制字段或联锁输入相对上一拍未变时跳过下发，避免每拍重写启停/功率设定打断爬坡。
        /// </summary>
        private static void ApplyCommandsIfChanged(
            EnergyStorageSystem ess,
            EnergyManagementData emu,
            int unitIndex0,
            int pcsBaseIndex)
        {
            int fingerprint = ComputeCommandFingerprint(ess, emu, unitIndex0);
            if (emu.HasCommandSyncFingerprint && fingerprint == emu.LastCommandSyncFingerprint)
                return;

            if (unitIndex0 < ess._unitBreakers.Count)
                ess.SetUnitBreakerClosed(unitIndex0, emu.Emu.PowerOnOff != 0);

            EmuPowerDispatcher.Dispatch(emu);
            EmuSystemOperationApplier.Apply(emu);
            PcsMapper.ApplyEmuCommands(emu, ess, pcsBaseIndex);

            emu.LastCommandSyncFingerprint = ComputeCommandFingerprint(ess, emu, unitIndex0);
            emu.HasCommandSyncFingerprint = true;
        }

        private static int ComputeCommandFingerprint(
            EnergyStorageSystem ess,
            EnergyManagementData emu,
            int unitIndex0)
        {
            var h = new HashCode();
            var e = emu.Emu;
            h.Add(e.PowerOnOff);
            h.Add(e.RemoteControlEnable);
            h.Add(e.RemoteControlMode);
            h.Add(e.SystemOperation);
            h.Add(e.BlackStartModeWrite);
            h.Add(e.TargetActivePower);
            h.Add(e.TargetReactivePower);
            h.Add(ess.IsMainBreakerClosed);
            h.Add(ess.IsUnitBreakerClosed(unitIndex0));
            h.Add(ess.IsUnitBreakerTripped(unitIndex0));

            int pcsBase = ess.PcsBaseIndexOfUnit(unitIndex0);
            for (int i = 0; i < emu.PcsList.Count; i++)
            {
                var pcs = emu.PcsList[i];
                h.Add(pcs.pcsOnOffSwitch);
                h.Add(pcs.PCSActivePowerSetting);
                h.Add(pcs.PCSReactivePowerSetting);
                h.Add(pcs.BlackStartEnabled);
                h.Add(pcs.IslandVoltageSetting);
                h.Add(pcs.IslandFrequencySetting);
                h.Add(pcs.ChargeProhibited);
                h.Add(pcs.DischargeProhibited);

                int simIdx = pcsBase + i;
                if (simIdx < 0 || simIdx >= ess._pcsList.Count)
                    continue;
                var sim = ess._pcsList[simIdx];
                h.Add(sim.HasLatchedFaultTrip);
                h.Add(sim.IsGridElectricallyAvailable);
                h.Add(sim.IsLiveBusFollower);
                h.Add(sim.IsPreSyncReadyToCutIn);
            }

            return h.ToHashCode();
        }

        /// <summary>
        /// 设备镜像刷新：EMU 级断路器状态跟随电气合闸（跳闸锁存时为分）；
        /// 单元变镜像优先抄电气层真实状态，缺失时用 PCS 求和合成；
        /// 单元电表镜像电压/频率跟随 PCS 交流母线，功率取 EMU 聚合值。
        /// </summary>
        private static void SyncDeviceMirrors(
            EnergyStorageSystem ess,
            EnergyManagementData emu,
            int unitIndex0,
            double unitXfRatedKw)
        {
            emu.Breaker.Closed = (ushort)(ess.IsUnitBreakerClosed(unitIndex0) ? 1 : 0);

            PcsMapper.MapElectricityMeterState(emu, ess, unitIndex0);

            if (emu.Transformers.Count == 0)
                return;
            var xf = emu.Transformers[0];
            xf.Closed = emu.Breaker.Closed;

            if (unitIndex0 < ess._unitTransformers.Count)
            {
                var state = ess._unitTransformers[unitIndex0].GetCurrentState();
                xf.ActivePowerKw = (float)state.Power;
                xf.LoadFraction = (float)Math.Min(1.5, state.LoadRatio);
                xf.OilTemperatureC = (float)state.Temperature;
                if (state.PowerFactor is > 0.05 and <= 1 && Math.Abs(state.Power) > 0.1)
                {
                    double s = Math.Abs(state.Power) / state.PowerFactor;
                    xf.ReactivePowerKvar = (float)Math.Sqrt(Math.Max(0, s * s - state.Power * state.Power));
                }
                else
                {
                    xf.ReactivePowerKvar = 0f;
                }
            }
            else if (unitXfRatedKw > 0)
            {
                xf.ActivePowerKw = emu.Emu.OutputActivePower;
                xf.ReactivePowerKvar = emu.Emu.OutputReactivePower;
                xf.LoadFraction = Math.Min(1.5f, Math.Abs(emu.Emu.OutputActivePower) / (float)unitXfRatedKw);
            }

            xf.OperationStatus = xf.Closed == 0 ? (ushort)1
                : Math.Abs(xf.ActivePowerKw) > 1 ? (ushort)3 : (ushort)2;
        }

        /// <summary>控制命令后立即把物理 PCS 状态映射到 DTO（不重复 ApplyEmuCommands）。</summary>
        public static void SyncPcsStateFromModel(
            EnergyStorageSystem ess,
            EnergyManagementData emu,
            int pcsBaseIndex)
        {
            int pcsCount = emu.PcsList.Count;
            if (pcsCount == 0 ||
                pcsBaseIndex + pcsCount > ess._pcsList.Count ||
                pcsBaseIndex + pcsCount > ess._batteryRacks.Count)
                return;

            for (int i = 0; i < pcsCount; i++)
            {
                PcsMapper.MapPcsState(
                    ess._pcsList[pcsBaseIndex + i].GetCurrentState(),
                    emu.PcsList[i],
                    ess._batteryRacks[pcsBaseIndex + i]);
                PcsMapper.SyncRunCommandFeedback(ess._pcsList[pcsBaseIndex + i], emu.PcsList[i]);
            }
        }
    }
}
