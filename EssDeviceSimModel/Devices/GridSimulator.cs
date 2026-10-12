using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed class GridSimulator : IGridDevice, ISelfActivatingElectricalSource
    {
        private readonly GridConfig _config;
        private double _aggregatedReactiveKvar;

        public GridSimulator(string deviceId, GridConfig config)
        {
            DeviceId = deviceId;
            _config = config;
            Port = CreatePort("grid", config.Connection);
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Grid;
        public ElectricalPort Port { get; }
        public ElectricalPort OutputPort => Port;
        public IReadOnlyList<ElectricalPort> Ports => new[] { Port };

        public void SetAggregatedReactivePowerKvar(double totalReactiveKvar) =>
            _aggregatedReactiveKvar = totalReactiveKvar;

        /// <summary>电网额定线电压设定（V，220kV 写 220000）。Q-U 反馈在此基础上偏移。</summary>
        public void SetNominalLineVoltage(double lineVoltageV)
        {
            if (lineVoltageV <= 0)
                throw new ArgumentOutOfRangeException(nameof(lineVoltageV), "电网电压必须大于 0");
            _config.NominalLineVoltageV = lineVoltageV;
        }

        /// <summary>电网额定频率设定（Hz）。并网时 PCS 跟网、电表 PCC 侧采样均取此值。</summary>
        public void SetNominalFrequency(double frequencyHz)
        {
            if (frequencyHz <= 0 || frequencyHz > 75)
                throw new ArgumentOutOfRangeException(nameof(frequencyHz), "电网频率须在 (0, 75] Hz");
            _config.NominalFrequencyHz = frequencyHz;
        }

        public double NominalLineVoltageV => _config.NominalLineVoltageV;
        public double NominalFrequencyHz => _config.NominalFrequencyHz;

        public void Activate(DeviceStepContext context, TimeSpan step) => Step(context, step);

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            double lineVoltageV = GridFeedbackConventions.CalculatePccLineVoltage(
                _config.NominalLineVoltageV,
                _aggregatedReactiveKvar,
                _config.ShortCircuitMva,
                _config.ReactiveVoltageInfluenceCoefficient,
                _config.MaxVoltageShiftPercent);

            // 电网始终是电压源；与站内的隔离由断路器完成，不在此把电网端口打成 0。

            var internalQty = new AcInternalQuantities
            {
                Connection = _config.Connection,
                LineVoltageV = lineVoltageV,
                LineCurrentA = 0,
                PhaseAngleDeg = 0,
                FrequencyHz = lineVoltageV > 1.0 ? _config.NominalFrequencyHz : 0
            };

            AcPortHelper.WriteAcOutput(Port, internalQty);
        }

        private static ElectricalPort CreatePort(string portId, ThreePhaseConnection connection)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = portId,
                Kind = PortKind.BusConnected,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
