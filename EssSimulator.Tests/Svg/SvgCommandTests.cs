using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Svg;

public class SvgCommandTests : SimulatorHostTestBase
{
    [Fact]
    public void SetSvg_WritesRunAndReactiveSetpoint()
    {
        using var ess = CreateEss(enable: true);
        SimulatorHost.Instance.RegisterEss(ess);
        var cmd = new EssCommand();

        var on = cmd.Execute(new[] { "setSvg", "run", "on" });
        var q = cmd.Execute(new[] { "setSvg", "reactive", "-2500" });

        Assert.True(on.Success);
        Assert.True(q.Success);
        Assert.Equal((ushort)1, ess.Svg!.RunCommand);
        Assert.Equal(-2500, ess.Svg.ReactiveSetpointKvar);
    }

    [Fact]
    public void SetSvg_FailsWhenDisabled()
    {
        using var ess = CreateEss(enable: false);
        SimulatorHost.Instance.RegisterEss(ess);

        var result = new EssCommand().Execute(new[] { "setSvg", "run", "on" });

        Assert.False(result.Success);
        Assert.Contains("未启用", result.Message);
    }

    private static EnergyStorageSystem CreateEss(bool enable) =>
        new(
            new SimulatorConfig
            {
                EnableSvg = enable,
                Devices = { new EssUnitConfig() }
            },
            new PcsPhysicalConfig(),
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
}