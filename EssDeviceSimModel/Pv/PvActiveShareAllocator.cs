namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 一台逆变器的有功分配输入。AvailableKw 是本步可发上限，调用方已取 min(额定, 交流可发)。
    /// </summary>
    public readonly record struct PvActiveShareRequest(double CommandedKw, double AvailableKw, bool Participating);

    /// <summary>
    /// 按各台设定分配有功。发不够的差额均分给仍有余量的参与者，不参与者输出 0 且不转出设定。
    /// </summary>
    public static class PvActiveShareAllocator
    {
        public static double[] Allocate(IReadOnlyList<PvActiveShareRequest> requests)
        {
            int n = requests.Count;
            var assigned = new double[n];
            var headroom = new double[n];
            double residual = 0;
            for (int i = 0; i < n; i++)
            {
                PvActiveShareRequest request = requests[i];
                if (!request.Participating)
                    continue;

                double cap = Math.Max(0, request.AvailableKw);
                double commanded = Math.Max(0, request.CommandedKw);
                double give = Math.Min(commanded, cap);
                assigned[i] = give;
                headroom[i] = cap - give;
                residual += commanded - give;
            }

            for (int round = 0; round < n && residual > 1e-9; round++)
            {
                int receivers = 0;
                for (int i = 0; i < n; i++)
                {
                    if (headroom[i] > 1e-9)
                        receivers++;
                }

                if (receivers == 0)
                    break;

                double share = residual / receivers;
                double moved = 0;
                for (int i = 0; i < n; i++)
                {
                    if (headroom[i] <= 1e-9)
                        continue;
                    double give = Math.Min(share, headroom[i]);
                    assigned[i] += give;
                    headroom[i] -= give;
                    moved += give;
                }

                if (moved <= 1e-9)
                    break;
                residual -= moved;
            }

            return assigned;
        }
    }
}
