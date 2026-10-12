using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace EssSimulator.EssSimModelApi
{
    public static class BmsDataGenerator
    {
        private static readonly Random random = new Random();

        public static BatteryManagementSystemData GenerateSampleData(int stackCount = 1, int clustersPerStack = 5)
        {
            var bmsData = new BatteryManagementSystemData
            {
                Timestamp = DateTime.Now
            };

            //var objectsCollect = ObjectsCollect.Instance;

            // 生成电池堆数据
            for (int i = 0; i < stackCount; i++)
            {
                var stack = new BatteryStack
                {
                    StackId = i + 1,
                    GridConnectStatus = 2,
                    GridConnectCommand = 0,
                    BlackStartCommand = 0,
                    BlackStartEnterSuccess = 0,
                    BlackStartStatus = 0,
                    IsPcsLinked = true,
                    TotalVoltage = 750 + random.Next(-50, 50),
                    Current = 100 + random.Next(-20, 20),
                    Power = 75000 + random.Next(-10000, 10000),
                    SOC = 80 + random.Next(-10, 10),
                    SOH = 95 + random.Next(-5, 5),
                    OperationStatus = random.Next(0, 5)
                };

                // 生成电池簇数据
                for (int j = 0; j < clustersPerStack; j++)
                {
                    var cluster = GenerateClusterData(j + 1);
                    stack.Cluseter.Add(cluster);
                    //添加到对象管理器
                    //objectsCollect.AddObjects("batCluseter[" + j.ToString()+']', cluster);
                }
                bmsData.BatteryStacks.Add(stack);
                //objectsCollect.AddObjects("batStack", stack);
                //WriteBmsApiClusterClassPropertiesToFile();
                //WriteBmsApiStackClassPropertiesToFile();
            }

            // 生成辅助系统数据
            //bmsData.AirConditioners.Add(GenerateAirConditionerData(1));
            //bmsData.FireProtectionSystems.Add(GenerateFireProtectionData(1));
            //bmsData.LiquidCoolingSystems.Add(GenerateLiquidCoolingData(1));
            //bmsData.ElectricityMeters.Add(GenerateElectricityMeterData(1));
            //bmsData.TempHumiditySensors.Add(GenerateTempHumidityData(1));
            //bmsData.IOStatus = GenerateIOStatusData();
            //bmsData.CommunicationStatus = GenerateCommStatusData();

            return bmsData;
        }

        private static BatteryCluster GenerateClusterData(int clusterId)
        {
            var cluster = new BatteryCluster
            {
                ClusterId = clusterId,
                Measurements = new ClusterBasicMeasurements()
                {
                    Insulation = 1500,
                    InsulationMinus = 750,
                    InsulationPlus = 750,
                    HVB1Temp = 25 + random.Next(-3, 3),
                    HVB2Temp = 25 + random.Next(-3, 3),
                },
                Alarms = new ClusterAlarms()
                // Mild/Moderate/Severe 保持默认 false；由真实保护评估或外部写入置位，避免启动随机假告警。
            };

            // 生成单体电压数据
            for (int i = 0; i < 416; i++)
            {
                cluster.ClusterCellVoltages.CellVoltages[i] = 3.6f + random.Next(-20, 20) * 0.01f;
            }

            // 生成温度数据
            for (int i = 0; i < 208; i++)
            {
                cluster.ClusterCellTemperatures.CellTemperatures[i] = 25 + random.Next(-5, 5);
            }

            for (int i = 0; i < 16; i++)
            {
                cluster.ClusterCellTemperatures.PositivePoleTemperatures[i] = 25 + random.Next(-5, 5);
            }

            return cluster;
        }

        private static AirConditionerData GenerateAirConditionerData(int unitId)
        {
            return new AirConditionerData
            {
                UnitId = unitId,
                CabinetTemp = 25 + random.Next(-3, 3),
                CabinetHumidity = 50 + random.Next(-10, 10),
                CompressorStatus = random.NextDouble() > 0.3
            };
        }

       
        private static void WriteBmsApiStackClassPropertiesToFile()
        {
            try
            {
                string filePath = "batStackProperties.txt";
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    //堆定义字段信息
                    foreach (PropertyInfo property in typeof(BatteryStack).GetProperties())
                    {
                        writer.WriteLine($"model=4|arg1=batStack.{property.Name}|arg2 = |arg3 = |arg4=1000");
                    }
                    //告警字段信息
                    foreach (PropertyInfo property in typeof(BatteryStack).GetProperties())
                    {
                        writer.WriteLine($"model=4|arg1=batStack.{property.Name}|arg2 = |arg3 = |arg4=1000");
                    }
                }
                Console.WriteLine($"成功写入文件: {Path.GetFullPath(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("错误: 没有文件写入权限");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"文件写入错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
        }

        private static void WriteBmsApiClusterClassPropertiesToFile()
        {
            try
            {
                string filePath = "batClusterProperties.txt";
                using (StreamWriter writer = new StreamWriter(filePath,true))
                {
                    for(int clusterid = 0; clusterid < 10; clusterid++)
                    {
                        //簇基础测量信息
                        foreach (PropertyInfo property in typeof(ClusterBasicMeasurements).GetProperties())
                        {
                            writer.WriteLine($"model=4|arg1=batCluseter[{clusterid}].Measurements.{property.Name}|arg2 = |arg3 = |arg4=1000");
                        }
                    }
                    for (int clusterid = 0; clusterid < 10; clusterid++)
                    {
                        //单体电压
                        for (int i = 0; i < 416; i++)
                        {
                            writer.WriteLine($"model=4|arg1=batCluseter[{clusterid}].ClusterCellVoltages.CellVoltages[{i}]|arg2 = |arg3 = |arg4=1000");
                        }
                        //单体温度
                        for (int i = 0; i < 208; i++)
                        {
                            writer.WriteLine($"model=4|arg1=batCluseter[{clusterid}].ClusterCellTemperatures.CellTemperatures[{i}]|arg2 = |arg3 = |arg4=1000");
                        }
                        //极柱温度
                        for (int i = 0; i < 16; i++)
                        {
                            writer.WriteLine($"model=4|arg1=batCluseter[{clusterid}].ClusterCellTemperatures.PoleTemperatures[{i}]|arg2 = |arg3 = |arg4=1000");
                        }
                    }

                    for(int clusterid = 0; clusterid < 10; clusterid++)
                    {
                        //簇告警信息
                        foreach (PropertyInfo property in typeof(ClusterAlarms).GetProperties())
                        {
                            writer.WriteLine($"model=4|arg1=batCluseter[{clusterid}].Alarms.{property.Name}|arg2 = |arg3 = |arg4=1000");
                        }
                    }


                }
                Console.WriteLine($"成功写入文件: {Path.GetFullPath(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("错误: 没有文件写入权限");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"文件写入错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
        }
        // 其他Generate方法类似...
    }
}
