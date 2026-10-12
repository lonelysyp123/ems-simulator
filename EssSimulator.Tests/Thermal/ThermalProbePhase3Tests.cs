using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Thermal;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.Thermal;

public class ThermalProbeSamplerTests
{
    [Fact]
    public void AirTop_WarmerThanAirBottom_WhenBatteryHotterThanShell()
    {
        var zone = new BmsCabinetThermalZone("bms1", new BmsCabinetThermalConfig(), 25);
        // 人工推高电池、压低外壳，制造空间温差
        for (int i = 0; i < 50; i++)
        {
            zone.PendingBatteryHeatW = 8000;
            zone.Step(TimeSpan.FromSeconds(1), outdoorCelsius: 10);
        }

        double top = ThermalProbeSampler.ReadCelsius(zone, ThermalProbeKind.AirTop);
        double bottom = ThermalProbeSampler.ReadCelsius(zone, ThermalProbeKind.AirBottom);
        double air = ThermalProbeSampler.ReadCelsius(zone, ThermalProbeKind.Air);

        Assert.True(zone.BatteryNodeCelsius > zone.ShellCelsius);
        Assert.True(top > bottom);
        Assert.InRange(air, bottom - 0.1, top + 0.1);
    }
}

public class BmsThermalProbeMapperTests
{
    /// <summary>
    /// common 版点表绝对路径。根目录 bms_bank.csv 可能随交付版本切换（如 LC 版不含空调/液冷点），
    /// 本测试固定验证 pointmaps/models/bms/standard 版本（原 pointmaps/common），不能依赖复制到 bin 的运行时点表。
    /// </summary>
    private static string CommonBankCsvPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.sln")))
                    return Path.Combine(dir.FullName, "pointmaps", "models", "bms", "standard", "bms_bank.csv");
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("找不到仓库根目录");
        }
    }

    [Fact]
    public void Apply_WritesDistinctProbeTempsToDto()
    {
        var thermal = new PlantThermalSystem(
            new ThermalRuntimeConfig
            {
                Enabled = true,
                Climate = new ClimateConfig { FixedCelsius = 20 },
                ProbeBiases = new ThermalProbeBiasesConfig
                {
                    DehumidifierTopCelsius = 1.0,
                    DehumidifierBottomCelsius = -1.0,
                    CoilCelsius = -2.0,
                    CondenserCelsius = 3.0
                }
            },
            bmsChannelCount: 1,
            initialTime: DateTime.UtcNow);

        var zone = thermal.Cabinets[0];
        for (int i = 0; i < 80; i++)
        {
            zone.PendingBatteryHeatW = 6000;
            thermal.Step(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            // Step 清空 Pending；下一圈再注热
            zone.PendingBatteryHeatW = 6000;
        }

        var bms = new BatteryManagementSystemData();
        BmsThermalProbeMapper.Apply(thermal, 0, bms);

        Assert.Single(bms.AirConditioners);
        Assert.Single(bms.LiquidCoolingSystems);
        Assert.Equal(3, bms.TempHumiditySensors.Count);

        Assert.NotNull(bms.AirConditioners[0].CabinetTemp);
        Assert.NotNull(bms.AirConditioners[0].DefrostTemp);
        Assert.NotNull(bms.AirConditioners[0].CondensationTemp);
        Assert.True(bms.TempHumiditySensors[0].Temperature > bms.TempHumiditySensors[2].Temperature);
        Assert.NotEqual(
            bms.LiquidCoolingSystems[0].SupplyLiquidTemp,
            bms.LiquidCoolingSystems[0].ReturnLiquidTemp);
    }

    [Fact]
    public void FromPointMap_BindsCabinetAndProbeTemps()
    {
        var pointMap = new EssSimulator.Protocol.Modbus.ModbusPointMap(CommonBankCsvPath, "simBms1", clusterCount: 12);
        var catalog = EssSimulator.DataExchange.Catalog.PointCatalogLoader.FromPointMap(
            pointMap, "simBms1", new EssSimulator.DataExchange.Config.DataExchangeOptions());

        var yc71 = catalog.TelemetryPoints.First(p => p.ParamName == "yc71");
        Assert.Equal("AirConditioners[0].CabinetTemp", yc71.Target.PropertyPath);

        var yc56 = catalog.TelemetryPoints.First(p => p.ParamName == "yc56");
        Assert.Equal("LiquidCoolingSystems[0].SupplyLiquidTemp", yc56.Target.PropertyPath);

        var yc93 = catalog.TelemetryPoints.First(p => p.ParamName == "yc93");
        Assert.Equal("TempHumiditySensors[0].Temperature", yc93.Target.PropertyPath);
    }
}
