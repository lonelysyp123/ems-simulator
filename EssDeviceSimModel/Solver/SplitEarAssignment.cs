namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>一台双耳箱变下 PCS 通道归属（全局通道索引）。</summary>
    public sealed class SplitXfmrEarChannels
    {
        public IReadOnlyList<int> LeftChannels { get; init; } = Array.Empty<int>();
        public IReadOnlyList<int> RightChannels { get; init; } = Array.Empty<int>();
    }

    /// <summary>单元内全部双耳箱变的 PCS 通道归属（全局通道索引）。</summary>
    public sealed class SplitEarAssignment
    {
        public IReadOnlyList<SplitXfmrEarChannels> Transformers { get; init; } =
            Array.Empty<SplitXfmrEarChannels>();

        /// <summary>兼容单台箱变测例：第一台左耳通道。</summary>
        public IReadOnlyList<int> LeftChannels =>
            Transformers.Count > 0 ? Transformers[0].LeftChannels : Array.Empty<int>();

        /// <summary>兼容单台箱变测例：第一台右耳通道。</summary>
        public IReadOnlyList<int> RightChannels =>
            Transformers.Count > 0 ? Transformers[0].RightChannels : Array.Empty<int>();

        public bool TryGetEar(int globalChannel, out int xfmrIndex, out bool right)
        {
            for (int t = 0; t < Transformers.Count; t++)
            {
                var xfmr = Transformers[t];
                if (xfmr.RightChannels.Contains(globalChannel))
                {
                    xfmrIndex = t;
                    right = true;
                    return true;
                }

                if (xfmr.LeftChannels.Contains(globalChannel))
                {
                    xfmrIndex = t;
                    right = false;
                    return true;
                }
            }

            xfmrIndex = 0;
            right = false;
            return false;
        }
    }
}
