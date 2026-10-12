using EssSimulator.Protocol.Modbus;
using Dpm = EssSimulator.Protocol.Modbus.AddressOverlapValidator.DevicePointMap;

namespace EssSimulator.Tests;

public class AddressOverlapValidatorTests
{
    private static MapEntry Entry(string name, int addr, int fc = 6, int size = 16) =>
        new() { ParamName = name, Address = addr, FunctionCode = fc, Size = size };

    [Fact]
    public void SameSlaveId_SameSpace_OverlappingAddresses_ReturnsConflict()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("A", 100) }),
            new Dpm("lc", 1, new[] { Entry("B", 100) })
        });

        Assert.Single(errors);
        Assert.Contains("emu", errors[0]);
        Assert.Contains("lc", errors[0]);
        Assert.Contains("100", errors[0]);
    }

    [Fact]
    public void SameSlaveId_AdjacentAddresses_NoConflict()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("A", 100, size: 32) }),  // [100, 102)
            new Dpm("lc", 1, new[] { Entry("B", 102) })               // [102, 103)
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void SameSlaveId_DifferentRegisterSpaces_NoConflict()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("H", 100, fc: 3) }),
            new Dpm("lc", 1, new[] { Entry("I", 100, fc: 4), Entry("C", 100, fc: 1) })
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void DifferentSlaveIds_SameAddress_NoConflict()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("A", 100) }),
            new Dpm("lc", 2, new[] { Entry("B", 100) })
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void SameDevice_DuplicateAddresses_NoConflict()
    {
        // 同设备内 FC1 读 / FC5 写同线圈不算跨设备冲突
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("R", 50, fc: 1, size: 1), Entry("W", 50, fc: 5, size: 1) }),
            new Dpm("lc", 1, new[] { Entry("B", 60) })
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void SizeGreaterThan16_MultiRegisterSpan_OverlapDetected()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("F32", 100, size: 32) }), // [100, 102)
            new Dpm("lc", 1, new[] { Entry("B", 101) })                // [101, 102)
        });

        Assert.Single(errors);
    }

    [Fact]
    public void UnsupportedFunctionCode_SkippedFromValidation()
    {
        var errors = AddressOverlapValidator.Validate(new[]
        {
            new Dpm("emu", 1, new[] { Entry("DI", 100, fc: 2) }),
            new Dpm("lc", 1, new[] { Entry("B", 100) })
        });

        Assert.Empty(errors);
    }
}
