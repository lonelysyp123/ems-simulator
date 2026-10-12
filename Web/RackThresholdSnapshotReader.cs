using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Web
{
    public sealed class RackThresholdPointDto
    {
        /// <summary>稳定行键，等于 <see cref="PropertyName"/>。</summary>
        public string ParamName { get; set; } = "";
        public int Address { get; set; }
        public int Scale { get; set; }
        public string Type { get; set; } = "u16";
        public string Description { get; set; } = "";
        public string PropertyName { get; set; } = "";
        /// <summary>点表控制点名（如 yt0）；当前型号未暴露寄存器时为空。</summary>
        public string? ProtocolParamName { get; set; }
        public bool ExposedOnProtocol { get; set; }
        /// <summary>1=一级故障 / 2=二级告警 / 3=三级保护。</summary>
        public int Level { get; set; }
        public bool IsRecovery { get; set; }
        public string Category { get; set; } = "其他";
        public string UnitHint { get; set; } = "";
        /// <summary>当前工程值（模型层 ClusterThresholds）。</summary>
        public double? EngineeringValue { get; set; }
        /// <summary>若点表暴露该门限，为工程值 × Scale 的寄存器原始值。</summary>
        public int? RawValue { get; set; }
    }

    public sealed class RackThresholdSnapshotDto
    {
        public int UnitNumber { get; set; }
        public string Device { get; set; } = "";
        public int RackIndex { get; set; }
        public int ClusterCount { get; set; }
        public List<RackThresholdPointDto> Points { get; set; } = new();
    }

    /// <summary>BMS 簇告警门限快照：模型层 ClusterThresholds + 可选点表寄存器表面。</summary>
    public static class RackThresholdSnapshotReader
    {
        public static RackThresholdSnapshotDto? Read(int unitNumber1Based, int rackIndex0)
        {
            if (SimulatorHost.Instance.TryGetBms(unitNumber1Based) == null)
                return null;

            int clusterCount = ResolveClusterCount(unitNumber1Based);
            if (clusterCount <= 0)
                return null;
            if (rackIndex0 < 0 || rackIndex0 >= clusterCount)
                rackIndex0 = 0;

            var protocolByProperty = IndexProtocolBindings(unitNumber1Based);
            var points = new List<RackThresholdPointDto>();
            foreach (var field in ClusterThresholdCatalog.ListFields())
            {
                string path = ThresholdPath(unitNumber1Based, rackIndex0, field.PropertyName);
                double? eng = TryReadEngineering(path);
                protocolByProperty.TryGetValue(field.PropertyName, out var proto);
                int scale = proto.Scale > 0 ? proto.Scale : 0;

                points.Add(new RackThresholdPointDto
                {
                    ParamName = field.PropertyName,
                    PropertyName = field.PropertyName,
                    Description = proto.Description ?? field.Description,
                    Level = field.Level,
                    IsRecovery = field.IsRecovery,
                    Category = field.Category,
                    UnitHint = field.UnitHint,
                    EngineeringValue = eng,
                    ExposedOnProtocol = proto.ParamName != null,
                    ProtocolParamName = proto.ParamName,
                    Address = proto.Address,
                    Scale = scale,
                    Type = proto.Type ?? "u16",
                    RawValue = eng != null && scale > 0 ? (int)Math.Round(eng.Value * scale) : null
                });
            }

            return new RackThresholdSnapshotDto
            {
                UnitNumber = unitNumber1Based,
                Device = $"simBms{unitNumber1Based}",
                RackIndex = rackIndex0,
                ClusterCount = clusterCount,
                Points = points
            };
        }

        internal static string ThresholdPath(int unitNumber1Based, int rackIndex0, string propertyName) =>
            $"bms{unitNumber1Based}.BatteryStacks[0].Cluseter[{rackIndex0}].Thresholds.{propertyName}";

        internal static Dictionary<string, ProtocolBinding> IndexProtocolBindings(int unitNumber1Based)
        {
            var map = new Dictionary<string, ProtocolBinding>(StringComparer.OrdinalIgnoreCase);
            var srv = SimulatorHost.Instance.Get<ModbusSimServer>($"simBms{unitNumber1Based}");
            if (srv == null)
                return map;

            foreach (var entry in srv.RackControlMaps)
            {
                var model = ModbusSimServer.GetModelParam(entry.ModelSim ?? "");
                string arg1 = model?.Arg1 ?? "";
                if (string.IsNullOrWhiteSpace(arg1) ||
                    !arg1.Contains("Thresholds.", StringComparison.OrdinalIgnoreCase))
                    continue;

                string propertyName = ExtractPropertyName(arg1);
                if (string.IsNullOrWhiteSpace(propertyName) || map.ContainsKey(propertyName))
                    continue;

                map[propertyName] = new ProtocolBinding(
                    entry.ParamName,
                    entry.Address,
                    entry.Scale <= 0 ? 1 : entry.Scale,
                    entry.Type ?? "u16",
                    string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description);
            }

            return map;
        }

        private static int ResolveClusterCount(int unitNumber1Based)
        {
            try
            {
                var v = SimServer.GetExtIfVariableVal($"bms{unitNumber1Based}.BatteryStacks[0].Cluseter.Count");
                if (v is int i) return Math.Max(0, i);
                if (v is long l) return (int)Math.Max(0, Math.Min(int.MaxValue, l));
                if (v != null && int.TryParse(v.ToString(), out int p)) return Math.Max(0, p);
            }
            catch
            {
                /* fall through */
            }

            return GuiSimDataAccess.GetClusterCount();
        }

        private static double? TryReadEngineering(string path)
        {
            try
            {
                var o = SimServer.GetExtIfVariableVal(path);
                if (o == null) return null;
                return Convert.ToDouble(o);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractPropertyName(string arg1)
        {
            int idx = arg1.LastIndexOf('.');
            return idx >= 0 && idx < arg1.Length - 1 ? arg1[(idx + 1)..] : arg1;
        }

        internal readonly record struct ProtocolBinding(
            string? ParamName,
            int Address,
            int Scale,
            string? Type,
            string? Description);
    }
}
