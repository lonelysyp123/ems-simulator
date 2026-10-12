using EssSimulator.EssDeviceSimModel.Diagnostics;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    public sealed class LoadDevice : ISinglePortDevice
    {
        private const double ActivePowerFluctuationKw = 0.1;

        private readonly LoadDeviceConfig _config;
        private readonly List<LoadWindow> _windows;
        private readonly Random _random = new();
        private bool _scheduleStopped;

        public LoadDevice(string deviceId, LoadDeviceConfig config)
            : this(deviceId, config.ActivePowerKw, config.ReactivePowerKvar)
        {
            SetPowered(config.Powered);
        }

        public LoadDevice(
            string deviceId,
            double activePowerPlan,
            double reactivePowerPlan,
            IEnumerable<LoadWindow>? windows = null)
        {
            DeviceId = deviceId;
            _config = new LoadDeviceConfig
            {
                ActivePowerKw = activePowerPlan,
                ReactivePowerKvar = reactivePowerPlan,
                Connection = ThreePhaseConnection.Star,
                Powered = true
            };
            _windows = (windows ?? new[]
            {
                new LoadWindow
                {
                    Start = TimeSpan.Zero,
                    ActivePowerPlan = activePowerPlan,
                    ReactivePowerPlan = reactivePowerPlan
                }
            }).OrderBy(w => w.Start).ToList();
            ActivePowerSetpointKw = activePowerPlan;
            ReactivePowerSetpointKvar = reactivePowerPlan;
            Port = CreatePort(_config.Connection);
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Load;
        public ElectricalPort Port { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Port };

        /// <summary>方向约定：+ 向电网送电，- 从电网取电。</summary>
        public double ActivePower { get; private set; }

        /// <summary>有功设定值（kW），不含随机波动。</summary>
        public double ActivePowerSetpointKw { get; private set; }

        /// <summary>无功约定：正=升压支撑，负=降压作用。</summary>
        public double ReactivePower { get; private set; }

        /// <summary>无功设定值（kvar）。</summary>
        public double ReactivePowerSetpointKvar { get; private set; }

        public void SetPowered(bool powered)
        {
            _config.Powered = powered;
            if (!powered)
            {
                ActivePower = 0;
                ReactivePower = 0;
            }
        }

        public void SetLoadCharacteristic(string characteristic, double value)
        {
            _scheduleStopped = true;
            if (characteristic == "activePower")
            {
                double next = value > 0 ? 0 : value;
                double prev = ActivePowerSetpointKw;
                ActivePowerSetpointKw = next;
                SimStateChangeLogger.LoadSetpointChanged("有功", prev, next);
            }
            else if (characteristic == "reactivePower")
            {
                double prev = ReactivePowerSetpointKvar;
                ReactivePowerSetpointKvar = value;
                SimStateChangeLogger.LoadSetpointChanged("无功", prev, value);
            }

            if (_config.Powered)
                ApplyMeasuredPowerFromSetpoints();
        }

        public void RefreshSchedule(DateTime simTime)
        {
            if (!_config.Powered)
            {
                ActivePower = 0;
                ReactivePower = 0;
                return;
            }

            if (_scheduleStopped)
            {
                ApplyMeasuredPowerFromSetpoints();
                return;
            }

            var tod = simTime.TimeOfDay;
            var active = _windows[0];
            foreach (var window in _windows)
            {
                if (tod >= window.Start)
                    active = window;
                else
                    break;
            }

            ActivePowerSetpointKw = active.ActivePowerPlan;
            ReactivePowerSetpointKvar = active.ReactivePowerPlan;
            ApplyMeasuredPowerFromSetpoints();
        }

        private void ApplyMeasuredPowerFromSetpoints()
        {
            ActivePower = ActivePowerSetpointKw == 0
                ? 0
                : ActivePowerSetpointKw + (_random.NextDouble() * 2.0 - 1.0) * ActivePowerFluctuationKw;
            ReactivePower = ReactivePowerSetpointKvar;
        }

        /// <summary>由 35kV 母线电压换算负载电流（A），供监视/兼容路径使用。</summary>
        public double ComputeLoadCurrentA(double lineVoltageV, DateTime simTime)
        {
            RefreshSchedule(simTime);
            if (lineVoltageV <= 0)
                return 0;

            var phasor = AcQuantityConverter.FromPowerToPhasor(lineVoltageV, ActivePower, ReactivePower);
            return phasor.LineCurrentA;
        }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            RefreshSchedule(context.SimulationTime);
            var bus = AcPortHelper.ReadAcInput(Port);
            if (bus.LineVoltageV <= 1.0)
            {
                ActivePower = 0;
                ReactivePower = 0;
            }

            double activeKw = _config.Powered ? ActivePower : 0;
            double reactiveKvar = _config.Powered ? ReactivePower : 0;

            var output = AcQuantityConverter.FromLineVoltageAndPower(
                bus.LineVoltageV,
                activeKw,
                reactiveKvar,
                _config.Connection,
                bus.FrequencyHz);

            AcPortHelper.WriteAcOutput(Port, output);
        }

        private static ElectricalPort CreatePort(ThreePhaseConnection connection)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = "ac",
                Kind = PortKind.BusConnected,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
