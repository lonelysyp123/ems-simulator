# 组态双耳变压器（分裂绕组）

## Phase 1: 组态模板与画布
- [ ] Task 1: 增加 `split_transformer` 模板与 `IsTransformerLike`
- [ ] Task 2: 画布三圆符号、几何与属性提示
- [ ] Task 3: 连线/保存校验（两耳禁共母线、必须 emuId、电压匹配）

## Checkpoint: Phase 1
- [ ] 可拖入双耳变压器：HV→35 kV，左右耳各接 690 V 母线
- [ ] 两耳共母线被拒绝；无 EMU 不能保存
- [ ] Topology 相关测试绿
- [ ] 与人确认 ΔQ 默认门槛（50 kvar / 5% / 10 s）

## Phase 2: 设备模型与 ΔQ 保护
- [ ] Task 4: DualEarTransformerDevice + 无功不平衡 10 s 保护（单测）

## Checkpoint: Phase 2
- [ ] 9.9 s 不报、≥10 s 报、复归与单耳运行测例通过
- [ ] 全量测试绿（尚未接入网络）

## Phase 3: 运行时映射与电气接入
- [ ] Task 5: Mapper 两耳分 690 母线；Converter 按连线把 PCS 归耳
- [ ] Task 6: 径向网络用双耳设备替代该单元的两绕组单元变

## Checkpoint: Phase 3
- [ ] 应用组态后两耳功率可独立，ΔQ 可观测
- [ ] 无双耳工程回归绿

## Phase 4: 告警表面与文档
- [ ] Task 7: AlarmSnapshot + 设备告警页
- [ ] Task 8: 主接线/单元详情显示两耳 Q 与故障
- [ ] Task 9: 系统设计说明 + 用户手册

## Checkpoint: Complete
- [ ] Phase 1–4 验收标准均满足
- [ ] `dotnet test EssSimulator.Tests` 与 `dotnet build ./EssSimulator.csproj` 通过
- [ ] 未做项已排除：自动迁移、序网、告警后跳机、点表新点、3D 新 mesh
