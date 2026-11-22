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
- Framework-dependent publish. Do not ship a private .NET runtime.
- Do not add a launcher in this phase.
- Remote is https://codeberg.org/bojanstrkovski-21/win-doc.git on branch main.

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

Win32 top AppBar with CPU, memory, disk throughput, network throughput, GPU utilization, battery when Windows reports one, and date/time. Right-click the bar for Settings and Exit. The left button opens Flow Launcher, and its right-click opens settings. The right button opens the Windows shutdown dialog. Settings cover edge, font, opacity, blur, floating gaps, module order, clock format, and the ISO builder palettes plus the system theme.

On this PC, 2026-11-22, the bar used about 36 MB working set and 14 MB private memory. After the first sample, each refresh was about 2 ms. Over 8 seconds that was about 1.4% of one core. Command Palette's host was about 235 MB. This machine did not report a battery, so the battery segment stayed hidden.

## Next Steps

1. Leave the bar running and compare its working set with the Command Palette host.
2. Decide whether disk should stay as throughput or also show busy time.
3. Add per-monitor placement when the single top bar is not enough.

## Recent Work

- 2026-11-22 — Measured Command Palette Dock at about 235 MB working set. Chose C# and Win32. First bar implemented.
