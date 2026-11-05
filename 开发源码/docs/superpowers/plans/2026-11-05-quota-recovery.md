# Quota Connection Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 连续额度查询失败后自动重建私有app-server连接，真实恢复成功后自动清除过期标记，发布1.2.0。

**Architecture:** Core中的状态策略计数可重试失败、3次阈值与2分钟冷却。QuotaClient在已有串行门内应用策略并重建自己创建的进程；TaskbarApplication仍只在真实成功快照后清除错误，沿用重试节流。

**Tech Stack:** C#/.NET10，WinForms，Codex app-server只读JSON-RPC。

**Spec:** 用户已确认连续几次失败自动恢复并授权更新本机与GitHub。

## Constraints / Review Focus

- 只重建组件自身查询进程，不操作Codex主进程、不读取凭据、不产生推理请求。
- 连续3次可重试RPC失败触发，成功或不可重连错误打断失败链；至少2分钟才可再次自动重建。
- 网络断连/超时保留已有立即重建行为；登录失效和无效协议请求不能靠重启伪装恢复。
- 重连不清除旧快照；只有同身份、有效额度快照成功后清除星号与过期标记。
- 旧进程的延迟通知不能影响新连接身份/待处理请求。
- 故障注入必须隔离于正式实例，测试文件与构建缓存完成后清理。

## Tasks

- [ ] Core策略测试RED→GREEN：阈值、成功/永久错误中断、冷却边界、持续失败限速。
- [ ] QuotaClient在串行gate内集成，增加必要诊断，忽略旧进程消息；以假app-server实际验证三次失败、PID变化、恢复与取消清理。
- [ ] 验证UI保留旧快照及成功后清除过期标记，完整回归和本机真实查询通过。
- [ ] 一次整体独立审查，修复重要问题并复验。
- [ ] 更新版本/中文说明/裁剪截图，打包核对，更新本机及GitHub公开Release和SHA256。
- [ ] 匿名下载核验，清理本次临时构建与上传数据。
