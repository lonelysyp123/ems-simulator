using IEC61850.GOOSE.Subscriber;
using log4net;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>一块网卡一个 GooseReceiver，多台 PCS 各挂一个 Subscriber。</summary>
    internal sealed class Iec61850GooseReceiverHost : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Iec61850GooseReceiverHost));
        private readonly object _gate = new();
        private GooseReceiver? _receiver;
        private string? _iface;
        private readonly List<GooseSubscriber> _subs = new();
        private bool _disposed;

        public string? InterfaceId
        {
            get { lock (_gate) { return _iface; } }
        }

        public bool IsRunning
        {
            get { lock (_gate) { return _receiver != null && _receiver.IsRunning(); } }
        }

        public bool TryAdd(string iface, GooseSubscriber subscriber, out string error)
        {
            error = string.Empty;
            lock (_gate)
            {
                if (_disposed)
                {
                    error = "receiver disposed";
                    return false;
                }

                if (_receiver != null
                    && _iface != null
                    && !string.Equals(_iface, iface, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"GOOSE 订阅网卡已是 {_iface}";
                    return false;
                }

                bool running = _receiver != null && _receiver.IsRunning();
                if (_receiver == null)
                {
                    _receiver = new GooseReceiver();
                    GC.SuppressFinalize(_receiver);
                    _receiver.SetInterfaceId(iface);
                    _iface = iface;
                }

                if (running)
                {
                    try { _receiver.Stop(); }
                    catch (Exception ex) { Log.Warn("暂停 GooseReceiver 以加入订阅失败", ex); }
                }

                _receiver.AddSubscriber(subscriber);
                _subs.Add(subscriber);

                if (running)
                    return TryStartUnlocked(out error);
                return true;
            }
        }

        public void Remove(GooseSubscriber? subscriber)
        {
            if (subscriber == null)
                return;
            lock (_gate)
            {
                if (_receiver == null)
                    return;
                try { _receiver.RemoveSubscriber(subscriber); }
                catch (Exception ex) { Log.Warn("RemoveSubscriber 异常", ex); }
                _subs.Remove(subscriber);
                try
                {
                    GC.SuppressFinalize(subscriber);
                    subscriber.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warn("GooseSubscriber.Dispose 异常", ex);
                }
            }
        }

        public bool TryStart(out string error)
        {
            lock (_gate)
                return TryStartUnlocked(out error);
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_receiver == null)
                    return;
                try
                {
                    if (_receiver.IsRunning())
                        _receiver.Stop();
                }
                catch (Exception ex)
                {
                    Log.Warn("GooseReceiver.Stop 异常", ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                try
                {
                    if (_receiver != null && _receiver.IsRunning())
                        _receiver.Stop();
                }
                catch { /* ignore */ }
                foreach (var sub in _subs)
                {
                    try { _receiver?.RemoveSubscriber(sub); }
                    catch { /* ignore */ }
                    try
                    {
                        GC.SuppressFinalize(sub);
                        sub.Dispose();
                    }
                    catch { /* ignore */ }
                }

                _subs.Clear();
                try
                {
                    if (_receiver != null)
                    {
                        GC.SuppressFinalize(_receiver);
                        _receiver.Dispose();
                    }
                }
                catch { /* ignore */ }
                _receiver = null;
                _iface = null;
            }
        }

        private bool TryStartUnlocked(out string error)
        {
            error = string.Empty;
            if (_receiver == null)
            {
                error = "无 GooseReceiver";
                return false;
            }

            if (_receiver.IsRunning())
                return true;

            try
            {
                _receiver.Start();
                if (_receiver.IsRunning())
                    return true;
                error = "GooseReceiver.Start 后未运行";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warn($"[IEC61850] GOOSE 订阅未启用（需要原始以太网权限）iface={_iface}", ex);
                return false;
            }
        }
    }
}
