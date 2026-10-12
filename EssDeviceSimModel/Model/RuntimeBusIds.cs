namespace EssSimulator.EssDeviceSimModel.Model
{
    /// <summary>径向运行时母线 Id，组态抽头与求解器共用。</summary>
    public static class RuntimeBusIds
    {
        public const string Grid = "BUS_GRID";
        public const string AfterMainBreaker = "BUS_AFTER_MAIN_BRK";
        public const string Station35 = "BUS_35";

        /// <summary>历史径向图 Id，等价于 <see cref="AfterMainBreaker"/>。</summary>
        public const string LegacyAfterMainBreaker = "BUS_MAIN_SEC";

        public static string Unit690(int unitIndex) => $"BUS_690_U{unitIndex}";
        public static string Unit690Left(int unitIndex) => Unit690Ear(unitIndex, 0, right: false);
        public static string Unit690Right(int unitIndex) => Unit690Ear(unitIndex, 0, right: true);

        /// <summary>
        /// 双耳箱变低压母线。第 0 台保持 <c>BUS_690_U{u}_L/R</c>；第 1 台为 <c>BUS_690_U{u}_T1_L/R</c>。
        /// </summary>
        public static string Unit690Ear(int unitIndex, int xfmrIndex, bool right)
        {
            string side = right ? "R" : "L";
            return xfmrIndex <= 0
                ? $"BUS_690_U{unitIndex}_{side}"
                : $"BUS_690_U{unitIndex}_T{xfmrIndex}_{side}";
        }

        /// <summary>把历史别名收成规范 Id；空或未知 Id 原样返回。</summary>
        public static string Canonicalize(string? busId)
        {
            if (string.IsNullOrWhiteSpace(busId))
                return busId ?? string.Empty;
            if (string.Equals(busId, LegacyAfterMainBreaker, StringComparison.Ordinal))
                return AfterMainBreaker;
            return busId;
        }

        public static bool TryParseUnit690(string busId, out int unitIndex)
        {
            unitIndex = -1;
            const string prefix = "BUS_690_U";
            if (string.IsNullOrWhiteSpace(busId) || !busId.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            return int.TryParse(busId.AsSpan(prefix.Length), out unitIndex) && unitIndex >= 0;
        }

        public static bool TryParseUnit690Ear(string busId, out int unitIndex, out bool rightEar) =>
            TryParseUnit690Ear(busId, out unitIndex, out _, out rightEar);

        public static bool TryParseUnit690Ear(string busId, out int unitIndex, out int xfmrIndex, out bool rightEar)
        {
            unitIndex = -1;
            xfmrIndex = 0;
            rightEar = false;
            const string prefix = "BUS_690_U";
            if (string.IsNullOrWhiteSpace(busId) || !busId.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            var rest = busId.AsSpan(prefix.Length);
            if (rest.EndsWith("_L", StringComparison.Ordinal))
                rightEar = false;
            else if (rest.EndsWith("_R", StringComparison.Ordinal))
                rightEar = true;
            else
                return false;

            var body = rest[..^2];
            int tAt = body.LastIndexOf("_T");
            if (tAt >= 0)
            {
                if (!int.TryParse(body[..tAt], out unitIndex) || unitIndex < 0)
                    return false;
                return int.TryParse(body[(tAt + 2)..], out xfmrIndex) && xfmrIndex >= 0;
            }

            xfmrIndex = 0;
            return int.TryParse(body, out unitIndex) && unitIndex >= 0;
        }
    }
}
