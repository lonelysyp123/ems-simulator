using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    public sealed class FormingSourceSample
    {
        public FormingSourceSample(string id, Complex internalVoltageV, Complex outputImpedanceOhm)
        {
            Id = id;
            InternalVoltageV = internalVoltageV;
            OutputImpedanceOhm = outputImpedanceOhm;
        }

        public string Id { get; }
        public Complex InternalVoltageV { get; }
        public Complex OutputImpedanceOhm { get; }
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
