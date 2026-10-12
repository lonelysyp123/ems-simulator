namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>IEC 61850 交互报文（GOOSE 入向 / 系统事件）。仅内存环形缓冲。</summary>
    public sealed class Iec61850TrafficMessage
    {
        public long Id { get; init; }
        public DateTime Utc { get; init; }
        /// <summary>ingress | egress | system</summary>
        public string Direction { get; init; } = "ingress";
        /// <summary>goose | mms | system</summary>
        public string Protocol { get; init; } = "goose";
        /// <summary>Associate | Release | Operate | Select | Write（MMS）；空表示 GOOSE/系统摘要。</summary>
        public string? Service { get; init; }
        public string ServerName { get; init; } = "";
        public string IedName { get; init; } = "";
        public string? ClientPeer { get; init; }
        public string? ParamName { get; init; }
        public string? ObjectRef { get; init; }
        public string? OrCat { get; init; }
        public int? CtlNum { get; init; }
        public int? AppId { get; init; }
        public string? GoCbRef { get; init; }
        public string? GoId { get; init; }
        public string? DatSet { get; init; }
        public long? StNum { get; init; }
        public long? SqNum { get; init; }
        public bool IsTest { get; init; }
        public bool NeedsCommission { get; init; }
        public long? ConfRev { get; init; }
        public long? TimeAllowedToLive { get; init; }
        /// <summary>GOOSE PDU 内时间戳（UTC ISO）。</summary>
        public string? GooseTimestampUtc { get; init; }
        public int? NumDatSetEntries { get; init; }
        /// <summary>applied | skip:test | skip:stNum | skip:* | error | system</summary>
        public string Result { get; init; } = "";
        public string Summary { get; init; } = "";
        public Dictionary<string, object?>? Writes { get; init; }
        public Dictionary<string, object?>? Values { get; init; }
        /// <summary>有序 allData（与 Wireshark 下标一致）。</summary>
        public IReadOnlyList<Iec61850GooseDataEntry>? AllData { get; init; }
    }

    public sealed class Iec61850GooseDataEntry
    {
        public int Index { get; init; }
        public string ParamName { get; init; } = "";
        public string Type { get; init; } = "";
        public object? Value { get; init; }
        public string? Description { get; init; }
    }

    /// <summary>进程内 GOOSE/系统报文环，供 Web API / SignalR 消费。</summary>
    public static class Iec61850TrafficLog
    {
        public const int DefaultCapacity = 3000;

        private static readonly object Gate = new();
        private static readonly LinkedList<Iec61850TrafficMessage> Buffer = new();
        private static long _nextId = 1;
        private static int _capacity = DefaultCapacity;

        public static event Action<Iec61850TrafficMessage>? MessageAppended;

        public static int Capacity
        {
            get { lock (Gate) return _capacity; }
            set { lock (Gate) { _capacity = Math.Clamp(value, 100, 10000); TrimUnlocked(); } }
        }

        public static Iec61850TrafficMessage Append(Iec61850TrafficMessage draft)
        {
            Iec61850TrafficMessage msg;
            lock (Gate)
            {
                msg = new Iec61850TrafficMessage
                {
                    Id = _nextId++,
                    Utc = draft.Utc == default ? DateTime.UtcNow : draft.Utc,
                    Direction = draft.Direction,
                    Protocol = draft.Protocol,
                    Service = draft.Service,
                    ServerName = draft.ServerName,
                    IedName = draft.IedName,
                    ClientPeer = draft.ClientPeer,
                    ParamName = draft.ParamName,
                    ObjectRef = draft.ObjectRef,
                    OrCat = draft.OrCat,
                    CtlNum = draft.CtlNum,
                    AppId = draft.AppId,
                    GoCbRef = draft.GoCbRef,
                    GoId = draft.GoId,
                    DatSet = draft.DatSet,
                    StNum = draft.StNum,
                    SqNum = draft.SqNum,
                    IsTest = draft.IsTest,
                    NeedsCommission = draft.NeedsCommission,
                    ConfRev = draft.ConfRev,
                    TimeAllowedToLive = draft.TimeAllowedToLive,
                    GooseTimestampUtc = draft.GooseTimestampUtc,
                    NumDatSetEntries = draft.NumDatSetEntries,
                    Result = draft.Result,
                    Summary = draft.Summary,
                    Writes = draft.Writes,
                    Values = draft.Values,
                    AllData = draft.AllData
                };
                Buffer.AddFirst(msg);
                TrimUnlocked();
            }

            try { MessageAppended?.Invoke(msg); }
            catch { /* 订阅方异常不影响协议 */ }
            return msg;
        }

        public static IReadOnlyList<Iec61850TrafficMessage> Snapshot(string? serverName = null, int limit = 200)
        {
            limit = Math.Clamp(limit, 1, 2000);
            lock (Gate)
            {
                IEnumerable<Iec61850TrafficMessage> q = Buffer;
                if (!string.IsNullOrWhiteSpace(serverName))
                {
                    q = q.Where(m =>
                        string.Equals(m.ServerName, serverName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.IedName, serverName, StringComparison.OrdinalIgnoreCase));
                }

                return q.Take(limit).ToList();
            }
        }

        public static void Clear()
        {
            lock (Gate) { Buffer.Clear(); }
        }

        private static void TrimUnlocked()
        {
            while (Buffer.Count > _capacity)
                Buffer.RemoveLast();
        }
    }
}
