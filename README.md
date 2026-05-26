# FocusDeck Native

这是 FocusDeck 的 C# 原生桌面重写版。界面与 Electron 版本保持一致：顶部封面图、开机自启开关、程序唤起快捷键配置、添加程序、保存设置和测试切换。

## 功能

- 使用 Windows 原生 `RegisterHotKey` 注册全局快捷键。
- 按快捷键时，如果目标程序不在前台，则恢复并置顶到当前桌面最前面。
- 如果目标程序已经在前台，再按一次快捷键会最小化。
- 支持桌面程序 exe 路径和商店应用 AppID。
- 支持托盘后台运行。
- 支持 Windows 登录后后台自启。
- 默认配置 `Alt+Z` 唤起 Codex。

## 运行

```powershell
dotnet build -c Release
.\bin\Release\net10.0-windows\FocusDeck.exe
```

后台启动：

```powershell
.\bin\Release\net10.0-windows\FocusDeck.exe --background
```

## 说明

本版本使用 WPF 实现。原因是当前机器没有可离线恢复的 Windows App SDK 包，WinUI 3 模板在线还原超时；WPF 可直接使用已安装的 .NET WindowsDesktop 运行时构建，并且仍然是 C# 原生桌面程序。后续如果 Windows App SDK 包恢复正常，可以把界面层迁移到 WinUI 3，底层快捷键和窗口切换服务可复用。
