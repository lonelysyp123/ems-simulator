namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>电压外环：Vref−Vmeas → Iref，限幅与电流内环 Imax 一致。无频率项。</summary>
    public sealed class VoltageOuterLoop
    {
        private readonly DiscretePiController _pi;

        public VoltageOuterLoop(double kp, double ki)
        {
            _pi = new DiscretePiController(kp, ki, double.NegativeInfinity, double.PositiveInfinity);
        }

        public void Reset() => _pi.Reset();

        internal double Integral => _pi.Integral;
        internal double Output => _pi.Output;

        internal void Restore(double integral, double output) => _pi.Restore(integral, output);

        public double Step(double vRef, double vMeas, double dt, double iMax)
        {
            iMax = Math.Max(0, iMax);
            _pi.SetOutputLimits(-iMax, iMax);
            if (dt <= 0)
                return Math.Clamp(_pi.Output, -iMax, iMax);

            return _pi.Step(vRef - vMeas, dt);
        }
    }
}
