# Windows Platform Implementation

This directory contains Windows-specific source code and build scripts for the Aether Launcher.

## Structure

```
windows/
├── README.md                    # This file
├── build.ps1                    # PowerShell build script
├── WindowsOptimizations.cs      # Platform-specific performance optimizations
└── setup/
    └── installer.nsi            # NSIS installer template (moved from package_windows.sh)
```

## Build Instructions

### Prerequisites
- .NET 8.0+ SDK
- NSIS (for installer creation)
- Windows 10/11

### Building
```powershell
.\build.ps1 -Configuration Release
```

### Why a separate folder?
The Linux version of Aether Launcher works well out of the box because Avalonia's rendering 
pipeline interacts cleanly with X11/Wayland compositors. On Windows, specific optimizations 
are needed:

1. **Rendering Backend** — Force AngleEGL over software rendering for skin preview
2. **DPI Awareness** — Per-monitor DPI scaling via app manifest
3. **Startup Speed** — ReadyToRun AOT compilation for faster cold start
4. **Skin Preview** — Lower interpolation quality + adaptive FPS based on focus state

These Windows-specific hooks live here to keep the main codebase clean.
