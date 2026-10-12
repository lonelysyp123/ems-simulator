using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>电气网络控制写入；Breaker 投影供 GUI / 对象路径读取。</summary>
    public static class NetworkControlBridge
    {
        public static bool IsBreakerClosed(BreakerSimulator breaker) =>
            breaker.SwitchState.IsClosed && !breaker.SwitchState.IsTripped;

        public static void ApplyMainBreakerClosed(
            ElectricalNetwork? network,
            Breaker legacy,
            LoadDevice loadDevice,
            bool closed)
        {
            if (network != null)
            {
                network.MainBreaker.ApplyCommand(new DeviceCommand
                {
                    Kind = closed ? DeviceCommandKind.CloseBreaker : DeviceCommandKind.OpenBreaker
                });
                legacy.IsClosed = IsBreakerClosed(network.MainBreaker);
                loadDevice.SetPowered(legacy.IsClosed);
                return;
            }

            legacy.IsClosed = closed;
            loadDevice.SetPowered(closed);
        }

        public static void ApplyUnitBreakerClosed(
            ElectricalNetwork? network,
            IReadOnlyList<Breaker> legacyUnitBreakers,
            int unitIndex,
            bool closed)
        {
            if (unitIndex < 0 || unitIndex >= legacyUnitBreakers.Count)
                return;

            if (network != null && unitIndex < network.UnitBreakers.Count)
            {
                network.UnitBreakers[unitIndex].ApplyCommand(new DeviceCommand
                {
                    Kind = closed ? DeviceCommandKind.CloseBreaker : DeviceCommandKind.OpenBreaker
                });
                legacyUnitBreakers[unitIndex].IsClosed = IsBreakerClosed(network.UnitBreakers[unitIndex]);
                return;
            }

            legacyUnitBreakers[unitIndex].IsClosed = closed;
        }

        public static void ResetUnitBreakerTrip(ElectricalNetwork? network, int unitIndex)
        {
            if (network == null || unitIndex < 0 || unitIndex >= network.UnitBreakers.Count)
                return;

            network.UnitBreakers[unitIndex].ApplyCommand(new DeviceCommand
            {
                Kind = DeviceCommandKind.ResetBreakerTrip
            });
        }

        public static void ProjectBreakersToLegacy(ElectricalNetwork network, EnergyStorageSystem ess)
        {
            ess._breaker.IsClosed = IsBreakerClosed(network.MainBreaker);
            for (int u = 0; u < network.UnitBreakers.Count && u < ess._unitBreakers.Count; u++)
                ess._unitBreakers[u].IsClosed = IsBreakerClosed(network.UnitBreakers[u]);

            ess._loadDevice.SetPowered(ess._breaker.IsClosed);
        }

        public static void SyncLoadPlan(ElectricalNetwork network, LoadDevice loadDevice, DateTime simTime)
        {
            bool powered = IsBreakerClosed(network.MainBreaker);
            loadDevice.SetPowered(powered);
            loadDevice.RefreshSchedule(simTime);
        }

        /// <summary>BMS 并网链路：写入 BmsRackDevice + DcLink + 网络 BmsDevice（同一实例）。</summary>
        public static void SetBmsPcsLinked(
            ElectricalNetwork? network,
            BmsRackDevice bms,
            int channelIndex,
            bool linked)
        {
            bms.SetPcsLinked(linked);

            if (network == null)
                return;

            if (channelIndex < network.DcLinks.Count)
                network.DcLinks[channelIndex].IsClosed = linked;

            if (channelIndex < network.BmsDevices.Count)
                network.BmsDevices[channelIndex].SyncPortFromRack();
        }

        /// <summary>步进后从 BmsRackDevice 刷新 DC 链路（供下一 Solver 周期使用）。</summary>
        public static void SyncBmsLinksFromRacks(
            ElectricalNetwork network,
            IReadOnlyList<BmsRackDevice> bmsDevices)
        {
            int count = Math.Min(network.DcLinks.Count, bmsDevices.Count);
            for (int i = 0; i < count; i++)
            {
                bool linked = bmsDevices[i].IsLinked;
                network.DcLinks[i].IsClosed = linked;
                if (i < network.BmsDevices.Count)
                    network.BmsDevices[i].SyncPortFromRack();
            }
        }
    }
}
