namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 单块光伏组件：单二极管 I-V，运行时对 P=V·I(V) 做 MPPT 搜索得到最大放电功率。
    /// </summary>
    public sealed class PvModuleSimulator
    {
        private const double ThermalVoltageStcV = 0.026;
        private const double MinIrradianceWm2 = 1.0;
        private const double StcAbsTempK = 298.15;

        public const double MinDegradationFactor = 0.70;

        private readonly double _stcIdealityA;

        public PvModuleSimulator(PvModuleSpec spec, double degradationFactor = 1)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            DegradationFactor = NormalizeDegradationFactor(degradationFactor);
            _stcIdealityA = FitIdealityFactorA(spec);
        }

        public PvModuleSpec Spec { get; }
        public double DegradationFactor { get; }

        public static PvModuleSimulator CreateNeg21c20q() =>
            new(TrinaPvModuleCatalog.Neg21c20q760());

        /// <summary>
        /// 投运年限 → 电流/功率缩放。0 年新产品为 1；满 1 年后按首年衰减 × 逐年衰减。
        /// </summary>
        public static double DegradationFactorFromYears(PvModuleSpec spec, double operatingYears)
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (!double.IsFinite(operatingYears) || operatingYears <= 0)
                return 1;
            double first = spec.FirstYearDegradation;
            double annual = spec.AnnualDegradation;
            double factor = operatingYears < 1
                ? 1.0 - first * operatingYears
                : (1.0 - first) * Math.Pow(1.0 - annual, operatingYears - 1.0);
            return NormalizeDegradationFactor(factor);
        }

        private static double NormalizeDegradationFactor(double factor)
        {
            if (!double.IsFinite(factor))
                return 1;
            return Math.Clamp(factor, MinDegradationFactor, 1.0);
        }

        /// <summary>NOCT 定义：800 W/㎡、20℃ 环境、1 m/s 风速时的电池温度。</summary>
        public static double EstimateCellTempC(PvModuleSpec spec, double ambientC, double gFrontWm2)
        {
            gFrontWm2 = Math.Max(0, gFrontWm2);
            return ambientC + (spec.NoctC - 20.0) / 800.0 * gFrontWm2;
        }

        public PvModuleOperatingPoint Evaluate(double gFrontWm2, double cellTempC, double gRearWm2 = 0)
        {
            var iv = BuildIv(gFrontWm2, cellTempC, gRearWm2);
            if (iv.GeffWm2 < MinIrradianceWm2 || iv.VocV <= 1e-9 || iv.IphA <= 1e-12)
                return new PvModuleOperatingPoint(0, 0, 0, 0, 0, iv.GeffWm2, cellTempC);

            var mpp = FindMaximumPowerPoint(iv);
            return new PvModuleOperatingPoint(
                mpp.PmpW, mpp.VmpV, mpp.ImpA, iv.VocV, iv.IscA, iv.GeffWm2, cellTempC);
        }

        public double CurrentAtVoltage(double voltageV, double gFrontWm2, double cellTempC, double gRearWm2 = 0)
        {
            var iv = BuildIv(gFrontWm2, cellTempC, gRearWm2);
            return CurrentAtVoltage(iv, voltageV);
        }

        private DiodeIv BuildIv(double gFrontWm2, double cellTempC, double gRearWm2)
        {
            double geff = EffectiveIrradiance(gFrontWm2, gRearWm2);
            if (geff < MinIrradianceWm2)
                return new DiodeIv(geff, 0, 0, 0, 0, 1);

            double gRatio = geff / PvModuleSpec.StcIrradianceWm2;
            double dT = cellTempC - PvModuleSpec.StcCellTempC;
            double a = IdealityA(cellTempC);

            double isc = Spec.IscStcA * gRatio * (1.0 + Spec.AlphaIscPerK * dT) * DegradationFactor;
            double voc = (Spec.VocStcV + Spec.SeriesCells * ThermalVoltageStcV * Math.Log(gRatio))
                         * (1.0 + Spec.BetaVocPerK * dT);
            isc = Math.Max(0, isc);
            voc = Math.Max(0, voc);
            if (voc <= 1e-9 || isc <= 1e-12)
                return new DiodeIv(geff, 0, 0, 0, 0, a);

            double expVoc = Math.Exp(Math.Min(voc / Math.Max(a, 1e-9), 80));
            double i0 = isc / Math.Max(expVoc - 1.0, 1e-18);
            return new DiodeIv(geff, isc, voc, isc, i0, a);
        }

        private static double CurrentAtVoltage(DiodeIv iv, double voltageV)
        {
            if (iv.VocV <= 1e-9 || iv.IphA <= 1e-12)
                return 0;
            if (voltageV <= 0)
                return iv.IscA;
            if (voltageV >= iv.VocV)
                return 0;

            double expV = Math.Exp(Math.Min(voltageV / Math.Max(iv.A, 1e-9), 80));
            return Math.Max(0, iv.IphA - iv.I0A * (expV - 1.0));
        }

        /// <summary>黄金分割搜索 P=V·I(V) 的最大值，即运行时 MPPT。</summary>
        private static (double PmpW, double VmpV, double ImpA) FindMaximumPowerPoint(DiodeIv iv)
        {
            double lo = iv.VocV * 0.15;
            double hi = iv.VocV * 0.98;
            const double phi = 0.6180339887498948;
            double x1 = hi - phi * (hi - lo);
            double x2 = lo + phi * (hi - lo);
            double p1 = x1 * CurrentAtVoltage(iv, x1);
            double p2 = x2 * CurrentAtVoltage(iv, x2);
            for (int i = 0; i < 28; i++)
            {
                if (p1 < p2)
                {
                    lo = x1;
                    x1 = x2;
                    p1 = p2;
                    x2 = lo + phi * (hi - lo);
                    p2 = x2 * CurrentAtVoltage(iv, x2);
                }
                else
                {
                    hi = x2;
                    x2 = x1;
                    p2 = p1;
                    x1 = hi - phi * (hi - lo);
                    p1 = x1 * CurrentAtVoltage(iv, x1);
                }
            }

            double vmp = p1 >= p2 ? x1 : x2;
            double imp = CurrentAtVoltage(iv, vmp);
            return (vmp * imp, vmp, imp);
        }

        private double IdealityA(double cellTempC) =>
            _stcIdealityA * (cellTempC + 273.15) / StcAbsTempK;

        private static double FitIdealityFactorA(PvModuleSpec spec)
        {
            double lo = spec.VocStcV / 40.0;
            double hi = spec.VocStcV / 8.0;
            for (int i = 0; i < 48; i++)
            {
                double a = 0.5 * (lo + hi);
                double i0 = spec.IscStcA / Math.Max(Math.Exp(spec.VocStcV / a) - 1.0, 1e-18);
                double iAtVmp = spec.IscStcA - i0 * (Math.Exp(spec.VmpStcV / a) - 1.0);
                if (iAtVmp > spec.ImpStcA)
                    lo = a;
                else
                    hi = a;
            }

            return 0.5 * (lo + hi);
        }

        private double EffectiveIrradiance(double gFrontWm2, double gRearWm2) =>
            Math.Max(0, gFrontWm2) + Spec.Bifaciality * Math.Max(0, gRearWm2);

        private readonly record struct DiodeIv(
            double GeffWm2, double IscA, double VocV, double IphA, double I0A, double A);
    }
}
