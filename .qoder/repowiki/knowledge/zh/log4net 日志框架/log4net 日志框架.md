---
kind: external_dependency
name: log4net 日志框架
slug: log4net
category: external_dependency
category_hints:
    - vendor_identity
scope:
    - '**'
---

项目使用 `log4net` v2.0.17 作为日志框架，配置文件为根目录 `log4net.config`，发布时随程序复制。Web 层通过自定义 `LogHubAppender` 将日志推送到前端 SignalR Hub，用于实时查看模拟器运行日志。