using System.Collections.Generic;

namespace EssSimulator.DataExchange.Pipeline
{
    /// <summary>
    /// 把同一设备上连续的外部控制写合并成一次扫描。
    /// 扫描进行中到达的新写入只做标记，当前扫描结束后再跑一轮。
    /// </summary>
    internal sealed class ControlDrainCoalescer
    {
        private readonly object _gate = new();
        private readonly HashSet<int> _racks = new();
        private bool _bankDirty;
        private int _scheduled;

        public void SignalBank(Action<bool, IReadOnlyList<int>> drain)
        {
            lock (_gate)
                _bankDirty = true;
            Schedule(drain);
        }

        public void SignalRack(int rackId, Action<bool, IReadOnlyList<int>> drain)
        {
            lock (_gate)
                _racks.Add(rackId);
            Schedule(drain);
        }

        private void Schedule(Action<bool, IReadOnlyList<int>> drain)
        {
            if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
                return;

            ThreadPool.UnsafeQueueUserWorkItem(_ => Run(drain), null);
        }

        private void Run(Action<bool, IReadOnlyList<int>> drain)
        {
            try
            {
                while (true)
                {
                    bool bank;
                    int[] racks;
                    lock (_gate)
                    {
                        bank = _bankDirty;
                        _bankDirty = false;
                        racks = _racks.Count == 0 ? Array.Empty<int>() : _racks.ToArray();
                        _racks.Clear();
                    }

                    if (!bank && racks.Length == 0)
                        break;

                    drain(bank, racks);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _scheduled, 0);
                bool more;
                lock (_gate)
                    more = _bankDirty || _racks.Count > 0;
                if (more)
                    Schedule(drain);
            }
        }
    }
}
