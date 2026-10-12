using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.Protocol;

public class ModbusFloatCodecTests
{
    [Fact]
    public void DataUnTranslation_and_Translation_roundtrip_ieee754_single()
    {
        const float expected = 36.5f;
        var le = Common.DataUnTranslation(expected, "System.Single");
        Assert.Equal(4, le.Length);
        Assert.Equal(BitConverter.GetBytes(expected), le);

        var decoded = Common.DataTranslation(le, 0, 32, "System.Single");
        Assert.Equal(expected, (float)decoded);
    }

    [Fact]
    public void Encode_uses_cdab_word_swap_like_int32()
    {
        const float value = 25.5f;
        var entry = FloatPoint("yc0");
        var registerBytes = ModbusPointCodec.Encode(value, entry, applyScale: true);

        var le = BitConverter.GetBytes(value);
        var expectedCdab = Common.ConverByteOrder(le, "CDAB");
        Assert.Equal(expectedCdab, registerBytes);
    }

    [Fact]
    public void Decode_reads_cdab_float_register_bytes()
    {
        const float value = -12.25f;
        var entry = FloatPoint("yc0");
        var encoded = ModbusPointCodec.Encode(value, entry, applyScale: true);
        var decoded = ModbusPointCodec.Decode(encoded, entry);
        Assert.Equal(value, Convert.ToSingle(decoded), precision: 5);
    }

    [Fact]
    public void Parser_DataParse_uses_type_float_not_int32()
    {
        var entry = FloatPoint("yc0");
        var parser = new ModbusParser(new List<MapEntry[]> { new[] { entry } });
        const float value = 690.5f;
        var raw = ModbusPointCodec.Encode(value, entry, applyScale: true);

        var parsed = parser.DataParse(new Dictionary<string, object> { ["yc0"] = raw });
        Assert.True(parsed.TryGetValue("yc0", out var actual));
        Assert.Equal(value, Convert.ToSingle(actual), precision: 5);
    }

    [Fact]
    public void Encode_applies_scale_without_integer_rounding()
    {
        var entry = FloatPoint("yc0");
        entry.Scale = 10;
        var encoded = ModbusPointCodec.Encode(3.14, entry, applyScale: true);
        var decodedRaw = BitConverter.ToSingle(Common.ConverByteOrder(encoded, "CDAB"), 0);
        Assert.Equal(31.4f, decodedRaw, precision: 4);
    }

    [Fact]
    public void Int32_cdab_roundtrip_unchanged()
    {
        var entry = new MapEntry
        {
            FunctionCode = 4,
            Address = 100,
            Type = "int32",
            Size = 32,
            ParamName = "yc1",
            Scale = 1
        };
        const int value = 123456;
        var encoded = ModbusPointCodec.Encode(value, entry, applyScale: true);
        Assert.Equal(4, encoded.Length);
        var decoded = ModbusPointCodec.Decode(encoded, entry);
        Assert.Equal(value, Convert.ToInt32(Convert.ToDouble(decoded)));
    }

    [Fact]
    public void Encode_int16_saturates_instead_of_throwing()
    {
        var entry = new MapEntry
        {
            FunctionCode = 4,
            Address = 16,
            Type = "int16",
            Size = 16,
            ParamName = "unit_param16",
            Scale = 1
        };

        var encoded = ModbusPointCodec.Encode(50_000, entry, applyScale: true);
        var decoded = ModbusPointCodec.Decode(encoded, entry);
        Assert.Equal(short.MaxValue, Convert.ToInt32(Convert.ToDouble(decoded)));

        encoded = ModbusPointCodec.Encode(-40_000, entry, applyScale: true);
        decoded = ModbusPointCodec.Decode(encoded, entry);
        Assert.Equal(short.MinValue, Convert.ToInt32(Convert.ToDouble(decoded)));
    }

    private static MapEntry FloatPoint(string name) => new()
    {
        FunctionCode = 4,
        Address = 8193,
        Type = "float",
        Size = 32,
        ParamName = name,
        Scale = 1,
        Description = "IEEE754 single"
    };
}
