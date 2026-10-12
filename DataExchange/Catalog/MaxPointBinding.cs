namespace EssSimulator.DataExchange.Catalog
{
    /// <summary>
    /// 取大点位绑定：ModelSim 为 model=max 的遥测点，寄存器值 = 各绑定路径的最大值（只读，仅 FC3/4）。
    /// 路径格式与 sum 一致：arg1/arg2 经典两路径，或 arg1 分号分隔多路径；
    /// 用于单元级温度等「取最严重模块」口径（如过温降载 NTC = 两模块 IGBT 温度取大）。
    /// </summary>
    public sealed class MaxPointBinding
    {
        /// <summary>ModelSim model 类型标识（不区分大小写）。</summary>
        public const string ModelType = "max";

        public required MapEntry Entry { get; init; }
        public required string ParamName { get; init; }

        /// <summary>参与取大的仿真绑定路径列表（至少两个）。</summary>
        public required IReadOnlyList<string> Paths { get; init; }

        /// <summary>取大运算：null 或非数值操作数忽略；无有效操作数返回 0。</summary>
        public static double ComputeMax(params object?[] operands)
        {
            double? max = null;
            foreach (var value in operands)
            {
                if (value == null || !double.TryParse(value.ToString(), out var number))
                    continue;
                max = max == null ? number : Math.Max(max.Value, number);
            }
            return max ?? 0;
        }
    }
}
