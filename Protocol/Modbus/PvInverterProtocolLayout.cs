using EssSimulator.Configuration;

namespace EssSimulator.Protocol.Modbus;

internal static class PvInverterProtocolLayout
{
    public readonly record struct Endpoint(int SimIndex1Based, int UnitIndex0, int InverterIndex0)
    {
        public string ServerName => $"simPvInv{SimIndex1Based}";
        public int UnitId => UnitIndex0 + 1;
    }

    public static IReadOnlyList<Endpoint> Enumerate(SimulatorConfig cfg)
    {
        var endpoints = new List<Endpoint>();
        int sim = 1;
        for (int u = 0; u < cfg.PvUnitCount; u++)
        {
            // 与 PvUnitDevice.ToConfig 的实际设备数量保持一致。
            int count = Math.Max(1, cfg.PvUnits[u].InverterCount);
            for (int i = 0; i < count; i++)
                endpoints.Add(new Endpoint(sim++, u, i));
        }

        return endpoints;
    }
}
