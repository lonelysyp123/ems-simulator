using EssSimulator.EssDeviceSimModel.Bms;

namespace EssSimulator.Tests.Devices;

public class ClusterThresholdCatalogTests
{
    [Fact]
    public void ListFields_IncludesProtectionAndRecovery_WithoutPointMapAliases()
    {
        var fields = ClusterThresholdCatalog.ListFields();

        Assert.True(fields.Count >= 100, $"门限字段过少: {fields.Count}");
        Assert.Contains(fields, f => f.PropertyName == "CellOvervoltageThreshold1");
        Assert.Contains(fields, f => f.PropertyName == "CellOvervoltageRecovery1");
        Assert.DoesNotContain(fields, f => f.PropertyName.StartsWith("LowSOCThreshold", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.PropertyName == "LowSOCTreshold1");
    }

    [Fact]
    public void ListFields_CellOvervoltageThreshold1_IsLevel3Protection()
    {
        var field = ClusterThresholdCatalog.ListFields()
            .Single(f => f.PropertyName == "CellOvervoltageThreshold1");

        Assert.Equal(3, field.Level);
        Assert.False(field.IsRecovery);
        Assert.Equal("单体过压", field.Category);
        Assert.Equal("V", field.UnitHint);
        Assert.Contains("三级保护", field.Description);
    }

    [Fact]
    public void IsKnownProperty_RejectsUnknownNames()
    {
        Assert.True(ClusterThresholdCatalog.IsKnownProperty("CellOvervoltageThreshold1"));
        Assert.False(ClusterThresholdCatalog.IsKnownProperty("NotAThreshold"));
        Assert.False(ClusterThresholdCatalog.IsKnownProperty(""));
    }
}
