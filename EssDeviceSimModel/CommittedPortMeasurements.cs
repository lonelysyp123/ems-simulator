using System;
using System.Collections.Generic;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>上一步已提交的端口测量，供下拍控制显式读取（阶段 2/3 接线）。</summary>
    public sealed class CommittedPortMeasurements
    {
        public static readonly CommittedPortMeasurements Empty = new()
        {
            SimTime = DateTime.MinValue,
            Pcs = Array.Empty<PcsCommittedSample>()
        };

        public DateTime SimTime { get; init; }
        public IReadOnlyList<PcsCommittedSample> Pcs { get; init; } = Array.Empty<PcsCommittedSample>();
    }

    public readonly struct PcsCommittedSample
    {
        public PcsCommittedSample(string deviceId, double acVoltageV, double activePowerKw, double reactivePowerKvar, double frequencyHz)
        {
            DeviceId = deviceId;
            AcVoltageV = acVoltageV;
            ActivePowerKw = activePowerKw;
            ReactivePowerKvar = reactivePowerKvar;
            FrequencyHz = frequencyHz;
        }

        public string DeviceId { get; }
        public double AcVoltageV { get; }
        public double ActivePowerKw { get; }
        public double ReactivePowerKvar { get; }
        public double FrequencyHz { get; }
    }
}
