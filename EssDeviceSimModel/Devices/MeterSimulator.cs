using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed class MeterSimulator : IMeterDevice
    {
        private double _forwardKwh;
        private double _reverseKwh;
        private double _forwardKvarh;
        private double _reverseKvarh;

        public MeterSimulator(string deviceId, MeterInstanceConfig config)
        {
            DeviceId = deviceId;
            Config = config;
            Port = CreatePort(config.Pt.Connection);
            Telemetry = new MeterTelemetry();
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Meter;
        public MeterInstanceConfig Config { get; }
        public MeterTelemetry Telemetry { get; private set; }
        public ElectricalPort Port { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Port };

        public void SampleFrom(AcInternalQuantities primaryQuantities, TimeSpan step)
        {
            double hours = step.TotalHours;
            if (primaryQuantities.ActivePowerKw >= 0)
                _forwardKwh += primaryQuantities.ActivePowerKw * hours;
            else
                _reverseKwh -= primaryQuantities.ActivePowerKw * hours;

            if (primaryQuantities.ReactivePowerKvar >= 0)
                _forwardKvarh += primaryQuantities.ReactivePowerKvar * hours;
            else
                _reverseKvarh -= primaryQuantities.ReactivePowerKvar * hours;

            Telemetry = MeterQuantityConverter.CreateTelemetry(
                primaryQuantities,
                Config.Pt,
                Config.Ct,
                Config.ReportedQuantity);

            Telemetry = new MeterTelemetry
            {
                Primary = Telemetry.Primary,
                Secondary = Telemetry.Secondary,
                ReportedTerminal = Telemetry.ReportedTerminal,
                ForwardActiveEnergyKwh = _forwardKwh,
                ReverseActiveEnergyKwh = _reverseKwh,
                ForwardReactiveEnergyKvarh = _forwardKvarh,
                ReverseReactiveEnergyKvarh = _reverseKvarh
            };

            AcPortHelper.WriteAcOutput(Port, primaryQuantities);
        }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            // 测量设备不改变网络；SampleFrom 由求解器在 S8 阶段调用。
        }

        private static ElectricalPort CreatePort(ThreePhaseConnection connection)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = "measurement",
                Kind = PortKind.MeasurementTap,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
