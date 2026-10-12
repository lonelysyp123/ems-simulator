using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Solver
{
    /// <summary>
    /// 解析当前仿真步的系统唯一频率源（Hz）：
    /// 主断合且电网有压 → 电网额定频率；主断分且 PCS 构网 → 构网 PCS 频率平均；否则 0。
    /// </summary>
    public static class SystemFrequencyResolver
    {
        public static double Resolve(ElectricalNetwork network, DeviceStepContext context)
        {
            if (context.MainBreakerClosed)
            {
                double gridV = network.Grid.Port.Output.Ac?.Internal.LineVoltageV ?? 0;
                if (gridV <= 1.0)
                    return 0;
                return network.Grid.NominalFrequencyHz;
            }

            double sumF = 0;
            int n = 0;
            foreach (var pcs in network.PcsDevices)
            {
                if (!pcs.TryGetIslandBusVoltageInjection(out var v, out var f))
                    continue;
                if (v <= 1.0 || f <= 1.0)
                    continue;

                sumF += f;
                n++;
            }

            return n > 0 ? sumF / n : 0;
        }

        public static void Refresh(ElectricalNetwork network, DeviceStepContext context) =>
            network.SystemFrequencyHz = Resolve(network, context);
    }
}
