using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Adapter;

public static class PlantMeasurementSampler
{
    public static PlantMeasurements Sample(EnergyStorageSystem ess, DateTime simTime)
    {
        ArgumentNullException.ThrowIfNull(ess);
        var net = ess.ElectricalNetwork;
        AcInternalQuantities pcc = net.PccMeter?.Telemetry.Primary ?? new AcInternalQuantities();
        double freq = net.SystemFrequencyHz > 1 ? net.SystemFrequencyHz : pcc.FrequencyHz;

        var branches = new List<PcsBranchState>(ess._pcsList.Count);
        for (int i = 0; i < ess._pcsList.Count; i++)
        {
            var pcs = ess._pcsList[i];
            var state = pcs.GetCurrentState();
            int unit = ess.UnitIndexOfPcs(i);
            int baseIdx = ess.PcsBaseIndexOfUnit(unit);
            int inUnit = i - baseIdx;
            double soc = 0.5;
            if (i < ess._batteryRacks.Count)
                soc = ess._batteryRacks[i].GetRackSOC();

            bool chargeProhibited = false;
            bool dischargeProhibited = false;
            var emu = SimulatorHost.Instance.TryGetEmu(unit + 1);
            if (emu != null && inUnit >= 0 && inUnit < emu.PcsList.Count)
            {
                chargeProhibited = emu.PcsList[inUnit].ChargeProhibited;
                dischargeProhibited = emu.PcsList[inUnit].DischargeProhibited;
            }

            double rated = pcs._config.RatedPower;
            double maxP = pcs._config.MaxPower > 0 ? pcs._config.MaxPower : rated;
            branches.Add(new PcsBranchState
            {
                Index = i,
                UnitIndex0 = unit,
                PcsIndexInUnit = inUnit,
                CommOk = true,
                Fault = pcs.HasLatchedFaultTrip || state.FaultType != 0,
                Running = pcs.IsExternalRunCommand || state.Mode != OperationMode.Off,
                ChargeProhibited = chargeProhibited,
                DischargeProhibited = dischargeProhibited,
                Soc = soc,
                RatedKw = rated,
                MaxChargeKw = state.DcLimitChgPower > 0 ? state.DcLimitChgPower : maxP,
                MaxDischargeKw = state.DcLimitDsgPower > 0 ? state.DcLimitDsgPower : maxP,
                MeasuredActiveKw = state.ActivePower,
                MeasuredReactiveKvar = state.ReactivePower
            });
        }

        return new PlantMeasurements
        {
            SimTime = simTime,
            FrequencyHz = freq,
            PccActivePowerKw = pcc.ActivePowerKw,
            PccReactivePowerKvar = pcc.ReactivePowerKvar,
            PccLineVoltageV = pcc.LineVoltageV,
            StationBus35LineVoltageV = net.StationBus35LineVoltageV,
            Branches = branches
        };
    }
}
