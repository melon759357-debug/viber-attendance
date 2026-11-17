# Viber 在线记录

面向 Windows 10/11 的本地 .NET 8 WPF 桌面软件，用于自动记录 Viber 通话参与者的上线时间、下线时间、本段在线时长和累计在线时长。

## 直接运行

最终发布文件位于：

```text
release\ViberAttendance.exe
```

这是 `win-x64` 自包含单文件版本，不要求电脑预先安装 .NET Desktop Runtime。软件以当前用户权限运行，不需要管理员权限。

## 普通界面

- 当前记录：Viber 连接状态、当前在线人数、本场时长和成员记录；
- 历史记录：按日期筛选、搜索成员、查看详情、导出和删除；
- 设置：自动监控、开机启动、扫描间隔、下线确认时间、数据位置和主题；
- 主题：跟随系统、浅色、深色；
- 停止监控和删除历史记录前使用应用内确认窗口。

窗口句柄、进程、UI Automation 树和扫描日志仅保留在高级诊断页。进入方式：`设置 → 关于 → 连续点击版本号 5 次`。

## 记录规则

- 默认每 2 秒读取一次参与者名单；
- 同一成员连续出现 2 次后确认上线；
- 连续 5 秒未出现后确认下线；
- 再次出现会创建新的在线区间；
- 状态变化立即写入 SQLite，在线成员定期刷新最后看到时间；
- Viber 通话结束或手动停止后保存本场记录。

## 本地数据位置

- 设置：`%LOCALAPPDATA%\ViberAttendance\settings.json`
- SQLite：`%LOCALAPPDATA%\ViberAttendance\Data\attendance.db`
- 日志：`%LOCALAPPDATA%\ViberAttendance\Logs\diagnostic-YYYYMMDD.jsonl`
- 默认导出：`文档\ViberAttendance\Exports`

所有记录均保存在本机。

## 构建和测试

```powershell
dotnet restore .\ViberAttendance.sln
dotnet build .\ViberAttendance.sln --configuration Release --no-restore -warnaserror
dotnet test .\ViberAttendance.sln --configuration Release --no-build --no-restore
```

发布自包含单文件 EXE：

```powershell
dotnet restore .\src\ViberAttendance.App\ViberAttendance.App.csproj --runtime win-x64
dotnet publish .\src\ViberAttendance.App\ViberAttendance.App.csproj `
  --configuration Release --runtime win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false --output .\release
```

## 隐私与稳定性

- 不注入 Viber，不读取其进程内存，不拦截网络、音频或视频；
- UI Automation 扫描在后台执行，并使用取消令牌和防重入保护；
- SQLite 使用 WAL、外键和事务；
- 保留崩溃恢复、应用会话心跳、隐私日志和高级诊断导出；
- 普通界面不显示 HWND、进程 ID、识别分数或异常堆栈。
