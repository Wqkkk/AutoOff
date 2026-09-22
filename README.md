# AutoOff

定时关机 / 睡眠小工具。单文件 exe（约 17KB，.NET Framework 4 自带，免安装），双击即用。

A tiny countdown shutdown / sleep utility for Windows. Single exe, no install.

## 功能

- 设定倒计时（时:分:秒），到点执行 **关机** 或 **睡眠**；默认 30 分钟，回车即开始关机倒计时
- 15分 / 30分 / 1时 / 2时 快捷预设
- 窗口打开即屏幕居中；exe 内嵌电源符号图标（桌面/标题栏/任务栏一致）
- 「熄屏但电脑不睡眠」：立即只关显示器，机器继续跑
- 倒计时期间自动阻止系统闲置睡眠（保持唤醒），点「取消」随时解除；倒计时中窗口置顶、取消键变橙色、状态行显示剩余时间
- **单实例锁**：已在后台运行时再双击，只提示"已经在运行"，不会开出第二个

## 用法

1. 双击 `AutoOff.exe`
2. 设时长（或点预设）→ 点「关机」或「睡眠」
3. 想反悔就回来点「取消」

## 构建（零下载，用系统自带 csc.exe）

```
powershell -File build.ps1            # 像素图标 → csc 编译（/win32icon）→ --dry-run 自检，不 PASS 不出件
powershell -File build.ps1 -Desktop   # 同上 + 拷到桌面
```

源码就一个文件：`AutoOff.cs`（Program / PowerOps / MainForm / SelfTest 四个类）。

- `--dry-run`：自检——倒计时到点触发、中途取消不触发、熄屏进入保持唤醒、取消解除，电源动作全部只落日志（绝不真关机/睡眠/熄屏），结果写 `%TEMP%\autooff-selftest.log`，退出码 0/1
- `--preview`：界面预演，电源动作同样只落日志，标题带「预演」字样，给构建后截图验收用

## 说明

- 绿色程序，配置不落盘、不写注册表
- 关机走系统 `shutdown.exe`，睡眠走 `Application.SetSuspendState`，熄屏走 `SC_MONITORPOWER` 广播，保持唤醒用 `SetThreadExecutionState`
