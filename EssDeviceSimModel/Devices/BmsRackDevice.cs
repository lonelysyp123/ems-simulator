using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Thermal;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    /// <summary>
    /// BMS 设备：四层电池堆物理、阈值保护、DC 端口与 PCS 并网链路。
    /// 与 <see cref="ElectricalNetwork.BmsDevices"/> 共用实例。
    /// </summary>
    public sealed class BmsRackDevice : IBmsDevice, IElectricalLossSource, ITemperatureAware
    {
        private readonly BatteryRackSimulator _rack;
        private DeviceFaultState _fault = new();
        private double _ambientCelsius = 25.0;
        private double _lastLossWatts;

        public BmsRackDevice(string deviceId, BatteryRackSimulator rack)
        {
            DeviceId = deviceId;
            DisplayLabel = deviceId;
            _rack = rack;
            Port = CreatePort();
            SyncPortFromRack();
            RefreshFaultState();
        }

        public string DeviceId { get; }
        /// <summary>日志/界面友好名，如 bms1。</summary>
        public string DisplayLabel { get; set; }
        public BatteryRackSimulator Rack => _rack;
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Bms;
        public DeviceFaultState Fault => _fault;
        public ElectricalPort Port { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Port };

        public double TemperatureCelsius
        {
            get
            {
                var state = _rack.GetRackState();
                return state?.AvgClusterTemp ?? _ambientCelsius;
            }
        }

        /// <summary>热网络电池节点温度（°C），由 PCS–BMS 直流耦合边在每步写入；作为电芯热环境（散热效率受其影响）。</summary>
        public double BatteryNodeTemperatureCelsius { get; private set; } = 25.0;

        public void ApplyBatteryNodeTemperature(double celsius) => BatteryNodeTemperatureCelsius = celsius;

        public void ApplyAmbientTemperature(double ambientCelsius) => _ambientCelsius = ambientCelsius;

        public double GetElectricalLossWatts() => _lastLossWatts;

        public bool IsLinked
        {
            get => _rack.GetRackState()?.IsPcsLinked ?? false;
            set
            {
                var state = _rack.GetRackState();
                if (state != null)
                    state.IsPcsLinked = value;
            }
        }

        public ushort FaultCode => _rack.GetRackState()?.IsFault ?? 0;

        public bool HasBlockingFault => FaultCode != 0;

        public double Soc
        {
            get
            {
                var state = _rack.GetRackState();
                if (state?.ClusterStates == null || state.ClusterStates.Count == 0)
                    return 0;
                return state.ClusterStates.Average(c => c.MinPackSOC);
            }
        }

        public void Step(DeviceStepContext context, TimeSpan step) => SyncPortFromRack();

        /// <summary>ESS 主循环：推进电芯/SOC 物理并刷新 DC 端口。电芯热环境使用电池节点温度。</summary>
        public void UpdatePhysics(double rackCurrent, double ambientTemp, DateTime timeStamp, TimeSpan step)
        {
            _ambientCelsius = ambientTemp;
            _rack.Update(rackCurrent, BatteryNodeTemperatureCelsius, timeStamp, step);
            _lastLossWatts = PlantThermalSystem.EstimateRackOhmicLossWatts(_rack, rackCurrent);
            SyncPortFromRack();
        }

        /// <summary>并网链路写入（Rack 状态 SSOT）。</summary>
        public void SetPcsLinked(bool linked) => IsLinked = linked;

        /// <summary>热设整堆 SOC（0~1），须待机（堆电流为 0）；写透电芯并刷新 DC 端口电压。</summary>
        public bool TrySetSoc(double soc, out string message)
        {
            var rackState = _rack.GetRackState();
            if (rackState != null &&
                (BmsRackProtection.IsCharging(rackState.TotalCurrent) ||
                 BmsRackProtection.IsDischarging(rackState.TotalCurrent)))
            {
                message = "当前仍在充/放电，请先待机后再修改 SOC";
                return false;
            }

            if (!_rack.TrySetSoc(soc, out message))
                return false;
            SyncPortFromRack();
            return true;
        }

        /// <summary>保护评估后刷新电气故障态（由 Mapper 在投影完成时调用）。</summary>
        internal void RefreshProtectionFault() => RefreshFaultState();

        public void SyncPortFromRack()
        {
            var state = _rack.GetRackState();
            double voltageV = IsLinked ? state?.TotalVoltage ?? 0 : 0;
            double currentA = Port.Input.Dc?.CurrentA ?? state?.TotalCurrent ?? 0;

            AcPortHelper.WriteDcOutput(Port, new DcSnapshot
            {
                VoltageV = Math.Max(0, voltageV),
                CurrentA = currentA
            });
        }

        public void ApplyDcInputFromPcs(double currentA)
        {
            Port.Input = ElectricalPortSnapshot.FromDc(new DcSnapshot { CurrentA = currentA });
        }

        private void RefreshFaultState()
        {
            var state = _rack.GetRackState();
            _fault = new DeviceFaultState { FaultCode = state?.IsFault ?? 0 };
        }

        private static ElectricalPort CreatePort() =>
            new()
            {
                PortId = "dc",
                Kind = PortKind.DcLink,
                Input = ElectricalPortSnapshot.FromDc(new DcSnapshot()),
                Output = ElectricalPortSnapshot.FromDc(new DcSnapshot())
            };
    }
}
