# SC Hauling Manager

**C0P3Y | Icarus Interstellar Inc.**

A portable Windows companion app for Star Citizen hauling. It reads accepted hauling contracts straight off the game screen (Windows OCR), plans the best pickup and drop-off order across all of them, and shows how to pack the cargo on your ship's real cargo grid in 3D. An optional in-game overlay shows your route steps and lets you tick them off with a hotkey without leaving the game.

- No installer, no registry changes, no runtime downloads. One `.exe` plus a few JSON files next to it.
- Read-only: it only takes screenshots and reads keyboard state for its hotkeys. It never touches the game's memory or files.

## Features
- **Contract capture**: a hotkey (works while the game has focus) or a countdown reads the mobiGlas contract with the Windows OCR engine. OCR slips are cleaned up and place names are fuzzy-matched.
- **Route planner**: pickup-and-delivery planning with your ship's capacity, multi-pickup contracts with unknown amounts, and re-planning as you go.
- **Route steps with undo**: done steps stay greyed out; un-ticking one undoes it everywhere (contracts, containers, 3D hold).
- **3D cargo hold**: 110 ships with their real cargo grids (including angled bays), drag-and-drop crates, an off-grid area, and four packing modes (tightest fit, grouped by cargo / destination, optional 1 SCU gaps).
- **In-game overlay**: a semi-transparent, always-on-top route list; hotkeys to complete or undo the current step and to show or hide the overlay.
- **Locations**: every station, Lagrange station, city, outpost and distribution centre, updatable from the UEX Corp API.
- **Themes**: six built-in themes (four dark, two light), fully custom colours and cargo colour order.
- **Flexible layout**: an aligned four-panel grid with proportions that persist, plus an Expand mode for the 3D hold.

## Requirements
- Windows 10 or 11 with .NET Framework 4.8 (built into Windows).
- Star Citizen in **Borderless** or **Windowed** mode (for capture and the overlay).

## Building
No Visual Studio or SDK is needed. The compiler that ships with Windows' .NET Framework is enough:

```bat
build.bat
```

This compiles everything in `src\` into `SCHaulingManager.exe` with `csc.exe` from `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`. `src\ships.json` and `src\places.json` are embedded as resources.

The code is C# 5 / .NET Framework 4.x (WinForms). It uses only the Base Class Library plus WinRT through reflection for OCR, with no NuGet packages.

## Running
Put `SCHaulingManager.exe` in any folder and run it. On first run it creates `settings.json`, `contracts.json`, `hold.json` and `locations.json` next to the exe. See **[docs/USER_GUIDE.md](docs/USER_GUIDE.md)** for the full guide.

Windows SmartScreen may warn because the exe is unsigned. You can always build it yourself with `build.bat`.

## Project layout
| Path | What it is |
|---|---|
| `src/Program.cs` | Entry point, single instance, restart handling |
| `src/MainForm.cs` | Main window: contracts, cargo to move, route plan, overlay and hotkey wiring |
| `src/Capture.cs`, `src/Ocr.cs` | Game-window capture and Windows OCR (WinRT through reflection) |
| `src/ContractParser.cs` | Turns OCR text into contracts and cargo lines |
| `src/LocationDb.cs`, `src/UexUpdater.cs` | Places, bodies, travel costs; UEX location updater |
| `src/RouteOptimizer.cs` | Pickup-and-delivery route planner |
| `src/Hold.cs`, `src/HoldView.cs`, `src/HoldPanel.cs` | Cargo hold model, packer and the 3D view |
| `src/ShipDb.cs`, `src/ships.json` | Ships and cargo grid geometry |
| `src/places.json` | Built-in location catalogue (UEX snapshot) |
| `src/OverlayForm.cs` | In-game route overlay |
| `src/Theme.cs`, `src/ThemeEditor.cs` | Themes, styling helpers, Colors & Themes settings page |
| `src/SettingsForm.cs`, `src/ContainerForm.cs`, `src/ContractEditForm.cs`, `src/SmallForms.cs` | Dialogs |
| `src/Models.cs`, `src/Storage.cs`, `src/Native.cs` | Data models, JSON storage, Win32 interop (hotkeys, dark title bar) |
| `sample-contract.txt` | Example contract text for **Add Manually → Parse text** |

## Data sources and credits
- **Ship cargo grids**: generated from [SuperCargo](https://github.com/AquatikJustice/SuperCargo) 0.9.1 (MIT, © 2026 Travis), whose grid sizes come from [sc-cargo.space](https://github.com/sccargospace/sc-cargo.space) (MIT) and Ratjack's cargo grid guides. Grid sizes were cross-checked against the [Star Citizen Wiki API](https://api.star-citizen.wiki).
- **Locations**: [UEX Corp API](https://uexcorp.space/api) (community-maintained).

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licences.

## Disclaimer
Star Citizen®, Roberts Space Industries® and Cloud Imperium® are trademarks of Cloud Imperium Rights LLC. This is an unofficial fan-made tool and is not affiliated with or endorsed by Cloud Imperium Games.

## License
[MIT](LICENSE)
