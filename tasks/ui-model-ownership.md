# 模型所有权 UI 清单

对照门限页原则：模型层已有语义的操作应直写设备/Facade，点表只做可选协议表面。本文件只列不改。

Grep 范围：`Web/src/views/` 中的 `postCommand` / `dpc `。

| 页面 | 调用 | 分类 | 现有 Facade / API | 后续建议 |
|------|------|------|-------------------|----------|
| `CommandView.vue` | `postCommand`；快捷 `dpc simEmu1.yt0` / `dpc simEmu1.yx3` | **协议调试** | `/api/command` | 保留。命令页就是点表/寄存器调试入口 |
| `CommandView.vue` | `esscmd link status`、`esscmd setLoad` | 混合：CLI 封装 | `EssCommand` → 部分已走 `DeviceControlFacade` | 保留命令页入口 |
| `MainLineView.vue` | `esscmd pcsN start/stop/power/reactive` | **已直写模型**（经命令） | `DeviceControlFacade.TrySetPcsRun` / `TrySetPcsPower` | 可改为专用 HTTP，不必再绕 `/command` 字符串 |
| `MainLineView.vue` | `esscmd setpvN run/power/reactive` | **已直写模型**（经命令） | `DeviceControlFacade.TrySetPvRun` / `TrySetPvPower` | 同上 |
| `MainLineView.vue` | `esscmd setbmsN power`、`bmsN fault clear`、`setbmsN soc` | **已直写模型**（经命令） | `EssCommand` BMS 路径 | 保持；门限页已不走 dpc |
| `MainLineView.vue` | `esscmd setLoad` / `setGrid` | **已直写模型**（经命令） | `EssCommand` | 保持 |
| `MainLineView.vue` | `postMainBreaker` / `postUnitBreaker` | **已直写模型** | `/api/breaker/*` → Facade 单元断路器 | 保持 |
| `MainLine3dView.vue` | `esscmd pcsN start/stop` | **已直写模型**（经命令） | `DeviceControlFacade.TrySetPcsRun` | 保持 |
| `MainLine3dView.vue` | `dpc simEmu{n}.{yt} set`（有功/无功） | **应走 Facade** | `DeviceControlFacade.TrySetPcsPower` 已存在 | 后续改与 2D 相同的 `esscmd pcsN power/reactive`，或专用 API |
| `MainLine3dView.vue` | `dpc simPv{n}.yt4/yt5/yt7` | **应走 Facade** | `DeviceControlFacade.TrySetPvRun` / `TrySetPvPower` 已存在 | 后续改 `esscmd setpvN` |
| `MainLine3dView.vue` | `esscmd setbmsN` / `bmsN fault clear` | **已直写模型**（经命令） | `EssCommand` | 保持 |
| `MainLine3dView.vue` | `esscmd setpvN array … temperature/angle` | **已直写模型**（经命令） | `EssCommand` | 保持 |
| `ConnectionsView.vue` | `postLink`（非 dpc） | 链路投退 | `/api/link/{target}/{state}` | 不在本次 grep 内；协议/模型边界另议 |
| `ThresholdsView.vue` | 无 `dpc` / `postCommand` | **已直写模型** | `GET/POST /api/bms/{unit}/rack-thresholds` | 保持 |

## 结论

- 唯一仍把 **PCS/PV 功率与光伏启停** 绑在点表控制点上的产品页是 `MainLine3dView.vue`。
- 2D 主接线已经走 `esscmd` → `DeviceControlFacade`，不依赖点位是否存在。
- `CommandView.vue` 明确保留为协议调试，不要改成 Facade-only。
