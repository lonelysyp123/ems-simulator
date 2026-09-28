using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Svg;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    internal sealed class LoadBusContributor : IBusPowerContributor
    {
        private readonly LoadDevice _load;

        public LoadBusContributor(LoadDevice load) => _load = load;

        public string ContributorId => _load.DeviceId;

        public BusPowerContribution GetBusPowerContribution(DeviceStepContext context)
        {
            _load.RefreshSchedule(context.SimulationTime);
            return new BusPowerContribution(_load.ActivePower, _load.ReactivePower);
        }
    }

    internal sealed class PcsBusContributor : IBusPowerContributor
    {
        private readonly PcsDevice _pcs;

        public PcsBusContributor(PcsDevice pcs) => _pcs = pcs;

        public string ContributorId => _pcs.DeviceId;

        public BusPowerContribution GetBusPowerContribution(DeviceStepContext context)
        {
            var st = _pcs.GetCurrentState();
            return new BusPowerContribution(_pcs.GetGridSideActivePower(), st.ReactivePower);
        }
    }

    internal sealed class PvUnitBusContributor : IBusPowerContributor
    {
        private readonly Pv.PvUnitDevice _unit;

        public PvUnitBusContributor(Pv.PvUnitDevice unit) => _unit = unit;

        public string ContributorId => _unit.DeviceId;

        public BusPowerContribution GetBusPowerContribution(DeviceStepContext context) =>
            new(_unit.ActivePowerKw, _unit.ReactivePowerKvar);
    }

    /// <summary>
    /// 用本步收集前母线上已有的电压和频率步进 SVG。电压在功率收集之后才更新，因此首步看到的是上一步的母线量。
    /// </summary>
    internal sealed class SvgBusContributor : IBusPowerContributor
    {
        private readonly SvgDevice _svg;
        private readonly ElectricalBusNode _bus;

        public SvgBusContributor(SvgDevice svg, ElectricalBusNode bus)
        {
            _svg = svg;
            _bus = bus;
        }

        public string ContributorId => _svg.DeviceId;

        public BusPowerContribution GetBusPowerContribution(DeviceStepContext context)
        {
            _svg.ApplyMeasurement(_bus.LineVoltageV, _bus.FrequencyHz, context.Step);
            return new BusPowerContribution(_svg.ActivePowerKw, _svg.ReactivePowerKvar);
        }
    }
}
