using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class SplitDomainHandshakeTests
{
    private static readonly Complex Zout = new(0.04, 0.18);
    private static readonly Complex Zgrid = new(0.20, 0.80);
    private static readonly Complex Zth = new(0.02, 0.12);
    private static readonly Complex Zload = new(12, 3);

    private static SplitDomainHandshake CreateTiedIsland(Complex pcsE)
    {
        var island = new IsolatedAcIsland("bus690");
        island.AddPcs(new MinPcsPort("pcs1", "bus690", Zout) { InternalVoltageV = pcsE });
        island.SetLoadImpedance(Zload);
        return new SplitDomainHandshake(island, gridEmf: 690, gridZ: Zgrid, theveninZ: Zth);
    }

    [Fact]
    public void FirstTick_VocEqualsGridEmf_WhenNoCommittedCurrent()
    {
        var hs = CreateTiedIsland(700);
        Assert.True(hs.TryTick(DomainEventKind.None, out var r));
        Assert.True((r.Voc - 690).Magnitude < 1e-9);
        Assert.NotNull(r.Lower);
        Assert.True((hs.CommittedTheveninCurrent - r.Lower!.TheveninCurrentIntoBus).Magnitude < 1e-9);
        Assert.True((r.Lower.PcsCurrentSum + r.Lower.TheveninCurrentIntoBus - r.Lower.LoadCurrent).Magnitude < 1e-6);
    }

    [Fact]
    public void LowerPowerStep_DoesNotMoveVoc_UntilNextUpperSolve()
    {
        var hs = CreateTiedIsland(700);
        Assert.True(hs.TryTick(DomainEventKind.None, out _));
        Assert.True(hs.TryTick(DomainEventKind.None, out var settled));
        Complex vocSettled = settled.Voc;
        Complex iSettled = hs.CommittedTheveninCurrent;

        hs.Lower.PcsPorts[0].InternalVoltageV = 740;
        Assert.True(hs.TryTick(DomainEventKind.Lower, out var tLower));
        Assert.True((tLower.Voc - vocSettled).Magnitude < 1e-9, "Lower event must keep last Voc (c–d only)");
        Complex iAfterLower = hs.CommittedTheveninCurrent;
        Assert.True((iAfterLower - iSettled).Magnitude > 1, "Lower solve must accept the new injection this tick");

        Assert.True(hs.TryTick(DomainEventKind.None, out var t1));
        Complex vocExpected = hs.GridEmf - iAfterLower * Zgrid;
        Assert.True((t1.Voc - vocSettled).Magnitude > 0.5, "Next full tick must apply delayed P/Q to Voc");
        Assert.True((t1.Voc - vocExpected).Magnitude < 1e-6);
    }

    [Fact]
    public void UpperEmfStep_RecomputesVocThisTick()
    {
        var hs = CreateTiedIsland(700);
        Assert.True(hs.TryTick(DomainEventKind.None, out _));
        Assert.True(hs.TryTick(DomainEventKind.None, out _));
        Complex iPrev = hs.CommittedTheveninCurrent;
        Complex vocIfUnchanged = 690 - iPrev * Zgrid;

        hs.GridEmf = 710;
        Assert.True(hs.TryTick(DomainEventKind.Upper, out var tUpper));
        Complex vocExpected = 710 - iPrev * Zgrid;
        Assert.True((tUpper.Voc - vocExpected).Magnitude < 1e-6, "Upper event must rebuild Voc this tick from new Emf");
        Assert.True((tUpper.Voc - vocIfUnchanged).Magnitude > 15);
    }

    [Fact]
    public void SolveFailure_DoesNotCommitBoundary()
    {
        var hs = CreateTiedIsland(700);
        Assert.True(hs.TryTick(DomainEventKind.None, out _));
        Complex s0 = hs.CommittedInjectionVa;
        Complex voc0 = hs.CommittedVoc;
        Complex i0 = hs.CommittedTheveninCurrent;

        hs.FailNextLower = true;
        Assert.False(hs.TryTick(DomainEventKind.None, out _));
        Assert.True((hs.CommittedInjectionVa - s0).Magnitude < 1e-12);
        Assert.True((hs.CommittedVoc - voc0).Magnitude < 1e-12);
        Assert.True((hs.CommittedTheveninCurrent - i0).Magnitude < 1e-12);
    }

    [Fact]
    public void ShadowTick_DoesNotWriteProductionBoundary()
    {
        var production = CreateTiedIsland(700);
        Assert.True(production.TryTick(DomainEventKind.None, out _));
        Complex sProd = production.CommittedInjectionVa;
        Complex vocProd = production.CommittedVoc;

        var shadow = production.CreateShadow();
        Assert.Equal(Complex.Zero, shadow.CommittedInjectionVa);
        Assert.Equal(Complex.Zero, shadow.CommittedVoc);

        shadow.Lower.PcsPorts[0].InternalVoltageV = 760;
        Assert.True(shadow.TryTick(DomainEventKind.None, out _));

        Assert.True((production.CommittedInjectionVa - sProd).Magnitude < 1e-12);
        Assert.True((production.CommittedVoc - vocProd).Magnitude < 1e-12);
        Assert.True(shadow.CommittedInjectionVa.Magnitude > 0);
    }

    [Fact]
    public void GridTiedIsland_RejectsZeroTheveninZ()
    {
        var island = new IsolatedAcIsland("bus690");
        Assert.Throws<NetworkSolveException>(() => island.SetThevenin(690, Complex.Zero));
    }
}
