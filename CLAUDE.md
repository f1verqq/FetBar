# StayBar: notes for Claude Code

StayBar replaces the Windows 11 taskbar with an always-on bar of rounded tiles ("tiles" style, inspired by lumabar) or a floating glass dock. Version 3 has 76 widgets, three layout zones with spacers and separators, an edit mode, widget copies and stacks, per-widget settings, a bar per screen, profiles, automations, live window previews, a launcher, themes and a widget gallery.

**This project will be published on GitHub for other people to use.** Every default must be generic (no personal cities, usernames, clocks, feeds or paths), nothing personal goes into the code, and the user's own settings files are never part of the repo.

## Build and run
- Source: `src\*.cs`. C# 5, WinForms, .NET Framework 4.x. No NuGet, nothing to install. `legacy\` holds the old single-file v1 (not built).
- Build: `build.bat` (or `build.bat nopause` from scripts). It runs `StayBar.exe --quit`, waits for the exe to unlock, then compiles with
  `csc /codepage:65001 /r:System.Management.dll /r:System.Data.dll /r:System.Numerics.dll /r:%FW%\System.Numerics.Vectors.dll /r:%FW%\System.Runtime.dll /r:%FW%\System.Runtime.InteropServices.WindowsRuntime.dll /r:%FW%\System.Runtime.WindowsRuntime.dll /r:%WINDIR%\System32\WinMetadata\Windows.{Foundation,Media,Devices,Networking,Storage,UI}.winmd src\*.cs`.
- Run the build from PowerShell (`cmd /c "C:\...\build.bat" nopause`). From Git Bash, `find` in build.bat resolves to the Unix `find` and the wait loop breaks.
- **C# 5 only**: no `$""`, `?.`, `nameof`, expression-bodied members, `out var`, tuples, pattern matching, `await` in catch.
- **WinRT without the SDK**: the split winmd files in `System32\WinMetadata` work, but `AsTask()` does not (it needs the unified Windows.winmd). Await WinRT calls with `WinRT.Wait(op)` (Services.cs). WinRT events (`+=`) work. `Windows.UI.Composition` works too (Glass.cs); it needs `System.Numerics.dll` + `System.Numerics.Vectors.dll` for `Vector2`.
- In Claude Code's shell, `cmd` will not run programs from the current folder (`NoDefaultCurrentDirectoryInExePath`). Use full paths.
- Glyph strings must stay `\uXXXX` escapes. The file-writing tools turn escapes into raw characters: Private Use glyphs then become invisible. After writing a .cs file, convert any U+E000-U+F8FF character back to `\uXXXX`. If you batch-edit with perl, write the backslash as `\x5c`, because `\u` in a perl replacement means "uppercase next".
- Restart after a build: `start "" "C:\...\StayBar.exe"`. **Never run `--restore` to restart**: it shows a message box to the user.

## Command line
| Command | What it does |
|---|---|
| `StayBar.exe` | Watchdog: starts `--bar` and restarts it if it crashes (exit 0 = quit on purpose, 2 = restart, 5 crashes/min = give up and restore the taskbar) |
| `--bar` | The bar process itself (one bar per screen inside it) |
| `--quit` | Quits the running bar cleanly. This restores the Windows taskbar |
| `--settings`, `--gallery`, `--launcher` | Open those windows in the running bar |
| `--reload` | Re-reads settings.txt, pins.txt and the automation rules |
| `--restore` | Kills StayBar and restores the Windows taskbar (emergency; shows a message box) |
| `--selftest` | Demo data, renders PNGs to the data folder: `selftest.png` (bar), `selftest-themes.png` (every theme, tiles + dock, sizes, blur off), `selftest-crowded.png`, `selftest-layouts.png` (zones, widths, spacers, separators, edit mode, stacks, sizes), `selftest-widgets.png` (every widget by category), `selftest-flyouts.png`, `selftest-settings-*.png` (+ `-full` for the long pages, `-widget` for a widget page), `selftest-gallery.png`. Never touches the taskbar or settings |
| `--selftest bench` | Paint timing, written to `bench.txt` (full paint is about 2 ms) |
| `--selftest data` | Runs every data source for real once (processes, disk, ping, Wi-Fi, battery, power, audio, toggles, rates, crypto, stocks, air, RSS, GitHub, translate...) and writes what each widget would show to `selftest-data.txt`. Use it to check data without looking at the user's screen |

## Files (`%LOCALAPPDATA%\StayBar\`)
`settings.txt` (key=value), `pins.txt` (one pin per line; a folder is `folder:Name=<pin>;;<pin>`), `pins\` (copied .lnk), `profiles\<Name>.txt` (profile snapshots), `taskbar-state.txt` (the user's auto-hide state, only while StayBar owns the taskbar), `note.txt` (+ `note-2.txt`... for copies), `todo.txt`, `schedule.txt`, `habits.txt`, `screentime\yyyy-MM-dd.txt`, `clipboard.txt` (only if "remember" is on; base64 lines), `launcher-history.txt`, `error.log`, `perf.log`.
Debug keys in settings.txt: `debug.perf=true` (perf.log every 30 s), `debug.hookinjected=true` (the key hook also reacts to injected keys, for automated tests; **never leave it on**).

### settings.txt keys added in version 3
- Zones: `zones.left` / `zones.center` / `zones.right`, comma lists of items. An item is a widget key (`clock`, a copy `worldclock#2`), `apps`, `spacer` (flexible), `spacer:24` (fixed px), `sep`, or `stack:a+b`. Old files: `left`/`right`/`centerapps` are migrated on load (apps at the end of left, or in the center).
- Geometry: `bar.width` (auto|full|percent|fixed|fit), `bar.widthpct`, `bar.widthpx`, `bar.align`, `bar.offset`, `bar.height` (0 = auto), `bar.gap`, `bar.pad` (-1 = auto), `bar.floating` (auto|on|off), `margin`. `auto` follows `style` (tiles = full + attached, dock = fit + floating).
- Hiding: `hide` (always|fullscreen|smart|auto; old `autohide=true` = auto), `hide.reveal`, `hide.delay` (ms), `dim.idle` (s, 0 = off), `dim.opacity`, `reserve`.
- Dock: `dock.trash` (Recycle Bin end cap), `hotcorner`. Keys: `barkeys` (Win+Alt+E/P/1-4).
- Apps: `apps.combine` (always|whenfull|never), `apps.labels`, `apps.show` (all|running), `apps.thisscreen`, `apps.hidden` (`|`-separated app keys), `app.name:<key>`, `app.icon:<key>` (key = exe path lower-case, or `aumid:...`).
- Widgets: `w.<key>.<option>` per copy (common ones: `size` s|m|l, `style`, `label`, `click` = `app:`/`folder:`/`url:`/`cmd:`/`ps:` + target).
- Screens: `screens` (all|primary|id list), `screen.<id>.own=true` + `screen.<id>.zones.*`, `screen.<id>.position`.
- Profiles: `profile.current`, `profile.order`. Automations: `auto.<n>=on|<when>|<do>` (see Profiles.cs), `auto.examples`.

## Source map
| File | What's in it |
|---|---|
| `App.cs` | `Program` (modes, watchdog, crash handling, exit codes), `Settings` (cached key=value file; `WithPrefix`, `ReplaceManaged` for profiles), `Startup`, `Taskbar` (hide/restore Shell_TrayWnd), `Perf`, `Bg` (one background thread for periodic jobs, plus `Bg.Pool` for network) |
| `Native.cs` | P/Invoke, Core Audio COM interfaces, DWM thumbnails, PDH, keyboard hook types, layered windows, regions, monitors, display devices; `DibSurface` (32bpp DIB we draw into with GDI+; `Present` = UpdateLayeredWindow) |
| `Glass.cs` | `GlassBack`: the blur window under the bar (acrylic accent, or a rounded `Windows.UI.Composition` host-backdrop for the dock) |
| `Config.cs` | `Config` (all settings, zones, screens, migration), `WallpaperColor`, `Palette` (6 themes), `Theme`, `Gfx` (tiles, meters, rings, sparklines, bars, weather icons, media icons, font cache) |
| `Shell.cs` | `Actions` (custom click actions), `ScreenIds` (stable monitor ids/names), `Win` (task-window rules, icons, launching, ForceForeground), `AppIndex` (launcher catalog), `Recent` |
| `Services.cs` | `Sys`, `Audio`, `Brightness`, `Radios`, `Media`, `Weather` (+ `Json`), `ScreenTime`, `Clip`, `Focus`, `Note`/`NoteStore`, `Recycle`, `Disk`, `Keyboard` |
| `Services2.cs` | Phase-3 data: `Net`, `Geo`, `Sky` (sun/moon), `Todo`, `Schedule`, `Habits`, `Ics`, `Words`, `Rates`, `ProcTop` (NtQuerySystemInformation), `Uptime`, `DiskIo`, `NetInfo`, `Wlan`, `BatteryHealth`, `PowerPlans`, `AudioX` (peak meter, sessions, mic, output devices via IPolicyConfig), `Privacy` (mic/camera in use), `Usb`, `Toggles` (dark mode, night light, DND, game mode, desktop icons, keep awake), `Online` (crypto, stocks, air quality, RSS, GitHub, translate), `ClaudeUsage`, `Downloads`, `CustomRunner` |
| `Widgets.cs` | `Ui`, `Widget` base (per-copy options, sizes, styles, labels, stacking line, `Tick`, `PollEvery`, `InfoTile`, `ToggleTile`), `Widgets.All` + `Get` (copies, stacks), `IWidgetOptions`, `StackWidget`, the original 26 widgets |
| `Widgets2.cs` | Time (flip/digital/analog clock, progress, countdown, stopwatch, timer, alarms, sun, moon) and Study (to-do, next class, habits, calendar events, word, translate, converter, calculator) |
| `Widgets3.cs` | System (top process, uptime, disk activity, ping/IP/VPN, Wi-Fi, battery health, power mode, level meter, mixer, mic, USB eject) and Toggles (dark mode, night light, DND, game mode, desktop icons, screenshot, color picker, keep awake, eye breaks, power menu, audio output) |
| `Widgets4.cs` | Info (crypto, exchange rates, stocks, air quality, headlines), Dev (Claude Code usage, GitHub activity, latest download), Custom (script widget, launcher tile) |
| `Bar.cs` | `BarForm` (one per screen): AppBar, zone layout into `Slot`s, layout caching per pass, animation, painting (dock shape), hover/tooltips/previews, drag to reorder / between zones, folders, file drops, edit mode + undo, hide modes, dim, apps block options, widget menus, shell hook, hotkey messages |
| `EditPanel.cs` | The glass panel shown in edit mode (add spacer/separator, undo, done) |
| `Popups.cs` | `GlassWindow`, `Flyout` (+ `ShowMenu` that keeps the flyout open) + Weather/Calendar/ScreenTime/Clipboard/Note/Folder flyouts, `TipWindow`, `ToastWindow`, `PreviewWindow` |
| `Popups2.cs` | To-do, habits, events, translate, converter, calculator and mixer flyouts; `ColorPickerWindow` |
| `Profiles.cs` | `Profiles` (snapshots, presets, switch, import/export, hotkeys) and `Automations` (rules, conditions, actions, undo when a rule stops) |
| `Launcher.cs` | Alt+Space launcher (apps, windows, calculator `Calc`, commands, web search; history-weighted) |
| `SettingsUI.cs` | Hand-drawn controls, `SettingsForm` (sidebar: Dock, Appearance, Widgets, Apps, Behavior, Profiles, Automations, Screens, General; widget pages `w:<key>` through `IWidgetOptions`), `GalleryForm`, `InputBox` |
| `Hooks.cs` | WH_KEYBOARD_LL on its own thread: Win+1..9 (Shift = new window), Win-key peek, Alt+Space, Win+Alt+E/P/1..4 |
| `SelfTest.cs` | `DemoData`, the `--selftest` renders, `bench` and `data` |

## How it works
- **Taskbar takeover**: the Windows taskbar is set to auto-hide and `Shell_TrayWnd` (and the secondary trays) are hidden every tick, except while Start, Search or quick settings are open. `Restore()` puts the user's state back. Each bar is an AppBar (`SHAppBarMessage`) while it reserves space (`Config.ReservesSpace`).
- **Window**: `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED`; `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`. Clicking the bar never steals focus. Empty bar space is painted with alpha 1 (invisible) so a layered window still gets the clicks; alpha 0 is click-through.
- **Glass** (`Glass.cs`, `GlassBack`): the bar is a per-pixel-alpha layered window (`DibSurface.Present` = `UpdateLayeredWindow`), so the blur lives in a second window directly under it. It **owns** the bar (`Native.SetOwner`), so Windows keeps the bar above it. **Un-own the bar before destroying the glass**: destroying an owner destroys its owned windows (this once killed the bar on a style switch). Tiles style: the classic acrylic accent on a rectangle. Floating dock on Windows 11: a `Windows.UI.Composition` visual tree (`DWMWA_USE_HOSTBACKDROPBRUSH` + `CreateHostBackdropBrush` + a tint sprite) clipped to a rounded rectangle, inset 1 px under the bar's anti-aliased rim. Tested on 25H2: the acrylic accent, the system backdrop and blur-behind all **ignore** `SetWindowRgn` (they fill the whole rectangle), and stacked acrylic strips band visibly. The composited tint uses `GlassBack.EffectiveTint` (`1-(1-a)^1.55`) so it is as dark as the acrylic was.
- **Bar shape**: the bar window is a full-width strip; the visible bar is `ShapeFor(size, width)` inside it (width mode, alignment, offset, floating gap). The dock is painted at the animated width `curW`, outer radius = tile radius + `DockPad`, the same padding on all four sides. Outside the shape every pixel is alpha 0. Show desktop is hidden in dock style; the dock ends with a divider + Recycle Bin (`dock.trash`), and `hotcorner` shows the desktop from the bottom-right corner.
- **Exit codes**: `Program.ExitCode` starts at 3, so if the bar's window ever disappears without `Quit()` (or Windows signing out), the watchdog restarts it instead of quitting.
- **Painting**: GDI+ into a persistent `DibSurface`, then `UpdateLayeredWindow` (its constant alpha does "dim when idle"). Use GDI+ `DrawString` on glass, never `TextRenderer`. `Gfx.Prep` uses `CompositingQuality.HighSpeed` + `PixelOffsetMode.Half`.
- **Layout**: three zones of items (`MyZones`: the main layout, or a screen's own). `LayoutZone` lines each zone up from x = 0 (hidden widgets take no room); spread layouts put left at the left edge, right right-aligned and center centered between them; "fit" and layouts with flexible spacers line everything up. `UpdateSlots` folds targets into `anims`, so tiles slide. `Fit()` keeps app icons usable: compact widgets first, then hides widgets (right zone first; never clock, show desktop, media, start, search). Widget sizes, the fit, the cell width and the target width are computed **once per layout pass** (`NewLayoutPass()`, `Sizes()`); without that a paint took 5 ms instead of 2.
- **Edit mode** (`editMode`, Win+Alt+E): items get outlines and a ×; empty zones show a drop area (a placeholder slot with `Item == null`); spacers resize from their right edge; holding a small widget over another one stacks them; a click on a widget opens its settings. Every layout change calls `PushUndo()` first (30 steps), which also gives a secondary screen its own layout on its first edit.
- **Widgets**: subclass `Widget` (`Width`, `Paint(g, slotRect, ui, mouse, hover)`, optional `Tip`, `Click`, `Wheel`, `SliderAt`/`SliderMove`, `Shown`, `Available`, `CompactWidth`, `Options(IWidgetOptions)`, `LineText` for stacks, `Tick` for timers/polling, `RepaintMs` for fast repaints, `DragOut`), then add it to `Widgets.All` and a gallery category. Per-copy options go through `Opt`/`SetOpt` (cached; `Widget.OptionsChanged()` invalidates). Poll with `PollEvery(seconds, network, work)`: it only runs while the widget is on a bar, network work goes to the thread pool, and errors back off. The bar draws the tile behind a widget (unless `Bare`); the widget draws inside `u.Tile(r)`. Size everything with `u.F()` / `u.FI()` and use `u.Title/Bold/Text/Small/Tiny` and the `u.Glyph*` fonts. Reuse `InfoTile` (glyph, caption, value) and `ToggleTile` so new widgets match.
- **Timers**: 500 ms main tick (windows/groups, taskbar watchdog, stats, screen time, focus timer, automations, widget `Tick`s, repaint), 15 ms animation tick (only while something moves), 40 ms edge tick (auto/smart hide only), 90 ms hover tick (only while hovering), a live timer only while a widget asks for `RepaintMs` (stopwatch, level meter), 100 ms hot-corner check (only if on).
- **Background**: `Bg` jobs and widget polls only run while their widget is on a bar (`Svc.Active(id)` covers every zone, stack, copy and screen). Services publish snapshots; the UI only reads them and `BarForm.Poke()` repaints every bar. Network: thread pool, 10 s timeout, keyless sources only.
- **More than one screen**: the main `BarForm` (`Instance`, `Primary`) creates one `BarForm` per other screen (`SyncScreens`, on start and `WM_DISPLAYCHANGE`). Global work (hooks, clipboard, services, screen time, automations, widget ticks, Settings, launcher) stays on the main bar; each bar has its own AppBar, glass, apps (its own screen's windows with `apps.thisscreen`) and zones. Single-screen machines behave exactly as before. **Not tested on real multiple monitors yet.**
- **Profiles and automations** (Profiles.cs): a profile is settings.txt minus `Profiles.Managed() == false` keys; switching rewrites those keys (`Settings.ReplaceManaged`), reloads and applies. Automation rules act while their condition holds and undo themselves (profile back, widget shown/hidden, hide mode override via `Automations.HideOverride`, dim, quiet). Overrides are runtime-only; `Config.Save` never writes them.
- **Popups**: tooltips and previews are no-activate windows shown with native `ShowWindow`, so check `.Showing`, not `.Visible`. Flyouts activate and close on deactivate; use `Flyout.ShowMenu` for a menu inside a flyout. `ToggleFlyout` handles click-to-close.
- **Keyboard hook**: reads modifiers with `GetAsyncKeyState` at the moment of the key press, so a missed key-up can never swallow normal typing. It swallows only Win+digit, Alt+Space and Win+Alt+E/P/1-4, then taps a dummy key (0xE8) so Start and menu bars don't open. Ignores injected keys.

## Rules
- Never leave the user without a working taskbar. Every exit path must restore it or hand off to the watchdog.
- Idle CPU stays under about 1% of one core (measured in phase 3: ticks 0.6-0.9 ms, paints about 2 ms, about 0.6% of one core with the bar visible, about 95 MB). Don't poll slow things on the UI thread; nothing network-related on the UI thread, ever.
- Check visuals with `--selftest` (and `--selftest bench` after painting changes), data with `--selftest data`, or a DPI-aware screenshot of the real screen (only when the user isn't using it; a fullscreen app hides the bar).
- Don't restyle existing visuals: the user will redesign the UI. New UI reuses `Gfx`, `Ui` fonts, `Palette` and the Settings controls.
- lumabar is only a design reference. Don't copy its code, icons or assets.
- Don't send synthetic mouse or keyboard input while the user may be using the PC.

## Not done yet (ideas for next phases)
- A vertical bar on the left/right edge (asked to do later).
- System tray icons (Discord, Steam...). Needs our own `Shell_TrayWnd` plus forwarding AppBar messages to Explorer's; risky, so the `quick` widget opens Windows quick settings for now.
- Translations (all text is inline English), notification count badge, true jump lists (IAutomaticDestinationList) instead of recent files by association.
- Night light and do-not-disturb use undocumented Windows state; if Windows changes it, they fall back to opening Settings.
- Stocks use Yahoo Finance's public chart endpoint (keyless but unofficial); Stooq now blocks scripts.
