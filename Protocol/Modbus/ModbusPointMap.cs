using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 负责从 CSV 文件读取 Modbus 点位表，并建立以下索引：
    ///   - dataMaps / controlMaps：按功能码分类的主设备点表
    ///   - rackDataMaps / rackControlMaps：BMS rack 级点表
    ///   - paramModelLookup / rackParamModelLookup：点名 → 模拟模型
    ///   - modelParamLookup / rackModelParamLookup：模型类型 → 点列表（用于分组 Worker）
    ///   - defaultBuffer：CSV 中直接给定固定值的点
    /// </summary>
    public class ModbusPointMap
    {
        public List<MapEntry> DataMaps    { get; } = new();
        public List<MapEntry> ControlMaps { get; } = new();
        public List<MapEntry> RackDataMaps    { get; } = new();
        public List<MapEntry> RackControlMaps { get; } = new();

        public Dictionary<string, ModesimModel>    ParamModelLookup     { get; } = new();
        public Dictionary<string, ModesimModel>    RackParamModelLookup { get; } = new();
        public Dictionary<string, List<MapEntry>>  ModelParamLookup     { get; } = new();
        public Dictionary<string, List<MapEntry>>  RackModelParamLookup { get; } = new();
        public Dictionary<string, object>          DefaultBuffer        { get; } = new();

        /// <summary>所有点位原始数组，按 [bank, rack, ...] 顺序，供 ModbusTCPSlave 使用</summary>
        public List<MapEntry[]> RawMaps { get; } = new();

        /// <summary>用已展开/拼装的条目建索引，不再读选型 CSV。</summary>
        public ModbusPointMap(
            IReadOnlyList<MapEntry> entries,
            string serverName,
            int? emuDeviceIdOverride = null,
            int? pvDeviceIdOverride = null,
            int? inverterIndex = null)
        {
            var arr = entries?.ToArray() ?? Array.Empty<MapEntry>();
            ApplyDeviceIdSubstitution(
                arr,
                serverName,
                isEmu: serverName.Contains("Emu", StringComparison.OrdinalIgnoreCase),
                emuDeviceIdOverride,
                pcsIndex: 0,
                pvDeviceIdOverride,
                inverterIndex);
            IndexBankEntries(arr);
            RawMaps.Add(arr);
        }

        /// <summary>从仓库 LC 片段按组拼装后建索引。</summary>
        public static ModbusPointMap FromComposedLc(
            string serverName,
            int groupCount,
            int? emuDeviceIdOverride = null,
            string? modelsRoot = null,
            int maxPcsPerGroup = 0)
        {
            int groups = Math.Max(1, groupCount);
            var composed = modelsRoot != null
                ? LcPointMapComposer.Compose(modelsRoot, groups, maxPcsPerGroup)
                : LcPointMapComposer.ComposeFromRepo(groups, maxPcsPerGroup);
            return new ModbusPointMap(composed, serverName, emuDeviceIdOverride);
        }

        /// <summary>
        /// 运行期 LC 点表：选中互斥型号且本单元 PCS≤2 时加载该整表；否则按组拼装片段。
        /// </summary>
        public static ModbusPointMap ForLocalControl(
            string serverName,
            int groupCount,
            int? emuDeviceIdOverride = null,
            string? modelsRoot = null,
            string? selectionRoot = null,
            IReadOnlyList<EssUnitConfig>? essUnits = null)
        {
            var unit = ResolveEssUnit(essUnits, emuDeviceIdOverride);
            int unitPcs = unit == null ? 0 : unit.PcsCount;
            int maxPcsPerGroup = LcLayout.MaxPcsInAnyGroup(unit);

            if (DeviceModelRegistry.TryGetExclusiveLcCsv(out var csv, selectionRoot)
                && LcLayout.ShouldUseExclusiveEmuMap(unitPcs))
                return new ModbusPointMap(csv, serverName, emuDeviceIdOverride: emuDeviceIdOverride, lcGroupCount: 1);

            return FromComposedLc(serverName, groupCount, emuDeviceIdOverride, modelsRoot, maxPcsPerGroup);
        }

        private static EssUnitConfig? ResolveEssUnit(IReadOnlyList<EssUnitConfig>? essUnits, int? emuDeviceIdOverride)
        {
            if (essUnits == null || emuDeviceIdOverride is not int id || id < 1 || id > essUnits.Count)
                return null;
            return essUnits[id - 1];
        }

        /// <param name="lcGroupCount">LC 点表按组展开的组数；非 LC 文件忽略。单片段 Expand 默认 2 与改模板前 8 通道口径一致。</param>
        /// <param name="pcsIndex">EMU 单台 PCS 点表：替换 ModelSim 中的 pcsIndex（单元内扁平下标）。</param>
        public ModbusPointMap(
            string mapFilePath,
            string serverName,
            int clusterCount = 0,
            int? emuDeviceIdOverride = null,
            int lcGroupCount = 2,
            int pcsIndex = 0,
            int? pvDeviceIdOverride = null,
            int? inverterIndex = null)
        {
            var resolvedPath = PointMapPathResolver.Resolve(mapFilePath);
            var entries = LoadBankEntries(resolvedPath, lcGroupCount);

            ApplyDeviceIdSubstitution(
                entries,
                serverName,
                isEmu: serverName.Contains("Emu", StringComparison.OrdinalIgnoreCase),
                emuDeviceIdOverride,
                pcsIndex,
                pvDeviceIdOverride,
                inverterIndex);

            IndexBankEntries(entries);
            RawMaps.Add(entries);

            if (resolvedPath.Contains("bms_bank", StringComparison.OrdinalIgnoreCase))
                DefaultBuffer.TryAdd("param4", (ushort)2);

            if (serverName.Contains("bms", StringComparison.OrdinalIgnoreCase) && clusterCount > 0)
                LoadRackMap(PointMapPathResolver.ResolveSibling(resolvedPath, "bms_rack.csv"), serverName);
        }

        private static MapEntry[] LoadBankEntries(string resolvedPath, int lcGroupCount)
        {
            if (string.Equals(Path.GetFileName(resolvedPath), "lc.csv", StringComparison.OrdinalIgnoreCase))
            {
                int groups = Math.Max(1, lcGroupCount);
                return LcPointMapExpander.ExpandFile(resolvedPath, groups).ToArray();
            }

            return CSVUtil.CSV2Class<MapEntry>(resolvedPath)?.ToArray()
                ?? throw new Exception($"Modbus bank map 读取失败: {resolvedPath}");
        }

        private void IndexBankEntries(MapEntry[] entries)
        {
            DataMaps.AddRange(entries.Where(m => m.FunctionCode is 3 or 4));
            ControlMaps.AddRange(entries.Where(m => m.FunctionCode is 5 or 6 or 16));

            foreach (var entry in entries)
            {
                var model = ModbusSimServer.GetModelParam(entry.ModelSim!);
                if (model == null)
                {
                    if (!string.IsNullOrWhiteSpace(entry.ModelSim) &&
                        float.TryParse(entry.ModelSim, out var dv))
                        DefaultBuffer[entry.ParamName!] = dv;
                    continue;
                }

                ParamModelLookup[entry.ParamName!] = model;

                if (string.IsNullOrWhiteSpace(model.ModelType) || entry.FunctionCode is 5 or 6 or 16)
                    continue;

                if (!ModelParamLookup.TryGetValue(model.ModelType, out var list))
                    ModelParamLookup[model.ModelType] = list = new List<MapEntry>();
                list.Add(entry);
            }
        }

        private void LoadRackMap(string rackPath, string serverName)
        {
            var entries = CSVUtil.CSV2Class<MapEntry>(rackPath)?.ToArray()
                ?? throw new Exception($"Modbus rack map 读取失败: {rackPath}");

            ApplyDeviceIdSubstitution(entries, serverName, isEmu: false, pcsIndex: 0);
            RawMaps.Add(entries);

            foreach (var entry in entries)
            {
                var model = ModbusSimServer.GetModelParam(entry.ModelSim!);
                if (model == null) continue;

                RackParamModelLookup[entry.ParamName!] = model;
                if (string.IsNullOrWhiteSpace(model.ModelType) || entry.FunctionCode is 5 or 6 or 16) continue;

                if (!RackModelParamLookup.TryGetValue(model.ModelType, out var list))
                    RackModelParamLookup[model.ModelType] = list = new List<MapEntry>();
                list.Add(entry);
            }

            RackDataMaps.AddRange(entries.Where(m => m.FunctionCode is 3 or 4));
            RackControlMaps.AddRange(entries.Where(m => m.FunctionCode is 5 or 6 or 16));
        }

        /// <summary>
        /// 设备号占位符替换。<paramref name="emuDeviceIdOverride"/> 非空时（LC 聚合组首机组语义），
        /// 无论设备本身是否为 EMU，都把 emuDeviceId 替换为指定机组根路径。
        /// </summary>
        private static void ApplyDeviceIdSubstitution(
            MapEntry[] entries,
            string name,
            bool isEmu,
            int? emuDeviceIdOverride = null,
            int pcsIndex = 0,
            int? pvDeviceIdOverride = null,
            int? inverterIndex = null)
        {
            if (pvDeviceIdOverride is <= 0)
                throw new ArgumentOutOfRangeException(nameof(pvDeviceIdOverride), "光伏单元编号必须大于 0");
            if (inverterIndex is < 0)
                throw new ArgumentOutOfRangeException(nameof(inverterIndex), "逆变器下标不能为负数");
            if (entries.Any(e => e.ModelSim?.Contains("inverterIndex", StringComparison.Ordinal) == true)
                && (!pvDeviceIdOverride.HasValue || !inverterIndex.HasValue))
                throw new ArgumentException("逆变器点表必须显式指定光伏单元编号和单元内逆变器下标");

            // 单机服务编号不是光伏单元编号，显式绑定不能依赖服务名中的数字。
            foreach (var e in entries)
            {
                if (pvDeviceIdOverride is int pvId)
                    e.ModelSim = e.ModelSim?.Replace("pvDeviceId", $"pv{pvId}", StringComparison.Ordinal);
                if (inverterIndex is int invIndex)
                    e.ModelSim = e.ModelSim?.Replace("inverterIndex", invIndex.ToString(), StringComparison.Ordinal);
            }

            if (!int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out int deviceId))
                return;

            string pcsToken = Math.Max(0, pcsIndex).ToString();
            foreach (var e in entries)
            {
                if (e.ModelSim != null)
                {
                    if (!isEmu)
                        e.ModelSim = e.ModelSim.Replace("bmsdeviceId", $"bms{deviceId}", StringComparison.Ordinal);

                    e.ModelSim = e.ModelSim.Replace("pvDeviceId", $"pv{deviceId}", StringComparison.Ordinal);

                    if (emuDeviceIdOverride is int emuId)
                        e.ModelSim = e.ModelSim.Replace("emuDeviceId", $"emu{emuId}", StringComparison.Ordinal);
                    else if (isEmu)
                        e.ModelSim = e.ModelSim.Replace("emuDeviceId", $"emu{deviceId}", StringComparison.Ordinal);

                    e.ModelSim = e.ModelSim.Replace("deviceId", deviceId.ToString(), StringComparison.Ordinal);
                    e.ModelSim = e.ModelSim.Replace("pcsIndex", pcsToken, StringComparison.Ordinal);
                }
            }
        }
    }
}
