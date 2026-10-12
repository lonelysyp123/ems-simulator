using EssSimulator.DataExchange.Pipeline;

namespace EssSimulator.Tests.DataExchange;

public class ControlDrainCoalescerTests
{
    [Fact]
    public void Burst_Collapses_And_Runs_A_FollowUp_For_Writes_During_Drain()
    {
        var coalescer = new ControlDrainCoalescer();
        int drains = 0;
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var sawRacks = new ManualResetEventSlim(false);
        try
        {
            void Drain(bool bank, IReadOnlyList<int> racks)
            {
                Interlocked.Increment(ref drains);
                if (racks.Count > 0)
                {
                    sawRacks.Set();
                    return;
                }

                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }

            for (int i = 0; i < 30; i++)
                coalescer.SignalBank(Drain);

            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < 8; i++)
                coalescer.SignalRack(i, Drain);
            release.Set();

            Assert.True(sawRacks.Wait(TimeSpan.FromSeconds(5)));
            Assert.InRange(Volatile.Read(ref drains), 2, 3);
        }
        finally
        {
            release.Set();
            started.Dispose();
            release.Dispose();
            sawRacks.Dispose();
        }
    }
}
