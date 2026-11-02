# AGENTS.md

给在本仓库工作的自动代理的硬规则。人类贡献者看 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 分支模型（GitFlow 变体）

| 分支 | 角色 | 谁能改 |
|------|------|--------|
| `master` | **发布线**，只有可交付状态 | 只接受来自 `develop` 的合并；不直接提交 |
| `develop` | **集成线**，仓库默认分支 | 日常提交与主题分支合并 |
| `develop2.0` | **v2 主线**（物理内核重写），当前可能搁置 | 只做 v2 相关改动；定期把 `develop` 合进来 |
| `trina` | **某客户专用独立分支** | 仅本地保留，**禁止推送远端** |
| `feature/<slug>` | 主题分支 | 从 `develop` 切，合回 `develop` |
| `fix/<slug>` | 修复分支 | 从 `develop` 切；线上紧急缺陷从 `master` 切 |
| `archive/*`、`download` | 历史备份 | 只读，不动 |

## 命名

- 新分支一律 `feature/<slug>`、`fix/<slug>`，`slug` 用小写英文、数字、短横线。
- 2026-11 之前的历史分支名（`feature_*` 下划线形式）保持原样，不要改名。

## 开始工作

1. `git fetch origin`；工作区不干净就先提交或 stash，不要带着改动切分支。
2. 新功能 → `git checkout -b feature/<slug> origin/develop`；修 bug → `fix/<slug>`。
3. 有风险的改动必须开分支；文档、注释、CI 配置这类低风险改动可以直接提交 `develop`。

## 收尾

1. 合入目标分支用 `git merge --no-ff <topic>`（保留合并点）。
2. 推送后删除主题分支：`git push origin --delete <topic>` + `git branch -d <topic>`。
3. 从 `master` 切出的 `fix/*` 要**双合并**：先合 `master` 并打 tag，再合回 `develop`。

## 发布

```
develop --(merge --no-ff)--> master --(tag vX.Y.Z)--> GitHub Actions 出 Windows 包
```

发布只由用户发起；代理不自行决定版本号。

## 禁区

- 不 `push --force`、不 `rebase` 已推送的 `master` / `develop` / `develop2.0`。
- 不把 `trina` 推到任何远端。
- 不在 `master` 上直接提交，不在 `develop2.0` 上做与 v2 无关的修补。
- 不删除 `archive/*`、`download`、`trina`。

## 常用命令

```bash
dotnet build EssSimulator.csproj                 # 编译
dotnet test EssSimulator.Tests/EssSimulator.Tests.csproj   # 1300+ 用例
./scripts/dev-up.sh                              # 开发态前后端
./scripts/publish-windows.sh                     # 出 Windows 包
```

## 约定文件位置

分支与协作规则写在 **`AGENTS.md`**（代理）和 **`CONTRIBUTING.md`**（人类）。
`.qoder/` 是另一个工具的目录，工具特定的技能文件不要放规范。
