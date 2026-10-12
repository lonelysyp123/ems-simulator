using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Control;
using EssSimulator.EssDeviceSimModel.Devices;

namespace EssSimulator.Tests.Control;

public class PcsFormingVoltageSettingsTests
{
    [Fact]
    public void PhysicalConfig_DefaultBlackStartRampIs138()
    {
        Assert.Equal(138, new PcsPhysicalConfig().BlackStartVoltageRampVs);
        Assert.Equal(138, new global::EssSimulator.EssDeviceSimModel.Model.PcsDeviceConfig().BlackStartVoltageRampVs);
    }

    [Fact]
    public void Factory_UnspecifiedUpRate_FloorsOld120ToFiveSecondRate()
    {
        var cfg = PcsDeviceFactory.CreateConfig(
            new PcsPhysicalConfig
            {
                AcVoltageNominal = 690,
                RatedPower = 1725,
                BlackStartVoltageRampVs = 120
            },
            new PcsRampConfig());

        Assert.Equal(138, cfg.VoltageRampUpVs);
        Assert.Equal(138, cfg.VoltageRampDownVs);
    }

    [Fact]
    public void Factory_ExplicitSlowUpRate_IsHonored()
    {
        var cfg = PcsDeviceFactory.CreateConfig(
            new PcsPhysicalConfig
            {
                AcVoltageNominal = 690,
                VoltageRampUpVs = 80,
                BlackStartVoltageRampVs = 138
            },
            new PcsRampConfig());

        Assert.Equal(80, cfg.VoltageRampUpVs);
    }

    [Fact]
    public void Factory_ZeroNq_ComputesFourPercentFormula()
    {
        var cfg = PcsDeviceFactory.CreateConfig(
            new PcsPhysicalConfig
            {
                AcVoltageNominal = 690,
                RatedPower = 1725,
                QvDroopCoefficientVPerKvar = 0
            },
            new PcsRampConfig());

        Assert.True(cfg.QvDroopCoefficientVPerKvar > 0);
        Assert.Equal(0.04 * 690 / 1725, cfg.QvDroopCoefficientVPerKvar, 9);
    }

    [Fact]
    public void ResolveNq_Disabled_IsZero()
    {
        Assert.Equal(0, PcsFormingVoltageSettings.ResolveNq(false, 0.02, 690, 1725));
    }
}
