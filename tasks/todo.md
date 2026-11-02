# 分域励磁电流、漏抗压降与命令退出

> 任务 1–35 已完成。本清单从 Task 36 起。未获确认前不改实现。
> 验证命令：`dotnet test EssSimulator.Tests --filter "<过滤>" --nologo --no-restore`
> 构建：`dotnet build EssSimulator.csproj --nologo`

## Task 36: 双耳铁损按分裂比例计入励磁导纳

**Description:** `EarLeakageConfig` 已把容量乘上 `SplitRatio`，空载损耗仍是整台的值，双耳单元的铁损导纳因此变成两倍。励磁导纳使用的铁损改为乘同一比例。漏抗用的容量和阻抗百分数不变。

**Acceptance criteria:**
- [x] 左右耳铁损之和等于该双耳变的铭牌空载损耗
- [x] 左右耳无功之和仍是空载电流百分比乘额定容量
- [x] 单绕组单元的 `MagnetizingAdmittance` 与改前一致

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~MagnetizingShuntTests|FullyQualifiedName~RadialIslandShadowTests" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: `LeakageOhmLv` 不读取 `NoLoadLossKw`

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Shadow/RadialIslandShadowAdapter.cs`
- `EssSimulator.Tests/Shadow/RadialIslandShadowTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 37: 690 V 岛能贴入固定注入电流

**Description:** 为共享励磁准备内核入口。`PhasorNetwork.AddInjectedCurrent` 已存在。岛求解、快照和 runner 增加一相固定注入电流，每拍可更新，重建 runner 的源键不包含它。本任务不改变现有励磁导纳的分配。

**Acceptance criteria:**
- [x] 单电压源加注入电流后，源流出电流与注入电流大小相等、方向相反（流入母线为正）
- [x] 注入为零时，现有无分流 KCL 结果不变
- [x] 已有 runner 在源集合不变时，下一拍能换上新的注入电流

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~MagnetizingShuntTests|FullyQualifiedName~IsolatedAcIsland" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: 并网岛快照的注入电流为空，求解不多出电流

**Dependencies:** None

**Files likely touched:**
- `EssDeviceSimModel/Kernel/IsolatedAcIsland.cs`
- `EssDeviceSimModel/Kernel/IslandShadowSnapshot.cs`
- `EssDeviceSimModel/Kernel/IslandShadowRunner.cs`
- `EssSimulator.Tests/Kernel/MagnetizingShuntTests.cs`

**Estimated scope:** Medium: 3-5 files

## Checkpoint: After Tasks 36–37

- [x] 上述过滤测试通过
- [x] `dotnet build EssSimulator.csproj --nologo` 成功
- [ ] 生产路径的励磁分配尚未改变

## Task 38: 主变与邻居励磁改为按 35 kV 电压分配的电流

**Description:** 本岛对地导纳只保留本单元变。离网且 35 kV 已带电时，主变和未构网邻居的励磁改成按计划中的 `I_i` 计算，经 Task 37 的入口贴入。`ProductionUnit690Host` 对已有 runner 每拍更新该电流，与现有 `SetBusShunt` 相同，避免第一次求解看到 35 kV 为零后支路不再更新。并网岛不贴这段电流。

**Acceptance criteria:**
- [x] 35 kV 未带电时，对地导纳仍只有本单元变，共享电流为零
- [x] 单岛、35 kV 为额定折算值时，对地导纳不含主变，注入电流等于 `-Y_main × V_lv`
- [x] 两岛电压同为额定、漏抗相同、高压都合闸时，两岛电流大小各为主变电流的一半，总和等于主变电流
- [x] 一岛接近额定、另一岛电压很低时，两岛电流之和仍等于 `Y_main × V35` 折到低压的电流，不因低电压岛被计成构网而少掉一半
- [x] 并网捕获结果的共享电流为空

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~RadialIslandShadowTests|FullyQualifiedName~MagnetizingShuntTests" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: `Capture_Live35kV_FormingIslandCarriesMainTransformerExcitation` 改为断言电流，不再断言导纳含主变

**Dependencies:** Task 37

**Files likely touched:**
- `EssDeviceSimModel/Shadow/RadialIslandShadowAdapter.cs`
- `EssDeviceSimModel/Shadow/ProductionUnit690Host.cs`
- `EssDeviceSimModel/Kernel/IslandShadowRunner.cs`
- `EssSimulator.Tests/Shadow/RadialIslandShadowTests.cs`

**Estimated scope:** Medium: 3-5 files

## Task 39: 35 kV 第二次提交计入励磁电流的漏抗压降

**Description:** 共享电流流过单元变漏抗，压降落在 35 kV 上。第二次 `CommitBus35` 使用各构网岛已经贴入的 `I_i` 和 `LeakageOhmLv`，把加权电压改成 `V690 − I_i·Z_leak` 再除以变比。第一次提交时电流仍为零。不在 690 V 网络里再串漏抗，也不增加第三次求解。

**Acceptance criteria:**
- [x] 空载、两岛电压接近额定时，35 kV 低于理想变比折算值，差额与 `|I_i·Z_leak|` 折到高压后同量级
- [x] 共享电流为零时，35 kV 仍是现有的导纳加权电压
- [x] 并网 `GridTiedBus35` 的开路电压路径不变

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~StationBoundaryCompareTests|FullyQualifiedName~Unit690KernelTakeoverTests|FullyQualifiedName~RadialIslandShadowTests" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: 690 V 岛的串联支路仍只有 PCS 输出阻抗和上域戴维南，没有第二段单元变漏抗

**Dependencies:** Task 38

**Files likely touched:**
- `EssDeviceSimModel/Shadow/StationBoundaryCompare.cs`
- `EssDeviceSimModel/Shadow/ProductionUnit690Host.cs`
- `EssSimulator.Tests/Shadow/StationBoundaryCompareTests.cs`

**Estimated scope:** Medium: 3-5 files

## Checkpoint: After Tasks 38–39

- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~EssSimulator.Tests.Devices|FullyQualifiedName~EssSimulator.Tests.Plant|FullyQualifiedName~EssSimulator.Tests.Kernel|FullyQualifiedName~EssSimulator.Tests.Shadow|FullyQualifiedName~EssSimulator.Tests.Propagation" --nologo --no-restore` 通过
- [ ] 电压断言若失败，只按实测差额调整，不预先放宽
- [ ] 确认后再做命令退出

## Task 40: 已接管构网时硬分摊不再加线损

**Description:** `kernelSuppliesExcitation` 为真时，`ApplyBlackStartStationElectricalLoadAcrossBus` 不再把 `|P|·lineCoeff/(1−lineCoeff)` 加入分摊有功。交流口功率仍以网络解为准。`GetGridSideActivePower` / `GridSideActiveKw` 继续按 `GridLossCoefficient` 折算网侧功率。未接管时这条线损分摊保留。

**Acceptance criteria:**
- [x] 离网且存在已接管构网机时，分摊有功不含线损项
- [x] 同一条件下网侧功率与交流口功率的比例仍是 `1 − GridLossCoefficient`（放电）或其倒数（充电）
- [x] 未接管 PCS 的线损分摊命令保持原值

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsNetworkPortTests|FullyQualifiedName~BlackStartTelemetryTraceTests|FullyQualifiedName~Unit690KernelTakeoverTests" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: `GridLossCoefficient` 仍参与 `GridSideActiveKw`，没有第二段折算

**Dependencies:** Task 39

**Files likely touched:**
- `EssDeviceSimModel/UnitTransformerIslandSync.cs`
- `EssSimulator.Tests/Devices/PcsNetworkPortTests.cs`

**Estimated scope:** Small: 1-2 files

## Task 41: 已接管构网机不再接收涌流功率命令

**Description:** `ApplyUnitTransformerInrushDemand` 跳过 `AcTerminalLockedToNetwork` 的 PCS，不把涌流有功、无功写入这些机器。变压器自身的涌流状态和 `GetInrushDemandKwKvar` 保留，供显示。未接管 PCS 仍按现有权重接收涌流命令。基波内核不增加涌流电流源。

**Acceptance criteria:**
- [x] 已接管构网机在涌流窗口内的涌流命令保持为零
- [x] 未接管 PCS 仍能收到本单元涌流命令
- [x] `BlackStartTelemetryTraceTests` 的建压无功断言仍通过

**Verification:**
- [x] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~BlackStartTelemetryTraceTests|FullyQualifiedName~PcsBlackStartTests|FullyQualifiedName~FormingDualLoopTests" --nologo --no-restore`
- [x] Build succeeds: `dotnet build EssSimulator.csproj --nologo`
- [x] Manual check: 岛求解支路中没有涌流电流源

**Dependencies:** Task 40

**Files likely touched:**
- `EssDeviceSimModel/UnitTransformerIslandSync.cs`
- `EssSimulator.Tests/Devices/BlackStartTelemetryTraceTests.cs`

**Estimated scope:** Small: 1-2 files

## Checkpoint: Complete

- [x] 重复 Phase 2 的 Devices / Plant / Kernel / Shadow / Propagation 过滤测试
- [ ] 四个阶段的验收标准都有对应测试
- [x] 未开始 35/220 kV 合并、拓扑编译或涌流磁化模型
