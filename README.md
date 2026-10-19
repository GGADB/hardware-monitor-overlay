# 硬件温度与占用监控

一个轻量的 Windows 桌面悬浮面板，用于实时查看主要硬件的温度、占用率和前台应用的实际帧率。

## 显示内容

- CPU 总体温度与总占用率
- GPU 温度与占用率
- 内存温度与占用率
- 两块 SSD 各自的温度
- 当前前台应用的实际帧率（内置 PresentMon，无需安装 RTSS）

## 功能

- 透明背景、纯色背景、图片背景三种模式
- 背景颜色支持调色板、屏幕取色和透明度调整
- 图片背景支持本地图片和透明度调整
- 字体族可从系统字体选择，也可导入 .ttf/.otf/.ttc 字体文件
- 字体大小可调（4–48 pt）
- 文字填充、内圈颜色、外圈颜色都可单独设置
- 文字颜色支持调色板、屏幕取色和透明度调整
- 横向单行 / 竖向多行两种排列方式，可自由切换
- 横向模式占用面积小，不遮挡游戏画面
- 间距可用滑块自由调节
- 鼠标穿透默认开启，点击面板区域会直接点到下面的游戏
- 默认按住 Ctrl+Alt+M 即可临时拖动面板，组合键可在设置中修改或清除
- 固定显示在屏幕右上角，始终置顶
- 右键菜单可切换刷新间隔、打开设置、设置开机自启或退出
- 托盘图标常驻

## 运行

双击 `release/HardwareMonitorOverlay.exe`。首次运行会请求管理员权限，用于读取硬件传感器。

右键托盘图标，选择 **设置...**，即可调整背景和文字样式。设置会自动保存到当前用户的 `%APPDATA%\HardwareMonitorOverlay\settings.ini`。

也可以直接运行下面的命令打开设置面板：

```powershell
HardwareMonitorOverlay.exe --settings
```

## 从源码构建

环境要求：Windows + .NET Framework 4.x。

依赖 DLL 已放在 `src/lib`，在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File src/build.ps1
```

构建结果会输出到 `build/HardwareMonitorOverlay.exe`（多文件版本）。`release` 中的版本是使用 ILMerge 合并后的单文件版本，并已内嵌 PresentMon。

## 数据来源

硬件传感器数据由 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 提供。

前台应用帧率由内置的 PresentMon 2.6.0 采集，无需单独安装 RTSS。没有可采集的帧数据时显示 `--`。

程序不再依赖 RTSS：悬浮面板本身就是游戏内叠加层，帧率由 PresentMon 直接采集。

## 说明

传感器名称和可用项目由主板、内存、SSD 及驱动决定。某些硬件没有公开温度传感器时显示 `--`，属于正常现象。
