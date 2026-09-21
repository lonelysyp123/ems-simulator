using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    public sealed class FormingSourceSample
    {
        public FormingSourceSample(
            string id,
            Complex internalVoltageV,
            Complex outputImpedanceOhm,
            double dcVoltageV = 0,
            double efficiency = AverageConverterPort.DefaultEfficiency)
        {
            Id = id;
            InternalVoltageV = internalVoltageV;
            OutputImpedanceOhm = outputImpedanceOhm;
            DcVoltageV = dcVoltageV;
            Efficiency = efficiency;
        }

        public string Id { get; }
        public Complex InternalVoltageV { get; }
        public Complex OutputImpedanceOhm { get; }

        /// <summary>已提交电池电压（V）。&lt;=0 表示本源无直流侧，本拍不结算直流。</summary>
        public double DcVoltageV { get; }

        /// <summary>变流器效率 (0,1]，用于直流结算。</summary>
        public double Efficiency { get; }
    }

    /// <summary>跟网（GFL）源样本：按 P/Q 设定注入母线，不建内电势。直流结算与构网源同法。</summary>
    public sealed class FollowingSourceSample
    {
        public FollowingSourceSample(
            string id,
            double activePowerW,
            double reactivePowerVAr = 0,
            double dcVoltageV = 0,
            double efficiency = AverageConverterPort.DefaultEfficiency,
            double? currentLimitA = null)
        {
            Id = id;
            ActivePowerW = activePowerW;
            ReactivePowerVAr = reactivePowerVAr;
            DcVoltageV = dcVoltageV;
            Efficiency = efficiency;
            CurrentLimitA = currentLimitA;
        }

        public string Id { get; }

        /// <summary>交付到母线的有功设定（W，流出设备为正）。一拍滞后的控制参考。</summary>
        public double ActivePowerW { get; }

        /// <summary>交付到母线的无功设定（VAr，流出设备为正）。</summary>
        public double ReactivePowerVAr { get; }

        /// <summary>已提交电池电压（V）。&lt;=0 表示无直流侧，本拍不结算直流。</summary>
        public double DcVoltageV { get; }

        /// <summary>变流器效率 (0,1]。</summary>
        public double Efficiency { get; }

        /// <summary>硬限流幅值（A）。null = 不限流。</summary>
        public double? CurrentLimitA { get; }
    }

    /// <summary>一次 690 V 岛的只读快照。不含生产母线引用，影子不得拿它去 SetVoltage。</summary>
    public sealed class IslandShadowSnapshot
    {
        public IslandShadowSnapshot(
            string busId,
            IReadOnlyList<FormingSourceSample> sources,
            Complex? referredGridEmf = null,
            Complex? gridZ = null,
            Complex? theveninZ = null,
            IReadOnlyList<FollowingSourceSample>? following = null)
        {
            BusId = busId;
            Sources = sources;
            ReferredGridEmf = referredGridEmf;
            GridZ = gridZ;
            TheveninZ = theveninZ;
            Following = following ?? Array.Empty<FollowingSourceSample>();
        }

        public string BusId { get; }
        public IReadOnlyList<FormingSourceSample> Sources { get; }
        public Complex? ReferredGridEmf { get; }
        public Complex? GridZ { get; }
        public Complex? TheveninZ { get; }

        /// <summary>跟网（GFL）源；与构网源互补，同岛求解后同法结算直流。</summary>
        public IReadOnlyList<FollowingSourceSample> Following { get; }

        public double ArithmeticAverageVoltageV
        {
            get
            {
                if (Sources.Count == 0)
                    return 0;
                double sum = 0;
                foreach (var s in Sources)
                    sum += s.InternalVoltageV.Magnitude;
                return sum / Sources.Count;
            }
        }
    }
}
