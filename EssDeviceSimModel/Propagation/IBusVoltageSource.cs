using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Propagation
{
    /// <summary>
    /// 向指定母线注入电压的本地源（如黑启动 PCS V/f），与电网等全局源互补。
    /// </summary>
    public interface IBusVoltageSource
    {
        string SourceId { get; }

        /// <summary>当前周期是否应向所注册母线注入电压。</summary>
        bool IsInjecting(DeviceStepContext context);

        /// <summary>注入电压（线电压 V）与频率 Hz。</summary>
        (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context);

        /// <summary>
        /// 该源贴入相量核时的构网输出阻抗（E 串 Z）。默认沿用阶段 2 占位；
        /// 实际 PCS 源按额定折算标幺电抗覆盖。
        /// </summary>
        Complex OutputImpedanceOhm => MinPcsPort.DefaultOutputImpedanceOhm;

        /// <summary>本拍直流结算用的已提交电池电压（V）。&lt;=0 表示无直流侧，不结算。默认 0。</summary>
        double DcVoltageV => 0;

        /// <summary>变流器效率 (0,1]，用于直流结算。默认取平均变流器占位效率。</summary>
        double Efficiency => AverageConverterPort.DefaultEfficiency;
    }
}
