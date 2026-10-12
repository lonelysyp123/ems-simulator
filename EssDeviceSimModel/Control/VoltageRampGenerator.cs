namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>离散电压斜坡发生器：按升降斜率将输出步进到目标，不涉及频率。</summary>
    public sealed class VoltageRampGenerator
    {
        public double UpRate { get; }
        public double DownRate { get; }
        public double Output { get; private set; }
        public double Target { get; set; }

        public VoltageRampGenerator(double upRate, double downRate, double initial = 0)
        {
            UpRate = Math.Max(0, upRate);
            DownRate = Math.Max(0, downRate);
            Output = initial;
            Target = initial;
        }

        public void Reset(double value)
        {
            Output = value;
            Target = value;
        }

        public double Step(double dt)
        {
            if (dt <= 0)
                return Output;

            double error = Target - Output;
            if (error == 0)
                return Output;

            double rate = error > 0 ? UpRate : DownRate;
            double maxDelta = rate * dt;
            if (maxDelta >= Math.Abs(error))
                Output = Target;
            else
                Output += Math.Sign(error) * maxDelta;

            return Output;
        }
    }
}
