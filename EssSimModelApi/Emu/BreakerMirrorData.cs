namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 断路器协议镜像。EMU 级 Closed 写入驱动单元高压断路器；组级 Breaker 仍为纯采集镜像，不产生电气动作。
    /// </summary>
    public class BreakerMirrorData
    {
        /// <summary>开合状态：0=分，1=合。</summary>
        public ushort Closed { get; set; } = 1;
        /// <summary>协议状态码：0xAA=合（动作），0xEE=分（复归）。</summary>
        public ushort ClosedAaEe => Closed != 0 ? (ushort)0xAA : (ushort)0xEE;
        /// <summary>跳闸信号（本期恒 0）。</summary>
        public ushort Trip { get; set; }
        /// <summary>故障字（本期恒 0）。</summary>
        public ushort Fault { get; set; }
    }
}
