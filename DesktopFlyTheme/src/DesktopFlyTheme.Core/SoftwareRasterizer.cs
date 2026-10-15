namespace DesktopFlyTheme;

public sealed class RasterFrame
{
    public RasterFrame(byte[] bgra, int width, int height)
    {
        Bgra = bgra;
        Width = width;
        Height = height;
    }

    public byte[] Bgra { get; }

    public int Width { get; }

    public int Height { get; }
}

public static class SoftwareRasterizer
{
    public static RasterFrame Render(IReadOnlyList<DrawCommand> commands, int width, int height, double originX, double originY)
    {
        if (width <= 0 || height <= 0)
            return new RasterFrame(Array.Empty<byte>(), 0, 0);

        var pixels = new byte[width * height * 4];
        foreach (var command in commands)
        {
            switch (command)
            {
                case FillRectDraw rect:
                    FillRect(pixels, width, height, originX, originY, rect);
                    break;
                case FillCircleDraw circle:
                    FillCircle(pixels, width, height, originX, originY, circle.Center, circle.Radius, circle.Color, null);
                    break;
                case FillEllipseDraw ellipse:
                    FillEllipse(pixels, width, height, originX, originY, ellipse, stroke: false);
                    break;
                case StrokeCircleDraw stroke:
                    StrokeCircle(pixels, width, height, originX, originY, stroke);
                    break;
                case StrokeEllipseDraw ellipseStroke:
                    FillEllipse(pixels, width, height, originX, originY, new FillEllipseDraw(ellipseStroke.Center, ellipseStroke.RadiusX, ellipseStroke.RadiusY, ellipseStroke.Rotation, ellipseStroke.Color), stroke: true, ellipseStroke.Width);
                    break;
                case FlyStainDraw stain:
                    DrawFlyStain(pixels, width, height, originX, originY, stain);
                    break;
                case MaggotStainDraw stain:
                    FillCircle(pixels, width, height, originX, originY, stain.Center, stain.Radius, ThemeRules.StainBase, null);
                    break;
            }
        }

        return new RasterFrame(pixels, width, height);
    }

    private static void DrawFlyStain(byte[] pixels, int width, int height, double originX, double originY, FlyStainDraw stain)
    {
        FillCircle(pixels, width, height, originX, originY, stain.Center, stain.Radius, ThemeRules.StainBase, stain);
        foreach (var offset in ThemeRules.FlyStainRedOffsets)
        {
            FillCircle(
                pixels,
                width,
                height,
                originX,
                originY,
                stain.Center + offset * stain.Radius,
                stain.Radius * 0.14,
                ThemeRules.StainRed,
                stain);
        }

        foreach (var offset in ThemeRules.FlyStainGreenOffsets)
        {
            FillCircle(
                pixels,
                width,
                height,
                originX,
                originY,
                stain.Center + offset * stain.Radius,
                stain.Radius * 0.12,
                ThemeRules.StainGreen,
                stain);
        }
    }

    private static void FillRect(byte[] pixels, int width, int height, double originX, double originY, FillRectDraw draw)
    {
        var left = Math.Clamp((int)Math.Floor(draw.Rect.Left - originX), 0, width);
        var right = Math.Clamp((int)Math.Ceiling(draw.Rect.Right - originX), 0, width);
        var top = Math.Clamp((int)Math.Floor(draw.Rect.Top - originY), 0, height);
        var bottom = Math.Clamp((int)Math.Ceiling(draw.Rect.Bottom - originY), 0, height);
        for (var y = top; y < bottom; y++)
        {
            var row = y * width;
            for (var x = left; x < right; x++)
                Blend(pixels, (row + x) * 4, draw.Color);
        }
    }

    private static void FillCircle(
        byte[] pixels,
        int width,
        int height,
        double originX,
        double originY,
        Vec2 center,
        double radius,
        Rgba color,
        FlyStainDraw? clip)
    {
        if (radius <= 0)
            return;
        var cx = center.X - originX;
        var cy = center.Y - originY;
        var minX = Math.Max(0, (int)Math.Floor(cx - radius));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(cx + radius));
        var minY = Math.Max(0, (int)Math.Floor(cy - radius));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(cy + radius));
        var r2 = radius * radius;
        for (var y = minY; y <= maxY; y++)
        {
            var dy = y + 0.5 - cy;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x + 0.5 - cx;
                if (dx * dx + dy * dy > r2)
                    continue;
                if (clip != null && !Inside(x, y, clip.Center, clip.Radius, originX, originY))
                    continue;
                Blend(pixels, (y * width + x) * 4, color);
            }
        }
    }

    private static void StrokeCircle(byte[] pixels, int width, int height, double originX, double originY, StrokeCircleDraw draw)
    {
        var cx = draw.Center.X - originX;
        var cy = draw.Center.Y - originY;
        var outer = draw.Radius + draw.Width * 0.5;
        var minX = Math.Max(0, (int)Math.Floor(cx - outer - 1));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(cx + outer + 1));
        var minY = Math.Max(0, (int)Math.Floor(cy - outer - 1));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(cy + outer + 1));
        var half = draw.Width * 0.5;
        for (var y = minY; y <= maxY; y++)
        {
            var dy = y + 0.5 - cy;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x + 0.5 - cx;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(distance - draw.Radius) <= half)
                    Blend(pixels, (y * width + x) * 4, draw.Color);
            }
        }
    }

    private static void FillEllipse(
        byte[] pixels,
        int width,
        int height,
        double originX,
        double originY,
        FillEllipseDraw draw,
        bool stroke,
        double strokeWidth = 1)
    {
        var rx = Math.Max(draw.RadiusX, 0.5);
        var ry = Math.Max(draw.RadiusY, 0.5);
        var reach = Math.Max(rx, ry) + (stroke ? strokeWidth : 0) + 1;
        var cx = draw.Center.X - originX;
        var cy = draw.Center.Y - originY;
        var minX = Math.Max(0, (int)Math.Floor(cx - reach));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(cx + reach));
        var minY = Math.Max(0, (int)Math.Floor(cy - reach));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(cy + reach));
        var cos = Math.Cos(draw.Rotation);
        var sin = Math.Sin(draw.Rotation);
        var half = strokeWidth * 0.5;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                var localX = dx * cos + dy * sin;
                var localY = -dx * sin + dy * cos;
                var value = (localX * localX) / (rx * rx) + (localY * localY) / (ry * ry);
                var paint = stroke
                    ? Math.Abs(Math.Sqrt(value) - 1) * Math.Min(rx, ry) <= half
                    : value <= 1;
                if (paint)
                    Blend(pixels, (y * width + x) * 4, draw.Color);
            }
        }
    }

    private static bool Inside(int x, int y, Vec2 center, double radius, double originX, double originY)
    {
        var dx = x + 0.5 - (center.X - originX);
        var dy = y + 0.5 - (center.Y - originY);
        return dx * dx + dy * dy <= radius * radius;
    }

    private static void Blend(byte[] pixels, int index, Rgba color)
    {
        if (color.A == 0)
            return;
        if (color.A == 255)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = 255;
            return;
        }

        var alpha = color.A / 255f;
        var inverse = 1f - alpha;
        pixels[index] = (byte)(color.B * alpha + pixels[index] * inverse);
        pixels[index + 1] = (byte)(color.G * alpha + pixels[index + 1] * inverse);
        pixels[index + 2] = (byte)(color.R * alpha + pixels[index + 2] * inverse);
        pixels[index + 3] = 255;
    }
}
