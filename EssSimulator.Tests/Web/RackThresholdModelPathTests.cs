using EssSimulator.Core;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.Web;

namespace EssSimulator.Tests.Web;

public class RackThresholdModelPathTests : SimulatorHostTestBase
{
    private static BatteryManagementSystemData CreateBms(int clusterCount)
    {
        var bms = new BatteryManagementSystemData();
        var stack = new BatteryStack();
        for (int i = 0; i < clusterCount; i++)
            stack.Cluseter.Add(new BatteryCluster { ClusterId = i });
        bms.BatteryStacks.Add(stack);
        return bms;
    }

    [Fact]
    public void Read_FromModel_DoesNotRequirePointMap()
    {
        var bms = CreateBms(2);
        bms.BatteryStacks[0].Cluseter[0].Thresholds.CellOvervoltageThreshold1 = 3.55f;
        SimulatorHost.Instance.RegisterBms(1, bms);

        var snap = RackThresholdSnapshotReader.Read(1, 0);

        Assert.NotNull(snap);
        Assert.Equal(2, snap!.ClusterCount);
        Assert.True(snap.Points.Count >= 100);
        var cellOv = snap.Points.Single(p => p.PropertyName == "CellOvervoltageThreshold1");
        Assert.Equal(3.55, cellOv.EngineeringValue!.Value, 3);
        Assert.False(cellOv.ExposedOnProtocol);
        Assert.Null(cellOv.ProtocolParamName);
    }

    [Fact]
    public void Write_UpdatesClusterThresholdsOnTargetRacks()
    {
        var bms = CreateBms(2);
        SimulatorHost.Instance.RegisterBms(1, bms);

        var result = RackThresholdWriter.Apply(1, new RackThresholdWriteRequest
        {
            Rack = "*",
            Items =
            {
                new RackThresholdWriteItemDto
                {
                    PropertyName = "CellOvervoltageThreshold1",
                    EngineeringValue = 3.77
                }
            }
        });

        Assert.True(result.Ok);
        Assert.Equal(2, result.Written);
        Assert.Equal(3.77f, bms.BatteryStacks[0].Cluseter[0].Thresholds.CellOvervoltageThreshold1);
        Assert.Equal(3.77f, bms.BatteryStacks[0].Cluseter[1].Thresholds.CellOvervoltageThreshold1);
    }

    [Fact]
    public void Write_RejectsUnknownProperty()
    {
        SimulatorHost.Instance.RegisterBms(1, CreateBms(1));

        var result = RackThresholdWriter.Apply(1, new RackThresholdWriteRequest
        {
            Rack = "0",
            Items = { new RackThresholdWriteItemDto { PropertyName = "Nope", EngineeringValue = 1 } }
        });

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("未知门限"));
    }
}
