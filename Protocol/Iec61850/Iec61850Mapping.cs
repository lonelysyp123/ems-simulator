using System.Globalization;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>ParamName → IEC 61850 对象引用映射行。</summary>
    public sealed class Iec61850MapEntry
    {
        public string ParamName { get; init; } = string.Empty;
        public string ObjectRef { get; init; } = string.Empty;
        public string Cdc { get; init; } = string.Empty;
        public string Fc { get; init; } = string.Empty;
        /// <summary>0=status-only，1=direct-with-normal-security。</summary>
        public int CtlModel { get; init; }
        public double Scale { get; init; } = 1;
        public string Description { get; init; } = string.Empty;
        /// <summary><c>mms</c>（URCB）或 <c>goose</c>（入向遥控订阅）。缺省按 ParamName 前缀 yk/yt 推断。</summary>
        public string Transport { get; init; } = "mms";

        public bool IsControllable => CtlModel > 0;
        public bool IsSetpoint => string.Equals(Fc, "SP", StringComparison.OrdinalIgnoreCase);
        public bool IsGoose => string.Equals(Transport, "goose", StringComparison.OrdinalIgnoreCase);
        public bool IsStatusOrMeas =>
            string.Equals(Fc, "MX", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Fc, "ST", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 加载 <c>pointmaps/models/emu/iec61850/mapping.csv</c>。
    /// ObjectRef 为 LD 相对引用（如 <c>PCS/MMXU1.TotW.mag.f</c>），运行时再拼 IED 名。
    /// </summary>
    public sealed class Iec61850Mapping
    {
        public const string RelativeDir = "pointmaps/models/emu/iec61850";
        public const string MappingFileName = "mapping.csv";
        public const string IcdFileName = "pcs.icd";
        public const string IngressIcdFileName = "ems_goose.icd";

        public IReadOnlyList<Iec61850MapEntry> Entries { get; }
        public IReadOnlyDictionary<string, Iec61850MapEntry> ByParam { get; }
        public IReadOnlyDictionary<string, Iec61850MapEntry> ByObjectRef { get; }

        public Iec61850Mapping(IReadOnlyList<Iec61850MapEntry> entries)
        {
            Entries = entries;
            ByParam = entries.ToDictionary(e => e.ParamName, StringComparer.OrdinalIgnoreCase);
            var byRef = new Dictionary<string, Iec61850MapEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                byRef[NormalizeRef(entry.ObjectRef)] = entry;
            ByObjectRef = byRef;
        }

        public static Iec61850Mapping Load(string? mappingPath = null)
        {
            string path = mappingPath ?? ResolveMappingPath();
            var rows = new List<Iec61850MapEntry>();
            using var reader = new StreamReader(path);
            string? header = reader.ReadLine();
            if (header == null)
                return new Iec61850Mapping(rows);

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var cols = SplitCsv(line);
                if (cols.Length < 6)
                    continue;

                string paramName = cols[0].Trim();
                rows.Add(new Iec61850MapEntry
                {
                    ParamName = paramName,
                    ObjectRef = cols[1].Trim(),
                    Cdc = cols[2].Trim(),
                    Fc = cols[3].Trim(),
                    CtlModel = int.TryParse(cols[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ctl)
                        ? ctl : 0,
                    Scale = double.TryParse(cols[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
                        ? scale : 1,
                    Description = cols.Length > 6 ? cols[6].Trim() : string.Empty,
                    Transport = ParseTransport(paramName, cols.Length > 7 ? cols[7] : null)
                });
            }

            return new Iec61850Mapping(rows);
        }

        public static string ResolveMappingPath() => ResolveUnderIec61850Dir(MappingFileName);

        public static string ResolveIcdPath() => ResolveUnderIec61850Dir(IcdFileName);

        public static string IedNameFor(string serverName)
        {
            int n = ParseSimIndex(serverName);
            return $"TRNA_PCS{n:D2}";
        }

        public static int ParseSimIndex(string serverName)
        {
            const string prefix = "simEmu";
            if (serverName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(serverName[prefix.Length..], out var n)
                && n > 0)
            {
                return n;
            }

            return 1;
        }

        public static string LogicalDeviceName => "PCS";

        /// <summary>绝对对象引用：IED 名与 LD 名拼接（IEC 61850-7-2）。</summary>
        public static string AbsoluteRef(string iedName, string objectRef)
        {
            objectRef = NormalizeRef(objectRef);
            if (objectRef.StartsWith(iedName, StringComparison.OrdinalIgnoreCase))
                return objectRef;
            return iedName + objectRef;
        }

        public bool TryResolve(string objectRef, string iedName, out Iec61850MapEntry entry)
        {
            entry = null!;
            if (string.IsNullOrWhiteSpace(objectRef))
                return false;

            var candidates = ExpandRefCandidates(objectRef, iedName);
            foreach (var candidate in candidates)
            {
                if (ByObjectRef.TryGetValue(candidate, out entry!))
                    return true;
            }

            foreach (var map in Entries)
            {
                foreach (var candidate in candidates)
                {
                    if (IsPrefixMatch(map.ObjectRef, candidate))
                    {
                        entry = map;
                        return true;
                    }
                }
            }

            return false;
        }

        public IReadOnlyList<Iec61850MapEntry> GooseEntries =>
            Entries.Where(e => e.IsGoose).ToList();

        public IReadOnlyList<string> LogicalDevices() => new[] { LogicalDeviceName };

        public IReadOnlyList<string> LogicalNodes()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add($"{LogicalDeviceName}/LLN0");
            set.Add($"{LogicalDeviceName}/LPHD1");
            foreach (var entry in Entries)
            {
                int slash = entry.ObjectRef.IndexOf('/');
                int dot = entry.ObjectRef.IndexOf('.', slash + 1);
                if (slash >= 0 && dot > slash)
                    set.Add(entry.ObjectRef[..dot]);
            }

            return set.ToList();
        }

        public IReadOnlyList<string> DataObjects(string logicalNode)
        {
            string prefix = NormalizeRef(logicalNode).TrimEnd('.') + ".";
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Entries)
            {
                var norm = NormalizeRef(entry.ObjectRef);
                if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    set.Add(norm);
            }

            return set.ToList();
        }

        private static string ResolveUnderIec61850Dir(string fileName)
        {
            foreach (var root in DeviceModelRegistry.CandidateRoots())
            {
                var candidate = Path.Combine(root, RelativeDir, fileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            throw new FileNotFoundException(
                $"找不到 IEC 61850 文件 `{fileName}`。请确认 `{RelativeDir}/` 已随程序输出。",
                fileName);
        }

        internal static string ParseTransport(string paramName, string? raw)
        {
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim().ToLowerInvariant();
            if (paramName.StartsWith("yk", StringComparison.OrdinalIgnoreCase)
                || paramName.StartsWith("yt", StringComparison.OrdinalIgnoreCase))
            {
                return "goose";
            }

            return "mms";
        }

        internal static string NormalizeRef(string objectRef) =>
            objectRef.Trim().Replace('\\', '/');

        private IEnumerable<string> ExpandRefCandidates(string objectRef, string iedName)
        {
            var norm = NormalizeRef(objectRef);
            yield return norm;

            string absPrefix = iedName + LogicalDeviceName + "/";
            string ldPrefix = LogicalDeviceName + "/";

            if (norm.StartsWith(iedName, StringComparison.OrdinalIgnoreCase))
            {
                var rest = norm[iedName.Length..];
                yield return rest;
                if (rest.StartsWith(LogicalDeviceName, StringComparison.OrdinalIgnoreCase))
                    yield return rest;
            }

            if (!norm.Contains('/'))
                yield return ldPrefix + norm;

            if (norm.StartsWith(absPrefix, StringComparison.OrdinalIgnoreCase))
                yield return norm[iedName.Length..];
        }

        private static bool IsPrefixMatch(string mapped, string candidate)
        {
            mapped = NormalizeRef(mapped);
            candidate = NormalizeRef(candidate);
            if (string.Equals(mapped, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
            if (mapped.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase))
                return true;
            if (candidate.StartsWith(mapped, StringComparison.OrdinalIgnoreCase)
                && (candidate.Length == mapped.Length || candidate[mapped.Length] == '.'))
            {
                return true;
            }

            string doName = StripLastDa(mapped);
            return string.Equals(doName, candidate, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripLastDa(string objectRef)
        {
            int last = objectRef.LastIndexOf('.');
            return last > objectRef.IndexOf('/') ? objectRef[..last] : objectRef;
        }

        private static string[] SplitCsv(string line)
        {
            var list = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (ch == ',' && !quoted)
                {
                    list.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }

                cur.Append(ch);
            }

            list.Add(cur.ToString());
            return list.ToArray();
        }
    }
}
