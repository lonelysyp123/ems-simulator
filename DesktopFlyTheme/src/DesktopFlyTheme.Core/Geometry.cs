namespace DesktopFlyTheme;

public readonly record struct Vec2(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);

    public static Vec2 Zero => new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);

    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public Vec2 Normalized()
    {
        var length = Length;
        return length < 1e-12 ? new Vec2(1, 0) : this / length;
    }

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public Vec2 Rotated(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }
}

public readonly record struct RectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public Vec2 Center => new((Left + Right) * 0.5, (Top + Bottom) * 0.5);

    public bool Contains(Vec2 point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool ContainsCircle(Vec2 center, double radius, double tolerance = 1e-4) =>
        center.X - radius >= Left - tolerance
        && center.X + radius <= Right + tolerance
        && center.Y - radius >= Top - tolerance
        && center.Y + radius <= Bottom + tolerance;

    public bool HasMargin(Vec2 point, double margin) =>
        point.X >= Left + margin
        && point.X <= Right - margin
        && point.Y >= Top + margin
        && point.Y <= Bottom - margin;
}

public sealed class WorkAreaMap
{
    private readonly List<RectD> _areas = new();

    public IReadOnlyList<RectD> Areas => _areas;

    public void Set(IReadOnlyList<RectD> areas)
    {
        _areas.Clear();
        _areas.AddRange(areas);
    }

    public bool Contains(Vec2 point)
    {
        for (var i = 0; i < _areas.Count; i++)
        {
            if (_areas[i].Contains(point))
                return true;
        }

        return false;
    }

    public Vec2 Slide(Vec2 from, Vec2 to)
    {
        if (_areas.Count == 0)
            return from;
        if (Contains(to))
            return to;
        if (!Contains(from))
            return ClampToNearest(to);

        if (_areas.Count == 1)
            return ClampRect(from, to, _areas[0]);

        var lo = 0.0;
        var hi = 1.0;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) * 0.5;
            var point = from + (to - from) * mid;
            if (Contains(point))
                lo = mid;
            else
                hi = mid;
        }

        var stop = from + (to - from) * lo;
        var delta = to - from;
        var length = delta.Length;
        if (length > 1e-9)
        {
            var back = stop - delta / length;
            if (Contains(back))
                return back;
        }

        return stop;
    }

    private static Vec2 ClampRect(Vec2 from, Vec2 to, RectD area)
    {
        var x = Math.Clamp(to.X, area.Left, area.Right);
        var y = Math.Clamp(to.Y, area.Top, area.Bottom);
        var moved = Math.Abs(x - to.X) > 1e-9 || Math.Abs(y - to.Y) > 1e-9;
        if (!moved)
            return to;

        if (x <= area.Left)
            x = Math.Min(area.Right, area.Left + 1);
        else if (x >= area.Right)
            x = Math.Max(area.Left, area.Right - 1);
        if (y <= area.Top)
            y = Math.Min(area.Bottom, area.Top + 1);
        else if (y >= area.Bottom)
            y = Math.Max(area.Top, area.Bottom - 1);

        var clamped = new Vec2(x, y);
        return area.Contains(clamped) ? clamped : from;
    }

    private Vec2 ClampToNearest(Vec2 point)
    {
        var best = _areas[0].Center;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _areas.Count; i++)
        {
            var area = _areas[i];
            var x = Math.Clamp(point.X, area.Left + 1, area.Right - 1);
            var y = Math.Clamp(point.Y, area.Top + 1, area.Bottom - 1);
            var candidate = new Vec2(x, y);
            var distance = Vec2.Distance(point, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }
}
