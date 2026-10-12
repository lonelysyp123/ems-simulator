namespace EssSimulator.EssDeviceSimModel.Model
{
    /// <summary>双绕组变压器铁损与铜损（W）。铁损随一次电压平方，铜损随负载率平方。</summary>
    public readonly record struct TransformerLossResult(double IronLossW, double CopperLossW)
    {
        public double TotalLossW => IronLossW + CopperLossW;
    }

    public static class TransformerLoss
    {
        public static double LoadRatioFromApparentKva(double apparentKva, double ratedKva) =>
            ratedKva > 1e-9 ? Math.Abs(apparentKva) / ratedKva : 0;

        public static TransformerLossResult Compute(
            double noLoadLossW,
            double loadLossW,
            double primaryVoltageV,
            double primaryNominalVoltageV,
            double loadRatio)
        {
            double ironW = noLoadLossW * Math.Pow(
                primaryVoltageV / Math.Max(primaryNominalVoltageV, 1.0), 2);
            double copperW = loadLossW * Math.Pow(loadRatio, 2);
            return new TransformerLossResult(ironW, copperW);
        }
    }
}
