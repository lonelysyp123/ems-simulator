using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using NModbus.Data;
using NModbus.Device;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// Modbus TCP 共享传输层：每个端口只建立一个 TcpListener 与一个 NModbus SlaveNetwork，
    /// 同端口不同从站号注册为独立从站；同端口同从站号的多个设备共享同一个从站寄存器镜像
    /// （挂载前经 <see cref="AddressOverlapValidator"/> 校验地址不重叠）。
    /// 可选来源 IP 白名单在 Accept 时过滤；空名单不限制。
    /// </summary>
    public sealed class ModbusPortHub
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ModbusPortHub));
        private static readonly Lazy<ModbusPortHub> _instance = new(() => new ModbusPortHub());

        /// <summary>全局共享实例（托管服务、从站与 Web 端点共用）。</summary>
        public static ModbusPortHub Instance => _instance.Value;

        private readonly object _gate = new();
        private readonly Dictionary<int, PortListenerHost> _hosts = new();
        private ModbusIpAllowList _allowList = ModbusIpAllowList.Unrestricted;

        /// <summary>设备挂载结果：失败时 Errors 给出具体冲突/绑定原因。</summary>
        public sealed class AttachResult
        {
            public bool Ok { get; init; }
            public List<string> Errors { get; init; } = new();
            /// <summary>挂载成功后的共享寄存器镜像，供从站挂载控制写钩子。</summary>
            public SlaveDataStore? DataStore { get; init; }

            public static AttachResult Success(SlaveDataStore store) =>
                new() { Ok = true, DataStore = store };

            public static AttachResult Fail(params string[] errors) =>
                new() { Ok = false, Errors = errors.ToList() };
        }

        /// <summary>当前进程已加载的白名单快照（热重建不重读磁盘）。</summary>
        public ModbusIpAllowList ActiveAllowList
        {
            get { lock (_gate) { return _allowList; } }
        }

        /// <summary>
        /// 设置后续新建监听使用的白名单。已在听的端口不受影响；
        /// 生产路径仅在进程启动 <c>StartAll</c> 时调用一次。
        /// </summary>
        public void SetAllowList(ModbusIpAllowList? list)
        {
            lock (_gate)
            {
                _allowList = list ?? ModbusIpAllowList.Unrestricted;
            }
        }

        /// <summary>
        /// 将设备点表挂载到 (端口, 从站号) 的共享从站上。
        /// 先与同从站号下已挂载设备做地址查重，再确保监听与从站存在。
        /// </summary>
        public AttachResult AttachDevice(int port, byte slaveId, string deviceName, MapEntry[] entries)
        {
            lock (_gate)
            {
                var host = EnsureHost(port, out string? bindError);
                if (host == null)
                    return AttachResult.Fail(bindError ?? $"端口 {port} 绑定失败");

                var slot = host.GetOrCreateSlot(slaveId);

                var probe = new List<AddressOverlapValidator.DevicePointMap>();
                foreach (var attached in slot.Devices)
                    probe.Add(new AddressOverlapValidator.DevicePointMap(attached.Key, slaveId, attached.Value));
                probe.Add(new AddressOverlapValidator.DevicePointMap(deviceName, slaveId, entries));

                var conflicts = AddressOverlapValidator.Validate(probe);
                if (conflicts.Count > 0)
                {
                    var prefix = $"端口 {port} ";
                    return AttachResult.Fail(conflicts.Select(c => prefix + c).ToArray());
                }

                slot.Devices[deviceName] = entries;
                return AttachResult.Success(slot.DataStore);
            }
        }

        /// <summary>
        /// 卸载设备：设备是所在从站的最后一个成员时移除从站；从站清空且端口无其它从站时释放监听。
        /// </summary>
        public void DetachDevice(int port, byte slaveId, string deviceName)
        {
            lock (_gate)
            {
                if (!_hosts.TryGetValue(port, out var host))
                    return;

                if (host.TryGetSlot(slaveId, out var slot))
                {
                    slot!.Devices.Remove(deviceName);
                    if (slot.Devices.Count == 0)
                        host.RemoveSlot(slaveId);
                }

                if (host.SlotCount == 0)
                {
                    host.Dispose();
                    _hosts.Remove(port);
                    Log.Info($"端口 {port} 已无挂载设备，释放 Modbus 监听");
                }
            }
        }

        public SlaveDataStore? GetDataStore(int port, byte slaveId)
        {
            lock (_gate)
            {
                return _hosts.TryGetValue(port, out var host) && host.TryGetSlot(slaveId, out var slot)
                    ? slot!.DataStore
                    : null;
            }
        }

        public NModbus.IModbusSlaveNetwork? GetNetwork(int port)
        {
            lock (_gate)
            {
                return _hosts.TryGetValue(port, out var host) ? host.Network : null;
            }
        }

        public bool IsPortListening(int port)
        {
            lock (_gate)
            {
                return _hosts.TryGetValue(port, out var host) && host.IsListening;
            }
        }

        /// <summary>挂载到某端口的设备清单（快照），供界面展示共享组。</summary>
        public Dictionary<byte, List<string>> GetAttachedDevices(int port)
        {
            lock (_gate)
            {
                var result = new Dictionary<byte, List<string>>();
                if (!_hosts.TryGetValue(port, out var host))
                    return result;
                foreach (var (slaveId, slot) in host.Slots)
                    result[slaveId] = slot.Devices.Keys.ToList();
                return result;
            }
        }

        /// <summary>释放全部监听与从站网络（进程退出 / 协议层重建前调用）。</summary>
        public void ShutdownAll()
        {
            lock (_gate)
            {
                foreach (var host in _hosts.Values)
                {
                    try { host.Dispose(); }
                    catch (Exception ex) { Log.Warn($"释放端口 {host.Port} 监听时异常", ex); }
                }
                _hosts.Clear();
            }
        }

        private PortListenerHost? EnsureHost(int port, out string? error)
        {
            if (_hosts.TryGetValue(port, out var existing))
            {
                error = null;
                return existing;
            }

            var host = new PortListenerHost(port, _allowList);
            try
            {
                host.Start();
            }
            catch (Exception ex)
            {
                Log.Error($"端口 {port} Modbus 监听启动失败：{ex.Message}");
                error = $"端口 {port} 监听启动失败：{ex.Message}";
                try { host.Dispose(); } catch { /* 启动失败时尽量释放半开监听 */ }
                return null;
            }

            _hosts[port] = host;
            string mode = _allowList.IsUnrestricted ? string.Empty : "（IP 白名单已启用）";
            Log.Info($"端口 {port} Modbus 共享监听已启动{mode}");
            error = null;
            return host;
        }

        /// <summary>单个端口的监听宿主：一个对外 TcpListener + 一个 NModbus 从站网络。</summary>
        private sealed class PortListenerHost : IDisposable
        {
            private static readonly TimeSpan DenyLogInterval = TimeSpan.FromSeconds(10);
            private static readonly ConcurrentDictionary<string, long> DenyLogUtcTicks = new();

            public int Port { get; }
            public bool IsListening { get; private set; }
            public NModbus.IModbusSlaveNetwork? Network { get; private set; }

            private readonly ModbusIpAllowList _allowList;
            private readonly NModbus.ModbusFactory _factory = new();
            private readonly Dictionary<byte, SharedSlaveSlot> _slots = new();
            private readonly ConcurrentBag<TcpClient> _pipedClients = new();

            private TcpListener? _publicListener;
            private TcpListener? _nmodbusListener;
            private CancellationTokenSource? _cts;

            public PortListenerHost(int port, ModbusIpAllowList allowList)
            {
                Port = port;
                _allowList = allowList ?? ModbusIpAllowList.Unrestricted;
            }

            public int SlotCount => _slots.Count;

            public IEnumerable<KeyValuePair<byte, SharedSlaveSlot>> Slots => _slots;

            public void Start()
            {
                if (_allowList.IsUnrestricted)
                {
                    var listener = new TcpListener(IPAddress.Any, Port);
                    listener.Start();
                    _publicListener = listener;
                    _nmodbusListener = listener;
                    Network = _factory.CreateSlaveNetwork(listener);
                    Network.ListenAsync();
                }
                else
                {
                    var loopback = new TcpListener(IPAddress.Loopback, 0);
                    loopback.Start();
                    _nmodbusListener = loopback;
                    int innerPort = ((IPEndPoint)loopback.LocalEndpoint).Port;
                    Network = _factory.CreateSlaveNetwork(loopback);
                    Network.ListenAsync();

                    var pub = new TcpListener(IPAddress.Any, Port);
                    pub.Start();
                    _publicListener = pub;
                    _cts = new CancellationTokenSource();
                    _ = Task.Run(() => AcceptLoopAsync(innerPort, _cts.Token));
                }

                IsListening = true;
            }

            public SharedSlaveSlot GetOrCreateSlot(byte slaveId)
            {
                if (_slots.TryGetValue(slaveId, out var slot))
                    return slot;

                slot = new SharedSlaveSlot(slaveId);
                var nmodbusSlave = _factory.CreateSlave(slaveId, slot.DataStore);
                Network!.AddSlave(nmodbusSlave);
                _slots[slaveId] = slot;
                return slot;
            }

            public bool TryGetSlot(byte slaveId, out SharedSlaveSlot? slot)
            {
                if (_slots.TryGetValue(slaveId, out var found))
                {
                    slot = found;
                    return true;
                }
                slot = null;
                return false;
            }

            public void RemoveSlot(byte slaveId)
            {
                if (!_slots.Remove(slaveId))
                    return;
                try { Network?.RemoveSlave(slaveId); }
                catch { /* 从站网络可能已释放 */ }
            }

            public void Dispose()
            {
                IsListening = false;
                _slots.Clear();
                try { _cts?.Cancel(); }
                catch { /* 忽略释放异常 */ }
                try { Network?.Dispose(); }
                catch { /* 忽略释放异常 */ }
                Network = null;
                try { _publicListener?.Stop(); }
                catch { /* 忽略释放异常 */ }
                if (!ReferenceEquals(_publicListener, _nmodbusListener))
                {
                    try { _nmodbusListener?.Stop(); }
                    catch { /* 忽略释放异常 */ }
                }
                _publicListener = null;
                _nmodbusListener = null;
                while (_pipedClients.TryTake(out var client))
                {
                    try { client.Close(); }
                    catch { /* 忽略释放异常 */ }
                }
                try { _cts?.Dispose(); }
                catch { /* 忽略释放异常 */ }
                _cts = null;
            }

            private async Task AcceptLoopAsync(int innerPort, CancellationToken ct)
            {
                var listener = _publicListener;
                if (listener == null)
                    return;

                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        if (ct.IsCancellationRequested)
                            break;
                        continue;
                    }

                    var remote = client.Client.RemoteEndPoint;
                    if (!_allowList.IsAllowed(remote))
                    {
                        LogDenied(remote);
                        Reject(client);
                        continue;
                    }

                    TcpClient inner;
                    try
                    {
                        inner = new TcpClient { NoDelay = true };
                        await inner.ConnectAsync(IPAddress.Loopback, innerPort, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        Reject(client);
                        continue;
                    }

                    client.NoDelay = true;
                    _pipedClients.Add(client);
                    _pipedClients.Add(inner);
                    _ = PumpAsync(client, inner, ct);
                }
            }

            private void LogDenied(EndPoint? remote)
            {
                string key = remote?.ToString() ?? "?";
                long now = DateTime.UtcNow.Ticks;
                if (DenyLogUtcTicks.TryGetValue(key, out var last) && now - last < DenyLogInterval.Ticks)
                    return;
                DenyLogUtcTicks[key] = now;
                Log.Warn($"拒绝 Modbus TCP 连接 {key} → 端口 {Port}（不在 IP 白名单）");
            }

            private static void Reject(TcpClient client)
            {
                try
                {
                    client.LingerState = new LingerOption(true, 0);
                    client.Close();
                }
                catch { /* 拒绝连接时忽略关闭异常 */ }
            }

            private static async Task PumpAsync(TcpClient a, TcpClient b, CancellationToken ct)
            {
                try
                {
                    var sa = a.GetStream();
                    var sb = b.GetStream();
                    var t1 = sa.CopyToAsync(sb, ct);
                    var t2 = sb.CopyToAsync(sa, ct);
                    await Task.WhenAny(t1, t2).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 监听关闭
                }
                catch
                {
                    // 对端断开
                }
                finally
                {
                    try { a.Close(); } catch { /* ignore */ }
                    try { b.Close(); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>同 (端口, 从站号) 的共享从站槽位：一个寄存器镜像 + 多个挂载设备。</summary>
        public sealed class SharedSlaveSlot
        {
            public SharedSlaveSlot(byte slaveId)
            {
                SlaveId = slaveId;
                DataStore = new SlaveDataStore();
            }

            public byte SlaveId { get; }
            public SlaveDataStore DataStore { get; }
            /// <summary>设备名 -> 该设备挂载的点表条目（用于后续挂载时的地址查重）。</summary>
            public Dictionary<string, MapEntry[]> Devices { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
