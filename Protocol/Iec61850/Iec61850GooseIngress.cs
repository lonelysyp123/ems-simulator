using IEC61850.Common;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>
    /// 入向 GOOSE 解码：按下标对齐 <c>dsGoose</c>，过滤 test、本机 GoCB、重复 stNum。
    /// </summary>
    internal sealed class Iec61850GooseIngress
    {
        private readonly IReadOnlyList<Iec61850MapEntry> _gooseEntries;
        private readonly string _iedName;
        private uint? _lastStNum;

        public Iec61850GooseIngress(IReadOnlyList<Iec61850MapEntry> gooseEntries, string iedName)
        {
            _gooseEntries = gooseEntries;
            _iedName = iedName;
        }

        public uint? LastStNum => _lastStNum;

        public DateTime? LastAcceptedUtc { get; private set; }

        public bool TryAccept(
            uint stNum,
            bool isTest,
            string? goCbRef,
            IReadOnlyList<object?> values,
            out IReadOnlyDictionary<string, object> writes,
            out string skipReason)
        {
            writes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (isTest)
            {
                skipReason = "test";
                return false;
            }

            if (IsLocalGoCbRef(goCbRef, _iedName))
            {
                skipReason = "local-gocb";
                return false;
            }

            if (_lastStNum.HasValue && _lastStNum.Value == stNum)
            {
                skipReason = "stNum";
                return false;
            }

            if (values == null || values.Count == 0 || _gooseEntries.Count == 0)
            {
                skipReason = "empty";
                return false;
            }

            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            int n = Math.Min(values.Count, _gooseEntries.Count);
            for (int i = 0; i < n; i++)
            {
                var raw = values[i];
                if (raw == null)
                    continue;
                map[_gooseEntries[i].ParamName] = raw;
            }

            if (map.Count == 0)
            {
                skipReason = "empty";
                return false;
            }

            _lastStNum = stNum;
            LastAcceptedUtc = DateTime.UtcNow;
            writes = map;
            skipReason = string.Empty;
            return true;
        }

        public static List<object?> FromDataset(MmsValue? dataset)
        {
            var list = new List<object?>();
            if (dataset == null)
                return list;

            int n = dataset.Size();
            for (int i = 0; i < n; i++)
                list.Add(FromMms(dataset.GetElement(i)));
            return list;
        }

        internal static bool IsLocalGoCbRef(string? goCbRef, string iedName)
        {
            if (string.IsNullOrWhiteSpace(goCbRef) || string.IsNullOrWhiteSpace(iedName))
                return false;

            string localDot = Iec61850PcsModel.LocalGoCbRef(iedName);
            string localMms = Iec61850PcsModel.LocalGoCbRefMms(iedName);
            return goCbRef.Equals(localDot, StringComparison.OrdinalIgnoreCase)
                   || goCbRef.Equals(localMms, StringComparison.OrdinalIgnoreCase);
        }

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
    }
}
