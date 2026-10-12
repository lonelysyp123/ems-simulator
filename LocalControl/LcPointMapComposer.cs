using EssSimulator.Protocol.Modbus;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// 扫描 <c>pointmaps/models/lc/*/</c> 下互补片段（跳过 <c>role=exclusive</c> 互斥整表），
    /// 按组展开后拼成一张 LC 点表。地址或 ParamName 冲突则失败。
    /// unit 片段的展开次数取 <c>model.json</c> 的 <c>pairCount</c>，不跟随组数。
    /// </summary>
    internal static class LcPointMapComposer
    {
        public const string LcTypeDir = "lc";

        public static List<string> ListFragmentPaths(string modelsRoot, int maxPcsPerGroup = 0) =>
            ListFragmentModels(modelsRoot, maxPcsPerGroup)
                .Select(m => Path.Combine(m.Directory, "lc.csv"))
                .ToList();

        internal static List<DeviceModelInfo> ListFragmentModels(string modelsRoot, int maxPcsPerGroup = 0)
        {
            var lcRoot = Path.Combine(modelsRoot, LcTypeDir);
            if (!Directory.Exists(lcRoot))
                return new List<DeviceModelInfo>();

            return DeviceModelRegistry.ListModels(lcRoot)
                .Where(m => !DeviceModelRegistry.IsExclusiveModel(m))
                .Where(m => m.MaxPcsPerGroup <= 0 || maxPcsPerGroup <= 0 || maxPcsPerGroup <= m.MaxPcsPerGroup)
                .Where(m => File.Exists(Path.Combine(m.Directory, "lc.csv")))
                .OrderBy(m => m.Directory, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<MapEntry> ComposeFromRepo(int groupCount, int maxPcsPerGroup = 0)
        {
            var root = DeviceModelRegistry.FindModelsRoot()
                ?? throw new InvalidOperationException("找不到 pointmaps/models，无法拼装 LC 点表");
            return Compose(Path.Combine(root, DeviceModelRegistry.ModelsRelativeDir), groupCount, maxPcsPerGroup);
        }

        public static List<MapEntry> Compose(string modelsRoot, int groupCount, int maxPcsPerGroup = 0)
        {
            if (groupCount > LcLayout.MaxGroupCount)
                throw new InvalidOperationException(
                    $"LC 组数 {groupCount} 超过安全上限 {LcLayout.MaxGroupCount}（组≥21 时单元遥测与组遥测、组遥控与 mv_param1 地址重叠）");

            var models = ListFragmentModels(modelsRoot, maxPcsPerGroup);
            if (models.Count == 0)
                throw new InvalidOperationException($"未找到 LC 片段: {Path.Combine(modelsRoot, LcTypeDir)}");

            var merged = new List<MapEntry>();
            var origins = new List<(string Fragment, MapEntry Entry)>();
            foreach (var model in models)
            {
                string fragment = Path.GetFileName(model.Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    ?? model.Id;
                int expandN = model.PairCount > 0 ? model.PairCount : groupCount;
                var expanded = LcPointMapExpander.ExpandFile(Path.Combine(model.Directory, "lc.csv"), expandN);
                foreach (var entry in expanded)
                {
                    merged.Add(entry);
                    origins.Add((fragment, entry));
                }
            }

            var errors = DetectConflicts(origins);
            if (errors.Count > 0)
                throw new InvalidOperationException("LC 点表拼装冲突：\n" + string.Join("\n", errors));

            return merged;
        }

        internal static List<string> DetectConflicts(IReadOnlyList<(string Fragment, MapEntry Entry)> origins)
        {
            var errors = new List<string>();
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fragment, entry) in origins)
            {
                var name = entry.ParamName ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (byName.TryGetValue(name, out var first))
                    errors.Add($"ParamName '{name}' 重复: {first} 与 {fragment}");
                else
                    byName[name] = fragment;
            }

            var spans = new List<(string Fragment, string Param, int Space, int Start, int End)>();
            foreach (var (fragment, entry) in origins)
            {
                int space = AddressOverlapValidator.RegisterSpaceOf(entry.FunctionCode);
                if (space < 0)
                    continue;
                int len = Math.Max(1, entry.Size / 16);
                spans.Add((fragment, entry.ParamName ?? "", space, entry.Address, entry.Address + len));
            }

            var ordered = spans.OrderBy(s => s.Space).ThenBy(s => s.Start).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    var a = ordered[i];
                    var b = ordered[j];
                    if (a.Space != b.Space)
                        break;
                    if (a.Start >= b.End || b.Start >= a.End)
                        continue;
                    errors.Add(
                        $"地址 {Math.Max(a.Start, b.Start)} 冲突: {a.Fragment}.{a.Param} 与 {b.Fragment}.{b.Param}");
                }
            }

            return errors;
        }
    }
}
