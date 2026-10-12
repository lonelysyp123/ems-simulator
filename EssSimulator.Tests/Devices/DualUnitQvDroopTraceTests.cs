using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using Xunit.Abstractions;

namespace EssSimulator.Tests.Devices;

/// <summary>双单元黑启动：单元2 合闸后再开机，采集 Q/V/相位轨迹（不改产品逻辑）。</summary>
public class DualUnitQvDroopTraceTests
{
    private readonly ITestOutputHelper _out;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DualUnitQvDroopTraceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Trace_Unit2HvCloseThenPcsStart_ReactiveAndVoltage()
    {
        var step = TimeSpan.FromMilliseconds(100);
        using var ess = CreateTwoUnitPlant();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetUnitBreakerClosed(1, false);

        for (int i = 0; i < ess._pcsList.Count; i++)
            ess.SetBmsPcsLinked(i, true);

        ArmForming(ess, 0, 2);

        var t = T0;
        var rows = new List<Row>();

        void Tick(string ev)
        {
            t = t.Add(step);
            ess.PlantEngine.Step(t, step, step);
            rows.Add(Capture(ess, t, ev));
        }

        for (int i = 0; i < 50; i++)
            Tick("build");

        var host = ess._pcsList[0].GetCurrentState();
        Assert.True(host.AcVoltage > 650, $"单元1 应已建压，U={host.AcVoltage:F1} V phase={host.BlackStartPhase}");
        Assert.True(
            host.BlackStartPhase is BlackStartPhase.VoltageRegulating or BlackStartPhase.Synchronized,
            $"单元1 相位 {host.BlackStartPhase}");

        Tick("hv_close");
        ess.SetUnitBreakerClosed(1, true);
        for (int i = 0; i < 50; i++)
            Tick(i == 0 ? "w1" : "w2_idle");

        ArmFollowerThenHold(ess, 2, 4, run: false);
        for (int i = 0; i < 30; i++)
            Tick("w2_pll");

        ArmFollowerThenHold(ess, 2, 4, run: true);
        Tick("cut_in");
        for (int i = 0; i < 150; i++)
            Tick("w4");

        Dump(rows);
        Summarize(rows);
        Assert.True(rows.Count > 100);
    }

    [Fact]
    public void Trace_Unit2HvCloseAndStartTogether_20ms()
    {
        var step = TimeSpan.FromMilliseconds(20);
        using var ess = CreateTwoUnitPlant();
        ess.SetMainBreakerClosed(false);
        ess.SetUnitBreakerClosed(0, true);
        ess.SetUnitBreakerClosed(1, false);
        for (int i = 0; i < ess._pcsList.Count; i++)
            ess.SetBmsPcsLinked(i, true);

        ArmForming(ess, 0, 2);
        var t = T0;
        var rows = new List<Row>();

        void Tick(string ev)
        {
            t = t.Add(step);
            ess.PlantEngine.Step(t, step, step);
            rows.Add(Capture(ess, t, ev));
        }

        for (int i = 0; i < 250; i++)
            Tick("build");

        ess.SetUnitBreakerClosed(1, true);
        ArmForming(ess, 2, 4);
        Tick("hv_close");
        for (int i = 0; i < 500; i++)
            Tick(i < 50 ? "w1" : "w4");

        Dump(rows);
        SummarizeTogether(rows);
        Assert.True(rows.Count > 100);
    }

    private static EnergyStorageSystem CreateTwoUnitPlant()
    {
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new Configuration.PcsDeviceConfig(), new Configuration.PcsDeviceConfig() }
                },
                new EssUnitConfig
                {
                    Pcs = { new Configuration.PcsDeviceConfig(), new Configuration.PcsDeviceConfig() }
                }
            }
        };
        var pcsCfg = new PcsPhysicalConfig
        {
            AcVoltageNominal = 690,
            FrequencyNominal = 50,
            MaxCurrent = 2000,
            BlackStartPrechargeDelayMs = 0,
            BlackStartVoltageRampVs = 400,
            InrushPeakMultiplier = 0.3,
            DvDtTripThresholdVPerSec = 10_000
        };
        var unitXf = new UnitTransformerConfig
        {
            RatedPower = 6300,
            MagnetizingInrushEnabled = true
        };
        return new EnergyStorageSystem(
            simCfg, pcsCfg, new TransformerConfig(), unitXf, new LoadConfig(), new PccConfig(), new MeterConfig());
    }

    private static void ArmForming(EnergyStorageSystem ess, int startInclusive, int endExclusive)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            Assert.True(ess.TrySetPcsBlackStart(i, true));
            var pcs = ess._pcsList[i];
            pcs.ApplyIslandVoltageCommand(690);
            pcs.SyncExternalRunCommand(true);
            pcs.TransitionToMode(OperationMode.Normal);
            pcs.TransitionToGMode(GridMode.Islanded);
        }
    }

    private static void ArmFollowerThenHold(EnergyStorageSystem ess, int startInclusive, int endExclusive, bool run)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            var pcs = ess._pcsList[i];
            if (run)
                pcs.SyncExternalRunCommand(true);
            else
                pcs.WithdrawExternalRunCommand();

            Assert.True(ess.TrySetPcsBlackStart(i, true));
            pcs.ApplyIslandVoltageCommand(690);
            pcs.TransitionToMode(run ? OperationMode.Normal : OperationMode.Standby);
            pcs.TransitionToGMode(GridMode.Islanded);
        }
    }

    private static Row Capture(EnergyStorageSystem ess, DateTime t, string ev)
    {
        var pcs = ess._pcsList;
        var xf = ess.ElectricalNetwork.UnitTransformers;
        var ch = new PcsSnap[4];
        for (int i = 0; i < 4; i++)
        {
            var p = pcs[i];
            var st = p.GetCurrentState();
            bool inj = p.TryGetIslandBusVoltageInjection(out var vInj, out var fInj);
            ch[i] = new PcsSnap(
                st.ReactivePower,
                st.ActivePower,
                st.AcVoltage,
                st.AcCurrent,
                st.BlackStartPhase.ToString(),
                p.IsLiveBusFollower,
                p.IsPreSyncReadyToCutIn,
                inj,
                vInj,
                fInj,
                st.Mode.ToString());
        }

        var (_, qIn0) = xf[0].GetInrushDemandKwKvar();
        var (_, qIn1) = xf[1].GetInrushDemandKwKvar();
        return new Row(
            (t - T0).TotalSeconds,
            ev,
            ess.ElectricalNetwork.StationBus35LineVoltageV,
            xf[0].GetCurrentState().SecondaryVoltage,
            xf[1].GetCurrentState().SecondaryVoltage,
            xf[0].GetSecondaryMagnetizingReactiveKvar(),
            xf[1].GetSecondaryMagnetizingReactiveKvar(),
            qIn0,
            qIn1,
            ch);
    }

    private void Dump(List<Row> rows)
    {
        _out.WriteLine(
            "t_s ev bus35 xf0V xf1V magQ0 magQ1 inQ0 inQ1 | " +
            "pcs Q U phase fol pre inj vInj | ...");
        foreach (var r in rows)
        {
            if (!ShouldPrint(r, rows))
                continue;
            _out.WriteLine(
                $"{r.T:F2,-6} {r.Ev,-10} {r.Bus35,8:F0} {r.Xf0V,6:F0} {r.Xf1V,6:F0} " +
                $"{r.MagQ0,6:F1} {r.MagQ1,6:F1} {r.InQ0,5:F0} {r.InQ1,5:F0} |" +
                FormatPcs(r.Pcs[0]) + FormatPcs(r.Pcs[1]) + " ||" +
                FormatPcs(r.Pcs[2]) + FormatPcs(r.Pcs[3]));
        }
    }

    private static bool ShouldPrint(Row r, List<Row> rows)
    {
        if (r.Ev is "hv_close" or "w1" or "cut_in")
            return true;
        if (r.Ev is "build")
            return r.T >= rows.Where(x => x.Ev == "build").Max(x => x.T) - 0.21;
        if (r.Ev is "w2_idle" or "w2_pll")
            return (int)Math.Round(r.T * 10) % 5 == 0;
        if (r.Ev == "w4")
            return r.T < rows.First(x => x.Ev == "w4").T + 1.05
                   || (int)Math.Round(r.T * 5) % 5 == 0;
        return true;
    }

    private static string FormatPcs(PcsSnap p) =>
        $"  Q={p.Q,7:F1} U={p.U,6:F1} {p.Phase,-16} fol={p.Follower,5} pre={p.Pre,5} inj={p.Inj,5} vI={p.VInj,6:F1}";

    private void Summarize(List<Row> rows)
    {
        Row Last(string ev) => rows.Last(x => x.Ev == ev);
        var w0 = Last("build");
        var w1 = rows.First(x => x.Ev == "w1");
        var w1End = rows.Last(x => x.Ev is "w1" or "w2_idle");
        var pllEnd = rows.Last(x => x.Ev == "w2_pll");
        var cut = rows.First(x => x.Ev == "cut_in");
        var w4 = rows.Where(x => x.Ev == "w4").ToList();

        double SumQ(Row r, int a, int b) => r.Pcs.Skip(a).Take(b - a).Sum(p => p.Q);

        _out.WriteLine("");
        _out.WriteLine("=== 窗口摘要 ===");
        PrintWindow("W0 单元1稳态", w0, SumQ);
        PrintWindow("W1 合闸首拍", w1, SumQ);
        PrintWindow("W2 合闸后、开机前末拍", w1End, SumQ);
        PrintWindow("W2 PLL末拍", pllEnd, SumQ);
        PrintWindow("W3 开机切入首拍", cut, SumQ);
        PrintWindow("W4 末拍", w4[^1], SumQ);

        var u1q = w4.Select(r => SumQ(r, 0, 2)).ToList();
        var u2q = w4.Select(r => SumQ(r, 2, 4)).ToList();
        var p0q = w4.Select(r => r.Pcs[0].Q).ToList();
        _out.WriteLine("");
        _out.WriteLine(
            $"W4 单元1 ΣQ min/max/Δ = {u1q.Min():F1} / {u1q.Max():F1} / {u1q.Max() - u1q.Min():F1} kvar");
        _out.WriteLine(
            $"W4 单元2 ΣQ min/max/Δ = {u2q.Min():F1} / {u2q.Max():F1} / {u2q.Max() - u2q.Min():F1} kvar");
        _out.WriteLine(
            $"W4 PCS0 Q min/max/Δ = {p0q.Min():F1} / {p0q.Max():F1} / {p0q.Max() - p0q.Min():F1} kvar  过零次数(dQ变号)={CountSignChanges(p0q)}");
        _out.WriteLine(
            $"W1 相对 W0：单元1 ΔQ={SumQ(w1, 0, 2) - SumQ(w0, 0, 2):F1}  单元2 ΔQ={SumQ(w1, 2, 4) - SumQ(w0, 2, 4):F1}  Δbus35={w1.Bus35 - w0.Bus35:F0} V");
        _out.WriteLine(
            $"切入相对 PLL末：单元1 ΔQ={SumQ(cut, 0, 2) - SumQ(pllEnd, 0, 2):F1}  单元2 ΔQ={SumQ(cut, 2, 4) - SumQ(pllEnd, 2, 4):F1}");
        _out.WriteLine(
            $"PCS2 切入: phase {pllEnd.Pcs[2].Phase}→{cut.Pcs[2].Phase} fol {pllEnd.Pcs[2].Follower}→{cut.Pcs[2].Follower} inj {pllEnd.Pcs[2].Inj}→{cut.Pcs[2].Inj}");
        DumpQSeries("切入后 Q 交接", rows.Where(x => x.Ev is "cut_in" or "w4").Take(25).ToList());
    }

    private void SummarizeTogether(List<Row> rows)
    {
        var w0 = rows.Last(x => x.Ev == "build");
        var close = rows.First(x => x.Ev == "hv_close");
        var w1 = rows.Where(x => x.Ev == "w1").ToList();
        var w4 = rows.Where(x => x.Ev == "w4").ToList();
        double SumQ(Row r, int a, int b) => r.Pcs.Skip(a).Take(b - a).Sum(p => p.Q);

        _out.WriteLine("");
        _out.WriteLine("=== 合闸同时开机 摘要 ===");
        PrintWindow("W0", w0, SumQ);
        PrintWindow("合闸+开机首拍", close, SumQ);
        if (w1.Count > 0)
            PrintWindow("W1 末", w1[^1], SumQ);
        PrintWindow("W4 末", w4[^1], SumQ);
        var p0q = w4.Select(r => r.Pcs[0].Q).ToList();
        var p2q = w4.Select(r => r.Pcs[2].Q).ToList();
        _out.WriteLine(
            $"W4 PCS0 Q min/max/Δ={p0q.Min():F1}/{p0q.Max():F1}/{p0q.Max() - p0q.Min():F1} 变号={CountSignChanges(p0q)}");
        _out.WriteLine(
            $"W4 PCS2 Q min/max/Δ={p2q.Min():F1}/{p2q.Max():F1}/{p2q.Max() - p2q.Min():F1} 变号={CountSignChanges(p2q)}");
        DumpQSeries("合闸后 25 拍", rows.SkipWhile(x => x.Ev == "build").Take(25).ToList());
    }

    private void DumpQSeries(string title, List<Row> rows)
    {
        _out.WriteLine($"--- {title} ---");
        foreach (var r in rows)
        {
            _out.WriteLine(
                $"  t={r.T:F2} {r.Ev,-8} u1={r.Pcs[0].Q + r.Pcs[1].Q,7:F1} u2={r.Pcs[2].Q + r.Pcs[3].Q,7:F1} " +
                $"p0={r.Pcs[0].Q,6:F1} p2={r.Pcs[2].Q,6:F1} {r.Pcs[2].Phase,-14} inj2={r.Pcs[2].Inj} " +
                $"U0={r.Pcs[0].U:F1} U2={r.Pcs[2].U:F1} mag={r.MagQ0:F0}+{r.MagQ1:F0}");
        }
    }

    private void PrintWindow(string name, Row r, Func<Row, int, int, double> sumQ)
    {
        _out.WriteLine(
            $"{name}: t={r.T:F1}s bus35={r.Bus35:F0} magQ={r.MagQ0:F1}+{r.MagQ1:F1} " +
            $"u1ΣQ={sumQ(r, 0, 2):F1} u2ΣQ={sumQ(r, 2, 4):F1} " +
            $"p0={r.Pcs[0].Phase}/{r.Pcs[0].Q:F1} p2={r.Pcs[2].Phase}/{r.Pcs[2].Q:F1} fol2={r.Pcs[2].Follower}");
    }

    private static int CountSignChanges(List<double> q)
    {
        int n = 0;
        for (int i = 2; i < q.Count; i++)
        {
            double d0 = q[i - 1] - q[i - 2];
            double d1 = q[i] - q[i - 1];
            if (Math.Abs(d0) < 0.5 || Math.Abs(d1) < 0.5)
                continue;
            if (Math.Sign(d0) != Math.Sign(d1))
                n++;
        }
        return n;
    }

    private readonly record struct PcsSnap(
        double Q, double P, double U, double I, string Phase,
        bool Follower, bool Pre, bool Inj, double VInj, double FInj, string Mode);

    private readonly record struct Row(
        double T, string Ev, double Bus35, double Xf0V, double Xf1V,
        double MagQ0, double MagQ1, double InQ0, double InQ1, PcsSnap[] Pcs);
}
