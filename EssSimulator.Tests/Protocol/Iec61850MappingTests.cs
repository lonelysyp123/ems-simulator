using EssSimulator.Protocol.Iec61850;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class Iec61850MappingTests
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

    [Fact]
    public void Mapping_CoversEveryStandardEmuParamName_AndViceVersa()
    {
        var root = FindRepoRoot();
        var emuCsv = Path.Combine(root, "pointmaps", "models", "emu", "standard", "emu.csv");
        var mappingCsv = Path.Combine(root, "pointmaps", "models", "emu", "iec61850", "mapping.csv");
        Assert.True(File.Exists(emuCsv));
        Assert.True(File.Exists(mappingCsv));

        var emuParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(emuCsv).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var cols = line.Split(',');
            if (cols.Length >= 5)
                emuParams.Add(cols[4].Trim());
        }

        var mapping = Iec61850Mapping.Load(mappingCsv);
        var mapped = mapping.Entries.Select(e => e.ParamName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(emuParams.Except(mapped, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(mapped.Except(emuParams, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(emuParams.Count, mapping.Entries.Count);
    }

    [Fact]
    public void Icd_ContainsRequiredLogicalNodes()
    {
        var icd = File.ReadAllText(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "pcs.icd"));
        foreach (var ln in new[] { "LLN0", "LPHD", "MMXU", "MMDC", "DRCC", "GGIO", "ZINV" })
            Assert.Contains(ln, icd, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AcceptsRelativeAndAbsoluteRefs()
    {
        var mapping = Iec61850Mapping.Load(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "mapping.csv"));
        const string ied = "TRNA_PCS01";
        Assert.True(mapping.TryResolve("PCS/GGIO1.SPCSO1", ied, out var yk3));
        Assert.Equal("yk3", yk3.ParamName);
        Assert.True(mapping.TryResolve("TRNA_PCS01PCS/MMXU1.TotW.mag.f", ied, out var p));
        Assert.Equal("yc27", p.ParamName);
        Assert.True(mapping.TryResolve("PCS/DRCC1.OutWSet.setMag.f", ied, out var yt0));
        Assert.Equal("yt0", yt0.ParamName);
    }

    [Fact]
    public void YkAndYt_AreGoose_YcAreMms()
    {
        var mapping = Iec61850Mapping.Load(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "mapping.csv"));
        var goose = mapping.GooseEntries.Select(e => e.ParamName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "yk2", "yk3", "yt0", "yt1", "yt2", "yt3", "yt4" })
            Assert.Contains(name, goose);
        Assert.Equal(7, goose.Count);
        Assert.All(mapping.Entries.Where(e => e.ParamName.StartsWith("yc", StringComparison.OrdinalIgnoreCase)),
            e => Assert.False(e.IsGoose));
    }

    [Fact]
    public void Icd_DeviceDoesNotPublishGoose()
    {
        var icd = File.ReadAllText(Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "pcs.icd"));
        Assert.DoesNotContain("GSEControl", icd, StringComparison.Ordinal);
        Assert.DoesNotContain("GoCB1", icd, StringComparison.Ordinal);
        Assert.DoesNotContain("dsGoose", icd, StringComparison.Ordinal);
        Assert.DoesNotContain("<GOOSE", icd, StringComparison.Ordinal);
        Assert.Contains("SPCSO2", icd, StringComparison.Ordinal);
        Assert.Contains("OutWSet", icd, StringComparison.Ordinal);
    }

    [Fact]
    public void IngressIcd_PublishesSubscribeAppId()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", Iec61850Mapping.IngressIcdFileName);
        Assert.True(File.Exists(path));
        var icd = File.ReadAllText(path);
        Assert.Contains("EMS_PCS01", icd, StringComparison.Ordinal);
        Assert.Contains("2001", icd, StringComparison.Ordinal);
        Assert.Contains("dsGoose", icd, StringComparison.Ordinal);
        Assert.Contains("SPCSO1", icd, StringComparison.Ordinal);
        Assert.Contains("OutHzSet", icd, StringComparison.Ordinal);
        Assert.Equal((ushort)0x2001, Iec61850PcsModel.SubscribeAppId(1, Iec61850PcsModel.DefaultSubscribeAppIdBase));
    }

    [Fact]
    public void Icd_StructAttributesResolveToDaType()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "iec61850", "pcs.icd");
        var doc = System.Xml.Linq.XDocument.Load(path);
        System.Xml.Linq.XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var daTypes = doc.Descendants(ns + "DAType")
            .Select(e => (string?)e.Attribute("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var doTypes = doc.Descendants(ns + "DOType")
            .Select(e => (string?)e.Attribute("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CMV", daTypes);
        Assert.DoesNotContain("CMV", doTypes);
        Assert.Contains("DEL", doTypes);
        Assert.Contains("WYE", doTypes);

        var missing = new List<string>();
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "DA" or "BDA"))
        {
            if (!string.Equals((string?)el.Attribute("bType"), "Struct", StringComparison.OrdinalIgnoreCase))
                continue;
            var typeId = (string?)el.Attribute("type");
            if (string.IsNullOrWhiteSpace(typeId) || !daTypes.Contains(typeId))
                missing.Add($"{el.Name.LocalName} {el.Attribute("name")} type={typeId}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void PointMapLoader_StillBindsStandardEmuCsv()
    {
        var path = Path.Combine(FindRepoRoot(), "pointmaps", "models", "emu", "standard", "emu.csv");
        var map = new ModbusPointMap(path, "simEmu1");
        Assert.Contains(map.ControlMaps, m => m.ParamName == "yk3");
        Assert.Contains(map.DataMaps, m => m.ParamName == "yc20");
    }
}
