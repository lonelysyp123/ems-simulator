# Implementation Plan: 组态双耳变压器（分裂绕组）

## Overview

在组态编辑器增加独立设备模板 **双耳变压器**（`split_transformer`）：高压一侧、低压左右两耳，对应储能场站常用的 **双分裂低压绕组箱变**（一高压绕组 + 两低压绕组，两台 PCS 各接一耳）。运行时按三绕组简化模型求解；核心保护按现场习惯：**两耳无功持续不平衡超过 10 s 报故障告警**。不改现有两绕组 `transformer`，不自动迁移旧工程。

## 行业特性（设计依据）

双耳箱变不是两台独立变压器并排，而是一台三绕组设备：

| 特性 | 现场含义 | 本期建模 |
|------|----------|----------|
| 端口 | HV 一组三相；LV 左右各一组三相（「两耳」） | 9 个 `ac_phase` 端口 |
| 容量分配 | 两耳通常 50/50，单耳额定 = 总容量 / 2 | `splitRatio=0.5`，单耳过载按半容量判 |
| 短路阻抗 | HV–LV 穿越阻抗较小；LV1–LV2 **分裂阻抗**较大，用来抑制环流 | `impedancePercent` + `splitImpedancePercent` |
| 电压 | 两耳额定电压必须相同（同为 690 V） | 一个 `secondaryVoltage`，两耳共用 |
| 无功不平衡 | 两耳 PCS 无功设定不同 → 分裂绕组环流发热、中性点偏移 | **\|Q左−Q右\| 越限并持续 ≥10 s → 告警** |
| 单耳运行 | 一耳断路器分开时，另一耳可带半容量运行 | `allowSingleEarOperation=true` 时，分闸耳不参与 ΔQ |

无功比有功更敏感：两低压绕组磁耦合强，ΔQ 会在分裂阻抗上形成环流。有功不平衡本期只做遥测（环流估算），不单独开故障位。

**无功不平衡判据（默认，均可组态）：**

- 启动：`|QL − QR| > max(qDeadbandKvar, qImbalancePercent × Sear)`  
  - `qDeadbandKvar = 50`  
  - `qImbalancePercent = 0.05`（半绕组额定 kVA 的 5%，作 kvar 门槛）  
  - 避免「略有不同」就误报（控制噪声、爬坡）
- 延时：`qImbalanceDelaySec = 10`（用户指定；可改）
- 返回：`|ΔQ| < 0.5 × 启动值` 后清计时并复归告警（滞回）
- 动作：本期 **只报错（告警位）**，不自动跳 PCS / 不强平无功  
- 一耳未带电且允许单耳运行：不启动该保护

## Architecture Decisions

- **新模板，不改旧模板。** `transformer` 仍是上下各三相的两绕组站用/单元变。双耳端口数量与图形都不同，不能靠参数开关兼容。
- **识别辅助函数。** 新增 `TopologyTemplates.IsTransformerLike(id)`（`transformer` | `split_transformer`），所有「算变压器」的走线、校验、映射、EMU 绑定改走它，避免漏改 `== "transformer"`。
- **电气身份：单元箱变，不是站用主变。** `IsStationTransformer` 仍只认无 `emuId` 的两绕组 `transformer`。双耳默认 35 kV / 690 V，须绑 `emuId`（保存校验）；站用 220/35 主变继续用旧模板。
- **两耳 = 两条 690 V 母线。** 组态上每耳接到各自 AC 母线，禁止两耳接到同一母线（否则分裂绕组被短接）。`TopologyElectricalMapper` 从电网走入 HV 后再分别走进两耳时，现有 `unit690++` 会分到两个 `BUS_690_U*`；须把 `split_transformer` 计入 `xfmrCrossed`。
- **新设备类，不塞进 `TransformerDevice`。** `ITransformerDevice : ITwoPortDevice` 只有一次/二次。新增 `DualEarTransformerDevice`（Primary / SecondaryLeft / SecondaryRight）。穿越用现有损耗+变比；环流 `Icirc ≈ |QL−QR| / (√3 · Vlv · zsplit)` 只作状态量。
- **径向耦合。** `TransformerBusCoupler` 仍服务两绕组。双耳用 `DualEarTransformerBusCoupler`：上游 35 kV 一次变化时同时 Step，再分别写左/右 690 V 母线。PCS 功率贡献挂在各自耳母线上。
- **耳 ↔ PCS 绑定看连线，不看手填。** `TopologyRuntimeConverter` 从每耳下游母线沿 AC 找到 PCS，写入 overlay（左耳 PCS 列表 / 右耳 PCS 列表）。`emuId`/`groupId` 只用于协议镜像与保存归属。
- **保护在设备 Step 内。** 计时用仿真 `step` 累加，不靠墙钟。告警进入 `AlarmSnapshotReader`（新 `DeviceType = transformer`），主接线可显示两耳 Q 与告警。
- **不做：** 旧工程自动改成双耳；三绕组序网/谐波；告警后自动跳机或改 PCS 无功；改 LC/EMU 点表；把 EMU 虚拟节点上的 `unitXf*` 参数删掉（无双耳的单元仍用隐式单元变）。

## 依赖图

```
模板 + IsTransformerLike + 画布符号
    │
    ├── 组态连线/保存校验（变比、电压、两耳禁共母线）
    │
    └── DualEarTransformerDevice + ΔQ 10s 保护（可与画布并行）
            │
            ├── Mapper：split 计入穿越；两耳分到不同 690 母线
            ├── Converter：按连线把 PCS 归到左/右耳
            └── Radial coupler + NetworkTopologyBuilder
                    │
                    └── 告警快照 / 主接线展示 / 文档
```

现有隐式单元变（EMU `unitXf*` → 每单元 1 台 `TransformerDevice`、1 条 690 母线）保持。仅当该 EMU 画布上存在已接线的 `split_transformer` 时，该单元改用双耳设备与两条耳母线，不再为该单元创建两绕组单元变。

## Task List

### Phase 1: 组态模板与画布

### Task 1: 增加 `split_transformer` 模板与变压器识别辅助

**Description:** 在 `TopologyTemplates` 增加双耳变压器：上侧 HV 三相（非电压源），下侧左耳 / 右耳各三相（电压源端口）。参数含一二次电压、总容量、分裂比、穿越/分裂阻抗、ΔQ 门槛与 10 s 延时、是否允许单耳运行、`emuId`/`groupId`。抽出 `IsTransformerLike`，后续校验/映射逐步改用。默认 35 kV / 690 V、6300 kVA（单耳 3150 kVA），贴近 2×PCS 箱变。

**Acceptance criteria:**
- [ ] `TopologyTemplates.Get("split_transformer")` 名称「双耳变压器」，分类「变电」，9 个端口：`pri_a/b/c`（top）、`ear_l_a/b/c`（bottom，offset 约 0.15/0.25/0.35）、`ear_r_a/b/c`（bottom，offset 约 0.65/0.75/0.85）
- [ ] 两耳端口 `VoltageParam = secondaryVoltage` 且 `IsVoltageSourcePort = true`；HV 端口 `IsVoltageSourcePort = false`
- [ ] 默认 `qImbalanceDelaySec = 10`，`qImbalancePercent = 0.05`，`qDeadbandKvar = 50`，`splitRatio = 0.5`，`allowSingleEarOperation = true`
- [ ] `IsTransformerLike("transformer")` 与 `IsTransformerLike("split_transformer")` 为 true，其它模板为 false
- [ ] 原 `transformer` 端口与默认参数不变

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyTemplates"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None

**Files likely touched:**
- `Web/Topology/TopologyTemplates.cs`
- `EssSimulator.Tests/Topology/TopologyTemplatesTests.cs`

**Estimated scope:** Small: 1-2 files

### Task 2: 画布符号、几何与属性提示

**Description:** 双耳比两绕组更宽。组态画布画「上一圆、下左右两圆」；节点尺寸约 160×120；调色与两绕组同属变电蓝。属性条显示 `HVkV / 690V · 双耳`。主接线 SVG 若遍历 `templateId === 'transformer'`，双耳用可区分符号（两低压圈），避免画成普通双圈。3D 主接线本期可用箱变网格 + 标注「双耳」，不做新 mesh。

**Acceptance criteria:**
- [ ] 组态库可拖入「双耳变压器」，画布显示三圆（一高压、两低压耳）且左右耳端口可连
- [ ] `nodeLayout.js` 有 `split_transformer` 尺寸与颜色
- [ ] 选中节点时参数面板列出 ΔQ 延时等项；电压提示含「双耳」

**Verification:**
- [ ] Tests pass: 若有 `nodeLayout` 的 node 测则跑；否则 `dotnet build`
- [ ] Manual check: 打开组态编辑，从库拖入双耳变压器，确认符号与左右耳拐角

**Dependencies:** Task 1

**Files likely touched:**
- `Web/src/components/topology/TopologyCanvas.vue`
- `Web/src/components/topology/nodeLayout.js`
- `Web/src/components/topology/topologyMainLineLayout.js`（若主接线预览画变压器）
- `Web/src/views/TopologyView.vue`（EMU 绑定列表角色名，如需）

**Estimated scope:** Medium: 3-5 files

### Task 3: 连线与保存校验

**Description:** 双耳走与两绕组相同的「上大下小、侧电压与母线匹配、二次侧可给母线充电」。额外：两耳禁止接到同一 AC 母线；保存时双耳必须有 `emuId`；同一耳三相须接同一母线（与电表/PCS 成组连线一致）。`ValidateEmuGroupBindings` 等把 `split_transformer` 视为可绑定设备。

**Acceptance criteria:**
- [ ] 一次电压 ≤ 二次电压 → `XFMR_RATIO`（与现变压器一致）
- [ ] 耳端口电压与母线不匹配 → `XFMR_BUS_MISMATCH`
- [ ] 左耳与右耳接到同一 `ac_bus` → `SPLIT_XFMR_EARS_SAME_BUS`，拒绝该边
- [ ] 保存时双耳无 `emuId` → `SPLIT_XFMR_EMU_REQUIRED`
- [ ] 两绕组旧用例（`TopologyValidatorTests` 主变路径）全部仍绿

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyValidator"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1

**Files likely touched:**
- `Web/Topology/TopologyValidator.cs`
- `EssSimulator.Tests/Topology/TopologyValidatorTests.cs`

**Estimated scope:** Medium: 3-5 files

## Checkpoint: Phase 1

- [ ] 组态可拖入双耳变压器，HV 接 35 kV 母线、左右耳各接一条 690 V 母线
- [ ] 两耳接同一母线被拒绝；无 EMU 归属不能保存
- [ ] `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Topology"` 绿
- [ ] 与人确认：默认门槛（50 kvar / 5% / 10 s）是否按场站改

### Phase 2: 设备模型与 ΔQ 保护

### Task 4: `DualEarTransformerDevice` 与无功不平衡 10 s 保护

**Description:** 新建设备：三端口、两耳各自 P/Q、HV 侧为归算和。`DualEarQImbalanceProtection`（可放在设备内）：越限累加仿真时间，满 10 s 置 `QImbalanceFault`；低于返回值清零并复归。单耳分闸且允许单耳运行时不启动。补纯模型单测，不依赖组态/Web。

**Acceptance criteria:**
- [ ] `|ΔQ|` 低于门槛任意时长不告警
- [ ] `|ΔQ|` 高于门槛持续 9.9 s 不告警；累计 ≥ 10 s 告警
- [ ] 告警后 ΔQ 回到返回值以下则复归，计时清零
- [ ] 中途 ΔQ 回落再越限：计时重新从 0 开始（不记忆）
- [ ] 左耳未带电 + `allowSingleEarOperation`：右耳有 Q 也不告警
- [ ] 状态可读：`ReactiveKvarLeft/Right`、`DeltaReactiveKvar`、`QImbalanceAccumulatedSec`、`QImbalanceFault`

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~DualEar"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** None（可与 Phase 1 并行）

**Files likely touched:**
- `EssDeviceSimModel/Devices/DualEarTransformerDevice.cs`（新）
- `EssDeviceSimModel/Devices/DualEarQImbalanceProtection.cs`（新，或设备内 nested）
- `EssDeviceSimModel/Model/DualEarTransformerConfig.cs`（新）
- `EssDeviceSimModel/Model/DualEarTransformerState.cs`（新）
- `EssSimulator.Tests/Devices/DualEarTransformerDeviceTests.cs`（新）
- `EssSimulator.Tests/Devices/DualEarQImbalanceProtectionTests.cs`（新）

**Estimated scope:** Medium: 3-5 files

## Checkpoint: Phase 2

- [ ] 保护单测覆盖延时、复归、单耳运行
- [ ] 全量测试仍绿（尚未接入网络）

### Phase 3: 运行时映射与电气接入

### Task 5: 电气映射与 overlay：两耳分母线、PCS 按连线归耳

**Description:** Mapper 将 `split_transformer` 计入变压器穿越；从 HV 走入后左右耳 AC 母线分到不同 `BUS_690_U*`。Converter：若某 EMU 有已接线双耳，则生成 `SplitTransformerRuntime`（左/右耳 PCS Id 或单元内序号），且该单元不再只用 EMU `unitXf*` 生成单二次单元变。无双耳的工程 overlay 与现在一致。

**Acceptance criteria:**
- [ ] 电网→主变→35 kV→双耳 HV→左 690 母线 / 右 690 母线：两耳 `BusRuntimeIds` 不同且均为 `BUS_690_U*`
- [ ] 左耳母线所挂 PCS 出现在 overlay 左耳列表，右耳同理
- [ ] 无 `split_transformer` 的现有 converter / mapper 测例行为不变
- [ ] 双耳不计入站用主变（`HasStationTransformer` / overlay.Transformer 仍来自两绕组无 emuId 节点）

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~TopologyElectricalMapper|FullyQualifiedName~TopologyRuntimeConverter"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 1, Task 3

**Files likely touched:**
- `Web/Topology/TopologyElectricalMapper.cs`
- `Web/Topology/TopologyRuntimeConverter.cs`
- `Configuration/` 或 overlay DTO（`SplitTransformerRuntime` / 扩展 `EssUnitConfig`）
- `EssSimulator.Tests/Topology/TopologyElectricalMapperTests.cs`
- `EssSimulator.Tests/Topology/TopologyRuntimeConverterTests.cs`

**Estimated scope:** Medium: 3-5 files

### Task 6: 径向网络接入双耳箱变

**Description:** 对声明了双耳的单元：`NetworkTopologyBuilder` 创建 `DualEarTransformerDevice` 替代该单元的两绕组单元变；`RadialNetworkGraph` 为该单元建两条 690 母线，PCS 按耳挂贡献者；新 coupler 从 35 kV 同时激励两耳。其余单元仍 1 变 1 母线。电表抽头解析走已有 `RuntimeBusIds`。

**Acceptance criteria:**
- [ ] 应用含双耳的组态后，仿真中左/右耳母线电压均可建立（HV 带电且路径合闸）
- [ ] 只改左耳 PCS 无功，右耳 Q 不跟手填值被抹成相同（两耳功率可独立）
- [ ] 无双耳工程：单元变数量、690 母线数量与现网测例一致
- [ ] 不改 PlantEngine 功率平衡主循环结构，只扩展图构建

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~RadialNetworkGraph|FullyQualifiedName~NetworkSolver|FullyQualifiedName~DualEar"`
- [ ] Build succeeds: `dotnet build ./EssSimulator.csproj`

**Dependencies:** Task 4, Task 5

**Files likely touched:**
- `EssDeviceSimModel/Solver/NetworkTopologyBuilder.cs`
- `EssDeviceSimModel/Propagation/RadialNetworkGraph.cs`
- `EssDeviceSimModel/Propagation/DualEarTransformerBusCoupler.cs`（新）
- `EssDeviceSimModel/Solver/ElectricalNetwork.cs`（若需挂双耳列表）
- `EssSimulator.Tests/Propagation/RadialNetworkGraphTests.cs`
- `EssSimulator.Tests/Solver/NetworkSolverTests.cs`（仅必要时补一条）

**Estimated scope:** Medium: 3-5 files（若 ElectricalNetwork 变更面大，拆成「挂设备」与「coupler」两任务）

## Checkpoint: Phase 3

- [ ] 组态应用后：两耳接不同 690 母线 + 各 1 台 PCS，改一台无功能看到 ΔQ 上升
- [ ] 无双耳工程回归绿
- [ ] 与人确认后再做告警页

### Phase 4: 告警表面与文档

### Task 7: 告警快照与设备告警页

**Description:** `AlarmSnapshotReader` 增加变压器设备：`DeviceType = transformer`，`DeviceId` 用组态/运行时 Id。标志至少 `QImbalanceFault`（故障）及可选 `QImbalanceAlarm`（若拆预警；本期可只做 Fault）。`AlarmsView` 的 deviceType 过滤能显示变压器。绿/红语义与 BMS/PCS 一致。

**Acceptance criteria:**
- [ ] `GET /api/alarms` 在双耳 ΔQ 持续 10 s 后出现该变压器且 `QImbalanceFault.Active == true`
- [ ] 复归后 Active 为 false
- [ ] 无双耳时告警列表与现在一致（无空变压器卡片）

**Verification:**
- [ ] Tests pass: 新增或扩展 AlarmSnapshot 测例；`dotnet test EssSimulator.Tests --filter "FullyQualifiedName~Alarm"`
- [ ] Manual check: 设备告警页能看到双耳变压器条目，越限 10 s 变红

**Dependencies:** Task 4, Task 6

**Files likely touched:**
- `Web/AlarmSnapshotReader.cs`
- `Web/src/views/AlarmsView.vue`（若类型写死了 bms/pcs）
- `EssSimulator.Tests/` 下告警相关测例

**Estimated scope:** Medium: 3-5 files

### Task 8: 主接线 / 单元详情展示两耳无功

**Description:** 电站概览或单元变压器表增加左耳 Q、右耳 Q、ΔQ、不平衡累计秒、故障位。协议镜像 `TransformerMirrorData` 可增加两耳 Q（无点表则只给 UI DTO，不改 CSV）。不把双耳强行塞进 `Transformers[0]` 的单二次语义而不加字段。

**Acceptance criteria:**
- [ ] 主接线或单元详情能读到左右耳无功与是否故障
- [ ] 无双耳单元仍显示原来的单台单元变镜像

**Verification:**
- [ ] Tests pass: `dotnet test EssSimulator.Tests --filter "FullyQualifiedName~MainLineEnricher|FullyQualifiedName~PcsEmuSynchronizer"`
- [ ] Manual check: 应用双耳组态后打开主接线，两台 PCS 设不同 Q，10 s 内故障位从绿到红

**Dependencies:** Task 6, Task 7

**Files likely touched:**
- `Web/MainLineEnricher.cs`
- `Web/src/views/MainLineView.vue`
- `EssSimModelApi/Emu/TransformerMirrorData.cs`（可选扩展）
- `EssSimModelApi/Mappers/PcsEmuSynchronizer.cs`

**Estimated scope:** Medium: 3-5 files

### Task 9: 文档

**Description:** 在系统设计说明的组态章节和用户手册组态编辑中写清：双耳变压器用途、接线（HV 一母线、两耳两母线、每耳 PCS）、ΔQ 10 s 告警、与普通变压器的区别。不写实现细节。

**Acceptance criteria:**
- [ ] `docs/系统设计说明.md` 组态编辑同构段提到 `split_transformer` 与 ΔQ 保护
- [ ] `docs/用户手册.md` 有如何拖入、接线、如何看到无功不平衡告警

**Verification:**
- [ ] Manual check: 文档与模板参数名、告警名一致

**Dependencies:** Task 7, Task 8

**Files likely touched:**
- `docs/系统设计说明.md`
- `docs/用户手册.md`

**Estimated scope:** Small: 1-2 files

## Checkpoint: Complete

- [ ] Phase 1–4 验收标准均满足
- [ ] `dotnet test EssSimulator.Tests` 与 `dotnet build ./EssSimulator.csproj` 通过
- [ ] 未做项已明确：自动迁移、序网、告警后跳机、点表新点、3D 新 mesh

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 大量 `templateId == "transformer"` 漏改 | High | Task 1 先落地 `IsTransformerLike`，Phase 3 前 grep 清零漏网 |
| 「每单元 1 条 690 母线」被写死 | High | 只对有双耳的单元拆两条母线；旧测例断言数量不变 |
| 「无功不同」按任意 ΔQ 会误报 | Med | 死区 50 kvar + 5% 半容量；Checkpoint 1 与人确认 |
| `ITransformerDevice` 两端口假设 | Med | 新类三端口，不实现该接口；径向用专用 coupler |
| 隐式单元变与画布双耳重复 | Med | 该 EMU 有接线双耳则不再建两绕组单元变 |
| 任务 6 文件数膨胀 | Med | 超 5 文件则拆「挂网」与「coupler」 |

## Open Questions

- ΔQ 门槛用 50 kvar / 5% / 10 s，还是改成场站定值（例如 100 kvar、8 s）？
- 告警后是否要在后续迭代自动把两耳 Q 拉齐或停机？（本期默认否）
- 一台 EMU 多台双耳（例如 4 PCS / 2 台箱变）是否本期就要支持？建议本期：**每 EMU 至多 1 台双耳**（对应 2 PCS）；多箱变放到下一迭代。
- 双耳是否允许作为站用主变？建议否，仅 35/0.69 单元箱变。
