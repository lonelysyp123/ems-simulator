using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Model;

public class AcQuantityConverterTests
{
    private const double Tolerance = 0.5;

    [Fact]
    public void Star_220kV_50MW_20Mvar_ProducesExpectedTerminalQuantities()
    {
        var internalQty = AcQuantityConverter.FromLineVoltageAndPower(
            lineVoltageV: 220_000,
            activePowerKw: 50_000,
            reactivePowerKvar: 20_000,
            connection: ThreePhaseConnection.Star,
            frequencyHz: 50);

        var terminal = AcQuantityConverter.ToTerminal(internalQty);

        Assert.Equal(220_000, terminal.Vab, Tolerance);
        Assert.Equal(127_017, terminal.Van, 1.0);
        Assert.Equal(141.2, Math.Abs(internalQty.LineCurrentA), 0.5);
        Assert.Equal(141.2, terminal.Ia, 0.5);
    }

    [Fact]
    public void Star_690V_2_5MW_ZeroQ_ProducesExpectedTerminalQuantities()
    {
        var internalQty = AcQuantityConverter.FromLineVoltageAndPower(
            lineVoltageV: 690,
            activePowerKw: 2_500,
            reactivePowerKvar: 0,
            connection: ThreePhaseConnection.Star,
            frequencyHz: 50);

        var terminal = AcQuantityConverter.ToTerminal(internalQty);

        Assert.Equal(690, terminal.Vab, Tolerance);
        Assert.Equal(398.4, terminal.Van, 0.2);
        Assert.Equal(2092, Math.Abs(internalQty.LineCurrentA), 1.0);
        Assert.Equal(2092, terminal.Ia, 1.0);
    }

    [Fact]
    public void Delta_690V_2_5MW_ZeroQ_PhaseCurrentIsLineOverSqrt3()
    {
        var internalQty = AcQuantityConverter.FromLineVoltageAndPower(
            lineVoltageV: 690,
            activePowerKw: 2_500,
            reactivePowerKvar: 0,
            connection: ThreePhaseConnection.Delta,
            frequencyHz: 50);

        var terminal = AcQuantityConverter.ToTerminal(internalQty);

        Assert.Equal(690, terminal.Vab, Tolerance);
        Assert.Equal(690, terminal.Van, Tolerance);
        Assert.Equal(2092, Math.Abs(internalQty.LineCurrentA), 1.0);
        Assert.Equal(1208, terminal.Ia, 1.0);
    }

    [Fact]
    public void FromTerminal_RoundTripsStarInternalQuantities()
    {
        var original = AcQuantityConverter.FromLineVoltageAndPower(
            690, 1000, 200, ThreePhaseConnection.Star);

        var terminal = AcQuantityConverter.ToTerminal(original);
        var roundTrip = AcQuantityConverter.FromTerminal(terminal);

        Assert.Equal(original.LineVoltageV, roundTrip.LineVoltageV, Tolerance);
        Assert.Equal(original.LineCurrentA, roundTrip.LineCurrentA, Tolerance);
    }

    [Fact]
    public void FromTerminal_RoundTripsDeltaInternalQuantities()
    {
        var original = AcQuantityConverter.FromLineVoltageAndPower(
            690, 1000, 200, ThreePhaseConnection.Delta);

        var terminal = AcQuantityConverter.ToTerminal(original);
        var roundTrip = AcQuantityConverter.FromTerminal(terminal);

        Assert.Equal(original.LineVoltageV, roundTrip.LineVoltageV, Tolerance);
        Assert.Equal(original.LineCurrentA, roundTrip.LineCurrentA, Tolerance);
    }

    [Fact]
    public void FromLineVoltageAndPower_RoundTripsActiveAndReactivePower()
    {
        var qty = AcQuantityConverter.FromLineVoltageAndPower(
            220_000, 50_000, 20_000, ThreePhaseConnection.Star);

        Assert.Equal(50_000, qty.ActivePowerKw, 1.0);
        Assert.Equal(20_000, qty.ReactivePowerKvar, 1.0);
        Assert.Equal(141.2, qty.LineCurrentA, 0.5);
        Assert.Equal(21.8, qty.PhaseAngleDeg, 0.5);
    }

    [Fact]
    public void FromLineVoltageAndPower_ImportPower_UsesQuadrantPhaseAngle()
    {
        var qty = AcQuantityConverter.FromLineVoltageAndPower(
            220_000, -50_000, 20_000, ThreePhaseConnection.Star);

        Assert.Equal(-50_000, qty.ActivePowerKw, 1.0);
        Assert.Equal(20_000, qty.ReactivePowerKvar, 1.0);
    }

    [Fact]
    public void MeterPtCt_ScalesPrimaryToSecondaryAndBack()
    {
        var primary = AcQuantityConverter.FromLineVoltageAndPower(
            220_000, 50_000, 20_000, ThreePhaseConnection.Star);

        var pt = new PtConfig { PrimaryLineVoltageV = 220_000, SecondaryLineVoltageV = 100 };
        var ct = new CtConfig { PrimaryCurrentA = 2000, SecondaryCurrentA = 5 };

        var secondary = MeterQuantityConverter.ToSecondary(primary, pt, ct);
        var reported = MeterQuantityConverter.ToReportedPrimary(
            secondary, pt, ct, MeterReportedQuantity.Primary);

        Assert.Equal(100, secondary.LineVoltageV, 0.01);
        Assert.Equal(141.2 / 400, Math.Abs(secondary.LineCurrentA), 0.001);
        Assert.Equal(220_000, reported.LineVoltageV, 1.0);
        Assert.Equal(141.2, Math.Abs(reported.LineCurrentA), 0.5);
    }

    [Theory]
    [InlineData(3000, 1000, true)]    // 放电 + 容性(Q>0) → PF > 0
    [InlineData(-3000, 1000, true)]   // 充电 + 容性(Q>0) → PF > 0（与充放电无关）
    [InlineData(3000, -1000, false)]  // 放电 + 感性(Q<0) → PF < 0
    [InlineData(-3000, -1000, false)] // 充电 + 感性(Q<0) → PF < 0
    public void ComputeSignedPowerFactor_SignMatchesReactivePower(
        double pKw, double qKvar, bool expectPositive)
    {
        double pf = AcQuantityConverter.ComputeSignedPowerFactor(pKw, qKvar);
        double expectedMag = Math.Abs(pKw) / Math.Sqrt(pKw * pKw + qKvar * qKvar);

        Assert.Equal(expectedMag, Math.Abs(pf), 1e-9);
        Assert.Equal(Math.Sign(qKvar), Math.Sign(pf));
        if (expectPositive)
            Assert.True(pf > 0);
        else
            Assert.True(pf < 0);
    }

    [Fact]
    public void ComputeSignedPowerFactor_ZeroReactive_IsNonNegative()
    {
        Assert.Equal(1.0, AcQuantityConverter.ComputeSignedPowerFactor(1000, 0), 1e-9);
        Assert.Equal(1.0, AcQuantityConverter.ComputeSignedPowerFactor(-1000, 0), 1e-9);
        Assert.Equal(1.0, AcQuantityConverter.ComputeSignedPowerFactor(0, 0), 1e-9);
    }

    [Theory]
    [InlineData(double.NaN, 10, 0)]
    [InlineData(1000, double.NaN, 0)]
    [InlineData(1000, 10, double.NaN)]
    [InlineData(double.PositiveInfinity, 10, 0)]
    [InlineData(double.NegativeInfinity, 10, 0)]
    public void ComputePower_NonFiniteInput_IsZero(double voltage, double current, double phaseAngleDeg)
    {
        Assert.Equal(0, AcQuantityConverter.ComputeActivePowerKw(voltage, current, phaseAngleDeg));
        Assert.Equal(0, AcQuantityConverter.ComputeReactivePowerKvar(voltage, current, phaseAngleDeg));
        Assert.Equal(1, AcQuantityConverter.ComputePowerFactor(voltage, current, phaseAngleDeg));
    }

    [Fact]
    public void AcPhasorSnapshot_NaNVoltage_ActivePowerIsZero()
    {
        var snap = new AcPhasorSnapshot(double.NaN, 10, 0, 50);
        Assert.Equal(0, snap.ActivePowerKw);
        Assert.Equal(0, snap.ReactivePowerKvar);
    }

    [Fact]
    public void ReferLineCurrent_690V_630A_To35kV()
    {
        double i35 = AcQuantityConverter.ReferLineCurrent(630, 690, 35_000);
        Assert.Equal(630 * 690 / 35_000.0, i35, 0.05);
        Assert.InRange(i35, 12.3, 12.5);
    }

    [Fact]
    public void ReferLineCurrent_PreservesApparentPower_InrushPrimary()
    {
        const double i35 = 1247;
        double s = Math.Sqrt(3.0) * 35_000 * i35 / 1000.0;
        double i690 = AcQuantityConverter.ReferLineCurrent(i35, 35_000, 690);
        Assert.InRange(i690, 63_000, 63_500);
        Assert.Equal(i35, AcQuantityConverter.LineCurrentFromApparent(35_000, s), 0.5);
        Assert.Equal(i690, AcQuantityConverter.LineCurrentFromApparent(690, s), 1.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReferLineCurrent_NonPositiveVoltage_IsZero(double badV)
    {
        Assert.Equal(0, AcQuantityConverter.ReferLineCurrent(100, badV, 35_000));
        Assert.Equal(0, AcQuantityConverter.ReferLineCurrent(100, 690, badV));
    }
}
