---
name: codex-taskbar
description: 启动、停止或查看 Windows Codex用量任务栏组件。用于用户要求在任务栏显示订阅剩余额度、今日累计 Token、重置时间，或检查该组件状态。
---

# Codex用量任务栏

仅适用于本机 Windows10（2004及以上）/Windows11 x64交互式桌面及主屏水平任务栏。组件独立运行，以自有子窗口嵌入任务栏，不修改Codex文件。Win10尚待真实设备验收，状态成功才表示当前机器已实际挂接。

从本 SKILL.md 所在目录解析 `../../scripts/control.ps1`，直接在当前 PowerShell 执行：

- 启动：`& <resolved-control.ps1> -Action Start`
- 停止：`& <resolved-control.ps1> -Action Stop`
- 状态：`& <resolved-control.ps1> -Action Status`

用户请求启动即授权启动本地组件。若执行环境隔离桌面，使用宿主提供的交互式进程启动能力或必要的进程沙箱授权；不要在隔离桌面启动后宣称用户已看到组件。

以返回的状态为准。只有 `attached=true` 表示成功嵌入；`running=true` 但 `attached=false` 时说明原因，不能称为已成功显示。启动后数据可能需要几秒加载。

不读取、展示或复制 `auth.json`、OAuth token 或 API Key。额度仅通过本机 Codex app-server 的只读接口查询；不启动推理、不登录或登出、不兑换重置额度、不向第三方发送用量。状态返回会话 ID 和统计数据，不必逐项复述，回答保持简洁。
