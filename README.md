# SquareDeck

A console-style home screen for your installed games and apps (WPF, .NET 8).

## Build and run

1. Install Visual Studio 2022 with the ".NET desktop development" workload (or just the .NET 8 SDK).
2. Open `SquareDeck.sln` and press F5, or run `dotnet run --project SquareDeck` from this folder.

Requires Windows 10 version 2004 or newer. The first build downloads one NuGet package (Microsoft.Web.WebView2, for the
built-in browser), so it needs an internet connection. The browser itself uses the WebView2 Runtime that ships with Windows 11
and current Windows 10.

The sound packs in `SquareDeck/SoundPacks` were supplied separately and several reproduce console interface sounds that belong
to their respective owners. Check that you have the right to redistribute them before publishing them with a release.

## Controls

| Action                    | Controller                | Keyboard            | Mouse / touch                 |
|---------------------------|---------------------------|---------------------|-------------------------------|
| Move                      | D-pad / left stick        | Arrow keys          | Wheel, or click a tile        |
| Start game / select       | A                         | Enter               | Click the selected tile       |
| Game details              | X                         | X                   | Options > View details        |
| Game options              | Y                         | Y                   | Right-click a tile            |
| Back                      | B                         | Esc                 | Back button                   |
| Next / previous page      | RB / LB                   | Page Down / Page Up |                               |
| Back to the running game  | B (on the home screen)    | Esc                 |                               |
| Stop the running game     | Down, Down, then A        | Down, Down, Enter   | Stop in the footer            |
| Next / previous screenshot| RB / LB (in the viewer)   | Page Down / Page Up | Buttons under the image       |
| Quick settings            | View                      | Q                   | Sliders icon, or pull down from the top edge |
| Scroll a long page        | Right stick               | Mouse wheel         | Drag                          |
| Jump by letter (library)  | LT / RT                   |                     |                               |
| Turn the page             | RB / LB                   | Page Down / Page Up | Swipe left / right            |
| In-game quick menu        | Guide, or View + Start (while a game runs) |    |                               |
| Status icons (top bar)    | Up from the games         | Up                  | Click                         |
| Round buttons             | Down, then A              | Down, then Enter    | Click                         |
| Settings                  | Start                     |                     | Gear button                   |
| Bring launcher to front   | Guide, or View + Start    |                     |                               |
| Windowed / fullscreen     |                           | F11                 |                               |

A, B, X and Y can be reassigned in Settings > Controller buttons (there is a Nintendo-layout preset).
LB / RB cycle: Games, Favorites, Apps, Game library, Captures, Music, Friends, Settings.

## What's where

- **Home row**: a wide **Continue playing** tile for your last game (when it was last played, time this
  week; A resumes it), the next most recent games, then a **Game library** tile. Badges show
  a running game and a pending or in-progress Steam update. Blurred artwork of the highlighted game sits
  behind the row.
- **Game library**: every game from Steam, Epic, GOG, Xbox / Game Pass, Battle.net, Steam's non-Steam
  shortcuts, and your own custom games. Filter (all, favorites, by store, by category), sort (recent,
  name, time played, size), search, group by category, add custom games.
- **Y on a tile**: stop, change image, favorite, edit title, set category, show in folder, store page,
  uninstall, remove from the list (restore from Settings).
- **Rating, status and notes** (Y menu or details page): 1 to 5 stars, Playing / Finished / Dropped /
  Backlog, and a note per game. The library can filter by status and by 4 stars and up, and sort by rating.
- **Themes** (Settings): ready-made looks, wallpapers (built-in or your own image), square / rounded /
  round tile corners, and sound sets (Classic, Soft, Retro, or WAV files from a folder of yours).
- **Achievements**: a pop-up when a Steam game you just closed gave you new achievements, and a
  "Recent achievements" page (Game library toolbar, or Settings).
- **X on a Steam game**: the game's logo, recent news and patch notes, description, developer, publisher, genres, screenshots, trailer,
  time played, achievements with icons, and a link to HowLongToBeat.
- **Play time** (Game library toolbar, or Settings): hours per day and per game for any week, plus
  all-time totals, measured by the launcher for every game it sees running.
- **Friends**: Steam friends who are in a game or online; pick one to message them, see their profile,
  or start the game they are playing. Needs your own Steam Web API key (Settings, or the page itself).
- **Top bar**: profile picture, clock, quick settings, Wi-Fi and Bluetooth (while on), wireless
  controller battery, PC battery.
- **Round buttons**: Favorites, Apps, Captures, Music, Friends, Steam Store, Settings, Power (lock, sleep,
  hibernate, restart, shut down, exit to desktop).
- **PC settings** (Settings > This PC): resolution and refresh rate (with a 15-second keep-or-revert
  check), default sound output, screen-off and sleep timers on battery and plugged in, power plan, and
  shortcuts to related Windows pages.
- **Format external storage** (Settings > This PC): exFAT, NTFS or FAT32 for USB drives and memory cards.
- **Settings**: library, theme and highlight color, hero background, animated covers, startup chime and
  intro, battery percentage, play time, Steam Web API key, controller
  buttons, Guide shortcut, other stores, return-to-launcher, start with Windows, replace the desktop,
  quick settings, Wi-Fi, Bluetooth, API key, device information.

## New in 2.6

- **Icons from SteamGridDB**: the cover picker shows a game's icons when it has no square image (or on request); a chosen icon is shown centered on a wash of its colors.
- **Capture albums**: an Albums filter groups captures by the game in their file name; Settings can open Captures as albums.
- **Settings** shows the Steam account (name and Steam ID) instead of the Windows user name.
- **Notification panel** shows the game's cover or icon next to game notifications (with the achievement's icon in the corner).
- **Xbox mode home app** (Settings > System and tools): one switch points AnyFSE at SquareDeck, with the Windows Home app setting and "open Xbox mode at sign-in".
- **ROG Ally buttons** (Settings > Controller): remap the Armoury Crate, Command Center / Library and Mode+ buttons, plus the Ally's other keys. The button reading is based on AnyFSE by Artem Shpynov (https://github.com/ashpynov/AnyFSE, MIT licence); AnyFSE's injected filter for Armoury Crate SE is not included.
- **Apps row**: the app opened last comes first; from ten apps on, Add app moves into More apps.
- **Quick settings** has a Night light switch.

## New in 2.5

- **Modern look across the pages**: list rows are rounded cards grouped by section, each with its icon in a tinted badge; bolder page titles and section headings; pill-shaped buttons; rounded tiles and pictures; rounded dialogs with a shadow.
- **Settings home** is a profile card and a grid of category cards.
- **Captures** has All / Screenshots / Videos filters, one section per day, the game's name on each capture, and badges on the thumbnails.
- 2.5.1: the **Continue where you left off** tile is larger (Settings > Home screen > Home row), and the Captures page's Open folder and badge labels show text instead of boxes.

## New in 2.4

- **Library**: the not-installed list and the new **Wishlist** section are saved between runs, so they and their counts show at once; each refreshes in the background when its section is picked. Updates and Collections now appear in the sidebar too.
- Games with no square cover on SteamGridDB show their **icon** in the library as well as on the home screen.
- **Graphics memory**: while a game runs the launcher draws in software, drops the blurred background and animated cover, and frees its pictures, so the game gets the video memory (Settings > Games > While playing). Pictures are never decoded larger than needed.
- **LB / RB** skip the Music and Friends pages when their buttons are switched off.
- **Favorites**: a heart pops up once when a game is added, instead of a badge that stays on the tile.
- **One highlight at a time**: moving the mouse moves the focus, the sidebar's chosen entry uses an accent bar, and the home ring hides while a button has the focus.
- **Apps row** shows ten apps, then **More apps** opens all of them.
- **Stores page**: Y hides a store or uninstalls it (through winget).
- **Profile page** redesigned as cards, with your Steam level, XP, badges and recently played games (needs the Steam Web API key).
- **Night light** switch in Settings > Appearance > Screen.
- **Background music** in Settings > Sound: five built-in tracks, Shuffle all, and your own MP3s; plays only while no game runs.
- **Music page search** (Settings > Sound > Music page): YouTube Music in the built-in browser, and Spotify search and playlists with your own Spotify app's Client ID.
- 2.4.1: **Game details page redesigned**: edge-to-edge artwork banner that fades as you scroll, cover and title with the logo on the right, a large Play button with pill actions, stat cards, an About card with facts, and card layouts for screenshots, news, DLC and achievements. X opens collections, Y rating and notes.

## New in 2.3

- **Main display**: Settings > Appearance > Screen chooses which display the launcher opens on.
- **Quick menu** shows the running game's cover and takes its color from it, like the game's details page.
- **Windows notifications** from other apps appear in the launcher's pop-ups and notification panel, when Windows allows it (Settings > Privacy and security > Notifications).
- **Profile page** behind the profile picture: latest games, Steam wishlist with prices, achievements, play-time and library numbers. The picture itself is changed from Settings or the top of that page.
- **Speed test** download no longer reports "Failed": it falls back through several test servers.
- Friends' profile pictures on their pop-ups and in the notification panel.

## New in 2.2

- **Button hints** look the same on every page as on the home screen, and switch to keyboard keys when a keyboard or mouse is used. Settings > Controller chooses Xbox, PlayStation or Switch names.
- **Home row buttons**: new Home and Web browser buttons; Music, Friends, Web browser and Explorer can each be hidden in Settings.
- **Web browser** (WebView2) with back, forward, reload, home, address bar, bookmarks, zoom, search engine and home page settings.
- **Library**: Updates and Collections sections. Collections can be created, renamed, deleted and given a 1:1 picture or an icon; games are added from their options menu or details page.
- **Game details**: always shows the title, with the game's logo above the information column.
- **Notifications** open as a panel that slides in from the right.
- **Music**: an Open "player" button for whichever app is playing, tap the artwork to go to it, and players can be removed from the Open player list.
- **Sound packs**: every folder in `SoundPacks` (Steam Deck audio-loader layout: `pack.json` plus `deck_ui_*.wav`) appears under Settings > Sound. Your own packs can also go in `%LOCALAPPDATA%\SquareDeck\SoundPacks`. A pack marked as music plays as optional menu music.
- **Updates**: checks github.com/tetraguy/SquareDeck/releases (automatic by default, or manually), shows the release notes, then downloads and installs.
- **Screen saver** on the home screen: moving gradient or a slideshow of game artwork with logos, after a time you choose.
- **Battery page** from the battery icon; **Steam avatar** as a profile picture; game icon on the session summary.
- **Highlight color**: follow Windows, or pick any color from a gradient.
- More closing animations, a 16:10 screen shape, reorganized Settings, and Reset the launcher.
- 2.2.1: **Restart to UEFI / BIOS setup** (Settings > This PC), and the session summary shows the game's icon.

## New in 2.1

- **Explorer** button on the home screen: a controller file explorer (A open, B up, X actions, Y select several, LT/RT page) with copy, move, rename, new folder, delete to the Recycle Bin, ZIP and extract.
- **Task Manager** (Settings > This PC): Apps, Processes and Performance tabs. X ends a task, Y changes the sort, LB/RB switch tabs. Windows' own processes are protected.
- **AnyFSE** page (Settings > This PC): what it is, download and run its installer from the project's GitHub releases, or open it if it is installed.
- **Accessibility** (Settings): text size 100 to 150%, high contrast (white on black, black on white), reduce motion, and a shortcut to Windows' color filters.
- **Interface sounds** page: eight sound sets, loudness, per-kind switches and test buttons. Sounds no longer cut out after a while.
- "Closing software" pop-up while a game is being stopped; B on the home screen never starts a game that has already closed.
- Wallpaper shows behind every page; the Music page uses the blurred album art while something plays; the library uses the full width.
- News and updates open in a built-in reader.
- Power menu icons, closing animation on Exit to desktop, six startup animations.
- Quick menu: slides in from the left or right (or the old centered pop-up), with a choice of controller buttons and an optional keyboard shortcut.
- Low-battery pop-up at 15% (and 5%), and a reminder when Windows is waiting to restart for an update.
- Cover downloads retry and fall back to the preview image instead of failing.

## New in 2.0

- **Controller file browser**: every "choose a file / folder" step now works with the controller
  (places, drives, folders, matching files; B goes up). A row at the bottom still opens the Windows window.
- **Library page**: a sidebar (All, Favorites, Not installed, stores, categories, status), achievement
  progress under covers, LT / RT jump by letter, and Steam games you own but haven't installed
  (needs your Steam Web API key) with an install queue.
- **Game profiles** (Y menu): resolution, refresh rate, HDR, power plan and sound output applied when a
  game starts and put back when it closes.
- **Y menu** also has View achievements. **Details** adds Steam's review summary and a DLC / Workshop mods list.
- **Notification center** (bell in the top bar): achievements, session summaries, friends, finished
  downloads and low-battery warnings.
- **Session summary**, **do not disturb while playing**, **3-second launch countdown**, **in-game quick
  menu**, **per-game highlight tint**: each has a switch in Settings.
- **Now playing** in the footer with play / pause, and a visualizer on the Music page.
- **Stores page** (cart button): the game stores installed on this PC, plus "Add more stores".
- **Settings** adds: backup and restore, vibration strength, controller test, network speed test, clock
  options, boot video, cache size limit, OLED (black) theme, and "Run first-time setup again".
- **First-run setup**, slide transitions on LB / RB, touch swipes, and a logo (`SquareDeck/Assets`).

## Good to know

- Stopping a game ends every program running from that game's install folder (for custom games and
  Steam shortcuts: that one program). Unsaved progress is lost.
- The full achievement list and "x of y unlocked" need your Steam profile's game details to be public;
  otherwise the handful the store highlights are shown.
- Trailers play in the launcher when Steam offers a plain video file; otherwise the store page opens.
- HowLongToBeat has no public data feed, so its button opens the site's search for the game.
- Animated covers use the short video preview SteamGridDB provides for its animated art. If this PC
  can't play that video format the tile simply keeps its still cover.
- Game profiles change the display temporarily (nothing is saved to Windows), and are put back when the
  game is seen to close, when the launcher closes, or after three minutes if the game never appeared.
- "Do not disturb" works by switching Windows notification banners off for your account while a game
  runs. Windows has no public switch for its own Do-not-disturb mode. If the launcher is ended abruptly
  mid-game, it turns banners back on the next time it starts.
- The in-game quick menu is a window on top of the game. The game still receives the same controller
  presses, and it can't appear over exclusive-fullscreen games (borderless works).
- The install queue opens Steam's install window for each game in turn; Steam still asks you to confirm.
- The music visualizer follows overall loudness, not individual frequencies.
- A backup file contains your API keys. Restoring replaces current settings and restarts the launcher.
- Formatting erases the whole drive. Only USB drives and memory cards are listed, never the Windows
  drive; it asks twice (Cancel is the first choice both times) and Windows asks for administrator approval.
- A game with no square cover on SteamGridDB shows its icon on a gradient instead.
- Play time is only counted while the launcher is open.
- Achievement pop-ups need your Steam profile's game details to be public, and the game to be started
  and closed while the launcher is open. Steam's page can take a minute to update after a session.
- Custom sound folder: WAV files named move.wav, select.wav, back.wav, error.wav, startup.wav, toast.wav, page.wav.
- Brightness only appears in quick settings on built-in screens (laptops and handhelds).
- "Airplane mode" in quick settings turns Wi-Fi and Bluetooth off together; it is not Windows' own switch.
- **Replace the Windows desktop** makes the launcher your shell after the next sign-in. If the launcher
  ever fails to open: Ctrl+Shift+Esc > Run new task > `explorer.exe`, then turn the setting off.
- Scanning for Wi-Fi networks needs Location turned on in Windows privacy settings.
- Windows' own file picker (custom images and programs) needs a mouse, touch or keyboard.
- Covers, settings and your changes are stored in `%LOCALAPPDATA%\SquareDeck`.

The SteamGridDB API key is set in `SquareDeck/Services/Settings.cs` and can be changed in Settings.
Remove it from the source before publishing this code anywhere public.
