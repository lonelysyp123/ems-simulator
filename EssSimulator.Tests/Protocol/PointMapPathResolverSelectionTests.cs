using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

/// <summary>
/// 设备型号选型生效时的点表解析优先级测试。
/// 选型文件写入测试 bin 的 configs/topology/device-models.json，测试结束清理，
/// 避免污染其他依赖默认兜底解析的用例。
/// </summary>
public class PointMapPathResolverSelectionTests : IDisposable
{
    public PointMapPathResolverSelectionTests()
    {
        CleanupSelection();
    }

    public void Dispose()
    {
        CleanupSelection();
    }

    private static void CleanupSelection()
    {
        try
        {
            var path = DeviceModelRegistry.SelectionFilePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
        DeviceModelRegistry.InvalidateCache();
    }

    [Fact]
    public void Resolve_WithSelection_PrefersModelDirectory()
    {
        var modelsRoot = DeviceModelRegistry.FindModelsRoot();
        Assert.True(modelsRoot != null && Directory.Exists(Path.Combine(modelsRoot, "pointmaps", "models", "bms", "standard")),
            "测试输出目录应包含 pointmaps/models/bms/standard（csproj 复制）");

        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bms"] = "standard"
                }
            });

            var resolved = PointMapPathResolver.Resolve("bms_bank.csv");
            Assert.Contains(Path.Combine("pointmaps", "models", "bms", "standard"), resolved);
            Assert.EndsWith("bms_bank.csv", resolved);

            // rack 伴随文件与 bank 同目录（型号内配对）
            var rack = PointMapPathResolver.ResolveSibling(resolved, "bms_rack.csv");
            Assert.Equal(Path.GetDirectoryName(resolved), Path.GetDirectoryName(rack));
        }
        finally
        {
            CleanupSelection();
        }
    }

    [Fact]
    public void Resolve_WithoutSelection_FallsBackToStandardModel()
    {
        var resolved = Path.GetFullPath(PointMapPathResolver.Resolve("emu.csv"));
        Assert.Contains(Path.Combine("pointmaps", "models", "emu", "standard"), resolved);
        Assert.EndsWith("emu.csv", resolved);
        Assert.True(File.Exists(resolved));
        AssertNotRootCopy(resolved, "emu.csv");
    }

    [Fact]
    public void Resolve_UnselectedType_FallsBackToStandardModel()
    {
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bms"] = "standard"
                }
            });

            var resolved = Path.GetFullPath(PointMapPathResolver.Resolve("emu.csv"));
            Assert.Contains(Path.Combine("pointmaps", "models", "emu", "standard"), resolved);
            Assert.EndsWith("emu.csv", resolved);
            Assert.True(File.Exists(resolved));
            AssertNotRootCopy(resolved, "emu.csv");
        }
        finally
        {
            CleanupSelection();
        }
    }

    [Fact]
    public void Resolve_BmsG2Pro_DoesNotReadRootBankCsv()
    {
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bms"] = "g2_pro"
                }
            });

            var resolved = Path.GetFullPath(PointMapPathResolver.Resolve("bms_bank.csv"));
            Assert.Contains(Path.Combine("pointmaps", "models", "bms", "g2_pro"), resolved);
            Assert.EndsWith("bms_bank.csv", resolved);
            AssertNotRootCopy(resolved, "bms_bank.csv");
        }
        finally
        {
            CleanupSelection();
        }
    }

    [Theory]
    [InlineData("emu.csv", "emu")]
    [InlineData("em.csv", "em")]
    [InlineData("bms_bank.csv", "bms")]
    [InlineData("bms_rack.csv", "bms")]
    [InlineData("pv_logger.csv", "pv")]
    [InlineData("pv_apm810.csv", "pv")]
    [InlineData("pv_inverter.csv", "pv")]
    public void Resolve_RuntimeLogicalName_HitsModelsDirectory(string fileName, string typeId)
    {
        var resolved = Path.GetFullPath(PointMapPathResolver.Resolve(fileName));
        Assert.Contains(Path.Combine("pointmaps", "models", typeId), resolved);
        Assert.EndsWith(fileName, resolved);
        Assert.True(File.Exists(resolved));
        AssertNotRootCopy(resolved, fileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_PvInverter_WithOrWithoutSelection_LoadsBindableStandardMap(bool selected)
    {
        if (selected)
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pv"] = "standard"
                }
            });

        var resolved = PointMapPathResolver.Resolve("pv_inverter.csv");
        Assert.Contains(Path.Combine("pointmaps", "models", "pv", "standard"), resolved);
        Assert.True(File.Exists(resolved));
        AssertNotRootCopy(resolved, "pv_inverter.csv");

        var map = new ModbusPointMap("pv_inverter.csv", "simPvInv17",
            pvDeviceIdOverride: 2, inverterIndex: 0);
        Assert.Equal("pv2.Inverters[0].Protocol.ActivePowerW", map.ParamModelLookup["yc14"].Arg1);
    }

    [Fact]
    public void Resolve_LcCsv_WithoutExclusiveSelection_Throws()
    {
        // 默认 LC 由片段拼装，仓库内无 standard/lc.csv；仅选型互斥整表时 Resolve 才有物理文件。
        var ex = Assert.Throws<FileNotFoundException>(
            () => PointMapPathResolver.Resolve("lc.csv"));
        Assert.Contains("pointmaps/models", ex.Message);
    }

    [Fact]
    public void Resolve_LcCsv_WithExclusiveSelection_HitsEmuDir()
    {
        try
        {
            DeviceModelRegistry.SaveSelection(new DeviceModelSelection
            {
                Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lc"] = "emu"
                }
            });

            var resolved = Path.GetFullPath(PointMapPathResolver.Resolve("lc.csv"));
            Assert.Contains(Path.Combine("pointmaps", "models", "lc", "emu"), resolved);
            Assert.EndsWith("lc.csv", resolved);
            Assert.True(File.Exists(resolved));
        }
        finally
        {
            CleanupSelection();
        }
    }

    [Fact]
    public void Resolve_MissingFile_ErrorMentionsModelsDirNotSyncScript()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => PointMapPathResolver.Resolve("no_such_pointmap.csv"));
        Assert.DoesNotContain("sync-pointmaps-to-root", ex.Message);
        Assert.Contains("pointmaps/models", ex.Message);
        Assert.Contains("device-models.json", ex.Message);
    }

    private static void AssertNotRootCopy(string resolved, string fileName)
    {
        foreach (var root in DeviceModelRegistry.CandidateRoots())
        {
            var decoy = Path.GetFullPath(Path.Combine(root, fileName));
            if (File.Exists(decoy))
                Assert.NotEqual(decoy, resolved);
        }
    }
}
