using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>
    /// 电气拓扑运行时容器：持有设备实例与求解器。
    /// </summary>
    public sealed class ElectricalNetwork
    {
        public NetworkTopology Topology { get; init; } = new();
        public GridSimulator Grid { get; init; } = null!;
        public BreakerSimulator MainBreaker { get; init; } = null!;
        public TransformerDevice MainTransformer { get; init; } = null!;
        public LoadDevice Load { get; init; } = null!;
        public MeterSimulator PccMeter { get; init; } = null!;
        public IReadOnlyList<BreakerSimulator> UnitBreakers { get; init; } = Array.Empty<BreakerSimulator>();
        public IReadOnlyList<TransformerDevice> UnitTransformers { get; init; } = Array.Empty<TransformerDevice>();
        /// <summary>与单元对齐；每单元 0～2 台双耳箱变（空列表表示走两绕组单元变）。</summary>
        public IReadOnlyList<IReadOnlyList<DualEarTransformerDevice>> DualEarTransformers { get; init; } =
            Array.Empty<IReadOnlyList<DualEarTransformerDevice>>();
        /// <summary>与单元对齐；双耳单元的 PCS 通道（全局 0 基）分耳，非双耳为 null。</summary>
        public IReadOnlyList<SplitEarAssignment?> SplitEarAssignments { get; init; } =
            Array.Empty<SplitEarAssignment?>();

        public IReadOnlyList<DualEarTransformerDevice> DualEarsOfUnit(int unit) =>
            unit >= 0 && unit < DualEarTransformers.Count
                ? DualEarTransformers[unit]
                : Array.Empty<DualEarTransformerDevice>();
        public IReadOnlyList<PcsDevice> PcsDevices { get; init; } = Array.Empty<PcsDevice>();
        public IReadOnlyList<BmsRackDevice> BmsDevices { get; init; } = Array.Empty<BmsRackDevice>();
        public IReadOnlyList<DcLink> DcLinks { get; init; } = Array.Empty<DcLink>();

        /// <summary>各储能单元下属 PCS 台数（缺失时求解器按每单元 2 台回退）。</summary>
        public IReadOnlyList<int> PcsPerUnit { get; init; } = Array.Empty<int>();

        /// <summary>指定单元第一台 PCS 的全局通道索引（0 基）。</summary>
        public int PcsBaseIndexOfUnit(int unit) => PcsUnitLayout.BaseIndexOfUnit(PcsPerUnit, unit);

        /// <summary>指定单元下属 PCS 台数。</summary>
        public int PcsCountOfUnit(int unit) => PcsUnitLayout.CountOfUnit(PcsPerUnit, unit);

        /// <summary>测试夹具求解器；生产路径不构造，保持 null。</summary>
        public INetworkSolver? Solver { get; set; }

        public double PccLineVoltageV { get; internal set; }
        public double StationBus35LineVoltageV { get; internal set; }
        /// <summary>组态是否包含站用主变；false 时主断下游即站用母线。</summary>
        public bool HasMainTransformer { get; init; } = true;

        /// <summary>当前步系统唯一频率（Hz），由 <see cref="SystemFrequencyResolver"/> 每步刷新。</summary>
        public double SystemFrequencyHz { get; internal set; }

        public ElectricalBus? GetBus(string busId) =>
            Topology.Buses.FirstOrDefault(b => b.BusId == busId);
    }
}
