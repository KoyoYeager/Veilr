# Veilr

**Screen Color Eraser** — A Windows tray app that hides or removes specific colors from your screen

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-blue.svg)]()

[日本語](README.md)

---

## What is Veilr?

Veilr is a Windows tray application that lets you hide or erase specific colors on your screen.

Use it as a digital version of a red study sheet to hide answers, or remove unwanted color information from your display.

## Features

- **Sheet Mode** — Place a colored, semi-transparent overlay on your screen to hide same-colored text (like a physical red study sheet)
- **Erase Mode** — Detect target-colored pixels and replace them with surrounding colors for complete removal
- **Full-Screen Support** — Select a monitor and expand the sheet to cover the entire screen
- **Image Export** — Save color-erased screenshots as PNG / JPEG / BMP
- **Color Picker** — Eyedropper tool to pick any color from your screen
- **Color History & Presets** — Automatically tracks used colors; save favorites as presets
- **Session Restore** — Remembers color, position, size, and mode between sessions
- **Dark / Light Theme** — Customizable accent colors
- **Japanese / English UI** — Switch language from settings
- **Portable** — Single exe, no installation required

## Screenshots

> *(To be added after development)*

## System Requirements

- Windows 10 / 11
- .NET 8 Runtime (not required for self-contained builds)

## Download

Download the latest `Veilr-win-x64.zip` from the [Releases](https://github.com/KoyoYeager/Veilr/releases) page, extract it, and run `Veilr.exe`.

## Usage

### Basic Operation

1. Launch `Veilr.exe` — it stays in the system tray and displays a sheet window
2. Drag the sheet to the area you want to cover
3. Drag corners or edges to resize

### Keyboard Shortcut

| Key | Action |
|-----|--------|
| `Ctrl+Shift+E` | Toggle sheet visibility |

Customizable in settings.

### Toolbar

The toolbar at the bottom of the sheet window provides all controls:

| Button | Action |
|--------|--------|
| Switch to Erase Mode / Switch to Sheet Mode | Toggle mode |
| 🖥 Full Screen | Select monitor and go full screen |
| 📷 Save | Export color-erased image of the sheet area |
| ⚙ | Open settings |
| ✕ | Hide sheet (minimize to tray) |

### Mode Comparison

**Sheet Mode (Default)**

A colored semi-transparent window overlays your screen. Text of the same color blends in and becomes invisible — just like a physical red study sheet.

**Erase Mode**

Captures the screen area under the sheet, detects target-colored pixels, and replaces them with neighboring colors for complete removal.

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Build

```bash
git clone https://github.com/KoyoYeager/Veilr.git
cd Veilr
setup.bat        # Restore NuGet packages
build.bat        # Debug build
run.bat          # Build and run
release.bat      # Release build (outputs dist/Veilr.exe)
```

### Batch Files

| File | Purpose |
|------|---------|
| `setup.bat` | Initial setup (SDK check + NuGet restore) |
| `build.bat` | Debug build |
| `release.bat` | Release build (single exe) |
| `run.bat` | Build and run |
| `clean.bat` | Clean build artifacts |
| `publish.bat` | Create zip for GitHub Releases |

> Visual Studio GUI is not required. Develop with VS Code + C# Dev Kit extension, or any editor of your choice.

## Configuration

Settings are saved automatically as JSON in the same folder as `Veilr.exe`.

Key settings include:

- Target color and HSV threshold
- Sheet color and opacity
- Erase mode update interval
- Theme (dark / light) and accent color
- UI language (Japanese / English)
- Keyboard shortcut
- Session restore preferences

## Project Structure

```
Veilr/
├── README.md               (Japanese)
├── README.en.md            (English)
├── LICENSE
├── setup.bat / build.bat / release.bat / ...
├── .github/workflows/build.yml
├── src/Veilr/
│   ├── Views/              (SheetWindow, SettingsWindow, ...)
│   ├── ViewModels/         (SheetViewModel, SettingsViewModel)
│   ├── Services/           (ScreenCapture, ColorDetector, ...)
│   ├── Helpers/            (Win32Interop, HsvConverter)
│   ├── Localization/       (Strings.ja.resx, Strings.en.resx)
│   └── Resources/          (Themes, Styles)
└── docs/                   (Specifications, UI Design)
```

## License

[MIT License](LICENSE)

## Contributing

Issues and Pull Requests are welcome. Please use [Issues](https://github.com/KoyoYeager/Veilr/issues) to report bugs or suggest features.
