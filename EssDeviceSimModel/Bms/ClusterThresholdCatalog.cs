using System.Reflection;

namespace EssSimulator.EssDeviceSimModel.Bms
{
    /// <summary>簇告警门限字段元数据（来自 <see cref="ClusterThresholds"/>，与点表无关）。</summary>
    public sealed class ClusterThresholdField
    {
        public string PropertyName { get; init; } = "";
        public string Description { get; init; } = "";
        /// <summary>1=一级故障 / 2=二级告警 / 3=三级保护。</summary>
        public int Level { get; init; }
        public bool IsRecovery { get; init; }
        public string Category { get; init; } = "其他";
        public string UnitHint { get; init; } = "";
    }

    /// <summary>
    /// 从 <see cref="ClusterThresholds"/> 反射出门限/恢复字段。
    /// 保护评估消费这些属性；点表只决定其中哪些会暴露为 Modbus 寄存器。
    /// </summary>
    public static class ClusterThresholdCatalog
    {
        private static readonly Lazy<IReadOnlyList<ClusterThresholdField>> Fields = new(DiscoverFields);

        public static IReadOnlyList<ClusterThresholdField> ListFields() => Fields.Value;

        public static bool IsKnownProperty(string propertyName) =>
            !string.IsNullOrWhiteSpace(propertyName) &&
            Fields.Value.Any(f => f.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<ClusterThresholdField> DiscoverFields()
        {
            var props = typeof(ClusterThresholds)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && IsFloatProperty(p) && !IsAlias(p.Name))
                .ToList();

            var fields = new List<ClusterThresholdField>(props.Count);
            foreach (var prop in props)
            {
                bool isRecovery = prop.Name.Contains("Recovery", StringComparison.OrdinalIgnoreCase);
                string category = DetectCategory(prop.Name);
                int level = DetectLevel(prop.Name);
                fields.Add(new ClusterThresholdField
                {
                    PropertyName = prop.Name,
                    Category = category,
                    Level = level,
                    IsRecovery = isRecovery,
                    UnitHint = DetectUnitHint(prop.Name),
                    Description = BuildDescription(category, level, isRecovery)
                });
            }

            return fields
                .OrderByDescending(f => f.Level)
                .ThenBy(f => f.Category, StringComparer.Ordinal)
                .ThenBy(f => f.IsRecovery)
                .ThenBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsFloatProperty(PropertyInfo prop)
        {
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            return t == typeof(float);
        }

        /// <summary>跳过 <c>LowSOCThresholdN</c> 点表别名，保留实际字段 <c>LowSOCTresholdN</c>。</summary>
        private static bool IsAlias(string name) =>
            name.Equals("LowSOCThreshold1", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LowSOCThreshold2", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LowSOCThreshold3", StringComparison.OrdinalIgnoreCase);

        private static int DetectLevel(string propertyName)
        {
            if (propertyName.EndsWith("3", StringComparison.Ordinal)) return 1;
            if (propertyName.EndsWith("2", StringComparison.Ordinal)) return 2;
            if (propertyName.EndsWith("1", StringComparison.Ordinal)) return 3;
            return 0;
        }

        private static string DetectCategory(string propertyName)
        {
            if (Contains(propertyName, "CellOvervoltage")) return "单体过压";
            if (Contains(propertyName, "CellUndervoltage")) return "单体欠压";
            if (Contains(propertyName, "TotalVoltageDifference")) return "总压压差";
            if (Contains(propertyName, "CellVoltageDifference")) return "单体压差";
            if (Contains(propertyName, "Overvoltage")) return "总压过压";
            if (Contains(propertyName, "Undervoltage")) return "总压欠压";
            if (Contains(propertyName, "ChargeOvercurrent")) return "充电过流";
            if (Contains(propertyName, "DischargeOvercurrent")) return "放电过流";
            if (Contains(propertyName, "ChargeHighTemp")) return "充电高温";
            if (Contains(propertyName, "ChargeLowTemp")) return "充电低温";
            if (Contains(propertyName, "DischargeHighTemp")) return "放电高温";
            if (Contains(propertyName, "DischargeLowTemp")) return "放电低温";
            if (Contains(propertyName, "LowSOC")) return "低SOC";
            if (Contains(propertyName, "PoleHighTemp")) return "极柱温度";
            if (Contains(propertyName, "Insulation")) return "绝缘";
            if (Contains(propertyName, "CellTempDifference")) return "温差";
            if (Contains(propertyName, "HVB")) return "高压箱温度";
            return "其他";
        }

        private static string DetectUnitHint(string propertyName)
        {
            if (Contains(propertyName, "LowSOC")) return "pu(0~1)";
            if (Contains(propertyName, "Temp") || Contains(propertyName, "HVB")) return "°C";
            if (Contains(propertyName, "Current")) return "A";
            if (Contains(propertyName, "Insulation")) return "kΩ";
            if (Contains(propertyName, "Voltage") || Contains(propertyName, "Overvoltage") ||
                Contains(propertyName, "Undervoltage"))
                return "V";
            return "";
        }

        private static string BuildDescription(string category, int level, bool isRecovery)
        {
            string levelName = level switch
            {
                1 => "一级故障",
                2 => "二级告警",
                3 => "三级保护",
                _ => ""
            };
            string recover = isRecovery ? "恢复" : "";
            return $"{category}{levelName}{recover}门限";
        }

        private static bool Contains(string propertyName, string key) =>
            propertyName.Contains(key, StringComparison.OrdinalIgnoreCase);
    }
}
