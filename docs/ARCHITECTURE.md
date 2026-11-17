# 阶段 1/2 架构说明

## 依赖方向

```text
ViberAttendance.App
  ├─ Core
  ├─ Infrastructure ──> Core
  ├─ Persistence ─────> Core
  ├─ UIAutomation ────> Core
  └─ Vision ──────────> Core（仅阶段边界）

ViberAttendance.Tests ──> Core / Infrastructure / Persistence / UIAutomation
```

`Core` 不引用 WPF、SQLite 或 UI Automation。参与者识别、身份解析、状态机、通话判断、考勤仓储和业务导出的接口已经预留，但阶段 2 没有伪实现这些业务能力。

## UIA 数据流

```text
EnumWindows + Viber PID
        │
        ▼
用户选择顶层窗口（不自动点击）
        │
        ├── RawView TreeWalker ──> 本机树形查看
        │                              │
        │                              └──> 隐私处理 ──> JSON + TXT
        │
        └── StructureChanged / PropertyChanged ──> 事件计数
```

定位只使用进程 PID、顶层 HWND、标题、类名、边界和真实 UIA 结构。标题只用于本机选择窗口；安全导出会将标题散列。未使用屏幕绝对坐标来建立任何成员身份。

## 并发与取消

- 窗口发现与 UIA 树采集分别使用 `SingleFlightGate`，并发触发时跳过/合并；
- 自动窗口发现使用 `PeriodicTimer`，每次 tick 等待上一轮完成；
- UIA 采集运行在后台 STA 线程，逐节点检查 `CancellationToken`；
- UIA 事件处理器不读取或记录属性的新文本值；
- 应用退出时先取消监控，再停止 UIA 事件和应用心跳，最后原子保存设置。

## SQLite

初始化顺序为：创建目录、打开连接、设置 WAL/synchronous/foreign_keys/busy_timeout、在单个事务内创建表和索引。时间统一以 UTC ISO-8601 文本保存，未来持续时长字段使用毫秒整数。

应用异常关闭时，上一条 `app_sessions` 会保留 `ended_at_utc = NULL`。下次启动只报告未关闭会话，不自动伪造结束时刻；阶段 4 将提供以 `last_heartbeat_at_utc` 或用户指定时间修复的流程。

## 下一阶段入口

拿到安全版 UIA JSON 后，阶段 3 才会：

1. 用真实层级与 Pattern 定位参与者容器；
2. 验证 UIA 是否包含离屏/虚拟化成员；
3. 实现 `IUiAutomationParticipantDetector`；
4. 只在证实 UIA 不足后评估阶段 5 的局部截图与完全本地 OCR。
