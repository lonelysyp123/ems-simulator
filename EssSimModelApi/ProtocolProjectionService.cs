using System;
using EssSimulator.EssDeviceSimModel;
using AfterPlantStepGate = EssSimulator.EssDeviceSimModel.AfterPlantStep;

namespace EssSimulator.EssSimModelApi
{
    /// <summary>
    /// 把 PCS/EMU、电表、BMS 协议镜像投影挂到物理步进末尾，与 <see cref="PlantEngine.Step"/> 共用同一拍。
    /// </summary>
    public sealed class ProtocolProjectionService : IAfterPlantStep, IDisposable
    {
        private readonly PcsDataServer _pcs;
        private readonly EmDataService _em;
        private readonly BmsDataService _bms;

        public ProtocolProjectionService(PcsDataServer pcs, EmDataService em, BmsDataService bms)
        {
            _pcs = pcs ?? throw new ArgumentNullException(nameof(pcs));
            _em = em ?? throw new ArgumentNullException(nameof(em));
            _bms = bms ?? throw new ArgumentNullException(nameof(bms));
        }

        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed)
        {
            _pcs.Project(ess);
            _em.Project(ess);
            _bms.Project(ess);
        }

        public void Dispose()
        {
            if (ReferenceEquals(AfterPlantStepGate.Current, this))
                AfterPlantStepGate.Reset();
        }
    }
}
