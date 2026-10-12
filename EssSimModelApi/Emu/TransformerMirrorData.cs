namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 变压器协议镜像（EMU 单元变）：负载率/功率由组内 PCS 求和合成，
    /// 油温为一阶 RC 简化热模型，不依赖电气层内部结构。
    /// </summary>
    public class TransformerMirrorData
    {
        /// <summary>投退状态：0=退出，1=投入（跟随单元高压断路器）。</summary>
        public ushort Closed { get; set; } = 1;
        /// <summary>负载率（0~1，单元 PCS 总有功 / 额定容量）。</summary>
        public float LoadFraction { get; set; }
        /// <summary>有功功率（kW）。</summary>
        public float ActivePowerKw { get; set; }
        /// <summary>无功功率（kvar）。</summary>
        public float ReactivePowerKvar { get; set; }
        /// <summary>油温（°C，一阶 RC 趋向 环境温 + 额定温升×负载率²）。</summary>
        public float OilTemperatureC { get; set; } = 25f;
        /// <summary>运行状态：1=停运，2=空载，3=负载运行。</summary>
        public ushort OperationStatus { get; set; } = 2;
    }
}
