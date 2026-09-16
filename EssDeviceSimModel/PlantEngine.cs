using System;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 电站物理步进门面：对外只暴露 <see cref="Step"/>。
    /// 电气潮流 → 热网络 → <see cref="Plant.PlantCouplingGraph"/> 耦合边 → 黑启动/断路器同步。
    /// </summary>
    public sealed class PlantEngine
    {
        private readonly EnergyStorageSystem _ess;

        public PlantEngine(EnergyStorageSystem ess)
        {
            _ess = ess ?? throw new ArgumentNullException(nameof(ess));
        }

        /// <summary>
        /// 推进电站物理状态一步。Host 主循环应只调用本方法。
        /// </summary>
        public void Step(DateTime simTime, TimeSpan elapsed, TimeSpan integrationElapsed)
        {
            _ess.StepPvUnits(simTime, elapsed);
            _ess.RefreshAllUnitBlackStartBusContexts();
            RunElectricalStep(simTime, elapsed, integrationElapsed);
            _ess.Thermal.Step(simTime, elapsed);
            _ess.CouplingGraph.StepCouplings(_ess.Thermal, simTime, elapsed, integrationElapsed);
            _ess.SyncUnitTransformerAfterPcsUpdate(simTime, elapsed);
            _ess.RefreshAllUnitBlackStartBusContexts();
            AfterPlantStep.Invoke(_ess, simTime, elapsed);
        }

        private void RunElectricalStep(DateTime simTime, TimeSpan elapsed, TimeSpan integrationElapsed)
        {
            _ess.PowerSweepEngine.SolveCycle(simTime, elapsed, integrationElapsed);
        }
    }
}
