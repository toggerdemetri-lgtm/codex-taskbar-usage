# 验收摘要

2026-11-05，当前版本1.2.0；包括1.1.0的Windows10/11 x64兼容适配及本次额度自动恢复。当前真实设备为Windows11 24H2（10.0.26100）；没有Windows10真机或虚拟机可用。

- 64项行为测试通过（含6项自动恢复策略）：包括本地跨日增量、重复快照、半行日志、归档移动、额度类别及身份版本隔离、短暂任务栏控件树缺失容错，以及传统任务栏角色、部分读取安全性和小任务栏中文字体行高。
- 实际任务栏确认透明背景没有色块，显示“今日”累计量，不含Tokens字样。
- 百分比阈值保持：大于20%正常文字色，11%～20%橙色，10%及以下红色。
- 真实额度查询、自动刷新、单实例和停止/启动已验证。
- 初版已实测Explorer重启恢复、浅/深主题、自动隐藏和100%/150%缩放；200%空间不足时隐藏文字。当前版本可再次双击EXE打开控制菜单。
- 已使用组件进程自己的临时不可达代理验证查询失败及恢复，没有改动系统网络。

实际效果截图位于上级项目目录的 `运行程序\效果截图.png`。历史调试状态JSON、旧截图、旧候选程序及构建缓存已清理，仅保留最新程序、源码和发布包。

当前界面无NotifyIcon，使用用量区域右键菜单。真实桌面状态确认任务栏挂接成功、无托盘图标、区域左上/中心/右下均命中组件窗口；透明背景无明显色块。已通过电脑控制工具点击“立即刷新”。动态菜单首次打开与持续显示已验证。最新程序区域截图为 `docs\images\taskbar.png`。电脑控制工具不能定位任务栏子窗口，区域右键由用户实际复验；菜单显示与命中范围通过程序自身诊断接口核对。

未做真实重新登录、全新机器或开机重启实验。程序默认不开启开机启动。

## 今日用量菜单与短暂消失修复

菜单改为“立即刷新、查看用量详情、开机启动、退出”。取消会话选择和锁定；旧锁定值不再读取，额度类别自动跟随最近活动主会话。悬停及详情统一为今日累计统计口径。

修复前真实采样：用户复现右键任务栏空白处时，同一PID 34268出现9次隐藏状态，原因为“任务栏控件边界暂不可用”，约1.8秒后恢复。证明是UI Automation暂时返回空控件树，程序隐藏组件，进程未退出。

修复为最多3秒保留最近确认安全的位置，只用于相同Explorer窗口、任务栏边界、DPI、通知区边界的暂时测量失败；失败不延长宽限期。成功测量确认空间不足时立即隐藏，窗口或显示环境变化不沿用旧位置。回归测试先复现失败，再验证恢复、超时、不覆盖新按钮和Explorer/缩放/通知区变化的安全边界。

修复后同机复验：用户再次右键空白处并确认“这次没有消失”。状态记录PID 7636、keepingLastLayout=true、visible=true、attached=true，说明暂时测量失败时保留布局并保持显示。此前55秒采样342次均为可见且已挂接。旧菜单的会话选项已不再显示。

## 1.1.0兼容适配与验证

- 目标为Windows10 2004/build19041及以上和Windows11，x64主屏水平任务栏。本机Codex桌面包清单的最低Windows版本也是10.0.19041.0。
- UIA扩大识别范围至标签、列表项、菜单项和链接；传统MSTaskListWClass通过只读MSAA获取子控件边界。MSAA只补充完整有效的全任务栏UIA测量；UIA失败/空树时不允许用局部任务列表推断整个任务栏空白。
- 窗口构造前匹配父任务栏DPI-awareness，并在finally恢复线程上下文。Explorer的DPI模式改变时重建整个Surface；仅重建Handle不能消除WinForms缓存的DPI模式。
- 小任务栏按系统字体的真实行距限制字体大小。本机Microsoft YaHei UI的12px字体实际行高约15.2402px，不能简单按15px估算。
- Windows原生集成检查7项通过：SystemAware和PerMonitorV2各自的窗口DPI匹配、真实SetParent无模式重置、线程DPI恢复，以及实际MSAA分页控件子元素遍历。
- 本机1.1.0候选真实验收：任务栏挂接、DPI模式匹配、真实额度/今日用量、停止/启动、重复启动、100%～200%文字宽度及Explorer重启恢复均通过，程序PID在Explorer重启前后保持。
- 独立代码审查发现UIA失败时局部MSAA可能漏掉开始/搜索控件的Important，已做RED→GREEN回归修复；审查最终无未解决Critical/Important。

**验证限制：Win10代码适配、原生控件与DPI模拟已经完成，但这些测试运行在Win11，不能等同于Win10 Explorer真机验收。** 未验证竖直任务栏、多屏分别显示、第三方替换任务栏或全新机器安装。

复验命令：

```powershell
scripts\dotnet.ps1 run --project tests\CodexTaskbar.Tests -c Release
scripts\dotnet.ps1 run --project tests\CodexTaskbar.WindowsTests -c Release
scripts\smoke.ps1 -Executable <CodexTaskbar.exe> -RestartExplorer
```

Windows10真机复验时，必须记录windowsVersion、boundarySource、dpiContextMatched及任务栏实际截图，确认开始/搜索/应用/通知图标可点击且不被覆盖。空间不足时显示隐藏状态；不能把该状态算作嵌入验收通过。

实现依据：[SetParent与DPI模式](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)、[AccessibleChildren](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-accessiblechildren)。

## 1.2.0额度查询自动恢复

- 连续3次可重试RPC错误（内部错误/服务端错误）触发私有app-server进程重建，随后立即重试；两次自动重建至少间隔2分钟。成功或永久错误打断连续计数。
- 网络断连与超时保留原有传输重置逻辑。登录/身份失效要求重新登录，不靠重启掩盖问题；无效方法、参数等协议错误不会反复重启。
- 重连本身保留旧额度及过期状态，只有有效且账号版本一致的查询成功才清除错误和过期标记。旧查询进程的延迟响应不影响新连接。
- 64项Core行为测试通过；恢复策略测试先复现失败再修复。6项隔离假app-server真实子进程检查通过：三次RPC失败后更换进程并恢复、成功中断计数、登录失效、永久协议错误、超时后恢复、退出时取消挂起请求并关闭子进程。7项MSAA/DPI原生检查继续通过。
- 一次独立整体审查无未解决Critical/Important。测试不修改正式Codex登录、系统网络或用户会话日志；只结束组件自身创建的查询子进程。
- 状态查询增加查询进程ID、连续失败数、自动重建次数、最近重建和下次刷新时间；发生自动重建后，悬停/详情可查看次数和最近时间。
1.2.0本机候选实测：2026-11-05 21:54（Asia/Shanghai），版本1.2.0，Windows11 24H2，UIA边界；attached/visible/dpiContextMatched/textFits均为true，trayIcon=false。取得真实额度“周余3%”、今日累计和对应重置时间，stale=false、quotaError=null。停止/启动、单实例、100%～200%文字宽度回归通过。本次不重复重启Explorer；Explorer重启恢复沿用1.1.0的同机已验收记录。截图仅捕获240×36组件区域。