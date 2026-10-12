using System.Text.RegularExpressions;
using EssSimulator.Configuration;

namespace EssSimulator.DataExchange.Catalog
{
    /// <summary>
    /// EMU 点表绑定按机组构成分门控：EMU 点表 ModelSim 绑定只能指向机组内实际存在的设备模型
    /// （PCS / 电表 / 断路器）。找不到对应设备的绑定在目录编译期剔除，
    /// 点位保持未绑定语义（寄存器仅维持默认值，遥测不刷新、控制写入不生效）。
    /// </summary>
    public sealed partial class EmuDeviceCatalogFilter
    {
        private static readonly Regex EmuRootPattern = EmuRootRegex();

        [GeneratedRegex(@"^emu(\d+)$", RegexOptions.IgnoreCase)]
        private static partial Regex EmuRootRegex();

        private readonly IReadOnlyList<EssUnitConfig> _units;

        private EmuDeviceCatalogFilter(IReadOnlyList<EssUnitConfig> units) => _units = units;

        /// <summary>
        /// 为指定设备创建门控过滤器。仅 EMU 同构设备（simEmu / simLc）且传入了机组构成时启用；
        /// 无拓扑 overlay 的 legacy 配置（机组列表为空）不门控，保持现状。
        /// </summary>
        public static EmuDeviceCatalogFilter? Create(string serverName, IReadOnlyList<EssUnitConfig>? units)
        {
            if (!DataExchangeSession.IsEmuLikeDevice(serverName) || units is not { Count: > 0 })
                return null;
            return new EmuDeviceCatalogFilter(units);
        }

        /// <summary>
        /// 判断单条模型绑定路径是否指向机组内实际存在的设备。
        /// 路径根须为 emuK 机组根路径；PcsList[i] 要求 i 小于该机组 PCS 台数；
        /// ElectricityMeter 要求该机组下属存在电表（直绑单元或任一分组绑定）；Breaker.*（断路器状态镜像）要求该机组下属存在断路器；Emu.PowerOnOff / Breaker.Closed（单元高压断路器控制）要求该机组直绑断路器（PowerOnOff）或下属存在断路器（Breaker.*）；
        /// Groups[g].PcsList[i] / Groups[g].Breaker 按分组构成校验；Transformers[k] 本期仅 k=0（单元变）；
        /// 其余 Emu.* 为单元虚拟模型，恒允许。
        /// </summary>
        public bool Allows(string? bindingPath)
        {
            var target = DataTarget.ParseBindingPath(bindingPath);
            if (target == null)
                return false;

            var match = EmuRootPattern.Match(target.RootKey);
            if (!match.Success)
                return false;

            int unitIndex = int.Parse(match.Groups[1].Value) - 1;
            if (unitIndex < 0 || unitIndex >= _units.Count)
                return false;

            var unit = _units[unitIndex];
            string path = target.PropertyPath;

            if (path.StartsWith("Groups[", StringComparison.OrdinalIgnoreCase))
                return AllowsGroupPath(unit, path);

            if (path.StartsWith("Transformers[", StringComparison.OrdinalIgnoreCase))
            {
                // 本期仅建模 Transformers[0]（电气层单元变）
                int close = path.IndexOf(']', 13);
                return close > 13 && int.TryParse(path[13..close], out int k) && k == 0;
            }

            if (path.Equals("Breaker", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Breaker.", StringComparison.OrdinalIgnoreCase))
                return HasAnyBreaker(unit);

            if (path.StartsWith("PcsList[", StringComparison.OrdinalIgnoreCase))
            {
                int close = path.IndexOf(']', 8);
                if (close <= 8)
                    return false;
                return int.TryParse(path[8..close], out int pcsIndex)
                    && pcsIndex >= 0 && pcsIndex < unit.PcsCount;
            }

            if (path.StartsWith("ElectricityMeter", StringComparison.OrdinalIgnoreCase))
                return HasAnyMeter(unit);

            if (path.StartsWith("Emu.PowerOnOff", StringComparison.OrdinalIgnoreCase))
                return unit.HasUnitBreaker;

            // 其余路径（Emu.* 单元虚拟模型、BMS/直流母线等）恒允许
            return true;
        }

        /// <summary>
        /// 机组下属是否存在电表：直绑单元电表，或任一分组绑定了组电表。
        /// </summary>
        private static bool HasAnyMeter(EssUnitConfig unit) =>
            unit.HasUnitMeter || unit.Groups.Any(g => g.MeterNames.Count > 0);

        /// <summary>
        /// 机组下属是否存在断路器：直绑单元断路器，或任一分组绑定了组断路器。
        /// </summary>
        private static bool HasAnyBreaker(EssUnitConfig unit) =>
            unit.HasUnitBreaker || unit.Groups.Any(g => !string.IsNullOrWhiteSpace(g.BreakerName));

        /// <summary>
        /// 分组路径门控：Groups[g] 索引须有效；组内 PcsList[i] 要求 i 小于组内 PCS 台数；
        /// 无 Groups 时视为隐式组 0（整单元 PCS）。Groups[g].Meters[k] / Breaker 按组绑定校验；
        /// 其余组聚合遥测恒允许。
        /// </summary>
        private static bool AllowsGroupPath(EssUnitConfig unit, string path)
        {
            int close = path.IndexOf(']', 7);
            if (close <= 7 ||
                !int.TryParse(path[7..close], out int groupIndex) ||
                groupIndex < 0)
                return false;

            string rest = path[(close + 1)..];
            if (!unit.HasGroups)
                return groupIndex == 0 && AllowsImplicitGroupRest(unit, rest);

            if (groupIndex >= unit.Groups.Count)
                return false;

            var group = unit.Groups[groupIndex];

            if (rest.StartsWith(".PcsList[", StringComparison.OrdinalIgnoreCase))
            {
                int indexOpen = rest.IndexOf('[', 8);
                int indexClose = indexOpen > 0 ? rest.IndexOf(']', indexOpen + 1) : -1;
                return indexClose > indexOpen
                    && int.TryParse(rest[(indexOpen + 1)..indexClose], out int pcsIndex)
                    && pcsIndex >= 0 && pcsIndex < group.PcsCount;
            }

            if (rest.Equals(".Breaker", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith(".Breaker.", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(group.BreakerName);

            if (rest.StartsWith(".Meters[", StringComparison.OrdinalIgnoreCase))
            {
                int indexClose = rest.IndexOf(']', 8);
                return indexClose > 8
                    && int.TryParse(rest[8..indexClose], out int meterIndex)
                    && meterIndex >= 0 && meterIndex < group.MeterNames.Count;
            }

            // 组聚合遥测（TotalActivePower 等）：索引有效即允许
            return rest.Length == 0 || rest.StartsWith(".", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>扁平机组：Groups[0] 对应整单元 PcsCount；无组断路器/组电表。</summary>
        private static bool AllowsImplicitGroupRest(EssUnitConfig unit, string rest)
        {
            if (rest.StartsWith(".PcsList[", StringComparison.OrdinalIgnoreCase))
            {
                int indexOpen = rest.IndexOf('[', 8);
                int indexClose = indexOpen > 0 ? rest.IndexOf(']', indexOpen + 1) : -1;
                return indexClose > indexOpen
                    && int.TryParse(rest[(indexOpen + 1)..indexClose], out int pcsIndex)
                    && pcsIndex >= 0 && pcsIndex < unit.PcsCount;
            }

            if (rest.Equals(".Breaker", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith(".Breaker.", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith(".Meters[", StringComparison.OrdinalIgnoreCase))
                return false;

            return rest.Length == 0 || rest.StartsWith(".", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 过滤 sum/max 点位的多条路径；过滤后少于两条（sum/max 非法绑定）时返回 null，整点剔除。
        /// </summary>
        public IReadOnlyList<string>? FilterPaths(IReadOnlyList<string> paths)
        {
            var allowed = new List<string>(paths.Count);
            foreach (var path in paths)
            {
                if (Allows(path))
                    allowed.Add(path);
            }
            return allowed.Count >= 2 ? allowed : null;
        }
    }
}
