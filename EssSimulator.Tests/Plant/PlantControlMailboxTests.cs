using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests.Plant;

[Collection("SimulatorHost")]
public class PlantControlMailboxTests
{
    [Fact]
    public void Submit_RunsImmediately_WhenDeferralIsOff()
    {
        PlantControlMailbox.Reset();
        int runs = 0;
        PlantControlMailbox.Submit("emu1.P", () => runs++);
        Assert.Equal(1, runs);
        Assert.False(PlantControlMailbox.IsPending("emu1.P"));
    }

    [Fact]
    public void Flush_KeepsOnlyTheLatestWrite_ForTheSamePath()
    {
        PlantControlMailbox.Reset();
        PlantControlMailbox.DeferralEnabled = true;
        try
        {
            int value = 0;
            int runs = 0;
            PlantControlMailbox.Submit("emu1.P", () =>
            {
                runs++;
                value = 10;
            });
            PlantControlMailbox.Submit("emu1.P", () =>
            {
                runs++;
                value = 20;
            });

            Assert.True(PlantControlMailbox.IsPending("emu1.P"));
            Assert.Equal(0, value);

            PlantControlMailbox.Flush();

            Assert.Equal(1, runs);
            Assert.Equal(20, value);
            Assert.False(PlantControlMailbox.IsPending("emu1.P"));
        }
        finally
        {
            PlantControlMailbox.Reset();
        }
    }

    [Fact]
    public void Flush_SkipsStaleApply_WhenANewerValueArrivesMidFlush()
    {
        PlantControlMailbox.Reset();
        PlantControlMailbox.DeferralEnabled = true;
        try
        {
            int value = 0;
            PlantControlMailbox.Submit("emu1.P", () =>
            {
                PlantControlMailbox.Submit("emu1.P", () => value = 20);
                if (PlantControlMailbox.IsSuperseded("emu1.P"))
                    return;
                value = 10;
            });

            PlantControlMailbox.Flush();
            Assert.Equal(0, value);
            Assert.True(PlantControlMailbox.IsPending("emu1.P"));

            PlantControlMailbox.Flush();
            Assert.Equal(20, value);
            Assert.False(PlantControlMailbox.IsPending("emu1.P"));
        }
        finally
        {
            PlantControlMailbox.Reset();
        }
    }
}
