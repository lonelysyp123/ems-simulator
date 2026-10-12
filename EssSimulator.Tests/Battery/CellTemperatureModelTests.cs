using EssSimulator.EssDeviceSimModel;
using Xunit;

namespace EssSimulator.Tests.Battery;

/// <summary>
/// 电芯温度模型测试：
/// 1) 每芯独立温度；2) 电芯热环境为电池节点温度；3) 节点温度越高散热效率越低（稳态温升越大）。
/// </summary>
public class CellTemperatureModelTests
{
    private static LiFePO4CellSimulator CreateCell(double internalResistance = 0.01, double mass = 0.05)
    {
        var spec = new CellSpecifications
        {
            NominalCapacity = 100,
            NominalVoltage = 3.2,
            MinVoltage = 2.5,
            MaxVoltage = 3.65,
            InitialSOC = 0.5,
            InternalResistance = internalResistance,
            Mass = mass,
            Volume = 0.0001
        };
        return new LiFePO4CellSimulator(spec);
    }

    [Fact]
    public void EachCell_HasIndependentTemperature()
    {
        // 两个相同规格的电芯，一个流过电流一个不流 → 温度各自演化、互不影响
        var heated = CreateCell();
        var idle = CreateCell();

        for (int i = 0; i < 600; i++)
        {
            heated.Update(10, nodeTempC: 30, DateTime.UtcNow, TimeSpan.FromSeconds(1));
            idle.Update(0, nodeTempC: 30, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        // 有电流的芯温升更大；无电流的芯向节点温度收敛
        Assert.True(heated.GetCurrentState().Temperature > idle.GetCurrentState().Temperature);
        Assert.InRange(idle.GetCurrentState().Temperature, 29.0, 31.0);
    }

    [Fact]
    public void CellTemp_ConvergesToNodeTempPlusOhmicRise()
    {
        var cell = CreateCell(internalResistance: 0.01);
        // 10A → P=1W；节点 40°C，CoolFactor(40)=1-(40-25)/30*0.7=0.65 → h=0.325
        // 稳态 = 40 + 1/0.325 ≈ 43.08
        for (int i = 0; i < 1200; i++)
            cell.Update(10, nodeTempC: 40, DateTime.UtcNow, TimeSpan.FromSeconds(1));

        Assert.InRange(cell.GetCurrentState().Temperature, 42.0, 44.2);
    }

    [Fact]
    public void HigherNodeTemp_SlowsCooling_SoCellRiseAboveNodeIsLarger()
    {
        var cold = CreateCell(internalResistance: 0.01); // 节点 25°C，CoolFactor=1
        var hot = CreateCell(internalResistance: 0.01);  // 节点 55°C，CoolFactor=0.3

        for (int i = 0; i < 1200; i++)
        {
            cold.Update(10, nodeTempC: 25, DateTime.UtcNow, TimeSpan.FromSeconds(1));
            hot.Update(10, nodeTempC: 55, DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        double tCold = cold.GetCurrentState().Temperature;
        double tHot = hot.GetCurrentState().Temperature;

        // 冷节点温升 P/(0.5*1)=2°C；热节点温升 P/(0.5*0.3)≈6.67°C
        Assert.InRange(tCold - 25, 1.5, 2.5);
        Assert.InRange(tHot - 55, 5.5, 7.8);
        // 散热越差 → 相对节点温升越大
        Assert.True((tHot - 55) > (tCold - 25));
    }
}
