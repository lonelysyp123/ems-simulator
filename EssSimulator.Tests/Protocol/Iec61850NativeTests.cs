using EssSimulator.Protocol.Iec61850;

namespace EssSimulator.Tests.Protocol;

public class Iec61850NativeTests
{
    [Fact]
    public void CandidatePaths_IncludeNativeFileName()
    {
        var paths = Iec61850Native.ListCandidatePaths();
        Assert.NotEmpty(paths);
        string fileName = Iec61850Native.NativeFileName();
        Assert.Contains(paths, p => p.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.Contains(Path.Combine("runtimes"), StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsLinux())
            Assert.Contains(paths, p => p.Contains("linux-x64") || p.Contains("linux-arm64"));
    }

    [Fact]
    public void CandidatePaths_IncludeNativeDllSearchDirectories()
    {
        var extra = Path.Combine(Path.GetTempPath(), "ess-iec61850-native-test");
        Directory.CreateDirectory(extra);
        var previous = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES");
        try
        {
            AppContext.SetData("NATIVE_DLL_SEARCH_DIRECTORIES", extra);
            var paths = Iec61850Native.ListCandidatePaths();
            Assert.Contains(paths, p => Path.GetFullPath(p).StartsWith(Path.GetFullPath(extra), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            AppContext.SetData("NATIVE_DLL_SEARCH_DIRECTORIES", previous);
        }
    }

    [NativeLibraryFact]
    public void TryEnsureNativeLibrary_SucceedsInRepo()
    {
        Assert.True(Iec61850Native.TryEnsureNativeLibrary(out var detail), detail);
        Assert.True(File.Exists(detail), detail);
    }

    [Fact]
    public void CanUseRawEthernet_EmptyIfaceIsFalse()
    {
        Iec61850GooseEthernet.ResetCacheForTests();
        Assert.False(Iec61850GooseEthernet.CanUseRawEthernet(null, out var detail));
        Assert.Contains("未指定", detail, StringComparison.Ordinal);
        Iec61850GooseEthernet.ResetCacheForTests();
        Assert.False(Iec61850GooseEthernet.CanUseRawEthernet("", out _));
    }

    [NativeLibraryFact]
    public void HasGooseSubscriberDestroy_MatchesLoadedNativeLibrary()
    {
        Assert.True(Iec61850Native.TryEnsureNativeLibrary(out var path), path);

        if (!Iec61850Native.HasGooseSubscriberDestroy())
        {
            // Windows 发布包的 iec61850.dll 没有编译 GOOSE 订户（无 GooseSubscriber_* 导出），
            // 运行期由 Iec61850LayerManager 安全降级为"不启用 GOOSE 订阅"（MMS 不受影响）。
            // 这是已知的平台构型差异，记在 docs/项目编译说明.md 的「已知限制」里；
            // 开发用的 macOS/Linux 原生库必须带该符号，缺了就是构建出了问题。
            Assert.True(OperatingSystem.IsWindows(),
                $"{path} 缺少 GooseSubscriber_destroy，但当前不是 Windows");
            return;
        }

        Assert.True(Iec61850Native.HasGooseSubscriberDestroy(), path);
    }
}
