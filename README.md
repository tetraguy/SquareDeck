<div align="center">

<img src="SquareDeck/Assets/SquareDeck.png" width="120" alt="SquareDeck logo">

# SquareDeck

**A controller-first game launcher for Windows handhelds and living-room PCs.**

Your games from every store, your apps, music and settings on one console-style home screen
you can drive entirely with a gamepad.

[Download](https://github.com/tetraguy/SquareDeck/releases) · [Changelog](CHANGELOG.md) · [Report a problem](https://github.com/tetraguy/SquareDeck/issues)

</div>

<!-- Add screenshots here, for example:
<p align="center">
  <img src="docs/home.png" width="49%"> <img src="docs/library.png" width="49%">
</p>
-->

## Why SquareDeck

<img width="2559" height="1439" alt="UI 4" src="https://github.com/user-attachments/assets/a1135cf3-5012-4a29-9315-76a8b651798d" />

Windows on a handheld means a desktop built for a mouse, and a different launcher for every store. SquareDeck puts
one home screen in front of all of it: pick it up, press A, play.

- **One library.** Steam, Epic, GOG, Xbox / Game Pass, Battle.net, Ubisoft Connect and the EA app, plus your own
  programs and apps. Covers, hero art and logos come from SteamGridDB.
- **Built for a controller.** Every page, setting and pop-up works with a gamepad, touch, keyboard or mouse.
- **Made for handhelds.** Power limits (TDP) per game, a quick menu over your game, and it gets out of the game's way
  while you play.
- **Yours to style.** 37 UI styles with matching colors, wallpapers, sounds and music.

## Features

### Library and games
- Home row of recent games, favorites, collections, categories, ratings, status and notes
- Game pages with details, achievements, screenshots, trailers and play time
- Pick any game's square cover, tall grid, hero image and logo from SteamGridDB, or use your own
- Launch splash that covers store pop-ups until the game is on screen, with an optional countdown
- Start Steam games straight from their .exe to skip Steam's launch pop-up
- Stop a game from the launcher, with a console-style "Closing software" screen
- Session summaries, play-time tracking and achievement pop-ups

### Handheld
- **TDP control** for AMD handhelds through RyzenAdj, with per-game limits for battery and plugged in
- **Game profiles:** resolution, refresh rate, HDR, power plan and audio device, applied on launch and put back after
- **In-game quick menu** on the Guide button: volume, brightness, TDP, Wi-Fi, Bluetooth and more
- **Xbox mode home app:** boot straight into SquareDeck on Windows 11's full screen experience, no extra tool needed
- **ROG Ally buttons:** remap Armoury Crate, Command Center and the back-button combos
- Frees memory and graphics resources while a game runs, and comes back with focus when it closes
- Performance mode, low-battery notice and Do not disturb while playing

### Look and feel
- 37 UI styles across five layouts: classic row, hero, top row, sidebar and stage
- Pixel-art, frosted glass, OLED black, console-inspired and themed styles
- 16 moving backgrounds, 9 music tracks and 7 interface sound packs
- Controller button pictures for Xbox, PlayStation and Switch, in two art styles, plus keyboard keys
- Light, dark, OLED and high-contrast themes, larger text and reduced motion

### Built in
- Web browser with tabs, favorites and controller navigation
- Music player for your own files, Spotify control, and now-playing from any app
- Captures gallery for Game Bar screenshots and clips, grouped by game, with a built-in share sheet
- File explorer, task manager, Wi-Fi, Bluetooth, display and sound settings
- Steam friends, game news, search across everything
- Backup and restore of settings and library

### Some of the Many Available UI Styles

## Pop Art

<img width="2559" height="1439" alt="UI 3" src="https://github.com/user-attachments/assets/143296ca-664d-48f8-a265-7d7f5a8c72c2" />

## Retro

<img width="2559" height="1439" alt="UI 2" src="https://github.com/user-attachments/assets/2203077b-14ae-4937-8ec5-1658b8d77b2b" />

## Aurora

<img width="2559" height="1439" alt="UI 6" src="https://github.com/user-attachments/assets/38a0fde8-304c-4c67-a2ab-5831475500f0" />

## Pixel Dawn

<img width="2559" height="1439" alt="UI 7" src="https://github.com/user-attachments/assets/a18d951a-94d7-432e-8499-8b0db6c1d8a3" />

## Pixel City

<img width="2559" height="1439" alt="UI 9" src="https://github.com/user-attachments/assets/7e3dad52-a7e5-40a8-81c8-7d6443882e90" />

## Install

1. Download the latest release from the [Releases page](https://github.com/tetraguy/SquareDeck/releases).
2. Extract it to a folder on an NTFS drive (for example `C:\Games\SquareDeck`).
3. Run `SquareDeck.exe`. A short setup helps you pick a look and your controller's buttons.

**Requirements**

| | |
|---|---|
| Windows | 10 version 2004 or newer. Windows 11 24H2 for Xbox mode. |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Browser | WebView2 Runtime (included with Windows 11 and current Windows 10) |
| TDP control | An AMD Ryzen handheld. Setup asks for administrator permission once. |

## Controls

| Action | Controller | Keyboard |
|---|---|---|
| Move | D-pad / left stick | Arrow keys |
| Start game / select | A | Enter |
| Back | B | Esc |
| Game details | X | X |
| Game options | Y | Y |
| Next / previous page | RB / LB | Page Down / Page Up |
| Jump by letter (library) | RT / LT | |
| Scroll a long page | Right stick | Mouse wheel |
| Search everything | Start / Menu | F3 or / |
| Quick settings | View | Q |
| In-game quick menu | Guide, or View + Start | |
| Windowed / full screen | | F11 |

A, B, X and Y can be swapped (there's a Nintendo-layout preset), and you choose which pages LB / RB switch
between, in Settings > Controller and quick menu.

## Set as the Xbox mode home app

On Windows 11 24H2 or later, SquareDeck can be what Xbox mode (the gaming full screen experience) opens to.

1. Turn on Developer Mode in Windows: Settings > System > For developers.
2. In SquareDeck: Settings > System and tools > Xbox mode home app > **Use SquareDeck as the Xbox mode home app**.
3. Optionally turn on **Open Xbox mode when Windows starts**.

SquareDeck registers its own folder with Windows; nothing is installed elsewhere. Developer Mode is needed because
Windows only accepts home apps from outside the Microsoft Store that way. [AnyFSE](https://github.com/ashpynov/AnyFSE)
remains available from the same page if you prefer it.

## Build from source

```
git clone https://github.com/tetraguy/SquareDeck.git
cd SquareDeck
dotnet run --project SquareDeck
```

Or open `SquareDeck.sln` in Visual Studio 2022 with the ".NET desktop development" workload and press F5.
The first build downloads one NuGet package (Microsoft.Web.WebView2).

SquareDeck is a WPF app on .NET 8. Settings, covers and your library edits live in `%LOCALAPPDATA%\SquareDeck`.

## Good to know

- **Stopping a game** ends every program running from its install folder. Unsaved progress is lost.
- **Achievements** and friends need your Steam profile's game details to be public, and a free Steam Web API key
  for some features.
- **The in-game quick menu** is a window over the game. It can't appear over exclusive-fullscreen games; borderless works.
- **Play time** is counted only while SquareDeck is open.
- **Starting Steam games directly** skips launch options set in Steam, may hide the Steam overlay, and some games
  still restart through Steam.
- **A backup file contains your API keys.** Keep it private.
- **Replace the Windows desktop** makes SquareDeck your shell. If it ever fails to open: Ctrl+Shift+Esc > Run new
  task > `explorer.exe`, then turn the setting off.

More notes on individual features are in the [changelog](CHANGELOG.md).


## Credits

- Artwork from [SteamGridDB](https://www.steamgriddb.com/)
- TDP control uses [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) (LGPL-3.0), bundled in `SquareDeck/Tools/RyzenAdj`
- ROG Ally button reading is based on [AnyFSE](https://github.com/ashpynov/AnyFSE) by Artem Shpynov (MIT)
- Sound packs, music, moving backgrounds and controller glyphs belong to their respective creators

SquareDeck is an independent project. It isn't affiliated with or endorsed by Valve, Microsoft, Nintendo, Sony,
ASUS or any store or publisher named here. All trademarks belong to their owners.
