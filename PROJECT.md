# win-dock

## Overview

A small Windows dock pinned to the top edge of the screen. It shows CPU, memory, disk, network, and GPU stats, plus battery when a battery is present. It exists to stay far under the memory of the PowerToys Command Palette Dock.

A launcher is out of scope. If one is added later, it should be something like Flow Launcher, not PowerToys Command Palette.

## Stack

- **Language**: C#
- **Framework / Tools**: .NET 10, a Win32 AppBar window, CsWin32 for native APIs
- **Key dependencies**: Microsoft.Windows.CsWin32 (build time only)

## Conventions

- One process, one window, one one-second sample timer.
- Stats that need two samples (CPU, disk, network, GPU) show a dash until the second sample.
- Battery is omitted when Windows reports no battery.

## Constraints

- Windows only.
- No WinUI 3 and no Windows App SDK.
- Day-to-day builds are framework-dependent. GitHub tag releases are self-contained single-file win-x64.
- Do not add a launcher in this phase.
- origin is https://codeberg.org/bojanstrkovski-21/win-doc.git. github is https://github.com/bojanstrkovski-21/win-doc.git. Branch main.

## Frozen Files

None.

## How to Run

The SDK used for this repo is the user-local .NET 10 install:

```powershell
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:Path
dotnet run -c Release
```

Right-click the bar and choose Exit.

To print one sample without creating the bar:

```powershell
dotnet run -c Release -- --dump
```

## Current State

Win32 top AppBar with CPU, memory, disk throughput, network throughput, GPU utilization, battery when Windows reports one, and date/time. The group is centered. Floating mode uses rounded corners and the saved gap. Clear glass paints TranslucentTB accent clear as premultiplied `#2D353B` at alpha `0x66` (blur amount 100). Icons and text stay opaque. Icon font is Segoe MDL2 Assets at FontSize+2, weight 600.

The bar has no taskbar button. A tray icon opens Settings on double-click and Settings/Exit on right-click. The first launch writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `win-dock` to the current exe. On this PC that path is `%LOCALAPPDATA%\win-dock\win-dock.exe`.

`main` is `a6b756d` on Codeberg and GitHub. Tag `v0.1.0` published https://github.com/bojanstrkovski-21/win-doc/releases/tag/v0.1.0 (`win-dock-win-x64.zip`). Later `v*` tags run `.github/workflows/release.yml`.

Close the bar with `WM_CLOSE` to the visible `WinDockBar` window. `MainWindowHandle` is 0 because the bar is owned. Do not `Stop-Process`. Start a keeper process with `Win32_Process.Create`.

On this PC, 2026-11-22, the framework-dependent bar used about 36 MB working set and 14 MB private memory. This machine did not report a battery.

## Next Steps

1. Decide whether disk should stay as throughput or also show busy time.
2. Add per-monitor placement when the single top bar is not enough.
3. Dark system menu and settings window, if wanted. Themes still color only the bar.

## Recent Work

- 2026-11-22 — Clear tint matched to TranslucentTB `#2D353B66`. Icons brought back in line with the text. App moved from the taskbar button to a tray icon.
- 2026-11-22 — Initialized git and pushed `main` to Codeberg and GitHub. Release `v0.1.0` is a self-contained exe. First launch registers user startup.
- 2026-11-22 — Measured Command Palette Dock at about 235 MB working set. Chose C# and Win32. First bar implemented.
