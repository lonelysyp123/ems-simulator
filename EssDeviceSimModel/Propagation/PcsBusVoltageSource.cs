using System.Numerics;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>黑启动 / 离网 V/f 模式下 PCS 作为 690V 母线电压源。</summary>
    internal sealed class PcsBusVoltageSource : IBusVoltageSource
    {
        private readonly PcsDevice _pcs;

        public PcsBusVoltageSource(PcsDevice pcs) => _pcs = pcs;

        public string SourceId => _pcs.DeviceId;

        public Complex OutputImpedanceOhm =>
            MinPcsPort.FormingImpedanceOhm(_pcs._config.RatedPower, _pcs._config.AcVoltageNominal);

        /// <summary>已提交电池电压（上一拍耦合边写入），供本拍直流结算用。</summary>
        public double DcVoltageV => _pcs.GetCurrentState().DcVoltage;

        public double Efficiency =>
            _pcs._config.Efficiency > 0 && _pcs._config.Efficiency <= 1.0
                ? _pcs._config.Efficiency
                : AverageConverterPort.DefaultEfficiency;

        public bool IsInjecting(DeviceStepContext context) =>
            _pcs.TryGetIslandBusVoltageInjection(out _, out _);

        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context)
        {
            if (!_pcs.TryGetIslandBusVoltageInjection(out var v, out var f))
                return (0, 50);
            return (v, f);
        }
    }
}
