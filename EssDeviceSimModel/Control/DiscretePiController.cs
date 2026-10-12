namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>离散 PI，输出限幅 + 积分抗饱和。无 I/O。</summary>
    public sealed class DiscretePiController
    {
        public double Kp { get; }
        public double Ki { get; }
        public double OutMin { get; private set; }
        public double OutMax { get; private set; }
        public double Integral { get; private set; }
        public double Output { get; private set; }

        public DiscretePiController(double kp, double ki, double outMin, double outMax)
        {
            Kp = Math.Max(0, kp);
            Ki = Math.Max(0, ki);
            SetOutputLimits(outMin, outMax);
        }

        public void SetOutputLimits(double outMin, double outMax)
        {
            if (outMax < outMin)
                (outMin, outMax) = (outMax, outMin);
            OutMin = outMin;
            OutMax = outMax;
            Output = Math.Clamp(Output, OutMin, OutMax);
            Integral = Math.Clamp(Integral, OutMin, OutMax);
        }

        public void Reset()
        {
            Integral = 0;
            Output = 0;
        }

        public double Step(double error, double dt)
        {
            if (dt <= 0)
                return Output;

            double p = Kp * error;
            double iCandidate = Integral + Ki * error * dt;
            double unsat = p + iCandidate;
            double sat = Math.Clamp(unsat, OutMin, OutMax);
            if (Math.Abs(unsat - sat) < 1e-12)
                Integral = iCandidate;
            Output = sat;
            return Output;
        }
    }
}
