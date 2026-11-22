---
name: project-win-dock
description: win-dock — C# Win32 stats bar at M:\win-dock; Codeberg and GitHub win-doc; tray, clear tint, v0.1.0 startup release
metadata:
  type: project
---

`M:\win-dock` is a lightweight Windows screen-edge stats bar. C# on .NET 10, x64, Win32 AppBar via CsWin32. No WinUI 3 and no Windows App SDK. Stats are CPU, memory, disk throughput, network, GPU, and battery only when Windows reports one. Flow Launcher is the future launcher, not PowerToys Command Palette. This PC had no battery on 2026-11-22.

Remotes, branch `main`, commit `a6b756d` (2026-11-22): origin `https://codeberg.org/bojanstrkovski-21/win-doc.git` and github `https://github.com/bojanstrkovski-21/win-doc.git`. Tag `v0.1.0`. GitHub release asset `win-dock-win-x64.zip` is a self-contained single-file exe. `.github/workflows/release.yml` publishes that zip on every `v*` tag. Day-to-day `dotnet run` stays framework-dependent. Build with the user-local SDK prepended: `$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:Path`. Git Credential Manager already had Windows Credential Manager entries for `git:https://codeberg.org` and `git:https://github.com`, user `bojanstrkovski-21`.

The visible window is an owned `WS_POPUP` with `WS_EX_TOPMOST` and no `WS_EX_APPWINDOW`, so it has no taskbar button. A hidden `WS_EX_TOOLWINDOW` is the owner. `MainWindowHandle` is 0. Close by posting `WM_CLOSE` (0x0010) to the visible `WinDockBar` window. Never `Stop-Process`. A shell inside a Windows job kills a `Start-Process` child when the command exits. Start a keeper with `Win32_Process.Create`. `ABE_TOP` is 1. A tray icon (drawn 32px mark) is right-click Settings/Exit and double-click Settings. `EnsureStartup` writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `win-dock` to the current exe whenever the path differs. On this PC the running copy is `%LOCALAPPDATA%\win-dock\win-dock.exe`.

Clear glass matches the user's TranslucentTB config: accent `clear`, color `#2D353B66`. `blur_radius` 9 is stored there but TranslucentTB does not apply it for accent clear. The bar uses `UpdateLayeredWindow` with a fresh premultiplied 32bpp frame. Background alpha is `0x66` at blurriness 100, so clicks hit the bar. Glyph pixels are alpha 255. Do not paint straight onto the window DC and do not use `LWA_COLORKEY`. Icons are Segoe MDL2 Assets at FontSize+2, weight 600. FontSize+8 was too big. Segoe Fluent Icons is not installed.

Settings live in `%LOCALAPPDATA%\win-dock\settings.json`. Themes color the bar only. The menu is a light Win32 popup and settings are light WinForms. Power uses `Shell.Application.ShutdownWindows`. `user32!ExitWindowsDialog` is not exported. Mutex is `Local\win-dock`. A second instance returns 0. Project doc is `PROJECT.md`. A mirror of this note is `memory/project-win-dock.md` in the repo.
