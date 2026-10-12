# 商业版发布脚本

面向 **社区版 / 商业版 / 定制版**；演示版用独立脚本。  
完整说明见 [docs/项目编译说明.md](../../docs/项目编译说明.md)。  
分档边界见 [docs/产品分档与交付边界.md](../../docs/产品分档与交付边界.md)。

主开关：各版 `appsettings` 中的 `Simulator.Edition.Name`（`Community` / `Commercial` / `Custom`）。  
同一套代码，发布时拷贝不同配置即可切换 API 能力与社区拓扑锁定。

## 常用命令

```bash
./scripts/commercial/check-edition-drift.sh      # 发布前档位差异检查
./scripts/commercial/sync-custom-from-root.sh    # 根 appsettings → 定制版模板（可 --dry-run）
EDITION=社区版 ./scripts/commercial/publish-windows.sh
EDITION=商业版 ./scripts/commercial/publish-windows.sh
EDITION=定制版 ./scripts/commercial/publish-linux.sh
EDITION=社区版 ./scripts/commercial/publish-macos.sh
./scripts/commercial/publish-all.sh              # 社区/商业/定制 × win+linux
./scripts/commercial/publish-demo.sh             # 演示版评估包
./scripts/commercial/sync-runtime.sh
```

兼容：`EDITION=充值版` 会自动映射为 **商业版**。

## 本目录文件

| 文件 | 作用 |
|------|------|
| `check-edition-drift.sh` | Edition 开关与单元规模检查 |
| `sync-custom-from-root.sh` | 将根 `appsettings.json` 同步为定制版模板 |
| `publish-common.sh` | 版本解析、拷贝运行时、打 zip/tar |
| `publish-windows.sh` / `publish-linux.sh` / `publish-macos.sh` | 单版本单平台 |
| `publish-demo.sh` | 演示版（免授权、含组态预设） |
| `publish-all.sh` | 标准三档批量发布 |
| `sync-runtime.sh` | 不编译，仅同步配置/点表（完整 `pointmaps/models`，含 LC 片段）/文档 |
| `editions/*/README.txt` | 各版本随包说明 |
