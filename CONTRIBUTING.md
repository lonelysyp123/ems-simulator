# 参与贡献

感谢使用和关注仿真模拟器（EssSimulator）。本项目以 **GPL-3.0** 发布，欢迎提交 Issue 和 PR。

## 开发环境

| 依赖 | 版本 | 说明 |
|------|------|------|
| .NET SDK | 8.0 | 编译与测试 |
| Node.js | 18+ | 前端（`Web/`，Vite 直出 `wwwroot/`） |
| .NET 运行时 | 不需要 | 发布包是自包含单文件 |

开发机可以是 Windows / macOS / Linux，**发布目标只有 Windows x64**。

```bash
dotnet build EssSimulator.csproj
dotnet test EssSimulator.Tests/EssSimulator.Tests.csproj
./scripts/dev-up.sh          # 前端 5173 + 后端 5050
```

## 分支模型

```
master          ← 发布线，只接受来自 develop 的合并
develop         ← 集成线（仓库默认分支），日常开发
develop2.0      ← v2 主线（物理内核重写），长期并行，可能搁置
feature/<slug>  ← 从 develop 切，合回 develop
fix/<slug>      ← 从 develop 切；线上紧急缺陷从 master 切，修完双合并
```

- 新分支用 `feature/<slug>`、`fix/<slug>`，`slug` 小写英文 + 数字 + 短横线。
- 合并用 `git merge --no-ff`，保留合并点；合并后删除主题分支。
- 不要在 `master` 上直接提交，不要 force push 共享分支。

## 提交信息

中文，**写清为什么改**，而不是"改了哪个文件"。仓库里的历史提交可作参考：

```
修掉 MMS 用例让测试宿主 SIGSEGV，并修正热探针用例的物理前提

两个 TcpClient_* 用例会让 dotnet test 直接段错误退出……
定位：崩溃发生在 MMS GetNameList 类服务，而不是整个客户端。
处理：……
```

一次提交只做一件事；格式、重命名这类纯机械改动尽量单独成提交。

## 提 PR 之前

1. `dotnet build` 无错误；新增行为**带上单元测试**（`EssSimulator.Tests/` 按模块分目录）。
2. `dotnet test` 全绿（1300+ 用例，约 1 分钟）。
3. 改了前端要能 `npm run build` 通过。
4. 改了点表、端口、配置语义，同步更新 `docs/` 下对应文档——文档和代码不一致会被当成 bug。

## Issue 建议

- **点表对不上**：附 CSV 片段（`FunctionCode/Address/Type/ParamName` 那几行）+ 设备名（`simEmu3` 等）+ 端口 + 日志。
- **协议问题**：说明客户端工具（IEDScout / mbpoll / 自研 EMS）、请求内容、期望与实际。
- **崩溃或异常**：附 `bin/.../Logs/` 里对应时间段的日志。

## 已知限制

- **Windows 包暂不启用 IEC 61850 GOOSE 订阅**：随包原生库未编译该能力，运行期安全降级，MMS 服务端不受影响。详见 [docs/项目编译说明.md](./docs/项目编译说明.md) 的「已知限制」。

## 许可

提交代码即表示同意以 GPL-3.0 授权。引用第三方代码请确认许可证兼容。
