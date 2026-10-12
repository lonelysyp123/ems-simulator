using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.EmsStrategy.Adapter;

public static class PcsCommandSink
{
    public static void Apply(EnergyStorageSystem ess, EmsControlOutput output)
    {
        ArgumentNullException.ThrowIfNull(ess);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.OutputEnabled)
            return;

        var units = new HashSet<int>();
        foreach (var cmd in output.Branches)
        {
            var emu = SimulatorHost.Instance.TryGetEmu(cmd.UnitIndex0 + 1);
            if (emu == null || cmd.PcsIndexInUnit < 0 || cmd.PcsIndexInUnit >= emu.PcsList.Count)
                continue;
            emu.PcsList[cmd.PcsIndexInUnit].PCSActivePowerSetting = (float)cmd.ActivePowerKw;
            emu.PcsList[cmd.PcsIndexInUnit].PCSReactivePowerSetting = (float)cmd.ReactivePowerKvar;
            units.Add(cmd.UnitIndex0 + 1);
        }

        foreach (int unit in units)
            EmuCommandPipeline.TryApplyUnit(unit);
    }
}
