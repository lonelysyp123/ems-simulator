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

    /// <summary>一次 690 V 岛的只读快照。不含生产母线引用，影子不得拿它去 SetVoltage。</summary>
    public sealed class IslandShadowSnapshot
    {
        public IslandShadowSnapshot(
            string busId,
            IReadOnlyList<FormingSourceSample> sources,
            Complex? referredGridEmf = null,
            Complex? gridZ = null,
            Complex? theveninZ = null)
        {
            BusId = busId;
            Sources = sources;
            ReferredGridEmf = referredGridEmf;
            GridZ = gridZ;
            TheveninZ = theveninZ;
        }

        public string BusId { get; }
        public IReadOnlyList<FormingSourceSample> Sources { get; }
        public Complex? ReferredGridEmf { get; }
        public Complex? GridZ { get; }
        public Complex? TheveninZ { get; }

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
