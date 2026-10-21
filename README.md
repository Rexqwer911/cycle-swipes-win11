# CycleSwipes

Makes the four-finger touchpad swipe between Windows 11 virtual desktops wrap around: on the last desktop,
swiping "forward" opens the first one; on the first desktop, swiping "back" opens the last one. On the other
desktops Windows switches as usual and the app stays out of the way. System gestures are not disabled.

Runs in the tray and does not need administrator rights.

## Install with Chocolatey
```
choco install cycleswipes
```
Then start "CycleSwipes" from the Start menu and turn on "Start with Windows" in the tray menu.

## Download
Grab `CycleSwipes-<version>-win-x64.zip` from [Releases](https://github.com/Rexqwer911/cycle-swipes-win11/releases),
unpack it to any folder you can write to (for example `%LOCALAPPDATA%\CycleSwipes`) and run `CycleSwipes.exe`.
The .NET runtime is bundled, nothing else needs to be installed. The exe is not signed, so Windows SmartScreen may
warn on the first start ("More info" -> "Run anyway").

## Requirements
- Windows 11 with a Precision Touchpad, with the four-finger gesture set to "Switch desktops and show desktop"
  in the touchpad settings.
- `VirtualDesktopAccessor.dll` next to the exe (included in the release zip). The repository ships it in `lib/`: release
  `2024-12-16-windows11` from https://github.com/Ciantic/VirtualDesktopAccessor/releases
  (SHA-256 `8740C572A1C000E3B87FFEB1E4C397EAE9AF3BD4A2ABDC3BCFFACAB4493F8FF5`, MIT license, see
  `lib/VirtualDesktopAccessor-LICENSE.txt`). Tested on Windows 11 26H2, build 26300.9457.

## Build from source
Needs the .NET 9 SDK and GNU make (for example `choco install make`). The recipes run in Windows PowerShell.

| Command | What it does |
|---|---|
| `make` / `make build` | Release build into `bin\Release\net9.0-windows` |
| `make run` | build and start the app |
| `make debug` | build and start the app with `--debug` |
| `make release` | self-contained single-file exe, zipped into `dist\CycleSwipes-<version>-win-x64.zip` |
| `make choco` | Chocolatey package for the published GitHub release of the current version, into `dist\` |
| `make clean` | remove `bin`, `obj`, `publish` and `dist` |

The version comes from `<Version>` in `CycleSwipes.csproj`. Without make, the same is
`dotnet build CycleSwipes.csproj -c Release`; see the `Makefile` for the publish command.

`config.json` and the DLL are copied next to the exe on build. Only one instance runs at a time. A running
instance locks its exe, so quit it before rebuilding.

The `--debug` flag turns on the log file and shows the developer menu items (Pause, Debug mode, Open log).
Without it the app writes no log at all; problems are reported with tray notifications.

## Elevated windows
While a window running as administrator (for example an IDE or Task Manager) is in the foreground, Windows does
not deliver touchpad input to standard apps (UIPI), so wrapping does not work. To make it work over such
windows too, choose "Restart as administrator" in the tray menu (one UAC prompt) or start CycleSwipes as
administrator. "Start with Windows" starts the app with standard rights.

## Tray menu
- **Restart as administrator** (or just **Restart** when already elevated).
- **Start with Windows**: an entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the path of the
  current exe. If you move the exe to another folder, turn it on again.
- **Exit**.

With `--debug` there are also developer items:
- **Pause / Resume**: temporarily ignore gestures.
- **Debug mode (log)**: detailed record of every four-finger gesture.
- **Open log**: `cycle-swipes.log` next to the exe (up to 5 MB, the previous file is kept as `.old`).

The log records startup, errors, wraps and `INPUT LOST?` lines, plus per-gesture details while "Debug mode" is checked.

## How it works
1. Touchpad frames are read via Raw Input (HID, Digitizer / Touch Pad).
2. When exactly four fingers are on the touchpad, the current desktop index is read from the registry
   (`HKCU\...\Explorer\VirtualDesktops`) and cross-checked with the DLL.
3. When the fingers lift, the gesture is evaluated using the last frame with four fingers. If it is a horizontal
   swipe "outwards" from an edge desktop, `GoToDesktopNumber` from the DLL is called.
4. After the wrap, the result is checked against the registry.

The wrap is instant, without animation: the DLL has no animated switch.

If the desktop changes while no touchpad reports arrived for a second, the app re-registers for touchpad input
(with `--debug`, the log gets an `INPUT LOST?` line naming the foreground window).

## Tuning thresholds
All thresholds are in `config.json`, each with a comment. Changes apply after a restart.

1. Start the app with `--debug`.
2. Swipe on the middle desktops. Each gesture gets a line with its travel (`dX`, `dY`, fractions of the touchpad
   size), the velocity at lift-off and the decision, followed by a comparison with what Windows did:
   - `MISMATCH ... threshold too high`: Windows switched the desktop, but the app would not have accepted the
     swipe. Lower `swipeThresholdX` or the `flick*` thresholds.
   - `MISMATCH ... threshold too low`: the opposite, raise them.
   - `DIRECTION MISMATCH`: flip `invertDirection`.
3. The goal: on an edge desktop the app fires on the same swipes on which Windows fires in the middle.

## If wrapping got disabled
The app disables wrapping by itself and shows a notification if the DLL failed to load, its answers did not
match the registry, or the desktop did not change after the call. The notification shows the reason (start with
`--debug` to get it in the log too). Usually it means a
Windows update changed the internal shell interface and a new DLL release is needed: replace the file next to the
exe and restart the app. There is no fallback switching method.

## Project layout
- `src/InputWindow.cs`, `src/TouchpadDevice.cs`: Raw Input and parsing HID reports into frames.
- `src/GestureRecognizer.cs`: the gesture state machine.
- `src/GestureController.cs`: the wrap decision and the comparison with Windows.
- `src/VirtualDesktops.cs`, `src/DesktopWatcher.cs`: desktop state from the registry and change notifications.
- `src/DesktopSwitcher.cs`: switching via the DLL.
- `src/Foreground.cs`: foreground window description for the log.
- `src/Autostart.cs`, `src/TrayVisuals.cs`, `src/Program.cs`: autostart, tray icon and menu, entry point.
- `config.json`: thresholds; `CycleSwipes.ico`: app icon; `lib/`: VirtualDesktopAccessor.dll and its license.
- `Makefile`: build, run and release commands.
- `choco/`: Chocolatey package template (the install script downloads the release zip and checks its SHA-256).

Release order: bump `<Version>`, `make release`, publish the zip as GitHub release `v<version>`, then `make choco`
(it takes the checksum from the published release) and `choco push`.

## License
MIT, see `LICENSE`. `VirtualDesktopAccessor.dll` is by Jari Pennanen, also under MIT.
