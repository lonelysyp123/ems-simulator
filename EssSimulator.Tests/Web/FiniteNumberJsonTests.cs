using System.Text.Json;
using EssSimulator.Display;
using EssSimulator.Web;

namespace EssSimulator.Tests.Web;

public class FiniteNumberJsonTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        FiniteNumberJson.Apply(options);
        return options;
    }

    [Fact]
    public void Write_NonFinite_BecomesNull_AndKeepsFiniteNumbers()
    {
        var options = Options();
        string json = JsonSerializer.Serialize(new NumberProbe
        {
            Value = double.NaN,
            Positive = double.PositiveInfinity,
            Negative = double.NegativeInfinity,
            Zero = 0,
            Scaled = 1.5,
            Ratio = float.NaN,
            RatioOk = 0.5f
        }, options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("positive").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("negative").ValueKind);
        Assert.Equal(0, root.GetProperty("zero").GetDouble());
        Assert.Equal(1.5, root.GetProperty("scaled").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ratio").ValueKind);
        Assert.Equal(0.5, root.GetProperty("ratioOk").GetDouble(), 6);
    }

    [Fact]
    public void Read_NullAndNonFinite_BecomeZero()
    {
        var options = Options();
        var fromNull = JsonSerializer.Deserialize<NumberProbe>("{\"value\":null,\"ratio\":null}", options);

        Assert.NotNull(fromNull);
        Assert.Equal(0, fromNull!.Value);
        Assert.Equal(0, fromNull.Ratio);
        Assert.Equal(0, FiniteNumberJson.Coerce(double.NaN));
        Assert.Equal(0, FiniteNumberJson.Coerce(double.PositiveInfinity));
        Assert.Equal(0, FiniteNumberJson.Coerce(double.NegativeInfinity));
        Assert.Equal(1.5, FiniteNumberJson.Coerce(1.5));
        Assert.Equal(0f, FiniteNumberJson.Coerce(float.NaN));
        Assert.Equal(0.25f, FiniteNumberJson.Coerce(0.25f));
    }

    [Fact]
    public void Warn_SameProperty_LogsOnce()
    {
        FiniteNumberJson.ResetWarningsForTests();
        var options = new JsonSerializerOptions();
        FiniteNumberJson.Apply(options);
        var probe = new NumberProbe { Value = double.NaN };

        JsonSerializer.Serialize(probe, options);
        JsonSerializer.Serialize(probe, options);

        Assert.True(FiniteNumberJson.WasLogged("NumberProbe.Value"));
        Assert.Equal(1, FiniteNumberJson.LoggedCount);
    }

    [Fact]
    public void CollectionElement_UsesSharedKey()
    {
        FiniteNumberJson.ResetWarningsForTests();
        var options = new JsonSerializerOptions();
        FiniteNumberJson.Apply(options);

        JsonSerializer.Serialize(new[] { double.NaN, double.PositiveInfinity }, options);

        Assert.True(FiniteNumberJson.WasLogged(FiniteNumberJson.ElementKey));
        Assert.Equal(1, FiniteNumberJson.LoggedCount);
    }

    [Fact]
    public void AcPhasorSnapshot_NaNVoltage_Serializes()
    {
        var options = Options();
        string json = JsonSerializer.Serialize(new AcPhasorSnapshot(double.NaN, 10, 0, 50), options);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("lineVoltageV").ValueKind);
    }

    public sealed class NumberProbe
    {
        public double Value { get; set; }
        public double Positive { get; set; }
        public double Negative { get; set; }
        public double Zero { get; set; }
        public double Scaled { get; set; }
        public float Ratio { get; set; }
        public float RatioOk { get; set; }
    }
}
