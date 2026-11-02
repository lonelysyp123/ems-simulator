using EssSimulator.DataExchange;

namespace EssSimulator.Tests.DataExchange;

/// <summary>
/// 共享调度器把"每个设备会话 3~4 条线程"压成"每条管道 1 条线程"。
/// 这些断言锁住这个约束：线程数不随会话数增长。
/// </summary>
[Collection("SimulatorHost")] // 与会启动协议服务的用例串行，避免任务计数互相干扰
public class DataExchangeSchedulerTests
{
    [Fact]
    public void ThreadCount_StaysFixed_RegardlessOfOwnerCount()
    {
        var scheduler = DataExchangeScheduler.Instance;
        var owners = Enumerable.Range(0, 40).Select(_ => new object()).ToArray();

        // 四条管道各注册 40 个所有者：线程数应停在 4，而不是 40×4
        foreach (var owner in owners)
        {
            foreach (var pipeline in Enum.GetValues<DataExchangeScheduler.Pipeline>())
                scheduler.Register(owner, pipeline, 60_000, () => { });
        }

        Assert.Equal(4, scheduler.WorkerThreadCount);

        foreach (var owner in owners)
            scheduler.Unregister(owner);

        Assert.Equal(4, scheduler.WorkerThreadCount);
    }

    [Fact]
    public void Register_ReplacesJob_ForSameOwner()
    {
        var scheduler = DataExchangeScheduler.Instance;
        var owner = new object();
        int before = scheduler.JobCount(DataExchangeScheduler.Pipeline.RackTelemetry);

        scheduler.Register(owner, DataExchangeScheduler.Pipeline.RackTelemetry, 60_000, () => { });
        scheduler.Register(owner, DataExchangeScheduler.Pipeline.RackTelemetry, 60_000, () => { });

        Assert.Equal(before + 1, scheduler.JobCount(DataExchangeScheduler.Pipeline.RackTelemetry));

        scheduler.Unregister(owner);
        Assert.Equal(before, scheduler.JobCount(DataExchangeScheduler.Pipeline.RackTelemetry));
    }

    [Fact]
    public void Unregister_RemovesOwner_FromAllPipelines()
    {
        var scheduler = DataExchangeScheduler.Instance;
        var owner = new object();
        var pipelines = Enum.GetValues<DataExchangeScheduler.Pipeline>();
        var before = pipelines.ToDictionary(p => p, p => scheduler.JobCount(p));

        foreach (var pipeline in pipelines)
            scheduler.Register(owner, pipeline, 60_000, () => { });

        scheduler.Unregister(owner);

        foreach (var pipeline in pipelines)
            Assert.Equal(before[pipeline], scheduler.JobCount(pipeline));
    }
}
