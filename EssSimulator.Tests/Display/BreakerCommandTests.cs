using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Display;

public class BreakerCommandTests : SimulatorHostTestBase
{
    [Fact]
    public void Set_RequestsSnapshotPush()
    {
        var recorder = new RecordingUiSnapshotNotifier();
        UiSnapshotNotifier.Current = recorder;
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new EssSimulator.Configuration.PcsDeviceConfig(), new EssSimulator.Configuration.PcsDeviceConfig() }
                }
            }
        };
        var ess = new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
        SimulatorHost.Instance.Register("ess", ess);
        try
        {
            using (ess)
            {
                var result = new BreakerCommand().Execute(["set", "false"]);
                Assert.True(result.Success);
                Assert.False(ess.IsMainBreakerClosed);
                Assert.Equal(1, recorder.Count);
            }
        }
        finally
        {
            UiSnapshotNotifier.Reset();
        }
    }

    private sealed class RecordingUiSnapshotNotifier : IUiSnapshotNotifier
    {
        public int Count { get; private set; }
        public void RequestImmediatePush() => Count++;
    }
}
