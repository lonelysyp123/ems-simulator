using System;
using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 同端口同从站号下的点位地址重叠校验器。
    /// 按寄存器空间分组校验：保持寄存器（FC3/6/16）、输入寄存器（FC4）、线圈（FC1/5），
    /// 每个点位占用 [Address, Address + max(1, Size/16)) 区间，区间相交即判定冲突。
    /// </summary>
    public static class AddressOverlapValidator
    {
        /// <summary>单个设备在某个从站号下挂载的点表。</summary>
        public sealed class DevicePointMap
        {
            public DevicePointMap(string deviceName, byte slaveId, IReadOnlyList<MapEntry> entries)
            {
                DeviceName = deviceName;
                SlaveId = slaveId;
                Entries = entries;
            }

            public string DeviceName { get; }
            public byte SlaveId { get; }
            public IReadOnlyList<MapEntry> Entries { get; }
        }

        /// <summary>
        /// 校验一组设备点表（可能来自不同设备、不同从站号）。
        /// 仅校验同 (SlaveId, 寄存器空间) 内跨设备的地址重叠；同设备内部重复不在本次校验范围。
        /// </summary>
        public static List<string> Validate(IEnumerable<DevicePointMap> maps)
        {
            var errors = new List<string>();
            var spans = new List<(byte SlaveId, int Space, string Device, MapEntry Entry, int Start, int End)>();
            foreach (var map in maps)
            {
                if (map.Entries == null)
                    continue;
                foreach (var entry in map.Entries)
                {
                    int space = RegisterSpaceOf(entry.FunctionCode);
                    if (space < 0)
                        continue;
                    int len = Math.Max(1, entry.Size / 16);
                    spans.Add((map.SlaveId, space, map.DeviceName, entry, entry.Address, entry.Address + len));
                }
            }

            var ordered = spans
                .OrderBy(s => s.SlaveId)
                .ThenBy(s => s.Space)
                .ThenBy(s => s.Start)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    var a = ordered[i];
                    var b = ordered[j];
                    if (a.SlaveId != b.SlaveId || a.Space != b.Space)
                        continue;
                    if (a.Start >= b.End || b.Start >= a.End)
                        continue;
                    // 同一设备内同地址的多条目（如 FC1 读 / FC5 写同线圈）不算跨设备冲突
                    if (string.Equals(a.Device, b.Device, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int overlapStart = Math.Max(a.Start, b.Start);
                    int overlapEnd = Math.Min(a.End, b.End);
                    errors.Add(
                        $"从站 {a.SlaveId} {SpaceName(a.Space)} 地址 {overlapStart}-{overlapEnd - 1} 冲突：" +
                        $"{a.Device}[{a.Entry.ParamName} @ {a.Start}] 与 {b.Device}[{b.Entry.ParamName} @ {b.Start}] 重叠");
                }
            }

            return errors;
        }

        /// <summary>寄存器空间：0=保持寄存器，1=输入寄存器，2=线圈，-1=不参与校验。</summary>
        public static int RegisterSpaceOf(int functionCode) => functionCode switch
        {
            3 or 6 or 16 => 0,
            4 => 1,
            1 or 5 => 2,
            _ => -1
        };

        private static string SpaceName(int space) => space switch
        {
            0 => "保持寄存器",
            1 => "输入寄存器",
            2 => "线圈",
            _ => "未知空间"
        };
    }
}
