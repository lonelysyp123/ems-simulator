namespace DesktopFlyTheme.Core.Tests;

public sealed class HitRateTests
{
    [Fact]
    public void ScriptedPursuit_HitsBetweenEightAndTwelvePercent()
    {
        const int seed = 20261015;
        var report = HitRateSimulator.Run(1000, seed);
        var text = report.Format();
        Console.WriteLine(text);
        Assert.True(report.Rate is >= 0.08 and <= 0.12, text);
        Assert.Contains("不是人手实测", text);
    }
}
