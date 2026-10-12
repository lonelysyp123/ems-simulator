namespace EssSimulator.DataExchange.Catalog
{
    /// <summary>
    /// 加法点位绑定：ModelSim 为 model=sum 的遥测点，寄存器值 = 各绑定路径之和（只读，仅 FC3/4）。
    /// 路径两种写法（可混用）：
    ///   - model=sum|arg1=&lt;路径A&gt;|arg2=&lt;路径B&gt;（两路径经典格式）
    ///   - arg1=&lt;路径1&gt;;&lt;路径2&gt;;...（分号分隔多路径，用于多机组系统级聚合）
    /// </summary>
    public sealed class SumPointBinding
    {
        /// <summary>ModelSim model 类型标识（不区分大小写）。</summary>
        public const string ModelType = "sum";

        /// <summary>多路径格式的分隔符（arg1 内部）。</summary>
        public const char MultiPathSeparator = ';';

        public required MapEntry Entry { get; init; }
        public required string ParamName { get; init; }

        /// <summary>参与求和的仿真绑定路径列表（至少两个）。</summary>
        public required IReadOnlyList<string> Paths { get; init; }

        /// <summary>加数 A 的仿真绑定路径（ModelSim arg1 首路径）。</summary>
        public string FirstPath => Paths[0];

        /// <summary>加数 B 的仿真绑定路径（第二个路径）。</summary>
        public string SecondPath => Paths[1];

        /// <summary>
        /// 解析 ModelSim 参数为路径列表：arg1 支持分号分隔多路径，arg2 作为经典第二路径补充；
        /// 空段剔除。路径少于两个返回 null（非法 sum 绑定）。
        /// </summary>
        public static IReadOnlyList<string>? ParsePaths(string? arg1, string? arg2)
        {
            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(arg1))
                paths.AddRange(arg1.Split(MultiPathSeparator)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0));
            if (!string.IsNullOrWhiteSpace(arg2))
                paths.Add(arg2.Trim());
            return paths.Count >= 2 ? paths : null;
        }

        /// <summary>加法运算：null 或非数值操作数按 0 处理。</summary>
        public static double ComputeSum(params object?[] operands) =>
            operands.Sum(ToNumber);

        private static double ToNumber(object? value) =>
            value != null && double.TryParse(value.ToString(), out var number) ? number : 0;
    }
}
