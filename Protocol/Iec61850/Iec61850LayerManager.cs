using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.LocalControl;
using EssSimulator.Web.Topology;
using log4net;

namespace EssSimulator.Protocol.Iec61850
{
    public sealed class Iec61850DeviceSnapshot
    {
        public string ServerName { get; set; } = string.Empty;
        public string IedName { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool Online { get; set; }
        public bool Enabled { get; set; }
        public int AssociatedClients { get; set; }
        public bool GooseSubscribing { get; set; }
        public int? GooseSubscribeAppId { get; set; }
        public string? GooseInterface { get; set; }
        public string? GooseSubscribeSkip { get; set; }
        public string? GooseSubscribeGoCbRef { get; set; }
        public uint? LastGooseStNum { get; set; }
        public DateTime? LastGooseUtc { get; set; }
        public IReadOnlyList<string> Protocols { get; set; } = Array.Empty<string>();
        public IReadOnlyList<Iec61850GoosePointDto> GoosePoints { get; set; } = Array.Empty<Iec61850GoosePointDto>();
    }

    public sealed class Iec61850GoosePointDto
    {
        public int Index { get; set; }
        public string ParamName { get; set; } = string.Empty;
        public string ObjectRef { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>IEC 61850 IED 编排器：一台 PCS 一个 IED，与 ProtocolLayerManager 并列。</summary>
    public sealed class Iec61850LayerManager
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Iec61850LayerManager));
        private static readonly Lazy<Iec61850LayerManager> InstanceHolder = new(() => new Iec61850LayerManager());

        public static Iec61850LayerManager Instance => InstanceHolder.Value;

        private readonly object _gate = new();
        private readonly List<Iec61850IedServer> _servers = new();
        private Iec61850Mapping? _mapping;
        private ProtocolBindings _bindings = new();
        private Iec61850GooseReceiverHost? _gooseHost;
        private string? _gooseIface;
        private bool _gooseSubscribe;
        private int _gooseSubscribeAppIdBase = Iec61850PcsModel.DefaultSubscribeAppIdBase;
        private string? _gooseSubscribeGoCbRef;
        private string? _gooseSubscribeSkip;

        public IReadOnlyList<Iec61850IedServer> Servers
        {
            get { lock (_gate) { return _servers.ToList(); } }
        }

        public ProtocolBindings Bindings
        {
            get { lock (_gate) { return _bindings; } }
        }

        public string? BindingsError
        {
            get { lock (_gate) { return _bindings.LoadError; } }
        }

        public static bool ShouldStartMms(SimulatorConfig? cfg) =>
            cfg != null && !Mw300EssStation.IsThis(cfg.StationProfileId);

        public void StartAll(SimulatorConfig cfg)
        {
            lock (_gate)
            {
                StopAllUnlocked();
                if (!ShouldStartMms(cfg))
                {
                    Log.Info("[IEC61850] 300MW 测试工程不启动 MMS");
                    return;
                }

                _bindings = ProtocolBindings.Load();
                if (_bindings.LoadError != null)
                    Log.Warn(_bindings.LoadError);

                try
                {
                    _mapping = Iec61850Mapping.Load();
                }
                catch (Exception ex)
                {
                    Log.Error("加载 IEC 61850 mapping.csv 失败，跳过 IED 启动", ex);
                    return;
                }

                if (!Iec61850Native.TryEnsureNativeLibrary(out var nativeDetail))
                {
                    Log.Error($"[IEC61850] 原生库未加载，跳过 IED 启动。{nativeDetail}");
                    return;
                }

                string icdPath;
                try { icdPath = Iec61850Mapping.ResolveIcdPath(); }
                catch { icdPath = string.Empty; }

                var store = SimulatorHost.Instance;
                var endpoints = EmuProtocolLayout.Enumerate(cfg);
                int basePort = cfg.Protocol.BaseEmuIec61850Port;
                int step = Math.Max(1, cfg.Protocol.EmuIec61850PortStep);

                for (int i = 0; i < endpoints.Count; i++)
                {
                    var ep = endpoints[i];
                    if (!_bindings.Allows(ep.ServerName, Iec61850Protocols.Iec61850))
                        continue;

                    var emu = store.Get<ModbusSimServer>(ep.ServerName);
                    if (emu == null)
                    {
                        Log.Warn($"[IEC61850] 找不到 {ep.ServerName}，跳过 IED");
                        continue;
                    }

                    int port = _bindings.PortOverride(ep.ServerName)
                               ?? ProtocolBindings.DefaultIec61850Port(ep.SimIndex1Based, basePort, step);

                    try
                    {
                        var ied = new Iec61850IedServer(ep.ServerName, port, _mapping)
                        {
                            IcdPath = icdPath,
                            GooseInterfaceId = cfg.Protocol.EmuIec61850GooseInterface
                        };
                        ied.Attach(emu.PointStore, (name, value) => emu.SetDataObjectByMesurePointName(name, value));
                        if (!ied.Start())
                            continue;

                        _servers.Add(ied);
                        SimServer.serverListenInfo[$"{ep.ServerName}.iec61850"] =
                            $"IEC 61850 MMS {ied.IedName} 端口 {ied.Port}";
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[IEC61850] {ep.ServerName} 创建 IED 失败", ex);
                    }
                }

                Log.Info($"[IEC61850] 已启动 {_servers.Count} 个 PCS IED");
                StartGooseSubscribeUnlocked(cfg);
            }
        }

        public void StopAll()
        {
            lock (_gate)
                StopAllUnlocked();
        }

        public List<Iec61850DeviceSnapshot> GetSnapshot()
        {
            lock (_gate)
            {
                return _servers.Select(s =>
                {
                    int n = Iec61850Mapping.ParseSimIndex(s.ServerName);
                    string goCb = string.IsNullOrWhiteSpace(_gooseSubscribeGoCbRef)
                        ? Iec61850PcsModel.IngressGoCbRefMms(n)
                        : _gooseSubscribeGoCbRef!;
                    return new Iec61850DeviceSnapshot
                    {
                        ServerName = s.ServerName,
                        IedName = s.IedName,
                        Port = s.Port,
                        Online = s.IsOnline,
                        Enabled = true,
                        AssociatedClients = s.AssociatedClients,
                        GooseSubscribing = s.GooseSubscribing,
                        GooseSubscribeAppId = s.GooseSubscribeAppId,
                        GooseInterface = _gooseIface,
                        GooseSubscribeSkip = s.GooseSubscribing ? null : _gooseSubscribeSkip,
                        GooseSubscribeGoCbRef = goCb,
                        LastGooseStNum = s.LastGooseStNum,
                        LastGooseUtc = s.LastGooseUtc,
                        Protocols = _bindings.ProtocolsFor(s.ServerName),
                        GoosePoints = _mapping == null
                            ? Array.Empty<Iec61850GoosePointDto>()
                            : _mapping.GooseEntries
                                .Select((e, i) => new Iec61850GoosePointDto
                                {
                                    Index = i,
                                    ParamName = e.ParamName,
                                    ObjectRef = e.ObjectRef,
                                    Description = e.Description
                                }).ToList()
                    };
                }).ToList();
            }
        }

        public bool TrySetOnline(string serverName, bool online)
        {
            lock (_gate)
            {
                var ied = _servers.FirstOrDefault(s =>
                    string.Equals(s.ServerName, serverName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.IedName, serverName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals($"{s.ServerName}.iec61850", serverName, StringComparison.OrdinalIgnoreCase));
                if (ied == null)
                    return false;

                if (online)
                {
                    if (!ied.IsOnline && !ied.Start())
                        return false;
                    TryBindSubscribeUnlocked(ied);
                    if (_gooseHost != null && _gooseHost.TryStart(out _))
                    {
                        foreach (var s in _servers)
                            s.MarkGooseSubscribing(true);
                    }
                    return true;
                }

                ied.UnbindGooseSubscribe();
                if (ied.IsOnline)
                    ied.Stop();
                SimServer.serverListenInfo.TryRemove($"{ied.ServerName}.iec61850", out _);
                return true;
            }
        }

        public void ReloadBindingsAndRestart(SimulatorConfig cfg)
        {
            StartAll(cfg);
        }

        private void StopAllUnlocked()
        {
            foreach (var ied in _servers)
            {
                try
                {
                    ied.UnbindGooseSubscribe();
                    ied.Stop();
                    SimServer.serverListenInfo.TryRemove($"{ied.ServerName}.iec61850", out _);
                }
                catch (Exception ex)
                {
                    Log.Warn($"停止 {ied.ServerName} IED 时异常", ex);
                }
            }

            _servers.Clear();
            try { _gooseHost?.Dispose(); }
            catch (Exception ex) { Log.Warn("关闭 GOOSE 订阅时异常", ex); }
            _gooseHost = null;
        }

        private void StartGooseSubscribeUnlocked(SimulatorConfig cfg)
        {
            _gooseSubscribeSkip = null;
            _gooseSubscribe = cfg.Protocol.EmuIec61850GooseSubscribe;
            _gooseSubscribeAppIdBase = cfg.Protocol.EmuIec61850GooseSubscribeAppIdBase > 0
                ? cfg.Protocol.EmuIec61850GooseSubscribeAppIdBase
                : Iec61850PcsModel.DefaultSubscribeAppIdBase;
            _gooseSubscribeGoCbRef = cfg.Protocol.EmuIec61850GooseSubscribeGoCbRef;
            _gooseIface = Iec61850IedServer.ResolveGooseInterface(cfg.Protocol.EmuIec61850GooseInterface);
            if (!_gooseSubscribe)
            {
                _gooseSubscribeSkip = "配置已关闭 EmuIec61850GooseSubscribe";
                return;
            }
            if (string.IsNullOrWhiteSpace(_gooseIface) || _servers.Count == 0)
            {
                _gooseSubscribeSkip = _servers.Count == 0 ? "无 PCS IED" : "未指定 GOOSE 网卡";
                return;
            }
            if (!Iec61850Native.HasGooseSubscriberDestroy())
            {
                _gooseSubscribeSkip = "原生库无 GooseSubscriber_destroy";
                Log.Warn("[IEC61850] GOOSE 订阅未启用：原生库无 GooseSubscriber_destroy，避免终结器崩溃");
                return;
            }
            if (!Iec61850GooseEthernet.CanUseRawEthernet(_gooseIface, out var skip))
            {
                _gooseSubscribeSkip = skip;
                Log.Warn($"[IEC61850] GOOSE 订阅未启用 iface={_gooseIface}：{skip}");
                return;
            }

            _gooseHost = new Iec61850GooseReceiverHost();
            foreach (var ied in _servers)
                TryBindSubscribeUnlocked(ied);

            if (_gooseHost.TryStart(out var err))
            {
                foreach (var ied in _servers)
                    ied.MarkGooseSubscribing(true);
                _gooseSubscribeSkip = null;
                Log.Info($"[IEC61850] GOOSE 订阅已启用 iface={_gooseIface} AppID 基数 0x{_gooseSubscribeAppIdBase:X}");
                foreach (var ied in _servers)
                {
                    Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                    {
                        Direction = "system",
                        Protocol = "system",
                        ServerName = ied.ServerName,
                        IedName = ied.IedName,
                        AppId = ied.GooseSubscribeAppId,
                        Result = "system",
                        Summary = $"GOOSE 订阅启用 iface={_gooseIface} AppID=0x{(ied.GooseSubscribeAppId ?? 0):X4}"
                    });
                }
            }
            else if (!string.IsNullOrEmpty(err))
            {
                _gooseSubscribeSkip = err;
                Log.Warn($"[IEC61850] GOOSE 订阅未启用 iface={_gooseIface}：{err}");
                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "system",
                    Protocol = "system",
                    ServerName = "",
                    IedName = "",
                    Result = "error",
                    Summary = $"GOOSE 订阅未启用 iface={_gooseIface}：{err}"
                });
            }
        }

        private void TryBindSubscribeUnlocked(Iec61850IedServer ied)
        {
            if (!_gooseSubscribe || _gooseHost == null || string.IsNullOrWhiteSpace(_gooseIface))
                return;
            int n = Iec61850Mapping.ParseSimIndex(ied.ServerName);
            ushort appId = Iec61850PcsModel.SubscribeAppId(n, _gooseSubscribeAppIdBase);
            string? goCbRef = string.IsNullOrWhiteSpace(_gooseSubscribeGoCbRef)
                ? Iec61850PcsModel.IngressGoCbRefMms(n)
                : _gooseSubscribeGoCbRef;
            if (!ied.TryBindGooseSubscribe(_gooseHost, _gooseIface, appId, goCbRef, out var err)
                && !string.IsNullOrEmpty(err))
            {
                Log.Warn($"[IEC61850] {ied.ServerName} GOOSE 订阅失败：{err}");
            }
        }
    }
}
