namespace EssSimulator.LocalControl
{
    /// <summary>LC CSV 模板行：Address / ParamName 可为含 <c>n</c> 的表达式。</summary>
    internal sealed class LcTemplateEntry
    {
        public int FunctionCode { get; set; }
        public string Address { get; set; } = "";
        public string? Type { get; set; }
        public int Size { get; set; }
        public string? ParamName { get; set; }
        public int Scale { get; set; }
        public string? Description { get; set; }
        public string? ModelSim { get; set; }
    }

    /// <summary>
    /// 将单组 LC 模板按组序号 n=1..G 展开为 Modbus 点表。
    /// 不含 <c>n</c> 的行视为单元级，只出现一次。
    /// </summary>
    internal static class LcPointMapExpander
    {
        public static List<LcTemplateEntry> LoadTemplate(string csvPath)
        {
            var rows = CSVUtil.CSV2Class<LcTemplateEntry>(csvPath)
                ?? throw new Exception($"LC 模板读取失败: {csvPath}");
            return rows;
        }

        public static List<MapEntry> Expand(IReadOnlyList<LcTemplateEntry> template, int groupCount)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (groupCount < 1)
                throw new ArgumentOutOfRangeException(nameof(groupCount), "组数至少为 1");

            var result = new List<MapEntry>();
            var groupRows = new List<LcTemplateEntry>();
            bool flushedGroups = false;

            foreach (var row in template)
            {
                if (IsGroupScoped(row))
                {
                    groupRows.Add(row);
                    continue;
                }

                FlushGroups(result, groupRows, groupCount, ref flushedGroups);
                result.Add(Materialize(row, n: 1));
            }

            FlushGroups(result, groupRows, groupCount, ref flushedGroups);
            return result;
        }

        public static List<MapEntry> ExpandFile(string csvPath, int groupCount) =>
            Expand(LoadTemplate(csvPath), groupCount);

        private static void FlushGroups(
            List<MapEntry> result,
            List<LcTemplateEntry> groupRows,
            int groupCount,
            ref bool flushed)
        {
            if (flushed || groupRows.Count == 0)
                return;

            for (int n = 1; n <= groupCount; n++)
            {
                foreach (var row in groupRows)
                    result.Add(Materialize(row, n));
            }
            flushed = true;
        }

        private static bool IsGroupScoped(LcTemplateEntry row) =>
            LcAddressExpr.IsGroupScoped(row.Address)
            || LcParamNameExpr.IsGroupScoped(row.ParamName ?? "");

        private static MapEntry Materialize(LcTemplateEntry row, int n)
        {
            if (string.IsNullOrWhiteSpace(row.Address))
                throw new FormatException($"LC 模板地址为空: {row.ParamName}");
            if (string.IsNullOrWhiteSpace(row.ParamName))
                throw new FormatException("LC 模板 ParamName 为空");

            return new MapEntry
            {
                FunctionCode = row.FunctionCode,
                Address = LcAddressExpr.Evaluate(row.Address, n),
                Type = row.Type,
                Size = row.Size,
                ParamName = LcParamNameExpr.Evaluate(row.ParamName, n),
                Scale = row.Scale,
                Description = row.Description,
                ModelSim = row.ModelSim
            };
        }
    }
}
