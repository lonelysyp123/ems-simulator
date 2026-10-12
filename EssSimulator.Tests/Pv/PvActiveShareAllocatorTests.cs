using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.Tests.Pv;

public class PvActiveShareAllocatorTests
{
    [Fact]
    public void OneSideShort_OtherFillsToCap()
    {
        AssertShare([320, 80], (200, 320, true), (200, 80, true));
    }

    [Fact]
    public void BothShort_StopAtCaps()
    {
        AssertShare([250, 80], (200, 250, true), (200, 80, true));
    }

    [Fact]
    public void BothAble_KeepCommands()
    {
        AssertShare([100, 100], (100, 320, true), (100, 320, true));
    }

    [Fact]
    public void Shortfall_SplitsAcrossTwoWithHeadroom()
    {
        AssertShare([40, 130, 130], (100, 40, true), (100, 200, true), (100, 200, true));
    }

    [Fact]
    public void NonParticipating_DoesNotDonateCommand()
    {
        AssertShare([0, 200], (200, 0, false), (200, 320, true));
    }

    private static void AssertShare(
        double[] expected,
        params (double Commanded, double Available, bool Participating)[] inputs)
    {
        var requests = new PvActiveShareRequest[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            requests[i] = new PvActiveShareRequest(
                inputs[i].Commanded, inputs[i].Available, inputs[i].Participating);
        }

        double[] actual = PvActiveShareAllocator.Allocate(requests);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], 6);
    }
}
