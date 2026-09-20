using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using EssSimulator.DataExchange.Adapters;
using IEC61850.Common;
using IEC61850.GOOSE.Subscriber;
using IEC61850.Model;
using IEC61850.Server;
using log4net;

namespace EssSimulator.Protocol.Iec61850
{
    public sealed class Iec61850ReportEventArgs : EventArgs
    {
        public required string RptId { get; init; }
        public required string Reason { get; init; }
        public required int SeqNum { get; init; }
        public required IReadOnlyList<Iec61850ReportEntry> Entries { get; init; }
    }

    public sealed class Iec61850ReportEntry
    {
        public string Ref { get; set; } = string.Empty;
        public object? Value { get; set; }
    }

    /// <summary>
    /// 一台 PCS 一个 libiec61850 MMS IED：动态模型、Get/Set、Direct-operate、URCB；yk/yt 只订阅入向 GOOSE 遥控，不发布。
    /// </summary>
    public sealed class Iec61850IedServer : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Iec61850IedServer));

        private readonly Iec61850Mapping _mapping;
        private readonly Iec61850PcsModel _model;
        private readonly Iec61850GooseIngress _ingress;
        private readonly Dictionary<IntPtr, Iec61850MapEntry> _attrToMap = new();
        private IedServer? _server;
        private IProtocolPointStore? _store;
        private Action<string, object>? _writeControl;
        private Func<string, object?>? _readPoint;
        private GooseSubscriber? _gooseSubscriber;
        private GooseListener? _gooseListener;
        private Iec61850GooseReceiverHost? _gooseHost;
        private IedServer.ConnectionIndicationHandler? _connectionHandler;
        private IedServer.ReadAccessHandler? _readAccessHandler;
        private IedServer.DirectoryAccessHandler? _directoryAccessHandler;
        private IedServer.DataSetAccessHandler? _dataSetAccessHandler;
        private IedServer.ControlBlockAccessHandler? _controlBlockAccessHandler;
        private RCBEventHandler? _rcbEventHandler;
        private readonly ConcurrentDictionary<string, long> _mmsReadThrottleTicks = new(StringComparer.Ordinal);
        private const int MmsReadThrottleMs = 300;
        private int _seqNum;
        private int _inNativeCallback;
        private bool _disposed;

        public Iec61850IedServer(string serverName, int port, Iec61850Mapping mapping)
        {
            Iec61850Native.EnsureLoaded();
            ServerName = serverName;
            Port = port;
            IedName = Iec61850Mapping.IedNameFor(serverName);
            _mapping = mapping;
            _model = Iec61850PcsModel.Build(IedName, mapping);
            _ingress = new Iec61850GooseIngress(_mapping.GooseEntries, IedName);
            foreach (var pair in _model.LeafByParam)
            {
                if (_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                    _attrToMap[pair.Value.self] = entry;
            }

            foreach (var pair in _model.SetpointByParam)
            {
                if (_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                    _attrToMap[pair.Value.self] = entry;
            }
        }

        public string ServerName { get; }
        public string IedName { get; }
        public int Port { get; private set; }
        public bool IsOnline { get; private set; }
        public string? IcdPath { get; init; }
        /// <summary>空=按操作系统选网卡供入向订阅；<c>none</c> 不收二层 GOOSE。</summary>
        public string? GooseInterfaceId { get; init; }
        public bool GooseSubscribing { get; private set; }
        public ushort? GooseSubscribeAppId { get; private set; }
        public uint? LastGooseStNum => _ingress.LastStNum;
        public DateTime? LastGooseUtc => _ingress.LastAcceptedUtc;

        public int AssociatedClients => _server?.GetNumberOfOpenConnections() ?? 0;
        internal int SpcHandlerCount => _model.ControlDoByParam.Count;
        internal int GooseEntryCount => _model.GooseEntryCount;

        public event EventHandler<Iec61850ReportEventArgs>? ReportGenerated;

        public void Attach(IProtocolPointStore store, Action<string, object> writeControl)
        {
            if (_store != null)
                _store.PointsChanged -= OnPointsChanged;
            _store = store;
            _writeControl = writeControl;
            _readPoint = store.GetCachedValue;
            store.PointsChanged += OnPointsChanged;
            PushAllFromShadow();
        }

        public void Attach(Func<string, object?> readPoint, Action<string, object> writeControl)
        {
            _readPoint = readPoint;
            _writeControl = writeControl;
            PushAllFromShadow();
        }

        public bool Start()
        {
            if (IsOnline && _server is { } running && running.IsRunning())
                return true;

            try
            {
                if (Port <= 0)
                    Port = PickFreePort();

                if (_server == null)
                {
                    var config = new IedServerConfig
                    {
                        Edition = Iec61850Edition.EDITION_2,
                        FileServiceEnabled = false,
                        LogServiceEnabled = false,
                        ReportBufferSizeForURCBs = 65535,
                        MaxMmsConnections = 8,
                        UseIntegratedGoosePublisher = false
                    };
                    _server = new IedServer(_model.Model, config);
                    _server.SetServerIdentity("TrinaStorage", "EssSimulator-PCS", "1.0");
                    _server.SetWriteAccessPolicy(FunctionalConstraint.SP, AccessPolicy.ACCESS_POLICY_ALLOW);
                    RegisterHandlers(_server);
                }

                PushAllFromShadow();
                _server.Start(Port);
                IsOnline = _server.IsRunning();
                if (IsOnline)
                    Log.Info($"[IEC61850] {ServerName} IED {IedName} 监听 MMS 端口 {Port}");
                else
                    Log.Error($"[IEC61850] {ServerName} IedServer.Start({Port}) 后未处于运行状态");
                return IsOnline;
            }
            catch (Exception ex)
            {
                Log.Error($"[IEC61850] {ServerName} 启动失败", ex);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            UnbindGooseSubscribe();
            IsOnline = false;
            try { _server?.Stop(); }
            catch (Exception ex) { Log.Warn($"[IEC61850] {ServerName} Stop 异常", ex); }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_store != null)
                _store.PointsChanged -= OnPointsChanged;
            Stop();
            try { _server?.Destroy(); }
            catch { /* native already gone */ }
            _server = null;
            _model.Dispose();
        }

        public IReadOnlyList<string> GetServerDirectory() => _mapping.LogicalDevices();

        public IReadOnlyList<string> GetLogicalDeviceDirectory(string? ld)
        {
            if (!string.IsNullOrWhiteSpace(ld)
                && !string.Equals(ld, Iec61850Mapping.LogicalDeviceName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ld, IedName + Iec61850Mapping.LogicalDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            return _mapping.LogicalNodes();
        }

        public IReadOnlyList<string> GetLogicalNodeDirectory(string ln) =>
            _mapping.DataObjects(StripIed(ln));

        public bool TryGetDataValues(IReadOnlyList<string> refs, out Dictionary<string, object?> values, out string error)
        {
            values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;
            foreach (var r in refs)
            {
                if (!_mapping.TryResolve(r, IedName, out var entry))
                {
                    error = $"未知对象引用: {r}";
                    return false;
                }

                values[Iec61850Mapping.AbsoluteRef(IedName, entry.ObjectRef)] = ReadEngineering(entry);
            }

            return true;
        }

        public bool TrySetDataValues(IReadOnlyDictionary<string, object?> values, out string error)
        {
            error = string.Empty;
            foreach (var pair in values)
            {
                if (!_mapping.TryResolve(pair.Key, IedName, out var entry))
                {
                    error = $"未知对象引用: {pair.Key}";
                    return false;
                }

                if (!entry.IsControllable)
                {
                    error = $"{entry.ObjectRef} 不可写（ctlModel=0）";
                    return false;
                }

                if (!TryWrite(entry, pair.Value, out error))
                    return false;
            }

            return true;
        }

        public bool TryOperate(string objectRef, object? ctlVal, out string error)
        {
            error = string.Empty;
            if (!_mapping.TryResolve(objectRef, IedName, out var entry))
            {
                error = $"未知对象引用: {objectRef}";
                return false;
            }

            if (!entry.IsControllable)
            {
                error = $"{entry.ObjectRef} 不支持 Operate（ctlModel=0）";
                return false;
            }

            return TryWrite(entry, ctlVal, out error);
        }

        public bool TryEnableUrcb(string? name, bool enabled, int? integrityPeriodMs, out string error)
        {
            error = string.Empty;
            if (!string.IsNullOrWhiteSpace(name)
                && !name.Contains("URCB", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("LLN0", StringComparison.OrdinalIgnoreCase)
                && !name.Contains(".RP.", StringComparison.OrdinalIgnoreCase))
            {
                error = $"未知 URCB: {name}";
                return false;
            }

            return true;
        }

        internal bool TryBindGooseSubscribe(
            Iec61850GooseReceiverHost host,
            string iface,
            ushort appId,
            string? goCbRefFilter,
            out string error)
        {
            error = string.Empty;
            UnbindGooseSubscribe();
            if (_model.GooseEntryCount == 0)
            {
                error = "无 GOOSE 点";
                return false;
            }

            try
            {
                // 绝不可传 null（macOS 上 GooseSubscriber_create(null) 会 SIGSEGV）。
                // 空串 + setObserver = 只按 AppID 匹配（libiec61850 goose_observer 用法）。
                bool matchAnyGoCb = string.IsNullOrWhiteSpace(goCbRefFilter);
                string goCbRef = matchAnyGoCb ? "" : goCbRefFilter!.Trim();
                var subscriber = new GooseSubscriber(goCbRef);
                GC.SuppressFinalize(subscriber);
                if (matchAnyGoCb && !Iec61850Native.TrySetGooseSubscriberObserver(subscriber))
                {
                    error = "无法启用 GooseSubscriber observer";
                    subscriber.Dispose();
                    return false;
                }
                subscriber.SetAppId(appId);
                _gooseListener = OnGooseReceived;
                subscriber.SetListener(_gooseListener, this);
                if (!host.TryAdd(iface, subscriber, out error))
                {
                    subscriber.Dispose();
                    _gooseListener = null;
                    return false;
                }

                _gooseSubscriber = subscriber;
                _gooseHost = host;
                GooseSubscribeAppId = appId;
                GooseSubscribing = host.IsRunning;
                Log.Info($"[IEC61850] {ServerName} GOOSE 订阅 AppID=0x{appId:X4} iface={iface}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warn($"[IEC61850] {ServerName} GOOSE 订阅未挂上", ex);
                UnbindGooseSubscribe();
                return false;
            }
        }

        internal void MarkGooseSubscribing(bool running) => GooseSubscribing = running && _gooseSubscriber != null;

        internal void UnbindGooseSubscribe()
        {
            GooseSubscribing = false;
            GooseSubscribeAppId = null;
            var sub = _gooseSubscriber;
            var host = _gooseHost;
            _gooseSubscriber = null;
            _gooseHost = null;
            _gooseListener = null;
            host?.Remove(sub);
        }

        internal bool TryApplyGoose(
            uint stNum,
            bool isTest,
            string? goCbRef,
            IReadOnlyList<object?> values,
            out string skipReason)
        {
            if (!_ingress.TryAccept(stNum, isTest, goCbRef, values, out var writes, out skipReason))
                return false;

            foreach (var pair in writes)
            {
                if (!_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                    continue;
                TryWrite(entry, pair.Value, out _);
            }

            return true;
        }

        private void OnGooseReceived(GooseSubscriber subscriber, object parameter)
        {
            Interlocked.Increment(ref _inNativeCallback);
            try
            {
                var values = Iec61850GooseIngress.FromDataset(subscriber.GetDataSetValues());
                uint stNum = subscriber.GetStNum();
                uint sqNum = subscriber.GetSqNum();
                bool isTest = subscriber.IsTest();
                string? goCbRef = subscriber.GetGoCbRef();
                string? goId = subscriber.GetGoId();
                string? datSet = subscriber.GetDataSet();
                uint confRev = subscriber.GetConfRev();
                uint ttl = subscriber.GetTimeAllowedToLive();
                bool ndsCom = subscriber.NeedsCommission();
                string? gooseTsUtc = null;
                try
                {
                    gooseTsUtc = subscriber.GetTimestampsDateTimeOffset().UtcDateTime.ToString("o");
                }
                catch { /* 时间戳异常时仍记其它头字段 */ }

                bool ok = _ingress.TryAccept(stNum, isTest, goCbRef, values, out var writes, out var reason);
                if (ok)
                {
                    foreach (var pair in writes)
                    {
                        if (!_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                            continue;
                        TryWrite(entry, pair.Value, out _);
                    }
                }

                string result = ok ? "applied" : (string.IsNullOrEmpty(reason) ? "error" : $"skip:{reason}");
                string writeSummary = writes.Count > 0
                    ? string.Join(" ", writes.Select(kv => $"{kv.Key}={FormatTrafficValue(kv.Value)}"))
                    : "";
                string summary = ok
                    ? $"GOOSE stNum={stNum} sqNum={sqNum} {writeSummary}".Trim()
                    : $"GOOSE stNum={stNum} sqNum={sqNum} {reason}";

                var goose = _mapping.GooseEntries;
                var valueMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var allData = new List<Iec61850GooseDataEntry>(values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    string param = i < goose.Count ? goose[i].ParamName : $"[{i}]";
                    string desc = i < goose.Count ? goose[i].Description : "";
                    object? raw = values[i];
                    valueMap[param] = raw;
                    allData.Add(new Iec61850GooseDataEntry
                    {
                        Index = i,
                        ParamName = param,
                        Type = FormatTrafficType(raw),
                        Value = raw is bool or float or double or int or long or string
                            ? raw
                            : FormatTrafficValue(raw ?? ""),
                        Description = string.IsNullOrEmpty(desc) ? null : desc
                    });
                }

                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "ingress",
                    Protocol = "goose",
                    ServerName = ServerName,
                    IedName = IedName,
                    AppId = GooseSubscribeAppId,
                    GoCbRef = goCbRef,
                    GoId = goId,
                    DatSet = datSet,
                    StNum = stNum,
                    SqNum = sqNum,
                    IsTest = isTest,
                    NeedsCommission = ndsCom,
                    ConfRev = confRev,
                    TimeAllowedToLive = ttl,
                    GooseTimestampUtc = gooseTsUtc,
                    NumDatSetEntries = values.Count,
                    Result = result,
                    Summary = summary,
                    Writes = writes.Count > 0
                        ? writes.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase)
                        : null,
                    Values = valueMap.Count > 0 ? valueMap : null,
                    AllData = allData
                });

                if (!ok && !string.IsNullOrEmpty(reason) && reason is not "stNum")
                    Log.Info($"[IEC61850] {ServerName} 忽略入向 GOOSE: {reason} stNum={stNum} goCb={goCbRef}");
                else if (ok)
                    Log.Info($"[IEC61850] {ServerName} 入向 GOOSE 已应用 stNum={stNum}");
            }
            catch (Exception ex)
            {
                Log.Warn($"[IEC61850] {ServerName} 入向 GOOSE 处理失败", ex);
                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "ingress",
                    Protocol = "goose",
                    ServerName = ServerName,
                    IedName = IedName,
                    AppId = GooseSubscribeAppId,
                    Result = "error",
                    Summary = $"GOOSE 处理异常: {ex.Message}"
                });
            }
            finally
            {
                Interlocked.Decrement(ref _inNativeCallback);
            }
        }

        private static string FormatTrafficType(object? value) =>
            value switch
            {
                null => "null",
                bool => "boolean",
                float => "floating-point",
                double => "floating-point",
                int or long or short or byte or uint or ulong => "integer",
                string => "visible-string",
                _ => value.GetType().Name
            };

        private static string FormatTrafficValue(object value) =>
            value switch
            {
                bool b => b ? "true" : "false",
                float f => f.ToString("G", CultureInfo.InvariantCulture),
                double d => d.ToString("G", CultureInfo.InvariantCulture),
                IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture) ?? "",
                _ => value?.ToString() ?? ""
            };

        internal static string? ResolveGooseInterface(string? configured)
        {
            if (string.Equals(configured, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(configured))
                return configured.Trim();

            if (OperatingSystem.IsWindows())
                return "0";
            if (OperatingSystem.IsMacOS())
                return "en0";
            return "eth0";
        }

        private void RegisterHandlers(IedServer server)
        {
            _connectionHandler = OnClientConnection;
            server.SetConnectionIndicationHandler(_connectionHandler, this);

            _readAccessHandler = OnReadAccess;
            server.SetReadAccessHandler(_readAccessHandler, this);

            _directoryAccessHandler = OnDirectoryAccess;
            server.SetDirectoryAccessHandler(_directoryAccessHandler, this);

            _dataSetAccessHandler = OnDataSetAccess;
            server.SetDataSetAccessHandler(_dataSetAccessHandler, this);

            _controlBlockAccessHandler = OnControlBlockAccess;
            server.SetControlBlockAccessHandler(_controlBlockAccessHandler, this);

            _rcbEventHandler = OnRcbEvent;
            server.SetRCBEventHandler(_rcbEventHandler, this);

            foreach (var pair in _model.ControlDoByParam)
            {
                if (!_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                    continue;
                if (!Iec61850PcsModel.TryParseRef(entry.ObjectRef, out var lnName, out var doName, out _))
                    continue;
                var controlDo = _model.Model.GetModelNodeByShortObjectReference(
                                    Iec61850Mapping.LogicalDeviceName + "/" + lnName + "." + doName) as DataObject
                                ?? pair.Value;
                server.SetControlHandler(controlDo, OnControl, entry);
            }

            foreach (var pair in _model.SetpointByParam)
            {
                if (!_mapping.ByParam.TryGetValue(pair.Key, out var entry))
                    continue;
                server.HandleWriteAccessForComplexAttribute(pair.Value, OnWriteAccess, entry);
            }
        }

        private MmsDataAccessError OnReadAccess(
            LogicalDevice ld,
            LogicalNode ln,
            DataObject dataObject,
            FunctionalConstraint fc,
            ClientConnection connection,
            object parameter)
        {
            string peer = SafePeer(connection);
            string objectRef = FormatModelRef(dataObject) ?? FormatModelRef(ln) ?? FormatModelRef(ld) ?? "";
            string key = $"{peer}|{fc}|{objectRef}";
            if (ShouldLogMmsRead(key))
            {
                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "ingress",
                    Protocol = "mms",
                    Service = "Get",
                    ServerName = ServerName,
                    IedName = IedName,
                    ClientPeer = peer,
                    ObjectRef = string.IsNullOrEmpty(objectRef) ? null : objectRef,
                    OrCat = fc.ToString(),
                    Result = "system",
                    Summary = $"MMS Get fc={fc} {objectRef} peer={peer}"
                });
            }

            return MmsDataAccessError.SUCCESS;
        }

        private bool OnDirectoryAccess(
            object parameter,
            ClientConnection connection,
            IedServer.IedServer_DirectoryCategory category,
            LogicalDevice ld)
        {
            string peer = SafePeer(connection);
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "ingress",
                Protocol = "mms",
                Service = "GetDirectory",
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                ObjectRef = FormatModelRef(ld),
                OrCat = category.ToString(),
                Result = "system",
                Summary = $"MMS GetDirectory {category} {FormatModelRef(ld)} peer={peer}"
            });
            return true;
        }

        private bool OnDataSetAccess(
            object parameter,
            ClientConnection connection,
            DataSetOperation operation,
            string datasetRef)
        {
            string peer = SafePeer(connection);
            string service = operation switch
            {
                DataSetOperation.DATASET_READ => "GetDataSet",
                DataSetOperation.DATASET_WRITE => "SetDataSet",
                DataSetOperation.DATASET_CREATE => "CreateDataSet",
                DataSetOperation.DATASET_DELETE => "DeleteDataSet",
                DataSetOperation.DATASET_GET_DIRECTORY => "GetDataSetDirectory",
                _ => operation.ToString()
            };
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "ingress",
                Protocol = "mms",
                Service = service,
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                ObjectRef = datasetRef,
                DatSet = datasetRef,
                Result = "system",
                Summary = $"MMS {service} {datasetRef} peer={peer}"
            });
            return true;
        }

        private bool OnControlBlockAccess(
            object parameter,
            ClientConnection connection,
            ACSIClass acsiClass,
            LogicalDevice ld,
            LogicalNode ln,
            string objectName,
            string subObjectName,
            ControlBlockAccessType accessType)
        {
            string peer = SafePeer(connection);
            string path = $"{FormatModelRef(ln) ?? FormatModelRef(ld)}.{objectName}"
                          + (string.IsNullOrEmpty(subObjectName) ? "" : $".{subObjectName}");
            string service = accessType == ControlBlockAccessType.IEC61850_CB_ACCESS_TYPE_WRITE
                ? "SetRCB"
                : "GetRCB";
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "ingress",
                Protocol = "mms",
                Service = service,
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                ObjectRef = path,
                OrCat = acsiClass.ToString(),
                Result = "system",
                Summary = $"MMS {service} {path} peer={peer}"
            });
            return true;
        }

        private void OnRcbEvent(
            object parameter,
            ReportControlBlock rcb,
            ClientConnection con,
            RCBEventType eventType,
            string parameterName,
            MmsDataAccessError serviceError)
        {
            string peer = SafePeer(con);
            string rcbName = "";
            try { rcbName = rcb?.Name ?? ""; } catch { /* ignore */ }
            bool egress = eventType is RCBEventType.REPORT_CREATED or RCBEventType.GI or RCBEventType.OVERFLOW;
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = egress ? "egress" : "ingress",
                Protocol = "mms",
                Service = "RCB." + eventType,
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                ObjectRef = string.IsNullOrEmpty(rcbName) ? null : rcbName,
                ParamName = string.IsNullOrEmpty(parameterName) ? null : parameterName,
                Result = serviceError == MmsDataAccessError.SUCCESS || (int)serviceError == 0
                    ? (egress ? "applied" : "system")
                    : "error",
                Summary = $"MMS RCB {eventType} {rcbName}"
                          + (string.IsNullOrEmpty(parameterName) ? "" : $".{parameterName}")
                          + $" peer={peer}"
            });
        }

        private void OnClientConnection(IedServer iedServer, ClientConnection clientConnection, bool connected, object parameter)
        {
            string peer = SafePeer(clientConnection);
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "system",
                Protocol = "mms",
                Service = connected ? "Associate" : "Release",
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                Result = "system",
                Summary = connected
                    ? $"MMS Associate peer={peer}"
                    : $"MMS Release peer={peer}"
            });
        }

        private ControlHandlerResult OnControl(ControlAction action, object parameter, MmsValue ctlVal, bool test)
        {
            if (parameter is not Iec61850MapEntry entry)
                return ControlHandlerResult.FAILED;

            string peer = SafePeer(action.GetClientConnection());
            string service = action.IsSelect() ? "Select" : "Operate";
            object? ctlObj = null;
            try { ctlObj = FromMms(ctlVal); } catch { /* 仍记头字段 */ }
            string orCat = "";
            int ctlNum = -1;
            try { orCat = action.GetOrCat().ToString(); } catch { /* ignore */ }
            try { ctlNum = action.GetCtlNum(); } catch { /* ignore */ }

            if (test)
            {
                AppendMmsControl(service, entry, peer, orCat, ctlNum, ctlObj, test: true, ok: true, "skip:test");
                return ControlHandlerResult.OK;
            }

            if (ctlObj == null)
            {
                AppendMmsControl(service, entry, peer, orCat, ctlNum, null, test: false, ok: false, "error");
                return ControlHandlerResult.FAILED;
            }

            Interlocked.Increment(ref _inNativeCallback);
            try
            {
                bool ok = TryWrite(entry, ctlObj, out _);
                AppendMmsControl(service, entry, peer, orCat, ctlNum, ctlObj, test: false, ok,
                    ok ? "applied" : "error");
                return ok ? ControlHandlerResult.OK : ControlHandlerResult.FAILED;
            }
            catch (Exception ex)
            {
                Log.Warn($"[IEC61850] {ServerName} {service} {entry.ParamName} 失败", ex);
                AppendMmsControl(service, entry, peer, orCat, ctlNum, ctlObj, test: false, ok: false, "error");
                return ControlHandlerResult.FAILED;
            }
            finally
            {
                Interlocked.Decrement(ref _inNativeCallback);
            }
        }

        private MmsDataAccessError OnWriteAccess(DataAttribute dataAttr, MmsValue value, ClientConnection connection, object parameter)
        {
            if (!_attrToMap.TryGetValue(dataAttr.self, out var entry))
                entry = parameter as Iec61850MapEntry;
            if (entry == null)
                return MmsDataAccessError.OBJECT_UNDEFINED;

            string peer = SafePeer(connection);
            object? raw = null;
            try { raw = FromMms(value); } catch { /* ignore */ }
            if (raw == null)
            {
                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "ingress",
                    Protocol = "mms",
                    Service = "Write",
                    ServerName = ServerName,
                    IedName = IedName,
                    ClientPeer = peer,
                    ParamName = entry.ParamName,
                    ObjectRef = entry.ObjectRef,
                    Result = "error",
                    Summary = $"MMS Write {entry.ParamName} decode-failed peer={peer}"
                });
                return MmsDataAccessError.OBJECT_VALUE_INVALID;
            }

            Interlocked.Increment(ref _inNativeCallback);
            try
            {
                bool ok = TryWrite(entry, raw, out _);
                Iec61850TrafficLog.Append(new Iec61850TrafficMessage
                {
                    Direction = "ingress",
                    Protocol = "mms",
                    Service = "Write",
                    ServerName = ServerName,
                    IedName = IedName,
                    ClientPeer = peer,
                    ParamName = entry.ParamName,
                    ObjectRef = entry.ObjectRef,
                    Result = ok ? "applied" : "error",
                    Summary = $"MMS Write {entry.ParamName}={FormatTrafficValue(raw ?? "")} peer={peer}",
                    Writes = ok
                        ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            { [entry.ParamName] = raw }
                        : null,
                    Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        { [entry.ParamName] = raw }
                });
                return ok ? MmsDataAccessError.SUCCESS : MmsDataAccessError.OBJECT_VALUE_INVALID;
            }
            finally
            {
                Interlocked.Decrement(ref _inNativeCallback);
            }
        }

        private void AppendMmsControl(
            string service,
            Iec61850MapEntry entry,
            string peer,
            string orCat,
            int ctlNum,
            object? ctlVal,
            bool test,
            bool ok,
            string result)
        {
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "ingress",
                Protocol = "mms",
                Service = service,
                ServerName = ServerName,
                IedName = IedName,
                ClientPeer = peer,
                ParamName = entry.ParamName,
                ObjectRef = entry.ObjectRef,
                OrCat = string.IsNullOrEmpty(orCat) ? null : orCat,
                CtlNum = ctlNum >= 0 ? ctlNum : null,
                IsTest = test,
                Result = result,
                Summary = $"MMS {service} {entry.ParamName}={FormatTrafficValue(ctlVal ?? "")}"
                          + (test ? " test=true" : "")
                          + (string.IsNullOrEmpty(peer) ? "" : $" peer={peer}"),
                Writes = ok && !test && ctlVal != null
                    ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        { [entry.ParamName] = ctlVal }
                    : null,
                Values = ctlVal == null
                    ? null
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        { [entry.ParamName] = ctlVal }
            });
        }

        private static string SafePeer(ClientConnection? connection)
        {
            if (connection == null)
                return "";
            try { return connection.GetPeerAddress() ?? ""; }
            catch { return ""; }
        }

        private bool ShouldLogMmsRead(string key)
        {
            long now = Environment.TickCount64;
            if (_mmsReadThrottleTicks.TryGetValue(key, out var last) && now - last < MmsReadThrottleMs)
                return false;
            _mmsReadThrottleTicks[key] = now;
            return true;
        }

        private static string? FormatModelRef(ModelNode? node)
        {
            if (node == null)
                return null;
            try
            {
                string? r = node.GetObjectReference(withoutIedName: true);
                return string.IsNullOrWhiteSpace(r) ? node.GetName() : r;
            }
            catch
            {
                try { return node.GetName(); }
                catch { return null; }
            }
        }

        private bool TryWrite(Iec61850MapEntry entry, object? raw, out string error)
        {
            error = string.Empty;
            if (_writeControl == null)
            {
                error = "IED 未绑定控制回写";
                return false;
            }

            object value = CoerceWrite(entry, raw);
            _writeControl(entry.ParamName, value);
            PushOne(entry, value, lockModel: _inNativeCallback == 0);
            return true;
        }

        private object? ReadEngineering(Iec61850MapEntry entry)
        {
            var raw = _readPoint?.Invoke(entry.ParamName);
            if (raw == null)
                return DefaultValue(entry);

            if (string.Equals(entry.Cdc, "SPC", StringComparison.OrdinalIgnoreCase))
                return ToBool(raw);

            return raw;
        }

        private void OnPointsChanged(object? sender, ProtocolPointChangedEventArgs e)
        {
            if (e.Values.Count == 0)
                return;

            var entries = new List<Iec61850ReportEntry>();
            bool locked = false;
            try
            {
                if (_server != null && _inNativeCallback == 0)
                {
                    _server.LockDataModel();
                    locked = true;
                }

                foreach (var pair in e.Values)
                {
                    if (!_mapping.ByParam.TryGetValue(pair.Key, out var map))
                        continue;
                    PushOne(map, pair.Value, lockModel: false);
                    if (!map.IsStatusOrMeas)
                        continue;
                    entries.Add(new Iec61850ReportEntry
                    {
                        Ref = Iec61850Mapping.AbsoluteRef(IedName, map.ObjectRef),
                        Value = pair.Value
                    });
                }
            }
            finally
            {
                if (locked)
                    _server?.UnlockDataModel();
            }

            if (entries.Count == 0)
                return;

            int seq = Interlocked.Increment(ref _seqNum);
            var args = new Iec61850ReportEventArgs
            {
                RptId = $"{IedName}PCS/LLN0.{Iec61850PcsModel.UrcbName}",
                Reason = "dchg",
                SeqNum = seq,
                Entries = entries
            };
            ReportGenerated?.Invoke(this, args);

            var allData = entries.Select((e, i) => new Iec61850GooseDataEntry
            {
                Index = i,
                ParamName = e.Ref,
                Type = FormatTrafficType(e.Value),
                Value = e.Value is bool or float or double or int or long or string
                    ? e.Value
                    : FormatTrafficValue(e.Value ?? ""),
                Description = null
            }).ToList();
            Iec61850TrafficLog.Append(new Iec61850TrafficMessage
            {
                Direction = "egress",
                Protocol = "mms",
                Service = "Report",
                ServerName = ServerName,
                IedName = IedName,
                ObjectRef = args.RptId,
                SqNum = seq,
                NumDatSetEntries = allData.Count,
                Result = "applied",
                Summary = $"MMS Report {args.RptId} seq={seq} reason={args.Reason} n={allData.Count}",
                AllData = allData,
                Values = entries.ToDictionary(e => e.Ref, e => e.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        private void PushAllFromShadow()
        {
            if (_server == null && _model.LeafByParam.Count == 0)
                return;
            bool locked = false;
            try
            {
                if (_server != null)
                {
                    _server.LockDataModel();
                    locked = true;
                }

                foreach (var entry in _mapping.Entries)
                    PushOne(entry, ReadEngineering(entry), lockModel: false);
            }
            finally
            {
                if (locked)
                    _server?.UnlockDataModel();
            }
        }

        private void PushOne(Iec61850MapEntry entry, object? raw, bool lockModel)
        {
            if (!_model.LeafByParam.TryGetValue(entry.ParamName, out var attr))
                return;

            bool locked = false;
            try
            {
                if (lockModel && _server != null)
                {
                    _server.LockDataModel();
                    locked = true;
                }

                ApplyAttribute(attr, entry, raw);
            }
            finally
            {
                if (locked)
                    _server?.UnlockDataModel();
            }
        }

        private void ApplyAttribute(DataAttribute attr, Iec61850MapEntry entry, object? raw)
        {
            if (_server == null)
                return;

            if (string.Equals(entry.Cdc, "SPC", StringComparison.OrdinalIgnoreCase))
            {
                _server.UpdateBooleanAttributeValue(attr, ToBool(raw));
                return;
            }

            if (string.Equals(entry.Cdc, "INS", StringComparison.OrdinalIgnoreCase))
            {
                _server.UpdateInt32AttributeValue(attr, ToInt32(raw));
                return;
            }

            if (string.Equals(entry.Cdc, "BCR", StringComparison.OrdinalIgnoreCase))
            {
                _server.UpdateInt64AttributeValue(attr, ToInt64(raw));
                return;
            }

            _server.UpdateFloatAttributeValue(attr, ToFloat(raw));
        }

        private static object CoerceWrite(Iec61850MapEntry entry, object? raw)
        {
            if (string.Equals(entry.Cdc, "SPC", StringComparison.OrdinalIgnoreCase))
                return ToBool(raw) ? 1 : 0;
            if (raw == null)
                return 0;
            return raw;
        }

        private static object DefaultValue(Iec61850MapEntry entry) =>
            string.Equals(entry.Cdc, "SPC", StringComparison.OrdinalIgnoreCase) ? false : 0;

        private static object? FromMms(MmsValue? value)
        {
            if (value == null)
                return null;
            return value.GetType() switch
            {
                MmsType.MMS_BOOLEAN => value.GetBoolean(),
                MmsType.MMS_FLOAT => value.ToFloat(),
                MmsType.MMS_INTEGER => value.ToInt64(),
                MmsType.MMS_UNSIGNED => (long)value.ToUint32(),
                MmsType.MMS_STRUCTURE when value.Size() > 0 => FromMms(value.GetElement(0)),
                _ => value.ToString()
            };
        }

        private static bool ToBool(object? raw) =>
            raw switch
            {
                null => false,
                bool b => b,
                string s when bool.TryParse(s, out var bv) => bv,
                _ => Convert.ToDouble(raw, CultureInfo.InvariantCulture) != 0
            };

        private static float ToFloat(object? raw) =>
            raw == null ? 0f : Convert.ToSingle(raw, CultureInfo.InvariantCulture);

        private static int ToInt32(object? raw) =>
            raw == null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);

        private static long ToInt64(object? raw) =>
            raw == null ? 0 : Convert.ToInt64(raw, CultureInfo.InvariantCulture);

        private string StripIed(string ln)
        {
            ln = Iec61850Mapping.NormalizeRef(ln);
            if (ln.StartsWith(IedName, StringComparison.OrdinalIgnoreCase))
                return ln[IedName.Length..];
            return ln;
        }

        private static int PickFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
