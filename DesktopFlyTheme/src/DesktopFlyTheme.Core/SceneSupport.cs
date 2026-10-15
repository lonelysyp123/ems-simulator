namespace DesktopFlyTheme;

public sealed class IconCache
{
    private readonly List<RectD> _pending = new();
    private readonly List<RectD> _rects = new();

    public double? SampledAt { get; private set; }

    public IReadOnlyList<RectD> Rects => _rects;

    public void Submit(IReadOnlyList<RectD> rects)
    {
        _pending.Clear();
        if (rects.Count == 0)
            return;
        _pending.AddRange(rects);
    }

    public void Sample(double now)
    {
        if (SampledAt is double sampled && now - sampled < ThemeRules.IconSampleInterval)
            return;

        _rects.Clear();
        if (_pending.Count > 0)
            _rects.AddRange(_pending);
        SampledAt = now;
    }

    public bool Covers(Vec2 point)
    {
        for (var i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].Contains(point))
                return true;
        }

        return false;
    }
}

public enum WindowKind
{
    Desktop,
    Other,
}

public static class HitClassifier
{
    public static HitClass Classify(
        Vec2 point,
        IReadOnlyList<RectD> iconRects,
        IReadOnlyList<RectD> buttonRects,
        WindowKind windowKind)
    {
        for (var i = 0; i < buttonRects.Count; i++)
        {
            if (buttonRects[i].Contains(point))
                return HitClass.Button;
        }

        if (windowKind != WindowKind.Desktop)
            return HitClass.OtherWindow;

        for (var i = 0; i < iconRects.Count; i++)
        {
            if (iconRects[i].Contains(point))
                return HitClass.OnIcon;
        }

        return HitClass.DesktopEmpty;
    }

    public static bool IsOverDesktop(HitClass hit) =>
        hit is HitClass.DesktopEmpty or HitClass.OnIcon;
}

public readonly record struct ButtonRects(RectD Initialize, RectD Rag, RectD Close)
{
    public int Hit(Vec2 point)
    {
        if (Initialize.Contains(point))
            return 0;
        if (Rag.Contains(point))
            return 1;
        if (Close.Contains(point))
            return 2;
        return -1;
    }
}

public static class ButtonLayout
{
    public static ButtonRects Layout(RectD workArea, RectD? topTaskbar, double dpi)
    {
        var scale = dpi / 96.0;
        var height = 26 * scale;
        var initWidth = 72 * scale;
        var smallWidth = 52 * scale;
        var gap = 6 * scale;
        var margin = 10 * scale;
        var top = workArea.Top + margin;
        if (topTaskbar is RectD taskbar)
        {
            var minTop = taskbar.Bottom + 8 * scale;
            if (top < minTop)
                top = minTop;
        }

        var right = workArea.Right - margin;
        var closeX = right - smallWidth;
        var ragX = closeX - gap - smallWidth;
        var initX = ragX - gap - initWidth;
        return new ButtonRects(
            new RectD(initX, top, initX + initWidth, top + height),
            new RectD(ragX, top, ragX + smallWidth, top + height),
            new RectD(closeX, top, closeX + smallWidth, top + height));
    }
}

public abstract record DrawCommand;

public sealed record FillRectDraw(RectD Rect, Rgba Color) : DrawCommand;

public sealed record FillCircleDraw(Vec2 Center, double Radius, Rgba Color) : DrawCommand;

public sealed record FillEllipseDraw(Vec2 Center, double RadiusX, double RadiusY, double Rotation, Rgba Color) : DrawCommand;

public sealed record StrokeCircleDraw(Vec2 Center, double Radius, double Width, Rgba Color) : DrawCommand;

public sealed record StrokeEllipseDraw(Vec2 Center, double RadiusX, double RadiusY, double Rotation, double Width, Rgba Color) : DrawCommand;

public sealed record FlyStainDraw(Vec2 Center, double Radius) : DrawCommand;

public sealed record MaggotStainDraw(Vec2 Center, double Radius) : DrawCommand;

public static class InitialPlacement
{
    public static (Vec2 Male, Vec2 Female) Place(RectD work)
    {
        var center = work.Center;
        var male = new Vec2(center.X - ThemeRules.InitialSeparation, center.Y);
        var female = new Vec2(center.X + ThemeRules.InitialSeparation, center.Y);
        if (work.HasMargin(male, ThemeRules.InitialEdgeMargin)
            && work.HasMargin(female, ThemeRules.InitialEdgeMargin))
            return (male, female);

        var horizontal = work.Width >= work.Height;
        if (horizontal)
        {
            male = new Vec2(work.Left + work.Width * 0.3, center.Y);
            female = new Vec2(work.Left + work.Width * 0.7, center.Y);
        }
        else
        {
            male = new Vec2(center.X, work.Top + work.Height * 0.3);
            female = new Vec2(center.X, work.Top + work.Height * 0.7);
        }

        return (male, female);
    }
}
