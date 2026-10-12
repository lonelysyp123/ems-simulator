using EssSimulator.Protocol.Iec61850;

namespace EssSimulator.Tests.Protocol;

public class Iec61850GooseIngressTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
    }

    private static Iec61850GooseIngress Ingress()
    {
        var mapping = Iec61850Mapping.Load(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "mapping.csv"));
        return new Iec61850GooseIngress(mapping.GooseEntries, "TRNA_PCS01");
    }

    [Fact]
    public void Accept_MapsSevenGoosePointsByIndex()
    {
        var ingress = Ingress();
        var values = new object?[] { true, false, 80.0, -10.0, 0.98, 690.0, 50.0 };
        Assert.True(ingress.TryAccept(1, false, "EMS_PCS01PCS/LLN0.GoCB1", values, out var writes, out var reason), reason);
        Assert.Equal(7, writes.Count);
        Assert.Equal(true, writes["yk2"]);
        Assert.Equal(false, writes["yk3"]);
        Assert.Equal(80.0, Convert.ToDouble(writes["yt0"]));
        Assert.Equal(50.0, Convert.ToDouble(writes["yt4"]));
        Assert.Equal(1u, ingress.LastStNum);
    }

    [Fact]
    public void SameStNum_IsSkipped()
    {
        var ingress = Ingress();
        var values = new object?[] { true, true, 1.0, 0.0, 1.0, 690.0, 50.0 };
        Assert.True(ingress.TryAccept(3, false, "EMS_PCS01PCS/LLN0.GoCB1", values, out _, out _));
        Assert.False(ingress.TryAccept(3, false, "EMS_PCS01PCS/LLN0.GoCB1", values, out var writes, out var reason));
        Assert.Equal("stNum", reason);
        Assert.Empty(writes);
    }

    [Fact]
    public void TestBit_IsSkipped()
    {
        var ingress = Ingress();
        Assert.False(ingress.TryAccept(1, true, "EMS_PCS01PCS/LLN0.GoCB1", new object?[] { true }, out _, out var reason));
        Assert.Equal("test", reason);
        Assert.Null(ingress.LastStNum);
    }

    [Fact]
    public void LocalGoCbRef_IsSkipped()
    {
        var ingress = Ingress();
        var values = new object?[] { true, false, 1.0, 0.0, 1.0, 690.0, 50.0 };
        Assert.False(ingress.TryAccept(1, false, "TRNA_PCS01PCS/LLN0.GoCB1", values, out _, out var reason));
        Assert.Equal("local-gocb", reason);
        Assert.False(ingress.TryAccept(1, false, "TRNA_PCS01PCS/LLN0$GO$GoCB1", values, out _, out reason));
        Assert.Equal("local-gocb", reason);
    }

    [Fact]
    public void ChangedStNum_IsAcceptedAgain()
    {
        var ingress = Ingress();
        var values = new object?[] { false };
        Assert.True(ingress.TryAccept(1, false, null, values, out _, out _));
        Assert.True(ingress.TryAccept(2, false, null, values, out var writes, out _));
        Assert.Equal(false, writes["yk2"]);
        Assert.Equal(2u, ingress.LastStNum);
    }
}
