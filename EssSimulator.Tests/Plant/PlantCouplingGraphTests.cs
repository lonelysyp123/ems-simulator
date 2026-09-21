using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Battery;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Interface;
using EssSimulator.EssDeviceSimModel.Plant;
using EssSimulator.EssDeviceSimModel.Thermal;
using ModelPcsDeviceConfig = EssSimulator.EssDeviceSimModel.Model.PcsDeviceConfig;

namespace EssSimulator.Tests.Plant;

public class PlantCouplingGraphTests
{
    private static PcsDevice CreatePcs(string id) =>
        PcsDeviceFactory.Create(id, new ModelPcsDeviceConfig
        {
            AcNominalLineVoltageV = 690,
            FrequencyHz = 50,
            DcVoltageRangeMinV = 1000,
            DcVoltageRangeMaxV = 1500,
            MaxCurrentA = 1100,
            RatedPowerKw = 1250,
            MaxPowerKw = 1250,
            Efficiency = 0.99,
            GridLossCoefficient = 0.01,
            RampSlope = 500,
            RampIntervalMs = 100,
            RampDelayMs = 0
        });

    [Fact]
    public void BuildDefault_PairsPcsAndBmsOneToOne()
    {
        var pcs = new List<PcsDevice> { CreatePcs("pcs1"), CreatePcs("pcs2") };
        var bms = new List<BmsRackDevice>
        {
            new("bms1", BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 })),
            new("bms2", BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 }))
        };

        var graph = PlantCouplingGraph.BuildDefault(pcs, bms);

        Assert.Equal(2, graph.DcLinks.Count);
        Assert.Same(pcs[0], graph.DcLinks[0].Pcs);
        Assert.Same(bms[1], graph.DcLinks[1].Bms);
        Assert.Equal(1, graph.DcLinks[1].ThermalCabinetIndex);
    }

    [Fact]
    public void DcLink_WhenUnlinked_RecordsZeroBatteryLoss()
    {
        var pcs = CreatePcs("pcs1");
        var bms = new BmsRackDevice("bms1", BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 }));
        bms.SetPcsLinked(false);

        var thermal = new PlantThermalSystem(
            new ThermalRuntimeConfig { Enabled = true, Climate = new ClimateConfig { FixedCelsius = 22 } },
            bmsChannelCount: 1,
            initialTime: DateTime.UtcNow);

        var link = new PcsBmsDcCouplingLink(pcs, bms, 0);
        link.Step(thermal, DateTime.UtcNow, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, ((IElectricalLossSource)bms).GetElectricalLossWatts());
        Assert.Equal(0, thermal.Cabinets[0].PendingBatteryHeatW); // 已在 Step 内登记并会在下一次热步消费；此处刚登记后被置入 Pending
    }

    [Fact]
    public void Bms_GetElectricalLoss_ScalesWithCurrent()
    {
        var bms = new BmsRackDevice("bms1", BmsRackFactory.CreateRack(new BmsDeviceConfig
        {
            ClusterCount = 2,
            PackCount = 2,
            CellSeriesCount = 10
        }));

        bms.UpdatePhysics(50, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
        double p1 = ((IElectricalLossSource)bms).GetElectricalLossWatts();
        bms.UpdatePhysics(100, 25, DateTime.UtcNow, TimeSpan.FromMilliseconds(100));
        double p2 = ((IElectricalLossSource)bms).GetElectricalLossWatts();

        Assert.True(p1 > 0);
        Assert.InRange(p2 / p1, 3.9, 4.1);
    }

    [Fact]
    public void Pcs_ImplementsThermalElectricalPorts()
    {
        var pcs = CreatePcs("pcs1");
        ITemperatureAware t = pcs;
        IElectricalLossSource loss = pcs;
        t.ApplyAmbientTemperature(30);
        Assert.True(loss.GetElectricalLossWatts() >= 0);
        Assert.True(t.TemperatureCelsius > 0);
    }

    private static PlantThermalSystem CreateThermal() =>
        new(
            new ThermalRuntimeConfig { Enabled = true, Climate = new ClimateConfig { FixedCelsius = 22 } },
            bmsChannelCount: 1,
            initialTime: DateTime.UtcNow);

    private static BmsRackDevice CreateLinkedBms(string id)
    {
        var bms = new BmsRackDevice(id, BmsRackFactory.CreateRack(new BmsDeviceConfig { ClusterCount = 1 }));
        bms.SetPcsLinked(true);
        return bms;
    }

    [Fact]
    public void DcLink_DrivesBmsFromNetworkSettledDc_NotControlDcCurrent()
    {
        var pcs = CreatePcs("pcs1");
        var bms = CreateLinkedBms("bms1");
        var thermal = CreateThermal();
        // 内核结算：流入设备 +120 A（放电）。BMS 堆电流约定充电为正，故应为 -120 A。
        var link = new PcsBmsDcCouplingLink(pcs, bms, 0, _ => 120);

        link.Step(thermal, DateTime.UtcNow, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(-120, bms.Rack.GetRackState()!.TotalCurrent, 6);
    }

    [Fact]
    public void DcLink_WhenNotSettledThisTick_DoesNotAdvanceSocFromControl()
    {
        var pcs = CreatePcs("pcs1");
        var bms = CreateLinkedBms("bms1");
        var thermal = CreateThermal();
        var link = new PcsBmsDcCouplingLink(pcs, bms, 0, _ => null);

        link.Step(thermal, DateTime.UtcNow, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, bms.Rack.GetRackState()!.TotalCurrent, 6);
    }

    [Fact]
    public void DcLink_WithoutResolver_FallsBackToControlDcCurrent()
    {
        var pcs = CreatePcs("pcs1");
        var bms = CreateLinkedBms("bms1");
        var thermal = CreateThermal();
        var link = new PcsBmsDcCouplingLink(pcs, bms, 0);

        link.Step(thermal, DateTime.UtcNow, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(-pcs.GetCurrentState().DcCurrent, bms.Rack.GetRackState()!.TotalCurrent, 6);
    }
}
