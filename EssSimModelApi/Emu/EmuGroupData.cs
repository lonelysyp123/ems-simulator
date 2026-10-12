namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// EMU 内协议分组镜像（EMU → group → PCS 支路）：
    /// PcsList 与单元扁平 PcsList 持有同一 PcsData 实例引用，
    /// 两条路径读写同一对象；组级断路器为协议镜像（无电气动作）。
    /// </summary>
    public class EmuGroupData
    {
        public string Name { get; set; } = "Group";
        /// <summary>组内 PCS（与单元扁平 PcsList 共享实例引用）。</summary>
        public List<PcsData> PcsList { get; set; } = new List<PcsData>();
        /// <summary>组级断路器协议镜像；组未绑定断路器时为 null。</summary>
        public BreakerMirrorData? Breaker { get; set; }
        /// <summary>组级电表协议镜像列表；按组态绑定顺序与组配置 MeterNames 索引对齐。</summary>
        public List<ElectricityMeterData> Meters { get; set; } = new List<ElectricityMeterData>();

        /// <summary>组总有功功率（kW，组内 PCS 求和）。</summary>
        public float TotalActivePower { get; set; }
        /// <summary>组总无功功率（kvar）。</summary>
        public float TotalReactivePower { get; set; }
        /// <summary>组内 PCS 总台数。</summary>
        public int TotalPcsCount { get; set; }
        /// <summary>组内在线（非停机）PCS 台数。</summary>
        public int OnlinePcsCount { get; set; }
        /// <summary>组内告警 PCS 台数。</summary>
        public int AlarmPcsCount { get; set; }
        /// <summary>组内故障 PCS 台数。</summary>
        public int FaultPcsCount { get; set; }
    }
}
