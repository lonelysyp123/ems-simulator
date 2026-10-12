namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>
    /// 电流内环（平均值模型）：一阶跟踪 Iref，输出限幅 |I|≤Imax。
    /// </summary>
    public sealed class CurrentInnerLoop
    {
        private readonly double _tauSec;

        public double Output { get; private set; }
        public bool Saturated { get; private set; }

        public CurrentInnerLoop(double kp, double ki, double tauSec = 0.02)
        {
            _tauSec = Math.Max(1e-4, tauSec);
        }

        public void Reset()
        {
            Output = 0;
            Saturated = false;
        }

        public double Step(double iRef, double iMeas, double dt, double iMax)
        {
            if (dt <= 0)
                return Output;

            _ = iMeas;
            iMax = Math.Max(0, iMax);
            double iref = Math.Clamp(iRef, -iMax, iMax);
            double alpha = 1.0 - Math.Exp(-dt / _tauSec);
            Output += (iref - Output) * alpha;
            Output = Math.Clamp(Output, -iMax, iMax);
            Saturated = iMax > 1e-9 && Math.Abs(iref) >= iMax - 1e-6 && Math.Abs(Output) >= iMax - 1e-3;
            return Output;
        }
    }
}
