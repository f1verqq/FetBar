# StayBar 3

An always-on replacement for the Windows 11 taskbar: a full-width bar of rounded tiles, or a floating glass dock. It has 76 live widgets, window previews, a launcher, themes, a widget gallery, profiles and automations, and it uses well under 1% of one CPU core when idle.

## Start it
1. Quit lumabar or any other taskbar replacement first. Two bars stack on top of each other.
2. Double-click **build.bat** (only needed after changing the code). It creates StayBar.exe.
3. Double-click **StayBar.exe**.

Nothing to install: it uses the C# compiler and Windows APIs that come with Windows.

## What's on the bar
| Where | Left click | Right click | Other |
|---|---|---|---|
| Start | Start menu | Power user menu (Win+X) | |
| Search | StayBar launcher (also **Alt+Space**) | Windows Search | |
| App icon | Open / switch / minimize; more clicks cycle windows | Recent files, private window, window list, pin, close, **name and icon** (rename, change icon, hide) | Middle click: new window. **Hover: live previews.** Drag to reorder. Drop one app onto another to make a folder. Drag off the bar to unpin. Drop files on it to open them with it |
| Now playing | Open the player; buttons: previous / play-pause / next | | Hides when nothing plays |
| Volume / Brightness | Drag the bar, or click the icon to mute | Sound / display settings | **Scroll over it** to change |
| Wi-Fi / Bluetooth pills | Turn on/off | Settings | Arrow: settings |
| Weather | 7-day forecast | Refresh / change city | |
| Any widget | What it does, or **your own click action** | Its menu + **Widget settings…**, add another copy, remove | Drag to reorder (also between zones), or drag it off the bar to remove it. Shift+right-click always shows the widget menu |
| Empty space | | Settings, gallery, **edit the bar**, hide mode, floating dock, **profile**, theme, quit | |

Keyboard: **Win+1…9** opens or switches to the 1st…9th app (Shift = new window), **Alt+Space** opens the launcher, **Win+Alt+E** edits the bar, **Win+Alt+P** switches to the next profile, **Win+Alt+1…4** picks a profile, and with auto-hide on, the **Win** key briefly shows the bar.

## Make the bar yours
- **Zones**: the bar has a left, a center and a right zone. Put widgets, the apps and **spacers** (flexible, or a fixed size) and **separators** in any of them.
- **Edit mode** (Win+Alt+E, or right-click > Edit the bar): drag anything between zones, × removes, drag a spacer's edge to resize it, hold one small widget over another to **stack** them in one tile, click a widget for its settings. Undo with Ctrl+Z or the Undo button.
- **Size and shape** (Settings > Dock): full width, a percentage, a fixed width or fit to content; left, center or right with an offset; floating (a gap all around) or attached; bar height, tile gap and tile padding.
- **Floating dock**: a real rounded glass dock that hugs its tiles, with the same padding all around. Like on a Mac it ends with the Recycle Bin; show the desktop with Win+D or the optional **hot corner** (bottom-right).
- **Hiding**: never, only for fullscreen apps, **smart** (only while a window touches the bar) or auto-hide, with reveal and hide delays. Optionally **dim when idle**. Reserve screen space on or off.
- **Apps**: combine windows always, never or when the bar is full; window titles next to icons; only running apps; hide, rename or give a custom icon to any app.
- **Widget settings**: size (small / medium / large), style, label on/off, and a **custom click action** (open an app, file, folder or website, or run a command) for any widget. Add **more copies** of widgets like world clocks, notes, countdowns, timers, scripts and launcher tiles.
- **Screens**: a bar on every screen (or just the ones you pick), each with its own layout and position.
- **Profiles**: save the whole bar as a profile (Study, Gaming, Coding and Night are ready to edit), switch from the menu or with Win+Alt+P, import and export them.
- **Automations**: "when X, do Y" rules. When: a fullscreen game, a time of day, on battery / plugged in / battery low, an app is open, a number of screens, a Wi-Fi network. Do: switch profile, show or hide a widget, auto-hide, dim, start the focus timer, no sounds. Rules undo themselves when the condition ends.

## Widgets (add them in the Widget gallery)
- **Essentials**: Start, Search, Clock, Quick settings, Keyboard language, Notifications, Show desktop.
- **Media**: Now playing, Volume, Brightness, Audio level meter, Volume mixer (per app), Microphone (mute, and a dot when the mic or camera is in use), Audio output switcher.
- **System**: CPU (with temperature), Memory, GPU, CPU+RAM bars, Disk space, Battery, Battery health, Top process, Uptime, Disk activity, Power mode, Safely remove (USB).
- **Network**: Wi-Fi & Bluetooth, Network speed, Ping & public IP (and VPN on/off), Wi-Fi signal.
- **Time & weather**: Weather, Air quality, Week strip, World clocks, Screen time, Flip clock, Digital clock, Analog clock, Time progress (day/week/month/year), Stopwatch, Timer, Alarms, Sunrise & sunset, Moon phase.
- **Productivity**: Focus timer (Pomodoro), Quick note, Clipboard history, Recycle Bin.
- **Study**: Countdown (exams!), To-do list, Next class (from a simple weekly timetable you write), Habit tracker, Calendar events (from a Google or Outlook calendar link), Word of the day, Quick translate, Unit and currency converter, Calculator.
- **Toggles**: Dark mode, Night light, Do not disturb, Game mode, Desktop icons, Screenshot, Color picker, Keep awake, Eye breaks (20-20-20), Power (lock, sleep, restart, shut down).
- **Info**: Crypto prices, Exchange rates, Stocks, Headlines (RSS).
- **Dev**: Claude Code usage today (read from its local logs, nothing is sent), GitHub activity, Latest download (drag it out of the bar).
- **Custom**: a **script widget** that runs a command or PowerShell line every few seconds and shows its output (text, a number with a meter, or an icon), and **launcher tiles** for any app, file, folder or website.

Online widgets use free sources that need no account or key: Open-Meteo (weather, air quality), CoinGecko, frankfurter.app / open.er-api.com, Yahoo Finance's public chart data (unofficial, may change), MyMemory (only when you translate), GitHub's public contribution calendar, and the feeds you choose. They only run while the widget is on the bar.

## Looks
Themes: **Midnight** (black tiles), **Glass**, **Liquid** (clear glass), **Graphite**, **Neon** (glowing edges), **Light**. Accent: pick a color, **match your wallpaper**, or use the Windows accent. Size presets, roundness, glass darkness, magnify-on-hover, sliding and bouncing animations.

## Settings
Right-click empty space and choose **Settings...**, or run `StayBar.exe --settings`. Pages: Dock, Appearance, Widgets, Apps, Behavior, Profiles, Automations, Screens and General, plus a page for each widget. Every change applies right away and is saved in settings.txt; settings from older versions keep working.

## Why it never closes
- It reserves its own strip of the screen, so maximized windows stop above it (unless you turn that off).
- It stays on top, and steps aside for fullscreen games and videos.
- A watchdog restarts it within a second if it crashes.
- If Explorer brings the Windows taskbar back, StayBar hides it again.

To close it on purpose: right-click empty space > **Quit**. That brings the Windows taskbar back.
If anything goes wrong, double-click **restore-taskbar.bat**.

## Files
`%LOCALAPPDATA%\StayBar`: settings.txt, pins.txt (your pinned apps, in order), profiles\, note.txt, todo.txt, schedule.txt (your timetable), habits.txt, screentime\, error.log.

For Claude Code: see **CLAUDE.md**.
