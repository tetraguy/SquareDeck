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
  programs and apps. Steam games use Steam's own artwork; covers and everything else come from SteamGridDB.
- **Built for a controller.** Every page, setting and pop-up works with a gamepad, touch, keyboard or mouse.
- **Made for handhelds.** Power limits (TDP) per game, a quick menu over your game, and it gets out of the game's way
  while you play.
- **Yours to style.** Color themes, wallpapers, moving backgrounds, sounds and music (a new theming system is on the way).

## Features

### Library and games
- Home row of recent games, favorites, collections, categories, ratings and status
- A notebook for every game: rich-text notes (headings, lists, checklists, highlights, colors) with your screenshots
  dropped right into the text, pinned and colored note cards, and templates to start from
- Game pages with details, achievements, screenshots, trailers and play time, for every game (details for non-Steam
  games come from the Steam store when the game is sold there, or Wikipedia)
- Pick any game's square cover, icon, tall grid, header capsule, hero image and logo from SteamGridDB (one tab each), or
  use your own, with a progress card while it downloads
- Library: section tabs (Installed, Favorites, Recently played, Not installed, Wishlist, Updates, Collections) switched
  with LB / RB, chips for each store, category and status, and a slim toolbar (search, sort, view, More). Covers as
  Square, Grid (tall poster art) or Rectangle (the wide store header), in eleven views: Grid, Compact, Large, Mini,
  Posters, Banners, List, Spotlight, Spotlight list, Shelves and Timeline, grouped by category, main genre, or played
  and never played
- Home row art as Square, Tall grid or Rectangle, in Small, Medium or Large, with fifteen highlight animations
- **Wishlist and sales:** your Steam wishlist and games you watch, with prices, discounts and the lowest price seen, a
  notification when one goes on sale, and a month-view **release calendar**
- Install stores from SquareDeck, including RecompLauncher (PC recomps) straight from its GitHub releases
- Launch splash that covers store pop-ups until the game is on screen, with six animations and an optional countdown
- How long to beat (from HowLongToBeat.com) on every game's page, next to your own play time
- Start Steam games straight from their .exe to skip Steam's launch pop-up
- Asks before starting a Steam game with an update waiting: update first (with live progress) or play anyway
- Stop a game from the launcher, with a console-style "Closing software" screen
- Session summaries, play-time tracking and achievement pop-ups
- **Cloud gaming:** Xbox Cloud Gaming, GeForce NOW, Amazon Luna and Antstream Arcade on the Stores page, full screen
  with your controller passed straight to the game. Leave by holding View + Start (or other buttons you choose in Settings), with Ctrl+Shift+Q (also changeable), or the hidden corner button

### Handheld
- **TDP control** for AMD and Intel handhelds (RyzenAdj, the device's own firmware on ROG Ally, Legion Go and MSI Claw,
  or KX.exe from HandheldCompanion for Intel), with per-game limits for battery and plugged in
- **<Your model> Specific Settings** (in Settings > Experimental): settings for the handheld SquareDeck recognizes (about 75
  models from ASUS, Lenovo, MSI, Valve, AYANEO, GPD, OneXPlayer, AOKZOE, AYN, ZOTAC and more), with its own power
  range, its controls, and the list of supported handhelds
- **My Device:** a live dashboard with a battery ring, gauges for processor, graphics, temperature and fan, and cards
  for power draw, battery health and pace, the power limit, controller battery, memory, every drive, display, network
  and sound (a home screen button, off by default)
- **Legion Go and MSI Claw:** Legion modes / MSI user scenarios, fan boost, charge limit and TDP through their firmware
- **VacuumTube** (experimental): YouTube's TV and console app, made for the controller, with VacuumTube's ad blocking,
  SponsorBlock, DeArrow and Return YouTube Dislike. It gets its own tab on the Music page, next to Spotify
- **Adrenaline Hook** (experimental, AMD graphics): adds Xbox, Store and other games to AMD Software so its per-game
  settings work for them, with Adrenalin switches per game, repair after updates, backups and the Radeon overlay shortcuts
- Live processor, memory, graphics, storage and network readouts in Settings > Power and performance
- **Performance Boost:** frees memory before a game starts, with fifteen animations and matching vibration
- **Performance overlay:** FPS, frame time, CPU / GPU use, CPU speed, RAM, TDP and battery over your games, in three
  layouts, three detail levels and six styles (FPS through the bundled [PresentMon](https://github.com/GameTechDev/PresentMon))
- **Game profiles:** resolution, refresh rate, HDR, power plan and audio device, applied on launch and put back after
- **In-game quick menu** over any full-screen game (Xbox mode too): live CPU / GPU / RAM use, screenshot and record,
  volume, brightness, TDP and exit
- **Xbox mode home app:** boot straight into SquareDeck on Windows 11's full screen experience, no extra tool needed
- **ROG Ally buttons:** remap Armoury Crate, Command Center, Xbox Library, M1 / M2 and the back-button combos
  (ROG Ally, Ally X, Xbox Ally, Xbox Ally X and Xbox Ally X20)
- **ROG Ally controls:** stick lights (effects, colors, battery level, the selected game's colors, low-battery pulse,
  game lights that follow the running game or its game profile),
  ASUS Silent / Performance / Turbo, fan curves, battery charge limit, TDP through ASUS's firmware with no setup,
  mouse mode and switching off the built-in controller
- Lean mode while a game runs: lets go of covers, pages and caches, frees graphics memory and lets Windows reclaim
  its memory first, then comes back with focus when the game closes
- Rests while another app is in front: music, moving backgrounds and animated covers pause
- Performance mode, low-battery notice and Do not disturb while playing

### Look and feel
- Themes, in three tabs:
  - **Basic:** 100 styles of the Switch design, each with its own colors, type, corners, buttons, wallpaper or moving
    background, sounds and music
  - **Advanced:** 18 styles made from UI kits, with the kit's own panels, buttons, dialogs, switches, mouse pointer and
    (for some) icons. Several come in more than one color
  - **Legendary:** 21 styles drawn from scratch by Claude: every panel, button, pop-up, switch, check box, pointer,
    icon and wallpaper, each with its own font, sounds and music
- Create your own styles: background, music, interface and navigation sounds, colors, font, button shape and look,
  shelf, effects (halftone, comic book, synthwave, Y2K, dreamcore and more), game art, animations and the Windows lock
  screen picture. Save them with your own name, and export or import them as .sdstyle files
- 22 color themes, plus your choice of font and game art shape and size
- 37 moving backgrounds (or add your own videos), 26 picture wallpapers, 19 music tracks and 8 interface sound packs
- Controller button pictures for Xbox, PlayStation and Switch, in two art styles, plus keyboard keys
- Light, dark, OLED and high-contrast themes, larger text and reduced motion
- 16 startup animations, and a "Good morning" greeting by your profile picture

### Built in
- Web browser with tabs, favorites and controller navigation; web links from anywhere in SquareDeck can open in it
- Multimedia page: music player for your own files, Spotify playing inside the launcher (Premium), now-playing from any app, and Netflix, YouTube, Hulu,
  Prime Video, Crunchyroll, Disney+, Paramount+ and Tubi full screen with controller controls (each locked to its own site),
  opening with a movie-night animation (curtains, film countdown, clapperboard, marquee lights or popcorn); full
  screen, with the controller read by the page itself, and a now-playing pill on the home screen while one is open
- Captures gallery for Game Bar screenshots and clips, grouped by game into albums you can export as a ZIP or delete,
  with a built-in share sheet and 3 to 6 captures per row
- File explorer, task manager, Wi-Fi, Bluetooth, display and sound settings
- Terminal (Command Prompt or PowerShell) inside the launcher, and a storage speed test for drives and memory cards
- Steam friends, game news, search across everything
- Backup and restore of settings and library
- Updates install by themselves: the new version downloads, installs silently behind a full-screen progress
  animation, and SquareDeck opens again
- Apps in Settings: see every installed app, and uninstall, modify, repair or reset it
- A play-time dashboard: weekly and 12-week charts, an activity heat map, most-played games, genres, habits, achievements and records
- Optional player stats on game pages: players now and peaks, owner estimates, monthly players and review history, with SteamDB a press away

### Built-in Solutions

## File Explorer

<img width="2559" height="1439" alt="File Explorer" src="https://github.com/user-attachments/assets/b17bd796-df4b-43cc-9b36-8d106b50dd41" />

## Music Player

<img width="2559" height="1439" alt="Music Player" src="https://github.com/user-attachments/assets/7716aaea-1b45-480f-a6e4-c1be6883a067" />

## Web Browser

<img width="2559" height="1439" alt="Browser" src="https://github.com/user-attachments/assets/1a5a70eb-ee64-454e-ab19-c3ac034e246c" />

## Task Manager

<img width="2559" height="1439" alt="image" src="https://github.com/user-attachments/assets/0995c3ca-374d-405b-9f68-077b6781fd4e" />


## Install

1. Download the latest release from the [Releases page](https://github.com/tetraguy/SquareDeck/releases).
**Requirements**

| | |
|---|---|
| Windows | 10 version 2004 or newer. Windows 11 24H2 for Xbox mode. |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Browser | WebView2 Runtime (included with Windows 11 and current Windows 10) |
| TDP control | An AMD Ryzen or Intel handheld (Intel: HandheldCompanion installed, for KX.exe). Setup asks for administrator permission once. |

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
| Library section | RB / LB | |
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
- **The in-game quick menu** and **performance overlay** are windows over the game. They can't appear over exclusive-fullscreen games; borderless works.
- **Cloud gaming** services need their own accounts and subscriptions, and run in SquareDeck's built-in Edge (WebView2).
- **Play time** is counted only while SquareDeck is open.
- **Starting Steam games directly** skips launch options set in Steam, may hide the Steam overlay, and some games
  still restart through Steam.
- **A backup file contains your API keys.** Keep it private.
- **Replace the Windows desktop** makes SquareDeck your shell. If it ever fails to open: Ctrl+Shift+Esc > Run new
  task > `explorer.exe`, then turn the setting off.

More notes on individual features are in the [changelog](CHANGELOG.md).


## Credits

- Artwork from [SteamGridDB](https://www.steamgriddb.com/)
- FPS in the performance overlay uses [PresentMon](https://github.com/GameTechDev/PresentMon) (MIT), bundled in `SquareDeck/Tools/PresentMon`
- TDP control uses [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) (LGPL-3.0), bundled in `SquareDeck/Tools/RyzenAdj`
- ROG Ally button reading is based on [AnyFSE](https://github.com/ashpynov/AnyFSE) by Artem Shpynov (MIT)
- ROG Ally firmware, lights and controller messages, and the Xbox Ally X20's model id, come from
  [HandheldCompanion](https://github.com/Valkirie/HandheldCompanion) by Valkirie (CC BY-NC-SA 4.0). The files ported
  from it (`Services/AllyHardware.cs`, and the parts of `Services/AllyButtons.cs` marked as such) are shared under the
  same licence. So are the handheld list and power ranges (`Services/Handheld.cs`) and the Legion Go, MSI Claw and
  Intel commands (`Services/DeviceHelper.cs`, `Services/HandheldVendors.cs`), which follow it too. Intel power limits
  use KX.exe from your own HandheldCompanion install; SquareDeck doesn't include it
- Adrenaline Hook (Settings > Experimental) is rebuilt from [Adrenaline Hook](https://github.com/tetraguy/Adrenaline-Hook) by tetraguy
- VacuumTube (Settings > Experimental) runs the page scripts of [VacuumTube](https://github.com/shy1132/VacuumTube) by shy
  (MIT licence), bundled in `SquareDeck/VacuumTube` by `tools/vacuumtube/build.js`; its licence is in that folder
- The Advanced styles use these UI kits (in `SquareDeck/Skins`), each under its own terms:
  NEONIX UI assets by GONKEN Games — https://gonkengames.itch.io/ ; Free Hologram Interface by Wenrexa; Glowing UI icons;
  Mega Cozy UI Pack and Cozy UI Pack demos by dobo_ui; Pixel UI Kit by Northleaf Studio; Tropical Night UI Lite by Brunka
  Games; Retro Windows GUI; Retro Arcade UI Mini Pack (CC0); Softline UI Kit – Flat Night by The Cooked Works; Fern & Brass
  by Kaliopa Studio; Nebula UI by kama-labs; Soft Modern UI Lite. The full list is in the app under Settings > Updates and data > Credits
- Indivial uses the Indivial UI kit (three colors)
- Scholar uses the Scholar GUI kit, Afacad Flux by Kristian Möller and OpenDyslexic by Abbie Gonzalez (SIL Open Font License)
- Music "At Rest", "Daybreak" and "Thinking of You" by Kevin MacLeod (incompetech.com), licensed under Creative Commons: By Attribution 4.0
- Sapphire uses the Sapphire GUI kit (three colors) and Barlow Condensed by Jeremy Tribby (SIL Open Font License)
- Spectrum uses the Spectrum UI kit (five colors)
- Atelier, Abyss, Greenhouse, Origami, Sumi-e, Forge, Glacier, Cartographer, Sweet Shop and Gatsby are original styles drawn for SquareDeck, with fonts under the SIL Open Font License: Philosopher, Play, Crimson Text, Varela Round, Spectral, Saira Condensed, Didact Gothic, IM FELL English, Sniglet and Poiret One
- Meridian is an original style drawn for SquareDeck (artwork, icons, pointer and star-chart wallpaper), with the Marcellus font by Brian J. Bonislawsky (SIL Open Font License)
- Ubuntu is an original skin in the spirit of the Ubuntu desktop, with the Ubuntu font by Dalton Maag (Ubuntu Font Licence 1.0). Ubuntu is a trademark of Canonical
- Fatanzia uses the Fatanzia UI kit
- Cozy Place uses the Travel Book UI (Lite) kit
- Luna XP uses the Windows XP UI pack packed by NullTale (https://nulltale.itch.io/). The Windows XP look, icons,
  sounds and the Bliss wallpaper belong to Microsoft
- Interface sounds: UI SFX by CAPTNPHANTOM (Essential UI packs) and UI & Menu SFX by Northleaf Studio
- Fonts: VT323 by Peter Hull and Jersey 10 by Sarah Cadigan-Fried (SIL Open Font License), Pixel Operator by Jayvee Enaguas (CC0)
- Sound packs, music, moving backgrounds and controller glyphs belong to their respective creators

SquareDeck is an independent project. It isn't affiliated with or endorsed by Valve, Microsoft, Nintendo, Sony,
ASUS or any store or publisher named here. All trademarks belong to their owners.
