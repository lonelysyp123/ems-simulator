using System;
using System.Collections.Generic;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Thermal;

namespace EssSimulator.EssDeviceSimModel.Plant
{
    /// <summary>
    /// 电站设备耦合图（阶段 4）：以边描述 PCS–BMS 直流耦合；后续可挂 PCS 热边、变压器热边等。
    /// </summary>
    public sealed class PlantCouplingGraph
    {
        private readonly List<PcsBmsDcCouplingLink> _dcLinks = new();

        public IReadOnlyList<PcsBmsDcCouplingLink> DcLinks => _dcLinks;

        public void AddDcLink(
            PcsDevice pcs,
            BmsRackDevice bms,
            int thermalCabinetIndex,
            Func<string, double?>? settledDcIntoDeviceResolver = null) =>
            _dcLinks.Add(new PcsBmsDcCouplingLink(pcs, bms, thermalCabinetIndex, settledDcIntoDeviceResolver));

        /// <summary>按通道一一配对构建默认直流耦合图。</summary>
        public static PlantCouplingGraph BuildDefault(
            IReadOnlyList<PcsDevice> pcsList,
            IReadOnlyList<BmsRackDevice> bmsList,
            Func<string, double?>? settledDcIntoDeviceResolver = null)
        {
            var graph = new PlantCouplingGraph();
            int n = Math.Min(pcsList.Count, bmsList.Count);
            for (int i = 0; i < n; i++)
                graph.AddDcLink(pcsList[i], bmsList[i], thermalCabinetIndex: i, settledDcIntoDeviceResolver);
            return graph;
        }

        public void StepCouplings(
            PlantThermalSystem thermal,
            DateTime simTime,
            TimeSpan elapsed,
            TimeSpan integrationElapsed)
        {
            foreach (var link in _dcLinks)
                link.Step(thermal, simTime, elapsed, integrationElapsed);
        }
    }
}
