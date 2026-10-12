using EssSimulator.SiteControl;

namespace EssSimulator.Tests.SiteControl;

public class SelControllerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0.69)]
    [InlineData(200, 138)]
    [InlineData(500, 345)]
    [InlineData(1000, 690)]
    public void VoltageOutsideBlackStart_ConvertsPercentToVoltsImmediately(ushort raw, double volts)
    {
        var controller = new SelController();
        Write(controller, 5, 500);
        var command = Write(controller, 2, raw);
        Assert.Equal(volts, command!.Value.VoltageV!.Value, 8);
        Assert.False(controller.IsRamping);
    }

    [Fact]
    public void BlackStartWithZeroDuration_SendsImmediately()
    {
        var controller = new SelController();
        Write(controller, 4, 1);
        Assert.Equal(690, Write(controller, 2, 1000)!.Value.VoltageV);
        Assert.False(controller.IsRamping);
    }

    [Theory]
    [InlineData(4500, 45)]
    [InlineData(5000, 50)]
    [InlineData(6500, 65)]
    public void Frequency_ConvertsHundredthsWithoutVoltageCommand(ushort raw, double frequency)
    {
        var command = Write(new SelController(), 3, raw)!.Value;
        Assert.Equal(frequency, command.FrequencyHz);
        Assert.Null(command.VoltageV);
    }

    [Fact]
    public void FiveSecondRamp_UsesCapturedStartAndSendsOncePerSecond()
    {
        var controller = PreparedController();
        Assert.Equal(20, controller.StartVoltagePercent);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(20)));
        Assert.Null(Write(controller, 2, 1000, seconds: 20));
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(20.999)));
        Assert.Equal((ushort)1000, controller.ReadRegisters(2, 1)[0]);

        double[] expected = { 248.4, 358.8, 469.2, 579.6, 690 };
        for (int i = 0; i < expected.Length; i++)
        {
            var command = controller.Tick(TimeSpan.FromSeconds(21 + i))!.Value;
            Assert.Equal(expected[i], command.VoltageV!.Value, 8);
            Assert.Null(command.FrequencyHz);
            Assert.Null(controller.Tick(TimeSpan.FromSeconds(21 + i)));
        }
        Assert.False(controller.IsRamping);
        Assert.Equal(100, controller.CurrentVoltagePercent);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RepeatedVoltage_DoesNotRestartRamp()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Null(Write(controller, 2, 1000, seconds: 1));
        Assert.Equal(358.8, controller.Tick(TimeSpan.FromSeconds(2))!.Value.VoltageV!.Value, 8);
        Assert.Equal(690, controller.Tick(TimeSpan.FromSeconds(5))!.Value.VoltageV);
    }

    [Fact]
    public void RepeatedDuration_RecapturesCurrentValueAndWaitsForChangedTarget()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        controller.Tick(TimeSpan.FromSeconds(2));
        Assert.Null(Write(controller, 5, 500, seconds: 2));
        Assert.Equal(52, controller.StartVoltagePercent);
        Assert.False(controller.IsRamping);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(10)));
        Assert.Null(Write(controller, 2, 800, seconds: 10));
        Assert.Equal(397.44, controller.Tick(TimeSpan.FromSeconds(11))!.Value.VoltageV!.Value, 8);
    }

    [Fact]
    public void RetargetDuringRamp_StartsFromLastSentValue()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        controller.Tick(TimeSpan.FromSeconds(2));
        Write(controller, 2, 800, seconds: 2);
        Assert.Equal(52, controller.StartVoltagePercent);
        Assert.Equal(397.44, controller.Tick(TimeSpan.FromSeconds(3))!.Value.VoltageV!.Value, 8);
        Assert.Equal(552, controller.Tick(TimeSpan.FromSeconds(7))!.Value.VoltageV);
    }

    [Fact]
    public void ExitMode_FreezesOutputAndReentryDoesNotResumeOldRamp()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Null(Write(controller, 4, 0, seconds: 1));
        Assert.Equal(36, controller.CurrentVoltagePercent);
        Assert.Equal((ushort)1000, controller.ReadRegisters(2, 1)[0]);
        Write(controller, 4, 1, seconds: 2);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(10)));
        Assert.False(controller.IsRamping);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 1)]
    [InlineData(250, 3)]
    public void FractionalDuration_CompletesOnNextWholeSecond(ushort duration, int completionSecond)
    {
        var controller = PreparedController(duration);
        Write(controller, 2, 1000);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(0.99)));
        if (completionSecond > 1)
        {
            Assert.True(controller.Tick(TimeSpan.FromSeconds(completionSecond - 1))!.Value.VoltageV < 690);
            Assert.True(controller.IsRamping);
        }
        Assert.Equal(690, controller.Tick(TimeSpan.FromSeconds(completionSecond))!.Value.VoltageV);
        Assert.False(controller.IsRamping);
    }

    [Fact]
    public void DelayedTick_CoalescesMissedIntervalsWithoutBurst()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        Assert.Equal(469.2, controller.Tick(TimeSpan.FromSeconds(3.8))!.Value.VoltageV!.Value, 8);
        Assert.Null(controller.Tick(TimeSpan.FromSeconds(3.9)));
        Assert.Equal(690, controller.Tick(TimeSpan.FromSeconds(6))!.Value.VoltageV);
    }

    [Fact]
    public void LowerTarget_RampsDownWithoutOvershoot()
    {
        var controller = new SelController();
        Write(controller, 2, 1000);
        Write(controller, 4, 1);
        Write(controller, 5, 500);
        Write(controller, 2, 200);
        Assert.Equal(579.6, controller.Tick(TimeSpan.FromSeconds(1))!.Value.VoltageV!.Value, 8);
        Assert.Equal(138, controller.Tick(TimeSpan.FromSeconds(5))!.Value.VoltageV);
    }

    [Fact]
    public void HeartbeatAndIgnoredRegisters_DoNotAlterRamp()
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        for (int i = 0; i < 3; i++)
        {
            Assert.Null(Write(controller, 0, 1));
            Assert.Equal((ushort)0, controller.ReadRegisters(0, 1)[0]);
        }
        Assert.Null(Write(controller, 1, ushort.MaxValue));
        Assert.Null(Write(controller, 6, ushort.MaxValue));
        Assert.Equal(248.4, controller.Tick(TimeSpan.FromSeconds(1))!.Value.VoltageV!.Value, 8);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 1001)]
    [InlineData(3, 4499)]
    [InlineData(3, 6501)]
    [InlineData(4, 2)]
    [InlineData(5, 10001)]
    public void InvalidValue_DoesNotChangeRegistersOrRamp(ushort address, ushort value)
    {
        var controller = PreparedController();
        Write(controller, 2, 1000);
        var before = controller.ReadRegisters(0, 7);
        var result = controller.TryWriteRegisters(address, new[] { value }, TimeSpan.Zero, out var command);
        Assert.Equal(SelWriteError.IllegalValue, result);
        Assert.Null(command);
        Assert.Equal(before, controller.ReadRegisters(0, 7));
        Assert.True(controller.IsRamping);
    }

    [Fact]
    public void InvalidBatch_IsRejectedBeforeAnyChange()
    {
        var controller = PreparedController();
        var before = controller.ReadRegisters(0, 7);
        ushort[] values = { 1000, 5000, 1, 10001 };
        Assert.Equal(SelWriteError.IllegalValue,
            controller.TryWriteRegisters(2, values, TimeSpan.Zero, out var command));
        Assert.Null(command);
        Assert.Equal(before, controller.ReadRegisters(0, 7));
        Assert.Equal(20, controller.CurrentVoltagePercent);
    }

    [Fact]
    public void Batch_UsesNewModeAndDurationBeforeApplyingTarget()
    {
        var controller = new SelController();
        Write(controller, 2, 200);
        ushort[] values = { 1000, 5100, 1, 500 };
        Assert.Equal(SelWriteError.None,
            controller.TryWriteRegisters(2, values, TimeSpan.Zero, out var command));
        Assert.Null(command!.Value.VoltageV);
        Assert.Equal(51, command.Value.FrequencyHz);
        Assert.True(controller.IsRamping);
        Assert.Equal(248.4, controller.Tick(TimeSpan.FromSeconds(1))!.Value.VoltageV!.Value, 8);
    }

    [Fact]
    public void InvalidAddress_IsRejected()
    {
        var controller = new SelController();
        Assert.Equal(SelWriteError.IllegalAddress,
            controller.TryWriteRegisters(7, new ushort[] { 1 }, TimeSpan.Zero, out _));
        Assert.Equal(SelWriteError.IllegalAddress,
            controller.TryWriteRegisters(6, new ushort[] { 1, 2 }, TimeSpan.Zero, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ReadRegisters(40003, 1));
    }

    private static SelController PreparedController(ushort duration = 500)
    {
        var controller = new SelController();
        Write(controller, 2, 200);
        Write(controller, 4, 1);
        Write(controller, 5, duration);
        return controller;
    }

    private static SelReferenceCommand? Write(SelController controller, ushort address, ushort value, double seconds = 0)
    {
        Assert.Equal(SelWriteError.None,
            controller.TryWriteRegisters(address, new[] { value }, TimeSpan.FromSeconds(seconds), out var command));
        return command;
    }
}
