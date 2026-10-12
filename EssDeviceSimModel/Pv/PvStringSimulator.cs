namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>一组串：若干组件串联，电流与单板相同，电压/功率按块数叠加。</summary>
    public sealed class PvStringSimulator
    {
        public const int DefaultModuleCount = 30;

        public PvStringSimulator(PvModuleSimulator module, int moduleCount = DefaultModuleCount)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            if (moduleCount < 1)
                throw new ArgumentOutOfRangeException(nameof(moduleCount));
            ModuleCount = moduleCount;
        }

        public PvModuleSimulator Module { get; }
        public int ModuleCount { get; }

        public static PvStringSimulator CreateDefault() =>
            new(PvModuleSimulator.CreateNeg21c20q(), DefaultModuleCount);

        public PvModuleOperatingPoint Evaluate(double gFrontWm2, double cellTempC, double gRearWm2 = 0)
        {
            var m = Module.Evaluate(gFrontWm2, cellTempC, gRearWm2);
            return new PvModuleOperatingPoint(
                PmpW: m.PmpW * ModuleCount,
                VmpV: m.VmpV * ModuleCount,
                ImpA: m.ImpA,
                VocV: m.VocV * ModuleCount,
                IscA: m.IscA,
                GeffWm2: m.GeffWm2,
                CellTempC: m.CellTempC);
        }

        public double CurrentAtVoltage(double stringVoltageV, double gFrontWm2, double cellTempC, double gRearWm2 = 0) =>
            Module.CurrentAtVoltage(stringVoltageV / ModuleCount, gFrontWm2, cellTempC, gRearWm2);
    }
}
