using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// Modbus TCP 来源 IP 白名单：空名单不限制；非空时仅放行命中条目（可选放行本机回环）。
    /// 进程启动时加载一次，热重建复用内存快照，不重读磁盘。
    /// </summary>
    public sealed class ModbusIpAllowList
    {
        public const string RelativePath = "configs/modbus-allowlist.json";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>空名单、允许回环：不限制任何来源。</summary>
        public static ModbusIpAllowList Unrestricted { get; } = new(true, Array.Empty<string>(), Array.Empty<Rule>());

        private readonly Rule[] _rules;

        private ModbusIpAllowList(bool allowLoopback, IReadOnlyList<string> addresses, Rule[] rules)
        {
            AllowLoopback = allowLoopback;
            Addresses = addresses;
            _rules = rules;
        }

        /// <summary>非空名单时是否额外放行 127.0.0.1 / ::1。</summary>
        public bool AllowLoopback { get; }

        /// <summary>配置中的原始条目（精确 IP 或 CIDR），已去空白与重复。</summary>
        public IReadOnlyList<string> Addresses { get; }

        /// <summary>名单为空时不限制来源 IP。</summary>
        public bool IsUnrestricted => Addresses.Count == 0;

        public bool IsAllowed(EndPoint? endPoint) =>
            IsAllowed((endPoint as IPEndPoint)?.Address);

        public bool IsAllowed(IPAddress? address)
        {
            if (IsUnrestricted)
                return true;
            if (address == null)
                return false;

            var normalized = Normalize(address);
            if (AllowLoopback && IPAddress.IsLoopback(normalized))
                return true;

            foreach (var rule in _rules)
            {
                if (rule.Matches(normalized))
                    return true;
            }

            return false;
        }

        /// <summary>磁盘策略与当前进程快照是否一致（两份都为空名单时视为相同，忽略回环开关）。</summary>
        public bool SamePolicy(ModbusIpAllowList other)
        {
            if (other == null)
                return false;
            if (IsUnrestricted && other.IsUnrestricted)
                return true;
            if (AllowLoopback != other.AllowLoopback)
                return false;
            if (Addresses.Count != other.Addresses.Count)
                return false;
            var self = new HashSet<string>(Addresses, StringComparer.OrdinalIgnoreCase);
            return self.SetEquals(other.Addresses);
        }

        /// <summary>从 <see cref="RelativePath"/> 加载；缺失视为不限制；损坏时退回不限制并记录原因。</summary>
        public static ModbusIpAllowList Load(out string? error) =>
            LoadFromPath(ResolveExistingPath(), out error);

        internal static ModbusIpAllowList LoadFromPath(string? path, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Unrestricted;

            try
            {
                var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOpts);
                if (dto == null)
                    return Unrestricted;
                if (TryCreate(dto.AllowLoopback, dto.Addresses, out var list, out var errors))
                    return list;

                error = $"{RelativePath} 条目无效，已忽略白名单限制：{string.Join("；", errors)}";
                return Unrestricted;
            }
            catch (Exception ex)
            {
                error = $"{RelativePath} 读取失败，已忽略白名单限制：{ex.Message}";
                return Unrestricted;
            }
        }

        public static void Save(ModbusIpAllowList list) =>
            SaveToPath(ResolveOrCreatePath(), list);

        internal static void SaveToPath(string path, ModbusIpAllowList list)
        {
            ArgumentNullException.ThrowIfNull(list);
            var dto = new FileDto
            {
                AllowLoopback = list.AllowLoopback,
                Addresses = list.Addresses.ToList()
            };
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOpts));
        }

        /// <summary>校验并构造名单；非法条目进入 errors 且整体失败。</summary>
        public static bool TryCreate(
            bool allowLoopback,
            IEnumerable<string>? addresses,
            out ModbusIpAllowList list,
            out List<string> errors)
        {
            errors = new List<string>();
            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rules = new List<Rule>();

            foreach (var raw in addresses ?? Array.Empty<string>())
            {
                foreach (var piece in SplitEntries(raw))
                {
                    if (!seen.Add(piece))
                        continue;
                    if (!TryParseRule(piece, out var rule, out var reason))
                    {
                        errors.Add($"{piece}: {reason}");
                        continue;
                    }
                    unique.Add(piece);
                    rules.Add(rule);
                }
            }

            if (errors.Count > 0)
            {
                list = Unrestricted;
                return false;
            }

            list = unique.Count == 0
                ? new ModbusIpAllowList(allowLoopback, Array.Empty<string>(), Array.Empty<Rule>())
                : new ModbusIpAllowList(allowLoopback, unique, rules.ToArray());
            return true;
        }

        private static IEnumerable<string> SplitEntries(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;
            foreach (var part in raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = part.Trim();
                if (piece.Length > 0)
                    yield return piece;
            }
        }

        private static bool TryParseRule(string text, out Rule rule, out string reason)
        {
            rule = default!;
            int slash = text.IndexOf('/');
            string ipText = slash < 0 ? text : text[..slash];
            if (!IPAddress.TryParse(ipText, out var network))
            {
                reason = "不是合法 IP 或 CIDR";
                return false;
            }

            network = Normalize(network);
            int maxPrefix = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            int prefix;
            if (slash < 0)
            {
                prefix = maxPrefix;
            }
            else if (!int.TryParse(text[(slash + 1)..], out prefix) || prefix < 0 || prefix > maxPrefix)
            {
                reason = $"前缀长度须为 0-{maxPrefix}";
                return false;
            }

            rule = new Rule(network, prefix);
            reason = string.Empty;
            return true;
        }

        internal static IPAddress Normalize(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                return address.MapToIPv4();
            return address;
        }

        private static string? ResolveExistingPath()
        {
            foreach (var root in DeviceModelRegistry.CandidateRoots())
            {
                var path = Path.Combine(root, RelativePath);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static string ResolveOrCreatePath()
        {
            var existing = ResolveExistingPath();
            if (existing != null)
                return existing;

            var cwd = Directory.GetCurrentDirectory();
            var root = string.IsNullOrWhiteSpace(cwd) ? AppContext.BaseDirectory : cwd;
            return Path.Combine(root, RelativePath);
        }

        private readonly struct Rule
        {
            private readonly byte[] _network;
            private readonly int _prefix;

            public Rule(IPAddress network, int prefix)
            {
                _network = network.GetAddressBytes();
                _prefix = prefix;
            }

            public bool Matches(IPAddress address)
            {
                var bytes = address.GetAddressBytes();
                if (bytes.Length != _network.Length)
                    return false;

                int fullBytes = _prefix / 8;
                int remBits = _prefix % 8;
                for (int i = 0; i < fullBytes; i++)
                {
                    if (bytes[i] != _network[i])
                        return false;
                }

                if (remBits == 0)
                    return true;

                int mask = 0xFF << (8 - remBits);
                return (bytes[fullBytes] & mask) == (_network[fullBytes] & mask);
            }
        }

        private sealed class FileDto
        {
            public bool AllowLoopback { get; set; } = true;
            public List<string> Addresses { get; set; } = new();
        }
    }
}
