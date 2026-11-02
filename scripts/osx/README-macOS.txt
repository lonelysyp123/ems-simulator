EssSimulator - macOS 快速说明
================================

完整使用说明请阅读同目录下的 README.txt。

快速启动
--------
1. 解压 EssSimulator-*-osx-*.tar.gz 到任意目录
2. 在终端进入该目录，执行：

     chmod +x 解除隔离.sh start.sh EssSimulator
     ./解除隔离.sh
     ./start.sh

3. 浏览器访问 http://localhost:5050（start.sh 会尝试自动打开）


关于「Apple 无法验证…恶意软件」提示
------------------------------------
本项目未使用 Apple 付费开发者证书公证，从 GitHub / 浏览器下载后，
系统会加上隔离标记并拦截首次运行。这是正常现象，不是病毒。

推荐处理（任选其一）：

【方式 A · 推荐】运行解除脚本
  ./解除隔离.sh

【方式 B · 终端一条命令】
  xattr -dr com.apple.quarantine .
  chmod +x EssSimulator start.sh
  ./start.sh

【方式 C · 系统设置】
  1. 先双击 EssSimulator（会被拦截一次）
  2. 打开：系统设置 → 隐私与安全性
  3. 向下找到「已阻止使用 EssSimulator…」→ 点「仍要打开」
  4. 再运行 ./start.sh

【方式 D · Finder】
  按住 Control 键点击 EssSimulator → 打开 → 在对话框中再点「打开」


默认 Modbus 端口：电表 1500，BMS1 1501，EMU1 1601（详见 README.txt）
详细说明见 README.txt；技术手册见 docs/用户手册.md（若已随包提供）
