# Veilr

**Screen Color Eraser** — A Windows tray app that hides or removes specific colors from your screen

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-blue.svg)]()

[Japanese](README.md)

---

## What is Veilr?

Veilr is a Windows tray application that lets you hide or erase specific colors on your screen.

Use it as a digital version of a red study sheet to hide answers, or remove unwanted color information from your display.

## Features

- **Sheet Mode** — Multiply blend (same physics as a real red study sheet) to hide same-colored text
- **Erase Mode** — Detect target-colored pixels and replace them with surrounding colors for seamless removal
- **3 Erase Algorithms** — Choose from Chroma Key / Lab Mask / YCbCr
- **Color Family Auto-Detection** — Pick one shade of red and it automatically catches all similar reds
- **Full-Screen Support** — Select a monitor and expand the sheet to cover the entire screen
- **Image Export** — Save color-erased screenshots as PNG / JPEG / BMP
- **Color Picker** — Eyedropper tool with zoom preview to pick any color from your screen
- **Session Restore** — Remembers color, position, size, and mode between sessions
- **Japanese / English UI** — Switch language from settings
- **Portable** — Single exe, no installation required

## System Requirements

- Windows 10 / 11
- .NET 8 Runtime (not required for self-contained builds)

## Download

Download the latest `Veilr-win-x64.zip` from the [Releases](https://github.com/KoyoYeager/Veilr/releases) page, extract it, and run `Veilr.exe`.

Or download `dist/Veilr.exe` (self-contained single exe) directly.

## Usage

### Basic Operation

1. Launch `Veilr.exe` — it stays in the system tray and displays a sheet window
2. Drag anywhere on the sheet to move it
3. Drag corners or edges to resize
4. Press F5 or Space to refresh (re-capture screen)

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+Shift+E` | Toggle sheet visibility |
| `F5` / `Space` | Refresh (re-capture screen) |
| `Escape` | Exit full screen mode |

### Mode Comparison

**Sheet Mode (Default)**

Uses **multiply blend** — the same physics as a real colored filter sheet. Captures the screen and applies a color filter. Red text blends into the red background and becomes invisible.

**Erase Mode**

Detects target-colored pixels and replaces them with surrounding background colors. Choose from 3 algorithms:

| Algorithm | Characteristics | Best For |
|---|---|---|
| **Chroma Key** (default) | CIE Lab + soft alpha blending + despill | Anti-aliased text, gradients |
| **Lab Mask** | CIE Lab + binary mask + dilation + median replacement | Solid fills, bold text, sharp edges |
| **YCbCr** | Broadcast-standard YCbCr + soft alpha blending | Fast processing, wide color range |

### Toolbar

| Button | Action |
|--------|--------|
| Switch to Erase / Switch to Sheet | Toggle mode |
| Full Screen | Select monitor and go full screen |
| Save | Export color-erased image of the sheet area |
| Settings | Open settings |
| X (top-right) | Hide sheet (minimize to tray) |

### Settings

Settings are saved automatically as JSON in the same folder as `Veilr.exe`.

Key settings include:

- **Sheet color** — Color picker, eyedropper, or HEX input
- **Sheet opacity** — Blend strength for sheet mode
- **Erase algorithm** — Chroma Key / Lab Mask / YCbCr
- **Erase tolerance** — Strict to flexible slider
- **Update interval** — Processing interval for erase mode (100-500ms)
- **Keyboard shortcut** — Customizable
- **UI language** — Japanese / English (app restarts on change)

## Technical Details

### Sheet Mode Physics

Not a simple semi-transparent overlay. Uses **multiply blend** — the same principle as a physical colored filter. Passes only the sheet color's light component, absorbing everything else.

```
result.R = pixel.R * sheet.R / 255
result.G = pixel.G * sheet.G / 255
result.B = pixel.B * sheet.B / 255
```

### Erase Mode Algorithm

Color detection uses **CIE Lab color space**. Unlike HSV, black and red don't share the same hue value.

- **Chrominance distance** for detecting same-color family
- **Color family auto-expansion** — Lab hue angle ±10° for automatic variant detection
- **Chroma key soft alpha** — continuous 0.0-1.0 values for smooth edges
- **Graduated despill** — distance-based color cast removal at erase boundaries
- **Median background estimation** — outlier-resistant replacement color calculation

See [docs/algorithm.md](docs/algorithm.md) for details.

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

## Project Structure

```
Veilr/
├── README.md / README.en.md
├── LICENSE
├── setup.bat / build.bat / release.bat / ...
├── .github/workflows/build.yml
├── dist/Veilr.exe              (self-contained single exe)
├── docs/
│   ├── spec.md                 (Specification)
│   ├── ui-design.md            (UI Design)
│   ├── algorithm.md            (Algorithm Specification)
│   └── improvement-backlog.md  (Improvement Backlog)
└── src/Veilr/
    ├── Views/          (SheetWindow, SettingsWindow, ColorPickerWindow, EyedropperOverlay)
    ├── ViewModels/     (SheetViewModel, SettingsViewModel)
    ├── Services/       (ScreenCapture, ColorDetector, HotkeyService, SettingsService)
    ├── Helpers/        (Win32Interop, HsvConverter, Loc)
    ├── Models/         (AppSettings, ColorTarget)
    ├── Localization/   (Strings.ja.resx, Strings.en.resx)
    └── Resources/      (Themes, Icons)
```

## License

[MIT License](LICENSE)

## Contributing

Issues and Pull Requests are welcome. Please use [Issues](https://github.com/KoyoYeager/Veilr/issues) to report bugs or suggest features.
