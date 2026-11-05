# Windows 10/11 Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 一个Windows x64程序适配Windows 10 2004及以上和Windows 11，保留现有透明双行显示、数据口径和控制入口。

**Architecture:** 继续使用.NET 10/WinForms/Win32，增加传统任务栏MSAA边界读取并扩展UIA控件识别。创建窗口时匹配任务栏DPI模式，小任务栏自动缩小文字以保留双行。所有原始数据接口不变。

**Tech Stack:** C#、.NET 10、WinForms、Win32、UI Automation、MSAA。

**Spec:** 用户本轮要求：兼容Windows10和Windows11，保持当前显示效果与功能，完成后上传GitHub。

## Global Constraints

- Windows x64；最低Windows 10 build19041（2004），推荐22H2或LTSC2021；Windows11。
- 仅主屏水平任务栏；无额外托盘图标；不移动/修改Explorer自身控件，不创建推理请求。
- 默认约240 DIP；透明背景、今日累计、额度周期自动选择/变色、重置时间与右键菜单保持。
- 已有3秒短暂测量失败宽限仍有效；不能把测量失败视为可用空白。
- 当前只发现Win11 24H2设备，不能声称Win10已真机验收。
- 用户已授权GitHub更新和发布；发布为1.1.0，并披露Win10真机验收缺口。

## Review Focus

- 传统任务栏角色不只有Button：分页标签、列表项和旧MSAA子元素不能被当作空白。
- 关闭的/离屏控件及窗口自身不能占用位置；通知区/开始/搜索/任务按钮不能被遮挡。
- 小任务栏、100%—200% DPI、不同DPI-awareness及Explorer重建。
- MSAA读取部分失败时不得使用不完整结果宣称边界完整。
- 构建、便携ZIP、插件ZIP、源码ZIP、README与版本号必须一致。

## Task 1: 传统任务栏边界与布局

Files: Core/TaskbarControls.cs, CodexTaskbar/TaskbarHost.cs, CodexTaskbar/LegacyTaskbarAccessibility.cs, tests/TaskbarCompatibilityTests.cs。

- [x] 为传统任务栏TabItem/ListItem/MSAA角色、容器排除和满位避让写失败测试并确认RED。
- [x] 实现统一边界过滤；经典MSTaskListWClass通过只读MSAA读取，与UIA合并。
- [x] 验证回归GREEN，UIA短暂失败不能丢失已有3秒保护。

## Task 2: DPI与小任务栏

Files: Core/UsageLayout.cs, CodexTaskbar/TaskbarSurface.cs, Native.cs, TaskbarApplication.cs, Program.cs。

- [x] 测试30/40/48像素任务栏及100%—200%缩放的双行适配，确认RED。
- [x] 在任务栏DPI-awareness上下文创建HWND并恢复线程上下文；必要时在Explorer重建后重新创建。
- [x] 增加系统/边界读取来源/实际显示比例诊断，旧系统给出明确启动提示。
- [x] 运行全部测试、Release构建和本机真实挂接/截图/重复启动/菜单/重启恢复验证。

## Task 3: 发布1.1.0

- [x] 一次独立整体审查，修复重要问题并验证。
- [x] 更新中文说明和兼容矩阵：Win11真机证据与Win10模拟/代码检查分开记录。
- [ ] 更新本机正式程序；三个ZIP与EXE、源码核对，截图只包含组件自身。
- [ ] 推送源码、发布GitHub1.1.0与SHA256SUMS；匿名下载验证。
- [ ] 清理本次工具缓存/临时包，保留最新程序、源码、正式发布包。
