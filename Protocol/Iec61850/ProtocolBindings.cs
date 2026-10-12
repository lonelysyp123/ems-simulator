using System.Text.Json;
using System.Text.Json.Serialization;

namespace EssSimulator.Protocol.Iec61850
{
    public static class Iec61850Protocols
    {
        public const string Modbus = "modbus";
        public const string Iec61850 = "iec61850";
    }

    public sealed class ProtocolBindingEntry
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Protocols { get; set; } = new();
        public int? Iec61850Port { get; set; }
    }

    public sealed class ProtocolBindingsFile
    {
        public List<ProtocolBindingEntry> Entries { get; set; } = new();
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// 按台协议开关（<c>configs/protocol-bindings.json</c>）。
    /// 缺省：PCS 同时开 Modbus 与 IEC 61850；其它设备仅 Modbus。
    /// </summary>
    public sealed class ProtocolBindings
    {
        public const string RelativePath = "configs/protocol-bindings.json";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Dictionary<string, ProtocolBindingEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public string? LoadError { get; private set; }

        public IReadOnlyCollection<ProtocolBindingEntry> Entries => _entries.Values;

        public static ProtocolBindings Load()
        {
            var bindings = new ProtocolBindings();
            var path = ResolvePath();
            if (path == null || !File.Exists(path))
                return bindings;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<ProtocolBindingsFile>(json, JsonOpts);
                if (file?.Entries == null)
                    return bindings;

                foreach (var entry in file.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        continue;
                    NormalizeProtocols(entry);
                    bindings._entries[entry.Name] = entry;
                }
            }
            catch (Exception ex)
            {
                bindings.LoadError = $"protocol-bindings.json 读取失败，已使用默认双协议：{ex.Message}";
            }

            return bindings;
        }

        public static void Save(IEnumerable<ProtocolBindingEntry> entries)
        {
            var list = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(e =>
                {
                    NormalizeProtocols(e);
                    return e;
                })
                .ToList();

            var file = new ProtocolBindingsFile
            {
                Entries = list,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var dir = Path.Combine(FindConfigRoot(), "configs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "protocol-bindings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOpts));
        }

        public IReadOnlyList<string> ProtocolsFor(string serverName)
        {
            if (_entries.TryGetValue(serverName, out var entry) && entry.Protocols.Count > 0)
                return entry.Protocols;

            if (serverName.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase))
                return new[] { Iec61850Protocols.Modbus, Iec61850Protocols.Iec61850 };

            return new[] { Iec61850Protocols.Modbus };
        }

        public bool Allows(string serverName, string protocol) =>
            ProtocolsFor(serverName).Any(p => string.Equals(p, protocol, StringComparison.OrdinalIgnoreCase));

        public int? PortOverride(string serverName) =>
            _entries.TryGetValue(serverName, out var entry) ? entry.Iec61850Port : null;

        public static int DefaultIec61850Port(int simIndex1Based, int basePort, int step) =>
            basePort + Math.Max(0, simIndex1Based - 1) * Math.Max(1, step);

        private static void NormalizeProtocols(ProtocolBindingEntry entry)
        {
            var set = new List<string>();
            foreach (var raw in entry.Protocols ?? new List<string>())
            {
                if (string.Equals(raw, Iec61850Protocols.Modbus, StringComparison.OrdinalIgnoreCase)
                    && !set.Contains(Iec61850Protocols.Modbus, StringComparer.OrdinalIgnoreCase))
                {
                    set.Add(Iec61850Protocols.Modbus);
                }
                else if (string.Equals(raw, Iec61850Protocols.Iec61850, StringComparison.OrdinalIgnoreCase)
                         && !set.Contains(Iec61850Protocols.Iec61850, StringComparer.OrdinalIgnoreCase))
                {
                    set.Add(Iec61850Protocols.Iec61850);
                }
            }

            if (set.Count == 0 && entry.Name.StartsWith("simEmu", StringComparison.OrdinalIgnoreCase))
            {
                set.Add(Iec61850Protocols.Modbus);
                set.Add(Iec61850Protocols.Iec61850);
            }

            entry.Protocols = set;
        }

        private static string? ResolvePath()
        {
            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;
                var path = Path.Combine(root, RelativePath);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static string FindConfigRoot()
        {
            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(Path.Combine(root, "configs")))
                    return root;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
