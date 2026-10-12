using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.ElectricMeter;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.EssSimModelApi
{
    /// <summary>
    /// 电表协议镜像：构造时注册 DTO，投影由 <see cref="ProtocolProjectionService"/> 在物理步进末尾调用。
    /// </summary>
    public class EmDataService
    {
        private readonly EmData _emData;
        private double _forwardKWh;
        private double _reverseKWh;

        public EmDataService()
        {
            _emData = EmDataGenerator.GenerateSampleData();
            SimulatorHost.Instance.Register("em", _emData);
        }

        public void Project(EnergyStorageSystem ess)
        {
            EmMapper.MapEssToEmData(ess, _emData, TimeSpan.Zero,
                ref _forwardKWh, ref _reverseKWh);
        }
    }
}
