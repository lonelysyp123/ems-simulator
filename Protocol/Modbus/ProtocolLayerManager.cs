using System;
using System.Collections.Generic;
using System.Linq;
using EssSimulator.Configuration;
using EssSimulator.Protocol.Iec61850;
using log4net;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>单设备启动/重建结果。</summary>
    public sealed class ProtocolDeviceReport
    {
        public string Name { get; set; } = string.Empty;
        public bool Started { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>协议层重建结果。</summary>
    public sealed class ProtocolRebuildResult
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public List<string> PlanErrors { get; set; } = new();
        public List<ProtocolDeviceReport> Devices { get; set; } = new();
    }

    /// <summary>
    /// 协议模拟层编排器：持有全部 Modbus 设备服务，按端口计划（默认配置 + protocol-ports.json 覆盖）
    /// 分配端口/从站号，完成点位地址查重后统一启动，并支持运行期热重建。
    /// 设备实例在重建过程中不替换，SimulatorHost 与各数据管道的既有引用保持有效。
    /// </summary>
    public sealed class ProtocolLayerManager
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProtocolLayerManager));
        private static readonly Lazy<ProtocolLayerManager> _instance = new(() => new ProtocolLayerManager());

        /// <summary>全局共享实例（托管服务与 Web 端点共用）。</summary>
        public static ProtocolLayerManager Instance => _instance.Value;

        private readonly object _gate = new();
        private readonly List<RegisteredDevice> _devices = new();
        private readonly ModbusPortHub _hub;
        private SimulatorConfig? _cfg;
        private ProtocolPortPlan? _currentPlan;
        private string? _overridesError;
        private bool _startupComplete;

        public ProtocolLayerManager() : this(ModbusPortHub.Instance) { }

        public ProtocolLayerManager(ModbusPortHub hub)
        {
            _hub = hub;
        }

        private sealed class RegisteredDevice
        {
            public IProtocolLayerServer Server { get; init; } = null!;
            public ProtocolDeviceType Type { get; init; }
            public string PointMapFile { get; init; } = string.Empty;
            public List<string> Errors { get; } = new();
            public bool Started { get; set; }
        }

        /// <summary>注册设备（不启动）。主设备由 ModbusHostedService 注册后统一 StartAll。</summary>
        public void RegisterDevice(IProtocolLayerServer server, ProtocolDeviceType type, string pointMapFile)
        {
            lock (_gate)
            {
                _devices.Add(new RegisteredDevice { Server = server, Type = type, PointMapFile = pointMapFile });
            }
        }

        /// <summary>
        /// 注册并立即按计划启动（用于 LC 等延迟创建的设备）；
        /// 首次启动（StartAll）完成前仅注册不启动。
        /// </summary>
        public ProtocolDeviceReport RegisterAndStart(IProtocolLayerServer server, ProtocolDeviceType type, string pointMapFile)
        {
            RegisteredDevice reg;
            ProtocolPortPlan? plan;
            lock (_gate)
            {
                reg = new RegisteredDevice { Server = server, Type = type, PointMapFile = pointMapFile };
                _devices.Add(reg);
                plan = _startupComplete ? _currentPlan : null;
                if (plan == null)
                    return ToReport(reg);
            }

            StartRegisteredDevice(reg, plan);
            return ToReport(reg);
        }

        /// <summary>首次启动：加载计划、校验、为已注册设备分配端口并启动。</summary>
        public ProtocolRebuildResult StartAll(SimulatorConfig cfg)
        {
            lock (_gate)
            {
                _cfg = cfg;
                ApplyAllowListOnce();
                var result = ApplyPlanAndStart();
                _startupComplete = true;
                return result;
            }
        }

        /// <summary>热重建：停止全部设备，按最新计划重新分配端口/从站号并启动。</summary>
        public ProtocolRebuildResult Rebuild()
        {
            lock (_gate)
            {
                if (_cfg == null)
                    return new ProtocolRebuildResult { Ok = false, Message = "协议层尚未初始化" };

                Log.Info("协议层开始热重建：停止全部 Modbus 设备");
                foreach (var reg in _devices)
                {
                    try
                    {
                        if (reg.Server.IsOnline)
                            reg.Server.Stop();
                        reg.Started = false;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"停止 {reg.Server.ServerName} 时异常", ex);
                    }
                }
                _hub.ShutdownAll();

                var result = ApplyPlanAndStart();
                Log.Info($"协议层热重建完成：{result.Devices.Count(d => d.Started)}/{result.Devices.Count} 个设备在线");
                return result;
            }
        }

        /// <summary>停止全部设备（进程退出时调用）。</summary>
        public void StopAll()
        {
            lock (_gate)
            {
                foreach (var reg in _devices)
                {
                    try
                    {
                        if (reg.Server.IsOnline)
                            reg.Server.Stop();
                        reg.Started = false;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"停止 {reg.Server.ServerName} 时异常", ex);
                    }
                }
                _hub.ShutdownAll();
            }
        }

        /// <summary>校验给定计划（范围 + 从站号占用 + 点位地址查重），返回全部错误。</summary>
        public List<string> ValidatePlan(ProtocolPortPlan plan)
        {
            var errors = plan.ValidateRanges();
            if (errors.Count > 0)
                return errors;

            var deviceErrors = ValidateAddressOverlaps(plan);
            errors.AddRange(deviceErrors.Values.SelectMany(v => v));
            return errors;
        }

        /// <summary>界面快照：计划条目 + 运行状态。</summary>
        public List<ProtocolDeviceSnapshot> GetSnapshot()
        {
            lock (_gate)
            {
                var snapshots = new List<ProtocolDeviceSnapshot>();
                if (_currentPlan == null)
                    return snapshots;

                foreach (var entry in _currentPlan.Entries)
                {
                    var reg = _devices.FirstOrDefault(d =>
                        string.Equals(d.Server.ServerName, entry.Name, StringComparison.OrdinalIgnoreCase));
                    snapshots.Add(new ProtocolDeviceSnapshot
                    {
                        Name = entry.Name,
                        Type = entry.Type,
                        PointMapFile = entry.PointMapFile,
                        RackCount = entry.RackCount,
                        DefaultPort = entry.DefaultPort,
                        DefaultSlaveId = entry.DefaultSlaveId,
                        Port = entry.Port,
                        SlaveId = entry.SlaveId,
                        IsDefault = entry.IsDefault,
                        Registered = reg != null,
                        Online = reg?.Server.IsOnline ?? false,
                        Errors = reg?.Errors.ToList() ?? new List<string>()
                    });
                }
                return snapshots;
            }
        }

        /// <summary>覆盖文件解析告警（界面展示用）。</summary>
        public string? OverridesError
        {
            get { lock (_gate) { return _overridesError; } }
        }

        /// <summary>仅进程首次启动读取白名单；热重建复用该快照。</summary>
        private void ApplyAllowListOnce()
        {
            var list = ModbusIpAllowList.Load(out var error);
            _hub.SetAllowList(list);
            if (error != null)
                Log.Warn(error);
            if (list.IsUnrestricted)
                Log.Info("Modbus IP 白名单未启用（空名单，不限制来源 IP）");
            else
                Log.Info($"Modbus IP 白名单已启用：{list.Addresses.Count} 条，允许回环={list.AllowLoopback}");
        }

        private ProtocolRebuildResult ApplyPlanAndStart()
        {
            var result = new ProtocolRebuildResult();
            var plan = ProtocolPortPlan.Load(_cfg!, out _overridesError);
            _currentPlan = plan;
            if (_overridesError != null)
            {
                Log.Warn(_overridesError);
                result.PlanErrors.Add(_overridesError);
            }

            var rangeErrors = plan.ValidateRanges();
            if (rangeErrors.Count > 0)
            {
                result.Ok = false;
                result.Message = "端口计划校验失败，未启动任何设备";
                result.PlanErrors.AddRange(rangeErrors);
                foreach (var error in rangeErrors)
                    Log.Error($"协议层计划校验失败：{error}");
                return result;
            }

            var deviceErrors = ValidateAddressOverlaps(plan);
            foreach (var (name, errors) in deviceErrors)
            {
                foreach (var error in errors)
                    Log.Error($"协议层点位冲突：{error}");
            }

            var targets = _devices.ToList();

            foreach (var reg in targets)
            {
                reg.Errors.Clear();
                var entry = plan.Find(reg.Server.ServerName);
                if (entry == null)
                {
                    reg.Errors.Add("端口计划中不存在该设备（拓扑可能已变化）");
                    result.Devices.Add(ToReport(reg));
                    continue;
                }

                if (deviceErrors.TryGetValue(entry.Name, out var conflicts))
                {
                    reg.Errors.AddRange(conflicts);
                    result.Devices.Add(ToReport(reg));
                    continue;
                }
            }

            // 各设备并行启动，避免单设备重试等待（最多 30s）线性累积
            var startables = targets.Where(reg => reg.Errors.Count == 0
                && plan.Find(reg.Server.ServerName) != null).ToList();
            System.Threading.Tasks.Parallel.ForEach(startables, reg =>
            {
                StartRegisteredDevice(reg, plan, prevalidated: true);
            });

            foreach (var reg in targets)
                result.Devices.Add(ToReport(reg));

            result.Ok = result.Devices.All(d => d.Started);
            result.Message = result.Ok
                ? $"全部 {result.Devices.Count} 个设备已启动"
                : $"部分设备未启动（{result.Devices.Count(d => !d.Started)}/{result.Devices.Count}），详见各设备错误";
            return result;
        }

        /// <summary>按给定计划启动单个已注册设备（含 LC 延迟注册路径）；调用方不得持有 _gate。</summary>
        private void StartRegisteredDevice(RegisteredDevice reg, ProtocolPortPlan plan, bool prevalidated = false)
        {
            var entry = plan.Find(reg.Server.ServerName);
            if (entry == null)
            {
                reg.Errors.Add("端口计划中不存在该设备（拓扑可能已变化）");
                reg.Started = false;
                return;
            }

            if (!prevalidated)
            {
                var conflicts = ValidateAddressOverlaps(plan);
                if (conflicts.TryGetValue(entry.Name, out var errors) && errors.Count > 0)
                {
                    reg.Errors.AddRange(errors);
                    reg.Started = false;
                    UpdateListenInfo(reg, entry, started: false);
                    return;
                }
            }

            reg.Server.Reconfigure(entry.Port, entry.SlaveId);

            if (reg.Type == ProtocolDeviceType.Emu
                && !ProtocolBindings.Load().Allows(reg.Server.ServerName, Iec61850Protocols.Modbus)
                && reg.Server is ModbusSimServer emu)
            {
                reg.Started = emu.StartDataPathOnly();
                if (!reg.Started)
                    reg.Errors.Add("内部点影子启动失败，详见日志");
                UpdateListenInfo(reg, entry, started: false);
                if (reg.Started)
                    SimServer.serverListenInfo[reg.Server.ServerName] = "内部寄存器（未监听 Modbus TCP）";
                return;
            }

            reg.Started = reg.Server.Start();
            if (!reg.Started)
                reg.Errors.Add("Modbus 监听启动失败（端口可能被占用），详见日志");
            UpdateListenInfo(reg, entry, reg.Started);
        }

        private void UpdateListenInfo(RegisteredDevice reg, ProtocolPortEntry? entry, bool started)
        {
            if (!started)
            {
                SimServer.serverListenInfo.Remove(reg.Server.ServerName);
                return;
            }

            int port = entry?.Port ?? reg.Server.Port;
            string suffix = reg.Type == ProtocolDeviceType.Lc ? "（LC 转发）" : string.Empty;
            SimServer.serverListenInfo[reg.Server.ServerName] = $"Modbus TCP 端口 {port}{suffix}";
        }

        /// <summary>
        /// 按 (端口, 从站号) 分组做点位地址查重；返回 设备名 -> 冲突错误列表。
        /// BMS rack 从站按 slaveId+r 展开到各自分组。
        /// </summary>
        private static Dictionary<string, List<string>> ValidateAddressOverlaps(ProtocolPortPlan plan)
        {
            var deviceErrors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // 先为每个条目加载一次点表
            var loaded = new Dictionary<string, ModbusPointMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in plan.Entries)
            {
                try
                {
                    loaded[entry.Name] = entry.Type == ProtocolDeviceType.Lc
                        ? ModbusPointMap.ForLocalControl(entry.Name, entry.LcGroupCount)
                        : new ModbusPointMap(entry.PointMapFile, entry.Name, entry.RackCount);
                }
                catch (Exception ex)
                {
                    deviceErrors.GetOrAdd(entry.Name).Add($"{entry.Name}: 点表加载失败 {ex.Message}");
                }
            }

            // 展开为 (端口, 从站号) 分组下的设备点表
            var groups = new Dictionary<(int Port, byte SlaveId), List<AddressOverlapValidator.DevicePointMap>>();
            foreach (var entry in plan.Entries)
            {
                if (!loaded.TryGetValue(entry.Name, out var pointMap))
                    continue;

                AddToGroup(groups, entry.Port, entry.SlaveId,
                    new AddressOverlapValidator.DevicePointMap(entry.Name, entry.SlaveId, pointMap.RawMaps[0]));

                if (entry.RackCount > 0 && pointMap.RawMaps.Count > 1)
                {
                    for (int r = 0; r < entry.RackCount; r++)
                    {
                        byte sid = (byte)(entry.SlaveId + r + 1);
                        AddToGroup(groups, entry.Port, sid,
                            new AddressOverlapValidator.DevicePointMap($"{entry.Name}#rack{r + 1}", sid, pointMap.RawMaps[1]));
                    }
                }
            }

            foreach (var ((port, slaveId), maps) in groups)
            {
                if (maps.Count < 2)
                    continue;

                var conflicts = AddressOverlapValidator.Validate(maps);
                if (conflicts.Count == 0)
                    continue;

                var involved = maps.Select(m => m.DeviceName.Split('#')[0]).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var deviceName in involved)
                {
                    deviceErrors.GetOrAdd(deviceName)
                        .AddRange(conflicts.Select(c => $"端口 {port} {c}"));
                }
            }

            return deviceErrors;
        }

        private static void AddToGroup(
            Dictionary<(int, byte), List<AddressOverlapValidator.DevicePointMap>> groups,
            int port, byte slaveId, AddressOverlapValidator.DevicePointMap map)
        {
            if (!groups.TryGetValue((port, slaveId), out var list))
            {
                list = new List<AddressOverlapValidator.DevicePointMap>();
                groups[(port, slaveId)] = list;
            }
            list.Add(map);
        }

        private static ProtocolDeviceReport ToReport(RegisteredDevice reg) => new()
        {
            Name = reg.Server.ServerName,
            Started = reg.Started,
            Errors = reg.Errors.ToList()
        };
    }

    /// <summary>界面快照条目。</summary>
    public sealed class ProtocolDeviceSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public ProtocolDeviceType Type { get; set; }
        public string PointMapFile { get; set; } = string.Empty;
        public int RackCount { get; set; }
        public int DefaultPort { get; set; }
        public byte DefaultSlaveId { get; set; }
        public int Port { get; set; }
        public byte SlaveId { get; set; }
        public bool IsDefault { get; set; }
        public bool Registered { get; set; }
        public bool Online { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    internal static class ProtocolDictionaryExtensions
    {
        public static List<string> GetOrAdd(this Dictionary<string, List<string>> dict, string key)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<string>();
                dict[key] = list;
            }
            return list;
        }
    }
}
