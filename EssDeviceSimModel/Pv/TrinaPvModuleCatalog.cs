namespace EssSimulator.EssDeviceSimModel.Pv
{
    /// <summary>
    /// 天合光能组件规格目录。TSM-NEG21C.20Q 官方公开了功率/效率/尺寸/片数/温度系数；
    /// STC 的 Vmp/Voc/Imp/Isc 在公开规格书到位前，按同版型 TSM-NEG21C.20 740 W 档
    /// （Vmp 42.10 V、Voc 50.30 V）保持电压、电流随 760/740 比例缩放。
    /// </summary>
    public static class TrinaPvModuleCatalog
    {
        public const string Neg21c20qModel = "TSM-NEG21C.20Q";

        public static IReadOnlyList<string> KnownModels { get; } = new[] { Neg21c20qModel };

        public static bool TryGet(string? model, out PvModuleSpec spec)
        {
            spec = null!;
            if (string.IsNullOrWhiteSpace(model))
                return false;
            if (model.Trim().Equals(Neg21c20qModel, StringComparison.OrdinalIgnoreCase))
            {
                spec = Neg21c20q760();
                return true;
            }

            return false;
        }

        /// <summary>空型号回退默认档；未知型号抛错并列出目录。</summary>
        public static PvModuleSpec GetOrThrow(string? model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return Neg21c20q760();
            if (TryGet(model, out var spec))
                return spec;
            throw new ArgumentException(
                $"未知光伏组件型号 '{model.Trim()}'。已知型号：{string.Join("、", KnownModels)}",
                nameof(model));
        }

        /// <summary>至尊 N 三代 760 W 档（Vertex N G3）。</summary>
        public static PvModuleSpec Neg21c20q760() => new()
        {
            Model = Neg21c20qModel,
            Technology = "N-type i-TOPCon Ultra bifacial dual glass",
            CellCount = 264,
            SeriesCells = 66,
            PmaxStcW = 760,
            VmpStcV = 42.10,
            ImpStcA = 760.0 / 42.10,
            VocStcV = 50.30,
            IscStcA = (760.0 / 42.10) / (17.58 / 18.66),
            Efficiency = 0.245,
            GammaPmaxPerK = -0.0026,
            BetaVocPerK = -0.0024,
            AlphaIscPerK = 0.0004,
            NoctC = 43.0,
            Bifaciality = 0.85,
            LengthMm = 2384,
            WidthMm = 1303,
            ThicknessMm = 33,
            WeightKg = 38.3,
            MaxSystemVoltageV = 1500,
            SeriesFuseA = 35,
            FirstYearDegradation = 0.01,
            AnnualDegradation = 0.0035
        };
    }
}
