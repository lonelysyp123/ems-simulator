# 构网双闭环、P-f 下垂与停机预同步

## 已确认
- [x] 上期斜坡 0→690 / 5 s 与 Q-V 下垂保留，不重写
- [x] 单机：电压外环 + 电流内环；软起线性斜坡
- [x] 多机：补 P-f 下垂（覆盖上期「不做频率」）；Q-V 已有
- [x] 停机：母线起来后 PLL → 预同步 → 跟网切入，不做第二台 V/f
- [x] 「已同步」只看本机斜坡，禁止用邻机母线假同步（pcs2 7 kA 根因）
- [x] 不改组级变压器、LC 点表、开关 EMT

## Phase 1: 单机双闭环建压
- [x] Task 1: `DiscretePiController` + `CurrentInnerLoop`
- [x] Task 2: `VoltageOuterLoop`（跟踪斜坡 Vref → Iref）
- [x] Task 3: 接入构网功率；同步/软起判据改为本机斜坡；同步前始终限流

## Checkpoint: 单机
- [x] 0→690 ≤5 s
- [x] 软起全过程电流 ≤ Imax（含预充结束第一拍）
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsBlackStart|FullyQualifiedName~PcsQvDroopRamp|FullyQualifiedName~FormingDualLoop"`

## Phase 2: 多机 P-f
- [x] Task 4: `PfDroopRegulator`
- [x] Task 5: 注入频率；两机构网 P 均分 < 15%

## Checkpoint: 下垂
- [x] 单机空载 f≈50 Hz
- [x] Q-V 测例仍绿

## Phase 3: PLL / 预同步 / 切入
- [x] Task 6: 构网相位积分；母线 θ 跟最高电压源
- [x] Task 7: `PllTracker` + `PreSyncSupervisor`
- [x] Task 8: 活母线禁止假同步与第二 V/f；跟网切入
- [x] Task 9: pcs1 建压后 pcs2 启动不过流

## Checkpoint: Complete
- [x] Phase 1–3 验收均满足
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRamp|FullyQualifiedName~QvDroop|FullyQualifiedName~PcsBlackStart|FullyQualifiedName~PfDroop|FullyQualifiedName~Pll|FullyQualifiedName~PreSync|FullyQualifiedName~FormingDualLoop"`
- [x] `dotnet build ./EssSimulator.csproj`
- [x] 未做：组变、合闸电磁环流、点表、开关 EMT
