# 构网 PCS：Q-V 下垂与电压斜坡

## 已确认
- [x] 不做 P-f / 频率下垂；构网频率保持额定
- [x] 算法独立成类，再接入 `PcsDevice` 电压源注入
- [x] 级联：先斜坡跟踪 \(V_0\)，再 \(V_{\mathrm{ref}}=V_{\mathrm{ramp}}-n_q\Delta Q\)
- [x] 斜坡须能在 **5 s 内** 将电压从 **0 V 抬到 690 V**（UpRate ≥ 138 V/s）
- [x] 不改 LC/EMU 点表、DroopSlices、组级变压器、带电合闸过流

## Phase 1: 纯算法类
- [x] Task 1: `VoltageRampGenerator`（UpRate / DownRate / dt 步进 / Reset；0→690 ≤5 s）
- [x] Task 2: `QvDroopRegulator`（公式 + 无功死区 + V 钳位）

## Checkpoint: 算法
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRampGenerator|FullyQualifiedName~QvDroopRegulator"`
- [x] `dotnet build ./EssSimulator.csproj`
- [x] 两个类的公开 API 无频率、无功以外的 P 项
- [x] `UpRate=138` 时 0→690 在 5.0 s 到达且不超调

## Phase 2: 配置与接入
- [x] Task 3: 配置贯通；`BlackStartVoltageRampVs` 默认 138；未写 `VoltageRampUpVs` 时 `max(旧键, 690/5)`
- [x] Task 4: 替换 `RampSoftCapTowardCommand`；`TryGetIslandBusVoltageInjection` 输出 \(V_{\mathrm{ref}}\)

## Checkpoint: 接入
- [x] 默认构网软起 0→690 V 在 ≤5 s 内到达
- [x] 死区外发 Q → 注入电压低于斜坡；死区内相等
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~PcsBlackStart"`

## Phase 3: 特性测例
- [x] Task 5: 0→690 ≤5 s、斜坡无阶跃 + 下垂外特性（死区 / Q 加倍降压）

## Checkpoint: Complete
- [x] Phase 1–3 验收标准均满足
- [x] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~VoltageRamp|FullyQualifiedName~QvDroop|FullyQualifiedName~PcsBlackStart"`
- [x] `dotnet build ./EssSimulator.csproj`
- [x] 未做：P-f、组变、合闸过流、点表、DroopSlices
