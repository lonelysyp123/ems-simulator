using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using Xunit;

namespace EssSimulator.Tests.Bms;

public class BmsCurrentLimitDeratingTests
{
    public BmsCurrentLimitDeratingTests()
    {
        BmsCurrentLimitDerating.Active = new BmsCurrentLimitDeratingConfig();
    }

    [Theory]
    [InlineData(25f, 1.0f)]
    [InlineData(5f, 0.2f)]
    [InlineData(0f, 0.1f)]
    [InlineData(45f, 0.5f)]
    [InlineData(50f, 0.0f)]
    public void ChargeTemp_MatchesSuggestedCurve(float t, float expected)
    {
        Assert.Equal(expected, BmsCurrentLimitDerating.ChargeTempFactor(t, t), 3);
    }

    [Theory]
    [InlineData(3.40f, 1.0f)]
    [InlineData(3.475f, 0.5f)]
    [InlineData(3.55f, 0.0f)]
    [InlineData(3.60f, 0.0f)]
    public void ChargeVoltage_LinearApproach(float v, float expected)
    {
        Assert.Equal(expected, BmsCurrentLimitDerating.ChargeVoltageFactor(v), 3);
    }

    [Theory]
    [InlineData(3.10f, 1.0f)]
    [InlineData(2.95f, 0.5f)]
    [InlineData(2.80f, 0.0f)]
    [InlineData(2.70f, 0.0f)]
    public void DischargeVoltage_LinearApproach(float v, float expected)
    {
        Assert.Equal(expected, BmsCurrentLimitDerating.DischargeVoltageFactor(v), 3);
    }

    [Fact]
    public void ChargeLimit_TakesMinOfSocTempVoltageThermal()
    {
        // SOC 中档=1，温度 45°C=0.5，电压未逼近=1，热=0.8 → min=0.5
        float f = BmsCurrentLimitDerating.ChargeLimitFactor(
            soc: 0.5f, minCellTempC: 45f, maxCellTempC: 45f, maxCellVoltageV: 3.3f, thermalFactor: 0.8f);
        Assert.Equal(0.5f, f, 3);
    }

    [Fact]
    public void Stack_MaxChargeCurrent_SumsClusters_AndRespectsClusterVoltageDerate()
    {
        var stack = new BatteryStack();
        var c1 = new BatteryCluster
        {
            Measurements =
            {
                SOC = 0.5f,
                NominalEnergyKWh = 100,
                MaxCRate = 0.5f,
                TotalVoltage = 1300f,
                MaxCellTemp = 25f,
                MinCellTemp = 25f,
                MaxCellVoltage = 3.3f,
                MinCellVoltage = 3.2f,
            }
        };
        var c2 = new BatteryCluster
        {
            Measurements =
            {
                SOC = 0.5f,
                NominalEnergyKWh = 100,
                MaxCRate = 0.5f,
                TotalVoltage = 1300f,
                MaxCellTemp = 25f,
                MinCellTemp = 25f,
                MaxCellVoltage = 3.3f,
                MinCellVoltage = 3.2f,
            }
        };
        stack.Cluseter.Add(c1);
        stack.Cluseter.Add(c2);

        float one = c1.Measurements.MaxChargeCurrent!.Value;
        Assert.Equal(one * 2f, stack.MaxChargeCurrent!.Value, 2);

        c1.Measurements.MaxCellVoltage = 3.55f;
        c2.Measurements.MaxCellVoltage = 3.55f;
        Assert.Equal(0f, stack.MaxChargeCurrent!.Value, 2);
    }

    [Fact]
    public void Cluster_MaxDischargePower_DeratesOnLowCellVoltage()
    {
        var m = new ClusterBasicMeasurements
        {
            SOC = 0.5f,
            NominalEnergyKWh = 100,
            MaxCRate = 0.5f,
            TotalVoltage = 1300f,
            MaxCellTemp = 25f,
            MinCellTemp = 25f,
            MaxCellVoltage = 3.3f,
            MinCellVoltage = 3.2f,
        };
        float fullP = m.MaxDischargePower!.Value;

        m.MinCellVoltage = 2.80f;
        Assert.Equal(0f, m.MaxDischargePower!.Value, 2);
        Assert.True(fullP > 10);
    }
}
