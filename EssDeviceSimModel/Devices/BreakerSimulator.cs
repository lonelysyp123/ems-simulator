using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed class BreakerSimulator : IBreakerDevice
    {
        private readonly BreakerBranchConfig _config;
        private DeviceFaultState _fault = new();

        public BreakerSimulator(string deviceId, BreakerBranchConfig config)
        {
            DeviceId = deviceId;
            _config = config;
            SwitchState = new BreakerState { IsClosed = config.InitialClosed };
            Primary = CreatePort("primary", config.PrimaryConnection, PortKind.SeriesUpstream);
            Secondary = CreatePort("secondary", config.SecondaryConnection, PortKind.SeriesDownstream);
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Breaker;
        public ElectricalPort Primary { get; }
        public ElectricalPort Secondary { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Primary, Secondary };
        public BreakerState SwitchState { get; private set; }
        public DeviceFaultState Fault => _fault;
        public double RatedLineVoltageV => _config.RatedVoltageKv * 1000.0;

        /// <summary>把另一电压等级下的线电流折到本断路器额定电压（功率不变）。失电入口不伪造额定电压。</summary>
        public AcInternalQuantities ReferToRated(AcInternalQuantities from)
        {
            double ratedV = RatedLineVoltageV;
            if (ratedV <= 1.0)
                return from;

            if (from.LineVoltageV <= 1.0 && Math.Abs(from.LineCurrentA) <= 1e-9)
            {
                return new AcInternalQuantities
                {
                    Connection = from.Connection,
                    LineVoltageV = 0,
                    LineCurrentA = 0,
                    PhaseAngleDeg = 0,
                    FrequencyHz = from.FrequencyHz
                };
            }

            double fromV = from.LineVoltageV > 1.0 ? from.LineVoltageV : ratedV;
            return new AcInternalQuantities
            {
                Connection = from.Connection,
                LineVoltageV = ratedV,
                LineCurrentA = AcQuantityConverter.ReferLineCurrent(from.LineCurrentA, fromV, ratedV),
                PhaseAngleDeg = from.PhaseAngleDeg,
                FrequencyHz = from.FrequencyHz
            };
        }

        public void ApplyCommand(DeviceCommand command)
        {
            switch (command.Kind)
            {
                case DeviceCommandKind.CloseBreaker:
                    if (!SwitchState.IsTripped)
                        SwitchState.IsClosed = true;
                    break;
                case DeviceCommandKind.OpenBreaker:
                    SwitchState.IsClosed = false;
                    break;
                case DeviceCommandKind.ResetBreakerTrip:
                    SwitchState.IsTripped = false;
                    _fault = new DeviceFaultState();
                    break;
            }
        }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            var priIn = AcPortHelper.ReadAcInput(Primary);
            var secIn = AcPortHelper.ReadAcInput(Secondary);

            if (SwitchState.IsClosed && !SwitchState.IsTripped)
            {
                double current = ProtectionCurrentA(priIn, secIn);
                if (current > _config.FaultThresholdA)
                {
                    SwitchState.IsTripped = true;
                    SwitchState.IsClosed = false;
                    _fault = new DeviceFaultState
                    {
                        FaultCode = 3,
                        FaultMessage = $"Breaker overcurrent trip: {current:F0}A"
                    };
                }
            }

            if (SwitchState.IsClosed && !SwitchState.IsTripped)
            {
                double iRated = ProtectionCurrentA(priIn, secIn);
                var passed = new AcInternalQuantities
                {
                    Connection = priIn.Connection,
                    LineVoltageV = priIn.LineVoltageV,
                    LineCurrentA = iRated,
                    PhaseAngleDeg = secIn.PhaseAngleDeg,
                    FrequencyHz = priIn.FrequencyHz
                };

                AcPortHelper.WriteAcOutput(Primary, passed);
                AcPortHelper.WriteAcOutput(Secondary, passed);
                return;
            }

            AcPortHelper.WriteAcOutput(Primary, new AcInternalQuantities
            {
                Connection = priIn.Connection,
                LineVoltageV = priIn.LineVoltageV,
                LineCurrentA = 0,
                PhaseAngleDeg = 0,
                FrequencyHz = priIn.FrequencyHz
            });
            AcPortHelper.WriteAcOutput(Secondary, new AcInternalQuantities
            {
                Connection = secIn.Connection,
                LineVoltageV = secIn.LineVoltageV > 1.0 ? secIn.LineVoltageV : 0,
                LineCurrentA = 0,
                PhaseAngleDeg = 0,
                FrequencyHz = secIn.FrequencyHz
            });
        }

        private double ProtectionCurrentA(AcInternalQuantities priIn, AcInternalQuantities secIn)
        {
            double ratedV = RatedLineVoltageV;
            if (ratedV <= 1.0)
                return Math.Abs(secIn.LineCurrentA);

            double fromV = secIn.LineVoltageV > 1.0
                ? secIn.LineVoltageV
                : (priIn.LineVoltageV > 1.0 ? priIn.LineVoltageV : ratedV);
            return Math.Abs(AcQuantityConverter.ReferLineCurrent(secIn.LineCurrentA, fromV, ratedV));
        }

        private static ElectricalPort CreatePort(
            string portId,
            ThreePhaseConnection connection,
            PortKind kind)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = portId,
                Kind = kind,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
