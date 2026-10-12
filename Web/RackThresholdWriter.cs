using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Web
{
    public sealed class RackThresholdWriteItemDto
    {
        public string PropertyName { get; set; } = "";
        public double EngineeringValue { get; set; }
    }

    public sealed class RackThresholdWriteRequest
    {
        /// <summary>簇索引（0-based）或 <c>*</c> 表示全部簇。</summary>
        public string? Rack { get; set; }
        public List<RackThresholdWriteItemDto> Items { get; set; } = new();
    }

    public sealed class RackThresholdWriteResult
    {
        public bool Ok { get; set; }
        public int Written { get; set; }
        public int ClusterCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Messages { get; set; } = new();
    }

    /// <summary>将工程值写入模型层 ClusterThresholds；若点表暴露对应寄存器则同步 Holding。</summary>
    public static class RackThresholdWriter
    {
        public static RackThresholdWriteResult Apply(int unitNumber1Based, RackThresholdWriteRequest request)
        {
            var result = new RackThresholdWriteResult();
            if (SimulatorHost.Instance.TryGetBms(unitNumber1Based) == null)
            {
                result.Errors.Add($"未找到设备 bms{unitNumber1Based}");
                return result;
            }

            int clusterCount = ResolveClusterCount(unitNumber1Based);
            result.ClusterCount = clusterCount;
            if (clusterCount <= 0)
            {
                result.Errors.Add("簇数量为 0");
                return result;
            }

            var racks = ResolveRacks(request.Rack, clusterCount, result);
            if (racks.Count == 0)
                return result;

            var items = request.Items ?? new List<RackThresholdWriteItemDto>();
            if (items.Count == 0)
            {
                result.Errors.Add("没有要写入的门限");
                return result;
            }

            var protocol = RackThresholdSnapshotReader.IndexProtocolBindings(unitNumber1Based);
            var srv = SimulatorHost.Instance.Get<ModbusSimServer>($"simBms{unitNumber1Based}");

            foreach (var rack in racks)
            {
                foreach (var item in items)
                {
                    if (!TryWriteOne(unitNumber1Based, rack, item, protocol, srv, result))
                        continue;
                    result.Written++;
                }
            }

            result.Ok = result.Errors.Count == 0 && result.Written > 0;
            return result;
        }

        private static bool TryWriteOne(
            int unitNumber1Based,
            int rackIndex,
            RackThresholdWriteItemDto item,
            IReadOnlyDictionary<string, RackThresholdSnapshotReader.ProtocolBinding> protocol,
            ModbusSimServer? srv,
            RackThresholdWriteResult result)
        {
            string propertyName = (item.PropertyName ?? "").Trim();
            if (!ClusterThresholdCatalog.IsKnownProperty(propertyName))
            {
                result.Errors.Add($"未知门限属性 `{propertyName}`");
                return false;
            }

            string path = RackThresholdSnapshotReader.ThresholdPath(unitNumber1Based, rackIndex, propertyName);
            if (!SimServer.SetExtIfVariableVal(path, (float)item.EngineeringValue))
            {
                result.Errors.Add($"写入模型失败: {path}");
                return false;
            }

            if (protocol.TryGetValue(propertyName, out var proto) &&
                proto.ParamName != null &&
                srv != null)
            {
                int scale = proto.Scale > 0 ? proto.Scale : 1;
                int raw = (int)Math.Round(item.EngineeringValue * scale);
                if (!srv.TrySetRackControl(rackIndex, proto.ParamName, raw, out var msg))
                    result.Messages.Add($"模型已写入；寄存器同步失败 r{rackIndex}.{proto.ParamName}: {msg}");
                else
                    result.Messages.Add(msg);
            }
            else
            {
                result.Messages.Add(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] bms{unitNumber1Based}.r{rackIndex}.{propertyName} " +
                    $"写入工程值 {item.EngineeringValue}");
            }

            return true;
        }

        private static List<int> ResolveRacks(string? rackToken, int clusterCount, RackThresholdWriteResult result)
        {
            var racks = new List<int>();
            string token = string.IsNullOrWhiteSpace(rackToken) ? "0" : rackToken.Trim();
            if (token == "*")
            {
                for (int i = 0; i < clusterCount; i++)
                    racks.Add(i);
                return racks;
            }

            if (!int.TryParse(token, out int rack) || rack < 0 || rack >= clusterCount)
            {
                result.Errors.Add($"簇索引无效: `{token}`（有效 0..{clusterCount - 1} 或 *）");
                return racks;
            }

            racks.Add(rack);
            return racks;
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
                /* ignore */
            }

            return 0;
        }
    }
}
