---
name: gitflow-branching
description: >-
  Enforces EssSimulator GitFlow: remote keeps only master and develop; new work
  uses feature_<slug> or fix_<slug>; topic branches are deleted after merge.
  Use when adding a feature, fixing a bug, creating or merging branches, or when
  the user mentions GitFlow, 分支, feature, fix, 新功能, 修 bug, 合并, 发布.
---

# GitFlow 分支管理（EssSimulator）

远程**长期只保留两条稳定分支**：

- **`master`**：发布线，已发布/可交付状态
- **`develop`**：集成线，日常开发合入这里

其它分支都是短期主题分支，合入目标后删除。不要在 `master` 或 `develop` 上直接改代码（用户明确要求除外）。

## 主题分支

| 类型 | 命名 | 从哪切 | 合回哪 | 何时用 |
|------|------|--------|--------|--------|
| 功能 | `feature_<slug>` | `develop` | `develop` | 新功能 |
| 修复 | `fix_<slug>` | `develop` | `develop` | 修 bug |

`slug`：小写英文、数字、短横线，例如 `feature_gitflow-branching`、`fix_pcc-meter-sign`。不要用斜杠（`feature/xxx` 禁止）。

线上已发布的 `master` 有紧急缺陷时：仍用 `fix_<slug>`，但从 **`master` 切出**，合回 **`master` 和 `develop`**，再删除。

发布：用户要求发布时，把 `develop` 合进 `master` 并推送。不为发布另建长期分支。

若远程没有 `origin/develop`：从最新 `master` 创建并推送一次，再切主题分支。不要动用户留下的本地历史备份（如 `archive/develop`）。

## 开始工作（Start）

1. `git fetch origin`。工作区不干净则先提交、stash，或问用户。
2. 判定：新功能 → `feature_<slug>`；修 bug → `fix_<slug>`。不确定就问一句。
3. 检出基线并快进：
   - 默认：`git checkout develop && git pull --ff-only origin develop`
   - 修已发布 `master` 上的紧急缺陷：`git checkout master && git pull --ff-only origin master`
4. `git checkout -b feature_<slug>` 或 `git checkout -b fix_<slug>`
5. `git push -u origin HEAD`（创建主题分支时推远程，不必再问）
6. 只在该主题分支上提交。

## 结束工作（Finish）

合入目标分支并推送后，删除该 `feature_*` / `fix_*` 分支。

1. 主题分支已提交。
2. `git checkout <target> && git pull --ff-only origin <target>`
3. `git merge --no-ff <topic>`
4. `git push origin <target>`
5. 若该 `fix_*` 是从 `master` 切出的：再合进 `develop` 并 `git push origin develop`
6. 删除远程：`git push origin --delete <topic>`
7. 删除本地：`git branch -d <topic>`（用户明确要求保留备份则跳过）
8. 停在目标分支上，不要停在已删的主题分支上。

用户只说「提交」、没说合并：只在当前 `feature_*` / `fix_*` 提交，不合 `develop`/`master`，不删分支。

用户说「合并 / 合到上级 / 结束功能」：执行 Finish。目标默认是 `develop`；从 `master` 切出的紧急 `fix_*` 目标是 `master`（并回灌 `develop`）。

## 禁区

- 不在 `master` / `develop` 上直接开发。
- 不 `push --force` 到 `master` / `develop`。
- 远程不要长期留下 `feature_*` / `fix_*`；也不要把无关本地备份推上去。
- 不删除用户的历史本地备份分支。

## 与现有约定

- 提交信息沿用仓库习惯（中文、说明原因）；用户要求提交时才 `git commit`。
- Finish 时推送 `develop`/`master` 以及删除主题分支，视为已授权。
