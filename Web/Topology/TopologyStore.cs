using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;

namespace EssSimulator.Web.Topology
{
    /// <summary>
    /// 组态工程与设备库的 JSON 文件持久化。
    /// 目录：{ContentRoot}/configs/topology/
    ///   project.json                 — 当前画布工程
    ///   projects/{id}.json           — 已保存的命名工程
    ///   runtime-mode.json            — 工程模式开关与激活工程
    ///   generated/runtime-overlay.json — 应用到仿真的运行时补丁
    ///   library/{id}.json            — 设备库
    /// </summary>
    public sealed class TopologyStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters = { new ObjectJsonConverter() }
        };

        private readonly object _gate = new();
        private readonly string _rootDir;
        private readonly string _projectPath;
        private readonly string _libraryDir;
        private readonly string _projectsDir;
        private readonly string _modePath;
        private readonly string _overlayPath;

        public TopologyStore(IWebHostEnvironment env)
        {
            _rootDir = Path.Combine(env.ContentRootPath, "configs", "topology");
            _projectPath = Path.Combine(_rootDir, "project.json");
            _libraryDir = Path.Combine(_rootDir, "library");
            _projectsDir = Path.Combine(_rootDir, "projects");
            _modePath = Path.Combine(_rootDir, "runtime-mode.json");
            _overlayPath = Path.Combine(_rootDir, "generated", "runtime-overlay.json");
            Directory.CreateDirectory(_libraryDir);
            Directory.CreateDirectory(_projectsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(_overlayPath)!);
        }

        public string RootDirectory => _rootDir;
        public string OverlayPath => _overlayPath;

        public TopologyProject LoadProject()
        {
            lock (_gate)
            {
                if (!File.Exists(_projectPath))
                    return new TopologyProject();

                try
                {
                    var json = File.ReadAllText(_projectPath);
                    var project = JsonSerializer.Deserialize<TopologyProject>(json, JsonOpts) ?? new TopologyProject();
                    TopologyValidator.RefreshAcBusEnergization(project);
                    return project;
                }
                catch
                {
                    return new TopologyProject();
                }
            }
        }

        public TopologyProject SaveProject(TopologyProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            lock (_gate)
            {
                Directory.CreateDirectory(_rootDir);
                if (string.IsNullOrWhiteSpace(project.Id))
                    project.Id = "current";
                NormalizeParameters(project);
                project.UpdatedAtUtc = DateTime.UtcNow;
                TopologyValidator.RefreshAcBusEnergization(project);
                var json = JsonSerializer.Serialize(project, JsonOpts);
                File.WriteAllText(_projectPath, json);
                // 同步到命名工程目录，供系统配置下拉选择
                SaveNamedProjectUnlocked(project);
                return project;
            }
        }

        public IReadOnlyList<TopologyProjectSummary> ListProjects()
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_projectsDir);
                var list = new List<TopologyProjectSummary>();
                foreach (var path in Directory.EnumerateFiles(_projectsDir, "*.json"))
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<TopologyProject>(File.ReadAllText(path), JsonOpts);
                        if (p == null) continue;
                        list.Add(ToSummary(p));
                    }
                    catch { /* skip */ }
                }

                // 若当前画布工程尚未入 projects，也列出来
                if (File.Exists(_projectPath))
                {
                    try
                    {
                        var cur = JsonSerializer.Deserialize<TopologyProject>(File.ReadAllText(_projectPath), JsonOpts);
                        if (cur != null && list.All(x => x.Id != cur.Id))
                            list.Add(ToSummary(cur));
                    }
                    catch { /* skip */ }
                }

                return list.OrderByDescending(x => x.UpdatedAtUtc).ToList();
            }
        }

        public TopologyProject? LoadNamedProject(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_gate)
            {
                var path = ProjectPath(id);
                if (!File.Exists(path) && File.Exists(_projectPath))
                {
                    var cur = JsonSerializer.Deserialize<TopologyProject>(File.ReadAllText(_projectPath), JsonOpts);
                    if (cur != null && string.Equals(cur.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        TopologyValidator.RefreshAcBusEnergization(cur);
                        return cur;
                    }
                }
                if (!File.Exists(path)) return null;
                try
                {
                    var p = JsonSerializer.Deserialize<TopologyProject>(File.ReadAllText(path), JsonOpts);
                    if (p != null) TopologyValidator.RefreshAcBusEnergization(p);
                    return p;
                }
                catch { return null; }
            }
        }

        public TopologyProject SaveNamedProject(TopologyProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            lock (_gate)
            {
                NormalizeParameters(project);
                if (string.IsNullOrWhiteSpace(project.Id))
                    project.Id = Guid.NewGuid().ToString("N");
                project.UpdatedAtUtc = DateTime.UtcNow;
                TopologyValidator.RefreshAcBusEnergization(project);
                SaveNamedProjectUnlocked(project);
                return project;
            }
        }

        /// <summary>按工程名称查找（忽略大小写与首尾空白）；用于保存时同名检测。</summary>
        public TopologyProjectSummary? FindProjectByName(string name, string? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var needle = name.Trim();
            return ListProjects().FirstOrDefault(p =>
                string.Equals(p.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase) &&
                (excludeId == null || !string.Equals(p.Id, excludeId, StringComparison.OrdinalIgnoreCase)));
        }

        public bool DeleteNamedProject(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_gate)
            {
                var path = ProjectPath(id);
                var deleted = false;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted = true;
                }

                // 若删除的是当前画布工程，清空画布文件
                if (File.Exists(_projectPath))
                {
                    try
                    {
                        var cur = JsonSerializer.Deserialize<TopologyProject>(File.ReadAllText(_projectPath), JsonOpts);
                        if (cur != null && string.Equals(cur.Id, id, StringComparison.OrdinalIgnoreCase))
                            File.Delete(_projectPath);
                    }
                    catch { /* ignore */ }
                }

                // 若删除的是激活运行工程，清除引用
                var mode = LoadRuntimeModeUnlocked();
                if (mode.EngineeringMode &&
                    string.Equals(mode.ActiveProjectId, id, StringComparison.OrdinalIgnoreCase))
                {
                    mode.ActiveProjectId = null;
                    mode.ActiveProjectName = null;
                    SaveRuntimeModeUnlocked(mode);
                }

                return deleted;
            }
        }

        /// <summary>
        /// 复制命名工程为新工程：新 Id，副本名默认「原名-副本」，
        /// 名称冲突时自动追加序号（-副本 2、-副本 3 …）。
        /// </summary>
        public TopologyProject CopyNamedProject(string id, string? newName = null)
        {
            var source = LoadNamedProject(id);
            if (source == null)
                throw new InvalidOperationException("待复制的工程不存在");

            var sourceName = string.IsNullOrWhiteSpace(source.Name) ? source.Id : source.Name.Trim();
            var baseName = string.IsNullOrWhiteSpace(newName) ? $"{sourceName}-副本" : newName.Trim();
            var name = baseName;
            for (int suffix = 2; FindProjectByName(name) != null; suffix++)
                name = $"{baseName} {suffix}";

            // LoadNamedProject 每次从 JSON 反序列化，Nodes/Edges 已是深拷贝
            var copy = new TopologyProject
            {
                SchemaVersion = source.SchemaVersion,
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Nodes = source.Nodes,
                Edges = source.Edges
            };
            return SaveNamedProject(copy);
        }

        /// <summary>将命名工程载入当前画布（供「修改」进入组态编辑）。</summary>
        public TopologyProject? OpenNamedProject(string id)
        {
            var project = LoadNamedProject(id);
            if (project == null) return null;
            return SaveProject(project);
        }

        /// <summary>新建空工程并写入当前画布（暂不入库 projects/，待用户首次保存）。</summary>
        public TopologyProject CreateEmptyProject(string? name = null)
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_rootDir);
                var project = new TopologyProject
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(name) ? "未命名工程" : name.Trim(),
                    Nodes = new List<TopologyNode>(),
                    Edges = new List<TopologyEdge>(),
                    UpdatedAtUtc = DateTime.UtcNow
                };
                NormalizeParameters(project);
                File.WriteAllText(_projectPath, JsonSerializer.Serialize(project, JsonOpts));
                return project;
            }
        }

        public TopologyRuntimeMode LoadRuntimeMode()
        {
            lock (_gate)
                return LoadRuntimeModeUnlocked();
        }

        public TopologyRuntimeMode SaveRuntimeMode(TopologyRuntimeMode mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            lock (_gate)
                return SaveRuntimeModeUnlocked(mode);
        }

        private TopologyRuntimeMode LoadRuntimeModeUnlocked()
        {
            if (!File.Exists(_modePath))
                return new TopologyRuntimeMode();
            try
            {
                return JsonSerializer.Deserialize<TopologyRuntimeMode>(File.ReadAllText(_modePath), JsonOpts)
                       ?? new TopologyRuntimeMode();
            }
            catch
            {
                return new TopologyRuntimeMode();
            }
        }

        private TopologyRuntimeMode SaveRuntimeModeUnlocked(TopologyRuntimeMode mode)
        {
            Directory.CreateDirectory(_rootDir);
            mode.UpdatedAtUtc = DateTime.UtcNow;
            File.WriteAllText(_modePath, JsonSerializer.Serialize(mode, JsonOpts));
            return mode;
        }

        public TopologyRuntimeOverlay? LoadOverlay()
        {
            lock (_gate)
            {
                if (!File.Exists(_overlayPath)) return null;
                try
                {
                    return JsonSerializer.Deserialize<TopologyRuntimeOverlay>(File.ReadAllText(_overlayPath), JsonOpts);
                }
                catch { return null; }
            }
        }

        public TopologyRuntimeOverlay SaveOverlay(TopologyRuntimeOverlay overlay)
        {
            if (overlay == null) throw new ArgumentNullException(nameof(overlay));
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_overlayPath)!);
                overlay.GeneratedAtUtc = DateTime.UtcNow;
                File.WriteAllText(_overlayPath, JsonSerializer.Serialize(overlay, JsonOpts));
                return overlay;
            }
        }

        public void ClearOverlay()
        {
            lock (_gate)
            {
                if (File.Exists(_overlayPath))
                    File.Delete(_overlayPath);
            }
        }

        private void SaveNamedProjectUnlocked(TopologyProject project)
        {
            Directory.CreateDirectory(_projectsDir);
            if (string.IsNullOrWhiteSpace(project.Id))
                project.Id = Guid.NewGuid().ToString("N");
            File.WriteAllText(ProjectPath(project.Id), JsonSerializer.Serialize(project, JsonOpts));
        }

        private string ProjectPath(string id) =>
            Path.Combine(_projectsDir, Path.GetFileName(id) + ".json");

        private static TopologyProjectSummary ToSummary(TopologyProject p) => new()
        {
            Id = p.Id,
            Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
            UpdatedAtUtc = p.UpdatedAtUtc,
            NodeCount = p.Nodes?.Count ?? 0,
            EmuCount = p.Nodes?.Count(n => n.TemplateId == "emu") ?? 0,
            PvCount = p.Nodes?.Count(n => n.TemplateId == "pv_unit") ?? 0
        };

        private static void NormalizeParameters(TopologyProject project)
        {
            foreach (var node in project.Nodes)
                node.Parameters = NormalizeDict(node.Parameters);
            MigrateMeterUnifiedPorts(project);
            PruneInvalidEdges(project);
        }

        /// <summary>
        /// 旧版电表下方 CT 口并入上方统一 PT/CT 口：ct_a→pt_a 等同相映射，再去重。
        /// </summary>
        private static void MigrateMeterUnifiedPorts(TopologyProject project)
        {
            if (project.Edges == null || project.Edges.Count == 0) return;
            static string? MapLegacyMeterPort(string? portId) => portId switch
            {
                "ct_a" => "pt_a",
                "ct_b" => "pt_b",
                "ct_c" => "pt_c",
                _ => null
            };

            var meterIds = new HashSet<string>(
                project.Nodes.Where(n => n.TemplateId == "ac_meter").Select(n => n.Id));
            foreach (var e in project.Edges)
            {
                if (meterIds.Contains(e.FromNodeId))
                {
                    var mapped = MapLegacyMeterPort(e.FromPortId);
                    if (mapped != null) e.FromPortId = mapped;
                }
                if (meterIds.Contains(e.ToNodeId))
                {
                    var mapped = MapLegacyMeterPort(e.ToPortId);
                    if (mapped != null) e.ToPortId = mapped;
                }
            }

            // 同一端口对可能因 PT+CT 双线并存而重复，保留一条
            var seen = new HashSet<string>(StringComparer.Ordinal);
            project.Edges.RemoveAll(e =>
            {
                var a = $"{e.FromNodeId}|{e.FromPortId}|{e.ToNodeId}|{e.ToPortId}";
                var b = $"{e.ToNodeId}|{e.ToPortId}|{e.FromNodeId}|{e.FromPortId}";
                if (seen.Contains(a) || seen.Contains(b)) return true;
                seen.Add(a);
                return false;
            });
        }

        /// <summary>去掉仍指向无效端口的连线。</summary>
        private static void PruneInvalidEdges(TopologyProject project)
        {
            if (project.Edges == null || project.Edges.Count == 0) return;
            var byId = project.Nodes.ToDictionary(n => n.Id);
            project.Edges.RemoveAll(e =>
            {
                if (!byId.TryGetValue(e.FromNodeId, out var from) || !byId.TryGetValue(e.ToNodeId, out var to))
                    return true;
                var fromTpl = TopologyTemplates.Get(from.TemplateId);
                var toTpl = TopologyTemplates.Get(to.TemplateId);
                if (fromTpl == null || toTpl == null) return true;
                return fromTpl.Ports.All(p => p.Id != e.FromPortId)
                       || toTpl.Ports.All(p => p.Id != e.ToPortId);
            });
        }

        private static Dictionary<string, object?> NormalizeDict(Dictionary<string, object?>? src)
        {
            var dst = new Dictionary<string, object?>();
            if (src == null) return dst;
            foreach (var kv in src)
                dst[kv.Key] = NormalizeValue(kv.Value);
            return dst;
        }

        private static object? NormalizeValue(object? raw)
        {
            if (raw is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                    JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText(), JsonOpts),
                    JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(je.GetRawText(), JsonOpts),
                    _ => je.ToString()
                };
            }
            return raw;
        }

        public IReadOnlyList<TopologyLibraryItem> ListLibrary()
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_libraryDir);
                var list = new List<TopologyLibraryItem>();
                foreach (var path in Directory.EnumerateFiles(_libraryDir, "*.json"))
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<TopologyLibraryItem>(File.ReadAllText(path), JsonOpts);
                        if (item != null) list.Add(item);
                    }
                    catch
                    {
                        /* skip corrupt */
                    }
                }

                return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public TopologyLibraryItem? GetLibraryItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_gate)
            {
                var path = LibraryPath(id);
                if (!File.Exists(path)) return null;
                try
                {
                    return JsonSerializer.Deserialize<TopologyLibraryItem>(File.ReadAllText(path), JsonOpts);
                }
                catch
                {
                    return null;
                }
            }
        }

        public TopologyLibraryItem SaveLibraryItem(TopologyLibraryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_gate)
            {
                Directory.CreateDirectory(_libraryDir);
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(item.Name))
                    item.Name = item.IsComposite ? "未命名组合" : "未命名设备";

                if (item.IsComposite)
                    NormalizeCompositeLibraryItem(item);
                else
                    NormalizeDeviceLibraryItem(item);

                item.UpdatedAtUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(item, JsonOpts);
                File.WriteAllText(LibraryPath(item.Id), json);
                return item;
            }
        }

        private static void NormalizeDeviceLibraryItem(TopologyLibraryItem item)
        {
            item.Kind = "device";
            item.Nodes = new List<TopologyNode>();
            item.Edges = new List<TopologyEdge>();
            if (string.IsNullOrWhiteSpace(item.TemplateId))
                throw new ArgumentException("TemplateId 不能为空", nameof(item));
            if (TopologyTemplates.Get(item.TemplateId) == null)
                throw new ArgumentException($"未知模板: {item.TemplateId}", nameof(item));
            item.Parameters = NormalizeDict(item.Parameters);
        }

        private static readonly string[] LibraryStripKeys = { "emuId", "groupId" };

        private static void NormalizeCompositeLibraryItem(TopologyLibraryItem item)
        {
            item.Kind = "composite";
            item.Nodes ??= new List<TopologyNode>();
            item.Edges ??= new List<TopologyEdge>();
            if (item.Nodes.Count < 2)
                throw new ArgumentException("组合图元至少需要 2 个设备", nameof(item));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in item.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id))
                    throw new ArgumentException("组合图元设备缺少 Id", nameof(item));
                if (!ids.Add(node.Id))
                    throw new ArgumentException($"组合图元设备 Id 重复: {node.Id}", nameof(item));
                if (string.IsNullOrWhiteSpace(node.TemplateId) || TopologyTemplates.Get(node.TemplateId) == null)
                    throw new ArgumentException($"组合图元含未知模板: {node.TemplateId}", nameof(item));
                node.Parameters = StripLibraryBinding(NormalizeDict(node.Parameters));
            }

            item.Edges = item.Edges
                .Where(e => e != null && ids.Contains(e.FromNodeId) && ids.Contains(e.ToNodeId))
                .ToList()!;

            if (string.IsNullOrWhiteSpace(item.TemplateId) || TopologyTemplates.Get(item.TemplateId) == null)
                item.TemplateId = item.Nodes[0].TemplateId;
            item.Parameters = NormalizeDict(item.Parameters);
        }

        private static Dictionary<string, object?> StripLibraryBinding(Dictionary<string, object?> parameters)
        {
            foreach (var key in LibraryStripKeys)
                parameters.Remove(key);
            return parameters;
        }

        public bool DeleteLibraryItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_gate)
            {
                var path = LibraryPath(id);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        private string LibraryPath(string id)
        {
            var safe = Path.GetFileName(id);
            return Path.Combine(_libraryDir, safe + ".json");
        }
    }

    /// <summary>
    /// 将 Dictionary&lt;string, object?&gt; 反序列化为可读写的标量/嵌套结构，避免裸 JsonElement 难以处理。
    /// </summary>
    internal sealed class ObjectJsonConverter : JsonConverter<object?>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ReadValue(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    writer.WriteNumberValue(Convert.ToInt64(value));
                    break;
                case float f:
                    writer.WriteNumberValue(f);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case decimal m:
                    writer.WriteNumberValue(m);
                    break;
                case Dictionary<string, object?> dict:
                    writer.WriteStartObject();
                    foreach (var kv in dict)
                    {
                        writer.WritePropertyName(kv.Key);
                        Write(writer, kv.Value, options);
                    }
                    writer.WriteEndObject();
                    break;
                case IEnumerable<object?> list when value is not string:
                    writer.WriteStartArray();
                    foreach (var item in list)
                        Write(writer, item, options);
                    writer.WriteEndArray();
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }

        private static object? ReadValue(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Null: return null;
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var l)) return l;
                    return reader.GetDouble();
                case JsonTokenType.StartObject:
                {
                    var dict = new Dictionary<string, object?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        var key = reader.GetString()!;
                        reader.Read();
                        dict[key] = ReadValue(ref reader);
                    }
                    return dict;
                }
                case JsonTokenType.StartArray:
                {
                    var list = new List<object?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        list.Add(ReadValue(ref reader));
                    return list;
                }
                default:
                    return null;
            }
        }
    }
}
