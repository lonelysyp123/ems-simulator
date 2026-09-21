using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EssSimulator.Configuration;
using EssSimulator.LocalControl;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>协议层设备类型。</summary>
    public enum ProtocolDeviceType
    {
        Bms,
        Emu,
        Em,
        Lc,
        PvLogger,
        PvMeter,
        Sel
    }

    /// <summary>单个协议设备的端口/从站号计划条目。</summary>
    public sealed class ProtocolPortEntry
    {
        public string Name { get; set; } = string.Empty;
        public ProtocolDeviceType Type { get; set; }
        public string PointMapFile { get; set; } = string.Empty;
        /// <summary>BMS 簇级从站数量（占用 slaveId+1..slaveId+rackCount 的从站号）。</summary>
        public int RackCount { get; set; }

        /// <summary>LC 点表按组展开的组数；非 LC 设备为 0。</summary>
        public int LcGroupCount { get; set; }

        /// <summary>配置文件计算出的默认端口。</summary>
        public int DefaultPort { get; set; }
        /// <summary>配置文件计算出的默认从站号。</summary>
        public byte DefaultSlaveId { get; set; } = 1;

        /// <summary>当前生效端口（无覆盖时等于 DefaultPort）。</summary>
        public int Port { get; set; }
        /// <summary>当前生效从站号（无覆盖时等于 DefaultSlaveId）。</summary>
        public byte SlaveId { get; set; } = 1;

        [JsonIgnore]
        public bool IsDefault => Port == DefaultPort && SlaveId == DefaultSlaveId;
    }

    /// <summary>持久化到 protocol-ports.json 的覆盖内容（仅保存非默认条目）。</summary>
    public sealed class ProtocolPortOverrides
    {
        public sealed class Entry
        {
            public string Name { get; set; } = string.Empty;
            public int Port { get; set; }
            public byte SlaveId { get; set; } = 1;
        }

        public List<Entry> Entries { get; set; } = new();
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// 协议层端口计划：按 <see cref="SimulatorConfig"/> 计算默认端口/从站号，
    /// 叠加 protocol-ports.json 中的手动覆盖，并提供范围与从站号冲突校验。
    /// </summary>
    public sealed class ProtocolPortPlan
    {
        public const string OverridesRelativePath = "configs/protocol-ports.json";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public List<ProtocolPortEntry> Entries { get; } = new();

        public ProtocolPortEntry? Find(string name) =>
            Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>按配置计算默认计划（不含覆盖），LC 仅在 EnableLocalControl 且存在 EMU 时纳入。</summary>
        public static ProtocolPortPlan BuildDefault(SimulatorConfig cfg)
        {
            var plan = new ProtocolPortPlan();
            var p = cfg.Protocol;
            var bmsCfg = cfg.GetBmsDeviceConfigs();

            for (int i = 0; i < cfg.UnitCount; i++)
            {
                int clusterCount = i < bmsCfg.Count ? bmsCfg[i].ClusterCount : new BmsDeviceConfig().ClusterCount;
                plan.Entries.Add(MakeEntry($"simBms{i + 1}", ProtocolDeviceType.Bms, "bms_bank.csv",
                    p.BaseBmsModbusPort + i * p.BmsPortStep, rackCount: clusterCount));
            }

            var emuEndpoints = EmuProtocolLayout.Enumerate(cfg);
            for (int i = 0; i < emuEndpoints.Count; i++)
            {
                var ep = emuEndpoints[i];
                plan.Entries.Add(MakeEntry(ep.ServerName, ProtocolDeviceType.Emu, "emu.csv",
                    p.BaseEmuModbusPort + i * p.EmuPortStep));
            }

            for (int i = 0; i < cfg.PvUnitCount; i++)
            {
                plan.Entries.Add(MakeEntry($"simPv{i + 1}", ProtocolDeviceType.PvLogger, "pv_logger.csv",
                    p.BasePvLoggerModbusPort + i * p.PvLoggerPortStep));
                plan.Entries.Add(MakeEntry($"simPvMeter{i + 1}", ProtocolDeviceType.PvMeter, "pv_apm810.csv",
                    p.BasePvMeterModbusPort + i * p.PvMeterPortStep));
            }

            plan.Entries.Add(MakeEntry("simEm", ProtocolDeviceType.Em, "em.csv", p.EmModbusPort));

            if (p.EnableLocalControl && cfg.EffectiveEssUnitCount > 0)
            {
                int lcCount = cfg.EffectiveEssUnitCount;
                for (int i = 0; i < lcCount; i++)
                {
                    plan.Entries.Add(MakeEntry($"simLc{i + 1}", ProtocolDeviceType.Lc, "lc.csv",
                        p.BaseLocalControlModbusPort + i * p.LocalControlPortStep,
                        lcGroupCount: LcLayout.GroupCountForUnit(cfg, i)));
                }
            }

            if (p.EnableSel)
                plan.Entries.Add(MakeEntry("simSel", ProtocolDeviceType.Sel, "sel.csv", p.SelModbusPort));

            return plan;
        }

        private static ProtocolPortEntry MakeEntry(
            string name, ProtocolDeviceType type, string pointMapFile, int defaultPort,
            int rackCount = 0, int lcGroupCount = 0)
        {
            return new ProtocolPortEntry
            {
                Name = name,
                Type = type,
                PointMapFile = pointMapFile,
                RackCount = rackCount,
                LcGroupCount = lcGroupCount,
                DefaultPort = defaultPort,
                DefaultSlaveId = 1,
                Port = defaultPort,
                SlaveId = 1
            };
        }

        /// <summary>加载计划：默认值 + protocol-ports.json 覆盖（覆盖文件缺失/损坏时退回默认并记录原因）。</summary>
        public static ProtocolPortPlan Load(SimulatorConfig cfg, out string? overridesError)
        {
            var plan = BuildDefault(cfg);
            overridesError = null;

            var path = ResolveOverridesPath();
            if (path == null || !File.Exists(path))
                return plan;

            try
            {
                var overrides = JsonSerializer.Deserialize<ProtocolPortOverrides>(File.ReadAllText(path), JsonOpts);
                if (overrides?.Entries == null)
                    return plan;

                foreach (var o in overrides.Entries)
                {
                    var entry = plan.Find(o.Name);
                    if (entry == null)
                        continue; // 拓扑变化后旧条目自然失效
                    entry.Port = o.Port;
                    entry.SlaveId = o.SlaveId;
                }
            }
            catch (Exception ex)
            {
                overridesError = $"protocol-ports.json 读取失败，已使用默认端口：{ex.Message}";
            }

            return plan;
        }

        /// <summary>保存覆盖文件（仅非默认条目）；全部为默认时删除覆盖文件。</summary>
        public static void SaveOverrides(IEnumerable<ProtocolPortEntry> entries)
        {
            var nonDefault = entries.Where(e => !e.IsDefault).ToList();
            var path = ResolveOrCreateOverridesPath();

            if (nonDefault.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var overrides = new ProtocolPortOverrides
            {
                Entries = nonDefault.Select(e => new ProtocolPortOverrides.Entry
                {
                    Name = e.Name,
                    Port = e.Port,
                    SlaveId = e.SlaveId
                }).ToList(),
                UpdatedAtUtc = DateTime.UtcNow
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(overrides, JsonOpts));
        }

        /// <summary>清除覆盖文件，恢复默认。</summary>
        public static void ClearOverrides()
        {
            var path = ResolveOverridesPath();
            if (path != null && File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// 范围校验（不含点位地址查重，点位查重由 ProtocolLayerManager 完成）。
        /// 同端口同从站号的多设备属于合并点表场景，合法性由地址查重判定，不在此拒绝。
        /// </summary>
        public List<string> ValidateRanges()
        {
            var errors = new List<string>();
            foreach (var e in Entries)
            {
                if (e.Port is < 1 or > 65535)
                    errors.Add($"{e.Name}: 端口 {e.Port} 超出合法范围 1-65535");
                if (e.SlaveId is < 1 or > 247)
                    errors.Add($"{e.Name}: 从站号 {e.SlaveId} 超出合法范围 1-247");
            }

            return errors;
        }

        private static string? ResolveOverridesPath()
        {
            foreach (var root in DeviceModelRegistry.CandidateRoots())
            {
                var path = Path.Combine(root, OverridesRelativePath);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static string ResolveOrCreateOverridesPath()
        {
            var existing = ResolveOverridesPath();
            if (existing != null)
                return existing;

            var cwd = Directory.GetCurrentDirectory();
            var root = string.IsNullOrWhiteSpace(cwd) ? AppContext.BaseDirectory : cwd;
            return Path.Combine(root, OverridesRelativePath);
        }
    }
}
