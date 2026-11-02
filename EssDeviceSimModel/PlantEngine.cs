using System;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Thermal;
using log4net;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 电站物理步进门面：对外只暴露 <see cref="Step"/>。
    /// 电气潮流 → 热网络 → <see cref="Plant.PlantCouplingGraph"/> 耦合边 → 黑启动/断路器同步。
    /// 步进前捕获 memento；失败整步恢复，不发布半步状态。
    /// </summary>
    public sealed class PlantEngine
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PlantEngine));
        private readonly EnergyStorageSystem _ess;

        public PlantEngine(EnergyStorageSystem ess)
        {
            _ess = ess ?? throw new ArgumentNullException(nameof(ess));
        }

        /// <summary>测试钩子：下一拍在物理步进之后、提交之前抛错。</summary>
        internal bool FailNextStep { get; set; }

        /// <summary>上一步已提交的端口测量。失败拍保持上一份。</summary>
        public CommittedPortMeasurements LastCommittedMeasurements { get; private set; } =
            CommittedPortMeasurements.Empty;

        internal void RestoreCommitted(CommittedPortMeasurements measurements) =>
            LastCommittedMeasurements = measurements ?? CommittedPortMeasurements.Empty;

        /// <summary>
        /// 推进电站物理状态一步。Host 主循环应只调用本方法。
        /// </summary>
        public bool Step(DateTime simTime, TimeSpan elapsed, TimeSpan integrationElapsed)
        {
            var memento = PlantStepMemento.Capture(_ess);
            try
            {
                _ess.StepPvUnits(simTime, elapsed);
                _ess.RefreshAllUnitBlackStartBusContexts();
                RunElectricalStep(simTime, elapsed, integrationElapsed);
                _ess.ProjectNetworkPortSolutions();
                _ess.Thermal.Step(simTime, elapsed);
                _ess.CouplingGraph.StepCouplings(_ess.Thermal, simTime, elapsed, integrationElapsed);
                _ess.SyncUnitTransformerAfterPcsUpdate(simTime, elapsed);
                _ess.RefreshAllUnitBlackStartBusContexts();

                if (FailNextStep)
                {
                    FailNextStep = false;
                    throw new InvalidOperationException("Injected plant step failure.");
                }

                LastCommittedMeasurements = BuildCommitted(simTime);
                _ess.ClearSolverFault();
                AfterPlantStep.Invoke(_ess, simTime, elapsed);
                return true;
            }
            catch (Exception ex)
            {
                FailNextStep = false;
                memento.Restore(_ess);
                _ess.SetSolverFault(ex.Message);
                Log.Error("[PlantEngine] 本拍求解失败，已恢复上一拍已提交状态", ex);
                return false;
            }
        }

        private void RunElectricalStep(DateTime simTime, TimeSpan elapsed, TimeSpan integrationElapsed)
        {
            _ess.PowerSweepEngine.SolveCycle(simTime, elapsed, integrationElapsed);
        }

        private CommittedPortMeasurements BuildCommitted(DateTime simTime)
        {
            var pcs = _ess._pcsList;
            var samples = new PcsCommittedSample[pcs.Count];
            for (int i = 0; i < pcs.Count; i++)
            {
                var st = pcs[i].GetCurrentState();
                samples[i] = new PcsCommittedSample(
                    pcs[i].DeviceId,
                    st.AcVoltage,
                    st.ActivePower,
                    st.ReactivePower,
                    st.Frequency);
            }

            return new CommittedPortMeasurements
            {
                SimTime = simTime,
                Pcs = samples
            };
        }
    }

    internal sealed class PlantStepMemento
    {
        private PcsStepMemento[] _pcs = Array.Empty<PcsStepMemento>();
        private BmsStepMemento[] _bms = Array.Empty<BmsStepMemento>();
        private ThermalStepMemento _thermal;
        private double _pccV;
        private double _bus35V;
        private double _networkPccV;
        private double _networkBus35V;
        private double _systemFrequencyHz;
        private double[] _bus690V = Array.Empty<double>();
        private double[] _bus690F = Array.Empty<double>();
        private double _graphBus35V;
        private double _graphBus35F;
        private CommittedPortMeasurements _committed = CommittedPortMeasurements.Empty;

        public static PlantStepMemento Capture(EnergyStorageSystem ess)
        {
            var m = new PlantStepMemento();
            m._pcs = new PcsStepMemento[ess._pcsList.Count];
            for (int i = 0; i < ess._pcsList.Count; i++)
                m._pcs[i] = ess._pcsList[i].CaptureStepMemento();

            m._bms = new BmsStepMemento[ess._bmsRackDevices.Count];
            for (int i = 0; i < ess._bmsRackDevices.Count; i++)
                m._bms[i] = ess._bmsRackDevices[i].CaptureStepMemento();

            m._thermal = ess.Thermal.CaptureStepMemento();
            m._pccV = ess.PccLineVoltageV;
            m._bus35V = ess.StationBus35LineVoltageV;
            m._networkPccV = ess.ElectricalNetwork.PccLineVoltageV;
            m._networkBus35V = ess.ElectricalNetwork.StationBus35LineVoltageV;
            m._systemFrequencyHz = ess.ElectricalNetwork.SystemFrequencyHz;
            var buses = ess.RadialGraph.AllUnit690Buses.ToList();
            m._bus690V = new double[buses.Count];
            m._bus690F = new double[buses.Count];
            for (int i = 0; i < buses.Count; i++)
            {
                m._bus690V[i] = buses[i].LineVoltageV;
                m._bus690F[i] = buses[i].FrequencyHz;
            }
            m._graphBus35V = ess.RadialGraph.Bus35.LineVoltageV;
            m._graphBus35F = ess.RadialGraph.Bus35.FrequencyHz;
            m._committed = ess.PlantEngine.LastCommittedMeasurements;
            return m;
        }

        public void Restore(EnergyStorageSystem ess)
        {
            for (int i = 0; i < _pcs.Length && i < ess._pcsList.Count; i++)
                ess._pcsList[i].RestoreStepMemento(_pcs[i]);

            for (int i = 0; i < _bms.Length && i < ess._bmsRackDevices.Count; i++)
                ess._bmsRackDevices[i].RestoreStepMemento(_bms[i]);

            ess.Thermal.RestoreStepMemento(_thermal);
            ess.ApplyNetworkGridVoltages(_pccV, _bus35V);
            ess.ElectricalNetwork.PccLineVoltageV = _networkPccV;
            ess.ElectricalNetwork.StationBus35LineVoltageV = _networkBus35V;
            ess.ElectricalNetwork.SystemFrequencyHz = _systemFrequencyHz;
            var buses = ess.RadialGraph.AllUnit690Buses.ToList();
            for (int i = 0; i < _bus690V.Length && i < buses.Count; i++)
            {
                buses[i].LineVoltageV = _bus690V[i];
                buses[i].FrequencyHz = _bus690F[i];
            }
            ess.RadialGraph.Bus35.LineVoltageV = _graphBus35V;
            ess.RadialGraph.Bus35.FrequencyHz = _graphBus35F;
            ess.PlantEngine.RestoreCommitted(_committed);
        }
    }
}
