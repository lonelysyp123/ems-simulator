using EssSimulator.EmsStrategy.Application;

namespace EssSimulator.EmsStrategy.Domain.Algorithms;

/// <summary>
/// 惯量响应。P = −(df/dt) × P_rated × Tj / f0。
/// 动作需同时：幅度死区、df/dt 与频偏同号、速率死区；频率须在合法范围才采样。
/// </summary>
public sealed class InertiaCalculator
{
    private readonly Queue<(double TimeSec, double FrequencyHz)> _samples = new();
    private double _timeSec;

    public void Reset()
    {
        _samples.Clear();
        _timeSec = 0;
    }

    public InertiaSample Evaluate(double frequencyHz, TimeSpan dt, double plantRatedKw, InertiaConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (dt < TimeSpan.Zero)
            dt = TimeSpan.Zero;
        _timeSec += dt.TotalSeconds;

        double f0 = cfg.RatedFrequencyHz <= 0 ? 50 : cfg.RatedFrequencyHz;
        bool inBand = frequencyHz >= cfg.FreqMinHz && frequencyHz <= cfg.FreqMaxHz;
        if (inBand)
        {
            _samples.Enqueue((_timeSec, frequencyHz));
            double window = Math.Max(cfg.ControlCycle.TotalSeconds, 1e-3);
            while (_samples.Count > 0 && _timeSec - _samples.Peek().TimeSec > window)
                _samples.Dequeue();
        }

        if (!inBand || _samples.Count < 2)
            return new InertiaSample(false, 0, 0);

        var oldest = _samples.Peek();
        double span = Math.Max(_timeSec - oldest.TimeSec, 1e-6);
        double dfdt = (frequencyHz - oldest.FrequencyHz) / span;
        double df = frequencyHz - f0;
        bool amp = Math.Abs(df) > cfg.AmplitudeDeadbandHz;
        bool sameSign = df * dfdt > 0;
        bool rate = Math.Abs(dfdt) > cfg.RateDeadbandHzPerSec;
        bool acting = amp && sameSign && rate;
        double p = acting
            ? -dfdt * Math.Max(plantRatedKw, 1) * Math.Max(cfg.InertiaTimeSec, 0) / f0
            : 0;
        return new InertiaSample(acting, dfdt, p);
    }
}

public readonly record struct InertiaSample(bool ConditionMet, double DfDtHzPerSec, double PowerKw);
