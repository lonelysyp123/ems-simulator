namespace EssSimulator.EssDeviceSimModel.Control
{
    /// <summary>
    /// 标量 PLL：母线电压高于使能门槛后跟踪 V/f/θ。捕获瞬间对齐参考，再一阶跟随。
    /// </summary>
    public sealed class PllTracker
    {
        private readonly double _tauSec;

        public bool Enabled { get; private set; }
        public double VoltageV { get; private set; }
        public double FrequencyHz { get; private set; }
        public double ThetaRad { get; private set; }

        public PllTracker(double tauSec = 0.1)
        {
            _tauSec = Math.Max(1e-4, tauSec);
        }

        public void Reset()
        {
            Enabled = false;
            VoltageV = 0;
            FrequencyHz = 0;
            ThetaRad = 0;
        }

        public void Step(double busV, double busF, double busTheta, double enableV, double dt)
        {
            if (busV < enableV)
            {
                Enabled = false;
                return;
            }

            if (!Enabled)
            {
                Enabled = true;
                VoltageV = busV;
                FrequencyHz = busF;
                ThetaRad = PhaseIntegrator.WrapToPi(busTheta);
                return;
            }

            if (dt <= 0)
                return;

            double alpha = 1.0 - Math.Exp(-dt / _tauSec);
            VoltageV += (busV - VoltageV) * alpha;
            FrequencyHz += (busF - FrequencyHz) * alpha;
            double err = PhaseIntegrator.AngleErrorRad(busTheta, ThetaRad);
            ThetaRad = PhaseIntegrator.WrapToPi(ThetaRad + err * alpha);
        }
    }
}
