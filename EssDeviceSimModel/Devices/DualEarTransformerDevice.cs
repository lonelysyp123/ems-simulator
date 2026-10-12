using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    /// <summary>
    /// 双耳箱变：高压一口、低压左右耳。穿越损耗/变比委托内部两绕组 <see cref="Through"/>；
    /// 两耳功率独立，二次电压共用穿越结果。本期不做无功不平衡保护。
    /// </summary>
    public sealed class DualEarTransformerDevice : IElectricalDevice
    {
        public DualEarTransformerDevice(string deviceId, DualEarTransformerConfig config)
        {
            DeviceId = deviceId;
            Config = config;
            Through = TransformerDeviceFactory.Create(deviceId, config.ToThroughConfig());
            SecondaryLeft = CreateEarPort("secondary_left", config.SecondaryConnection);
            SecondaryRight = CreateEarPort("secondary_right", config.SecondaryConnection);
        }

        public string DeviceId { get; }
        public ElectricalDeviceKind Kind => ElectricalDeviceKind.Transformer;
        public DualEarTransformerConfig Config { get; }
        /// <summary>穿越两绕组模型，供 GUI / 岛同步沿用单元变路径。</summary>
        public TransformerDevice Through { get; }
        public ElectricalPort Primary => Through.Primary;
        public ElectricalPort SecondaryLeft { get; }
        public ElectricalPort SecondaryRight { get; }
        public IReadOnlyList<ElectricalPort> Ports => new[] { Primary, SecondaryLeft, SecondaryRight };

        public double TurnsRatio => Through.TurnsRatio;
        public double ActivePowerKwLeft { get; private set; }
        public double ActivePowerKwRight { get; private set; }
        public double ReactiveKvarLeft { get; private set; }
        public double ReactiveKvarRight { get; private set; }
        public double CirculatingCurrentA { get; private set; }

        public void Step(DeviceStepContext context, TimeSpan step)
        {
            var priIn = AcPortHelper.ReadAcInput(Primary);
            var leftIn = AcPortHelper.ReadAcInput(SecondaryLeft);
            var rightIn = AcPortHelper.ReadAcInput(SecondaryRight);

            ActivePowerKwLeft = leftIn.ActivePowerKw;
            ActivePowerKwRight = rightIn.ActivePowerKw;
            ReactiveKvarLeft = leftIn.ReactivePowerKvar;
            ReactiveKvarRight = rightIn.ReactivePowerKvar;

            double p = leftIn.ActivePowerKw + rightIn.ActivePowerKw;
            double q = leftIn.ReactivePowerKvar + rightIn.ReactivePowerKvar;
            double vSecHint = leftIn.LineVoltageV > 1.0
                ? leftIn.LineVoltageV
                : (rightIn.LineVoltageV > 1.0 ? rightIn.LineVoltageV : Config.SecondaryNominalLineVoltageV);

            var combined = AcQuantityConverter.FromLineVoltageAndPower(
                vSecHint, p, q, Config.SecondaryConnection, priIn.FrequencyHz > 1 ? priIn.FrequencyHz : 50);

            Through.Primary.Input = ElectricalPortSnapshot.FromAc(priIn);
            Through.Secondary.Input = ElectricalPortSnapshot.FromAc(combined);
            Through.Step(context, step);

            double vSec = Through.Secondary.Output.Ac?.Internal.LineVoltageV ?? 0;
            double freq = vSec > 1.0 ? (priIn.FrequencyHz > 1 ? priIn.FrequencyHz : 50) : 0;
            AcPortHelper.WriteAcOutput(SecondaryLeft, AcQuantityConverter.FromLineVoltageAndPower(
                vSec, leftIn.ActivePowerKw, leftIn.ReactivePowerKvar, Config.SecondaryConnection, freq));
            AcPortHelper.WriteAcOutput(SecondaryRight, AcQuantityConverter.FromLineVoltageAndPower(
                vSec, rightIn.ActivePowerKw, rightIn.ReactivePowerKvar, Config.SecondaryConnection, freq));

            CirculatingCurrentA = EstimateCirculatingCurrentA(vSec);
        }

        public void WriteDeenergizedPorts()
        {
            Through.WriteDeenergizedPorts();
            var empty = new AcInternalQuantities { Connection = Config.SecondaryConnection };
            AcPortHelper.WriteAcOutput(SecondaryLeft, empty);
            AcPortHelper.WriteAcOutput(SecondaryRight, empty);
            ActivePowerKwLeft = ActivePowerKwRight = 0;
            ReactiveKvarLeft = ReactiveKvarRight = 0;
            CirculatingCurrentA = 0;
        }

        private double EstimateCirculatingCurrentA(double secondaryLineVoltageV)
        {
            if (secondaryLineVoltageV < 1.0)
                return 0;
            double zSplitPu = Math.Max(Config.SplitImpedancePercent, 0.1) / 100.0;
            double deltaQ = Math.Abs(ReactiveKvarLeft - ReactiveKvarRight);
            if (deltaQ < 1e-6)
                return 0;
            double denom = Math.Sqrt(3.0) * secondaryLineVoltageV * zSplitPu;
            return denom > 1e-9 ? deltaQ * 1000.0 / denom : 0;
        }

        private static ElectricalPort CreateEarPort(string portId, ThreePhaseConnection connection)
        {
            var empty = new AcInternalQuantities { Connection = connection };
            return new ElectricalPort
            {
                PortId = portId,
                Kind = PortKind.SeriesDownstream,
                Input = ElectricalPortSnapshot.FromAc(empty),
                Output = ElectricalPortSnapshot.FromAc(empty)
            };
        }
    }
}
