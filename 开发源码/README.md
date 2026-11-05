# Codex 任务栏用量

Windows 10（2004/build19041及以上）和Windows 11 x64兼容组件，在主屏水平任务栏空白位置显示两行：

```text
周余 22%    今日 12.80M
重置 11-07 16:08
```

示例数值仅用于说明布局。文字使用透明背景直接显示在任务栏上，不绘制底色色块。剩余比例大于20%为正常文字色、11%～20%为橙色、10%及以下为红色。

## 使用

解压“发布包”中的 `Codex任务栏用量-便携版-Windows-x64.zip`，双击 `CodexTaskbar.exe`。发布程序自带 .NET 运行环境，不需要单独安装 .NET。需要已安装并登录 Codex 桌面版。

在任务栏文字上右键：立即刷新、查看用量详情、开机启动、退出。开机启动默认关闭，启用后请保持程序路径不变。详情及悬停显示今日精确 Token、统计范围、各周期剩余额度、重置倒计时和更新时间。

组件不显示系统托盘图标；在用量文字区域（包括字间空白处）右键即可打开菜单。运行时再次双击EXE也可打开菜单。任务栏空间不足时暂时隐藏文字，减少任务栏按钮后自动重试；仍可双击程序或用 `--stop` 控制。

## 数据含义

- **剩余额度**：当前额度类别中，剩余比例较少的周期；平手选较短周期。窗口标签来自实际时长，不能把 primary 固定理解为5小时。
- **今日累计**：按Windows本地日期汇总本机桌面主会话的Token增量，每2秒检查新记录；包括现存已归档日志。跨日会话扣除午夜前的累计基线，重复快照不重复计数，午夜自动切换日期。排除CLI和子代理日志，缓存输入和推理子项不再次相加。额度类别自动取最近活动主会话的类别，不再提供会话锁定；旧版本锁定设置不再生效。
- **重置**：所选周期的重置时间，采用 Windows 本地时区。到时重新查询服务端，不自行假定额度已恢复。
- `--` 表示未知，不能理解为0；`*` 和“数据过期”表示最后一次额度快照已过期或查询失败。额度通常每60秒刷新，失败后间隔逐渐增至最多5分钟。

支持本机桌面会话、主屏水平任务栏和订阅剩余额度。没有 API 金额、跨机器聚合或充值 Credits 功能。自定义 `CODEX_HOME` 时跟随该目录。版本1.1.0增加Windows10传统任务栏控件和DPI适配；Win11 24H2已实测，Win10尚待真实设备验收。

## 命令和本地插件

```powershell
.\CodexTaskbar.exe --start
.\CodexTaskbar.exe --status --output .\status.json
.\CodexTaskbar.exe --menu
.\CodexTaskbar.exe --stop --output .\stop.json
.\CodexTaskbar.exe --capture --output .\taskbar.png
```

`--menu` 打开用量区域的控制菜单。`--capture` 截取任务栏及已展开菜单，作为本地诊断文件。重复启动复用现有实例，并打开菜单；`--start` 则只启动或刷新。状态中 `attached=true` 才表示成功嵌入。

`Codex任务栏用量-Codex插件版-Windows-x64.zip` 包含本地 Codex 插件清单、skill、控制脚本和同一程序；解压后可由本地插件加载机制加载，也可直接执行 `scripts\control.ps1 -Action Start|Stop|Status`。未发布到插件市场，未自动更改 Codex 插件配置。

开机启动仅由菜单开关写入当前用户 Run 项。旧版本的会话锁定文件不再读取。卸载时先取消开机启动并退出，然后删除解压目录；可选删除 `%LOCALAPPDATA%\CodexTaskbar` 中的旧设置及诊断数据。

本工具不读取凭据文件。Codex 自己管理登录并通过 app-server 查询额度；工具不创建聊天或推理请求，不向第三方上传用量。原生任务栏嵌入依赖 Explorer 的窗口结构，Windows 更新后可能需要适配。

## 从源码构建

安装 .NET 10 SDK，或将 SDK 放在 `.tools\dotnet`，在源码根目录运行：

```powershell
.\scripts\build.ps1
```

构建缓存和临时产物位于源码目录的 `.tools` 和 `artifacts`；正式ZIP输出到上一级的“发布包”目录，并使用中文明确命名。本次整理已移走SDK缓存，再次开发需要安装.NET 10 SDK或放入 `.tools\dotnet`。测试为无第三方测试框架的可执行行为测试：

```powershell
.\scripts\dotnet.ps1 run --project tests\CodexTaskbar.Tests
```

实际验收记录见源码包中的 `docs\verification.md`。58项行为测试和7项Windows原生集成检查覆盖传统任务栏角色、MSAA读取、两种DPI上下文、实际SetParent和小任务栏双行字体。原生检查运行 `scripts\dotnet.ps1 run --project tests\CodexTaskbar.WindowsTests -c Release`，会短暂创建测试控件。
