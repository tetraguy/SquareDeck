# Changelog

All notable changes to SquareDeck, newest first. Each section can be pasted as the description of the
matching GitHub release; the launcher's updater shows that description as the changelog before installing.

## 5.20.2

### Changed
- **Cloud gaming tiles match the other store tiles:** they have the same rounded corners, following the tile-corner
  style in Themes.
- **Turn cloud gaming services on or off from the Stores page too.** "Add more stores" now starts with a Cloud
  gaming section that has a switch for each service. These are the same switches as in Settings > Games, and the
  Stores page updates when you go back.

## 5.20.1

### Added
- **Cloud gaming logos:** Xbox Cloud Gaming, GeForce NOW, Amazon Luna and Antstream Arcade now show their own logo
  tiles on the Stores page and while they load.

### Fixed
- **Menu music and moving backgrounds pause during cloud games.** They stay paused while a cloud gaming service is
  open, even with a menu over it, and pick up again when you leave.
- **The notification panel's close button (X) can be reached with the controller.** Press Up from the first
  notification, or Right from anywhere in the list. It's also where the cursor starts when there are no notifications.
- **The welcome message no longer runs into the page tabs and buttons** in UI styles that put them next to the
  profile picture (Waves, for example). It now slides out in its own rounded bubble that sits over the top bar for
  its three seconds, then slides back behind the picture.

## 5.20.0

### Added
- **Cloud gaming** in Settings > Games: turn on Xbox Cloud Gaming, NVIDIA GeForce NOW, Amazon Luna and Antstream
  Arcade one by one. Each one you turn on shows up on the Stores page with its own tile. Opening it plays the service
  full screen in SquareDeck's built-in Edge (WebView2). SquareDeck stops reading the controller while you play, so
  the game in the page gets every button. To leave:
  - hold **View + Start** for a second and a half (a ring fills in the corner while you hold), or
  - press **Ctrl+Shift+Q** on a keyboard, or
  - tap the faint dot in a corner of the screen. It opens into Exit, Reload and Hide, and fades back after a few
    seconds. You can choose which corner in Settings.
  Sign-ins are kept between visits.
- **Welcome message:** "Good morning", "Good afternoon" or "Good evening" with your first name. It slides out from
  behind your profile picture at startup, stays three seconds, then slides back and fades away. Turn it off, or play
  it again, in Settings > Appearance.
- **Ten new startup animations** (16 in all): Glitch, Pixel assemble, Neon sign, Liquid fill, Orbit, Blinds,
  Particle burst, Hologram scan, Card flip and Aurora.
- **Five new Performance Boost animations** (10 in all), each with its own vibration pattern:
  - **Vortex:** a spiral winds tighter and tighter, then lets go.
  - **Power grid:** squares light up in waves under the cover.
  - **Lightning:** bolts strike the cover, each with a jolt.
  - **Equalizer:** bars bounce to a beat around the cover.
  - **Hyperloop:** a rush down a tunnel of light rings.
- **Performance overlay** in Settings > Power and performance. It shows FPS, frame time, CPU use and speed, GPU use,
  memory, the TDP limit and the battery over your games. Clicks and taps go straight through it. You can choose:
  - **Details:** Minimal, Standard, or Full (adds a frame-time graph, memory in GB, battery time left and the clock).
  - **Layout:** Bar, Column or Tiles.
  - **Style:** Dark panel, Frosted glass, Text only, Neon, Highlight color or Retro terminal.
  - **Position, size and opacity**, and whether it shows only in full-screen games or all the time.
  FPS comes from Intel's PresentMon 2.6.0 (MIT), bundled in `Tools\PresentMon`. It needs SquareDeck to run as
  administrator, or your account in Windows' "Performance Log Users" group. Without it, everything else in the
  overlay still works.

## 5.19.0

### Added
- **Ten new UI styles** (65 in all):
  - **Synthwave:** a hot-pink to violet sky over a neon grid, with cyan Play and the game huge in the middle.
  - **Draftsman:** white ink on blueprint blue, a measuring grid, outlined buttons and typewriter type.
  - **Comic Pop:** sunshine yellow, halftone dots, thick black lines, hard shadows and a bang-red highlight.
  - **Riverlight:** lanterns on a night river (moving), smoked glass and lantern teal.
  - **Geyser Basin:** steaming hot springs (moving), mineral orange and sulfur gold.
  - **Chrome:** liquid silver waves (moving), glossy gunmetal buttons and steel blue.
  - **Velvet:** soft-focus velvet in wine and rose, with the game's art glowing behind its logo.
  - **Pit Lane:** carbon fibre, racing red, a yellow-flag Play button and condensed type.
  - **Pocket Green:** a 1989 pocket console, with four shades of green, an LCD dot screen and pixel buttons.
  - **Living Room:** couch gaming, with the game's art filling the screen in deep navy and bright blue.
- **Pixel-art store logos** for the pixel-art UI styles (the pixel styles and Pocket Green), on the Stores page and in
  the library's sidebar. Other styles keep the regular logos.
- **The NPU** in Settings > Power and performance, when the device has one (Intel AI Boost, AMD Ryzen AI, Qualcomm
  Hexagon…). It shows the name, maker and driver version, and its use where Windows reports it. Devices without one
  don't show the card.
- **Windows lock screen** in Settings > Appearance. Pick one of SquareDeck's wallpapers, your own SquareDeck
  wallpaper, or "Pick my own" for any picture. If Windows won't allow the change directly, SquareDeck can set it
  through the lock screen policy instead, which asks for administrator permission once.
- **A crash log.** If something goes wrong, the details go to crash.log in SquareDeck's data folder
  (%LOCALAPPDATA%\SquareDeck). Errors in a page or menu no longer close the whole launcher.

### Fixed
- **SquareDeck crashed when the in-game quick menu was summoned.** Possible causes, all fixed:
  - The menu's live CPU / GPU readout could be read from two threads at once, or closed while being read. Either
    crashes the app.
  - The menu window was maximized, so it couldn't open without taking the focus.
  - View (half of the summon combo) closed it straight away.
  If it still won't open, SquareDeck now brings the launcher forward instead and writes the reason to crash.log.

## 5.18.0

### Added
- **Apps, in Settings.** Installed apps lists every app on the device, like Windows' own list: desktop programs and
  Microsoft Store apps, with publisher, version, size and install date (and each program's icon). Search, sort by
  name, size or install date, and switch between all, desktop and Store apps. Choosing an app shows what can be done
  with it:
  - Uninstall: the program's own uninstaller, or removing the Store app.
  - Modify: the program's setup.
  - Repair: Windows Installer programs put back damaged files.
  - Reset: Store apps go back to how they were installed.
  - Open its folder.
  Shortcuts to Windows' startup apps, default apps and optional features are there too.
- **Performance Boost** (Settings > Power and performance). Just before a game starts it frees memory: other
  programs' unused memory is handed back to Windows, and Windows' file cache is emptied when SquareDeck runs as
  administrator. It shows how much came free. There are five animations, each with its own vibration pattern timed
  to it (on or off):
  - Pulse rings: a tap with each ring.
  - Warp speed: the taps speed up with the stars, then a thump.
  - Turbo gauge: ticks as it climbs.
  - Orbit: a heartbeat.
  - Shockwave: a rising buzz, then a jolt.
  "Surprise me" picks one each time, and "Try it" plays it without freeing anything. B skips it.
- **Six library layouts:** Sidebar, Compact (icons-only sidebar, small tiles), Large tiles (no sidebar; a Show button
  picks the section), List (one row per game with store, play time, last played and size), Shelves (rows that scroll
  sideways: recent, favorites, each store, collections) and Spotlight (the highlighted game large across the top).
  Choose with the Layout button in the library's toolbar, or in Settings > Games and library, next to the
  Square / Grid / Rectangle view.
- **Your own moving backgrounds.** "Add your own…" in Settings > Appearance > Moving background copies a video into
  SquareDeck's folder and uses it. Y removes one.
- **Storage and network** in Settings > Power and performance, next to the processor, memory and graphics:
  - The system drive's name, free space and how busy it is, with read and write speeds.
  - The network: Wi-Fi network name and signal, or Ethernet, the link speed, address, and download / upload right
    now.
- **Full screen in the capture viewer:** Start or the new toolbar button shows the picture alone across the whole
  screen. B or Start comes back.
- **The game's picture next to album names** in Captures (on by default; a switch in Settings > Games and library).
- **Vibration options:** a buzz when a notification pops up, and a light tick moving from row to row in Settings.
  Both are on by default and use your vibration strength.
- **A secret.** Press "This version" (or "SquareDeck version" on the About page) five times. From the second press, a
  small message counts down the presses left.

### Changed
- **The update download shows its progress:** a bar, the percentage, how much has arrived, the speed and the time
  left. B cancels.
- **An icon of SquareDeck's own for custom games** in the library's sidebar, replacing the one from the logo pack.

### Fixed
- **The capture viewer's toolbar no longer covers the picture.** The space for the header and toolbar is measured from
  them, so the picture stays clear at any screen size and scale. The hint bar there also showed its button names in
  braces; it now shows the button pictures.
- **The Streaming page's hint bar** shows the same button pictures as every other page (it showed plain text).
- **The buttons on the "update before playing" card** were a little too big, so the highlighted one was cut off.
  They're smaller and grow less.

## 5.17.0

### Added
- **Ten new UI styles** (55 in all), built around the new backgrounds, wallpapers and music:
  - **Aero:** Frutiger Aero, with glossy bubble buttons, milky glass and aqua highlights over a glowing aurora, and
    its own soundtrack. Glossy buttons are new: a shine across the top half of the round buttons.
  - **Rolling Hills:** green hills under a blue sky, with glossy pill tabs on bright glass.
  - **Daydream:** clouds over a grassy field, small tiles under the tabs, white glass and sky blue.
  - **Terra:** a ringed planet over a far-off beach, with a sidebar on bright glass.
  - **Jewels:** a sun rising over frozen dunes, the game large in the middle, smoked glass and pale gold. It has its
    own soundtrack.
  - **Contour:** dithered contour lines in pure black and white, with thin outlines and typewriter type.
  - **Sacred Grove, Treehouse, Stargazer and Campfire:** four pixel-art scenes (a forest waterfall, a hideout in the
    woods, a starry night by a car, a campfire at sunset), each in a different layout.
- **Eight moving backgrounds** (Aero, Heaven, Pasture, Plague, Ocarina, Pokee, Stars and Me, Witcher), **two picture
  wallpapers** (Jewels, Terra) and **two music tracks** (Aero, Jewels). The videos were converted to H.264, so they
  play on any Windows PC.
- **Library views.** The Game library page can show games as **Square** (the cover as it is), **Grid** (tall poster art
  from SteamGridDB) or **Rectangle** (the wide header from the store page: Steam's own for Steam games, SteamGridDB's
  for the rest). Pick it with the new "View" button in the toolbar.
- **Store logos** for Steam, Epic Games, Xbox, Battle.net, EA, Ubisoft and imported games, on the Stores page and next
  to each store in the library's sidebar.
- **This device**, in Settings > Power and performance. Three live cards:
  - Processor: name, cores and threads, speed now and at most, and use.
  - Memory: capacity, type (DDR5, LPDDR5…), speed in MT/s and how much is in use.
  - Graphics: the chip, its own memory, and use.
  Each has a graph of the last minute. They're read straight from Windows (performance counters, the memory modules'
  firmware tables and the graphics driver), with nothing extra to install.

### Changed
- **A new in-game quick menu.** It's a floating sheet with the game, the time and battery, live CPU / GPU / RAM use and
  TDP, and six quick tiles (Screenshot, Record, Keyboard, Sound on/off, Night light, Microphone). Below them are
  volume, brightness and TDP sliders, then Resume, SquareDeck and Exit game. The controller moves around it in two
  directions.
  - **It opens over any full-screen game,** not just the ones SquareDeck started. That includes games started from
    the Xbox app in Xbox mode, from Steam, or from a shortcut. Exit game asks such a game to close, and ends it if it
    hasn't closed after a few seconds.
  - **It no longer takes the focus from the game.** Windows often refused that under the Xbox full screen
    experience, so the menu never appeared, and some full-screen games minimized when it did. It now stays on top
    without being activated.
  - It works with "Summon buttons bring the launcher forward" turned off, as long as the quick menu is on.
- **A modern News page.** The sections sit on one rounded bar, with the open one filled in. The newest story is
  featured large across the top, with its picture, the game, the kind of post and how long ago. The rest are cards,
  three to a row, each with a picture on top: the post's own, or the game's wide store header.

### Fixed
- **The menu music carries on where it stopped** when a game closes, instead of starting the track again (shuffle
  too).
- **Tall grid art fits its tile.** In poster styles (pixel art, Crimson, Stage and others), the picture area wasn't
  2:3, so the sides of the art were cut off. Poster tiles are now sized so the art fits exactly, whatever the style's
  gap and ring.
- **The home screen's power button** works with the controller (fixed in 5.16.1 by making the power menu's buttons
  selectable).

## 5.16.1

### Fixed
- **The controller works in quick settings and the power menu again.** Since their redesign, the tiles and the round
  power buttons couldn't take the highlight, so the d-pad, stick and A did nothing there (and the Power button at the
  bottom of quick settings couldn't be reached). They take it again: quick settings opens on Wi-Fi, the power menu on
  Sleep.

## 5.16.0

### Changed
- **A new capture viewer.** Screenshots and clips open on a blurred, dimmed copy of themselves, so the picture's own
  colors fill the screen. The picture has rounded corners and a soft shadow, and slides in from the side you moved to.
  - A floating toolbar of round buttons: previous, play / pause, zoom, share, delete, close and next. The name of the
    highlighted button shows under it.
  - A filmstrip of the captures around this one, with the current one larger and ringed in your highlight color. Tap
    or click any of them to jump there.
  - The header shows the game, the date and time and the file size, with "3 / 12" in a pill on the right.
  - **Zoom:** LT / RT (or the mouse wheel, or the zoom button) zoom a screenshot up to 4×, and the right stick moves
    around it. B zooms back out before it closes the viewer.
  - Clips show a progress bar with the time played and the length.
  - The header, toolbar and filmstrip fade away after a few seconds and come back with any button, the mouse or a
    touch. They stay up while a clip is paused.

### Fixed
- **The d-pad and left stick move between the streaming services** on the Multimedia page's Streaming tab, and the
  right stick scrolls the tab.

## 5.15.1

### Changed
- **The power menu shows your name** instead of the Windows user name. It's the name Windows has
  for your account (your work or school account's display name, or your Microsoft or local account's full name),
  falling back to the user name if there isn't one. The profile page uses it too when Steam isn't signed in.

## 5.15.0

### Added
- **Disney+, Paramount+ and Tubi** on the Multimedia page's Streaming tab, with their logos. Like the others, each opens
  full screen with controller controls and stays on its own site.
- **The launch countdown vibrates.** A small controller pulse on each second, and a longer one as the game starts
  (when vibration is on in Settings).

### Changed
- **The Streaming button just opens the Streaming tab.** No more dropdown: the tab already shows every service, and
  Resume / Close for the one that's open.

## 5.14.0

### Added
- **Eight new UI styles, one for each wallpaper.** Each brings its own wallpaper, colors, sounds and menu music:
  - **Lookout**: smoky purple glass, lilac highlights and a floating shelf of buttons.
  - **Moonlit**: true black (OLED), small tiles under the tabs and one cool blue glow.
  - **Night Café**: warm neon yellow on deep blue glass with cozy lo-fi music.
  - **Crimson**: hero layout, with the game's logo and Play big at the top and a scarlet glow.
  - **Gold Dust**: stage layout, with the highlighted game large in the middle and amber light.
  - **Lavender Fields**: a light style with milky glass, indigo type and violet highlights.
  - **Rooftops**: sidebar layout on dusky blue glass with pink and violet light.
  - **Nostalgia**: an '80s bedroom, with chunky square buttons, orange neon and CRT scan lines.
- **Surprise me.** A new row at the top of Settings > Appearance > UI style switches to a random style you're not
  using now.

## 5.13.0

### Changed
- **The now-playing pill shows the song.** With Spotify playing inside SquareDeck, the pill at the bottom shows the
  song's cover, title and artist instead of "SquareDeck player". Windows' media controls (and the volume pop-up) show
  the song and its cover too, and their play / pause / next / previous buttons work.
- **The now-playing pill opens the player.** Clicking or tapping it (or A when the controller is on it) opens the
  Multimedia page, on the Spotify tab for SquareDeck's own player. X on it plays / pauses.
- **Notifications can be tapped.** Tap or click a notification while it's showing to open what it's about: the game's
  page for achievements, downloads and play sessions, the Friends page for friends, quick settings for the battery,
  the notification center for Windows notifications, or else the whole message.

### Fixed
- **Play, favorite and More can be reached with the controller** in the styles that show them under the game (Pixel
  Dawn, Stage and the others with words or icon buttons). Down from the games highlights Play; Left / Right move
  between the buttons, A presses, and Up or B goes back to the games.

## 5.12.0

### Added
- **Spotify plays inside SquareDeck.** SquareDeck is now a Spotify device of its own, called "SquareDeck", using Spotify's
  Web Playback SDK, so music comes out of the launcher itself with no Spotify app installed or open.
  - Choosing a song, album, playlist or artist on the Multimedia page's Spotify tab plays it here (moving playback from
    another device if one was playing). With nothing playing anywhere, play / pause starts it here too.
  - "Play on" lists SquareDeck (play here) next to your phone, speakers and the Spotify app.
  - It keeps playing on other pages and while a game runs. The launcher's background music makes way for it.
  - Settings > Sound > Multimedia page: Play Spotify inside SquareDeck (on by default) and Stop the built-in Spotify
    player.
  - Spotify only allows this for **Premium** accounts. Without Premium, SquareDeck says so, and songs still open in
    the Spotify app as before.
  - It needs one more Spotify permission ("streaming"), so you'll be asked to **sign in to Spotify once more**.
- **Spotify works without setting up an app.** SquareDeck now has its own Spotify app built in, so the Spotify tab
  goes straight to signing in; there's no Client ID to create or paste. A Client ID you entered before is still used
  (it takes priority); clear it in Settings > Sound > Multimedia page to switch to SquareDeck's.

## 5.11.0

### Added
- **Game details for every game,** not only Steam ones: games added by hand and games from Epic, GOG, Xbox, Battle.net,
  Ubisoft Connect and the EA app now open the details page too (X on the home row, Game details in the options, the
  library).
  - When the same game is sold on Steam, its Steam store page fills in the description, developer, publisher, release
    date, genres, Metacritic score, screenshots, trailer, reviews and news (the page says where it came from). Matches
    are by name and remembered, so each game is looked up once.
  - Otherwise the description comes from the game's Wikipedia article, with a button to read all of it.
  - The banner artwork, logo and release date come from SteamGridDB.
  - From this PC: where it's installed (with a Files button that opens the folder), the program it starts, its size on
    disk, play time and when you last played.

### Changed
- **Quick settings, redesigned.** It's now a panel that slides in from the right, with the time, date and battery at
  the top; tiles that light up in the highlight color when on (Wi-Fi, Bluetooth, airplane mode, night light, mute,
  settings); rounder sliders for volume, brightness and TDP; and a Power button. Y on the Wi-Fi or Bluetooth tile opens
  its full page. It follows the UI style's colors and corners.
- **Power menu, redesigned.** Big round buttons for lock, sleep, hibernate, restart, shut down and exit,
  under your name and battery level, with a line saying what the highlighted one does. Restart and shut down ask on
  the button itself (press once and it turns red, press again to do it); a running game is named as a warning.

## 5.10.0

### Added
- **Update first, or play anyway?** Starting a Steam game that has an update waiting now asks first, on a card in the
  game's colors that follows the UI style (square corners in the pixel, blueprint and newsprint styles, the style's
  font). **Update** opens Steam's Downloads page, where the waiting update starts with one press, and the card follows
  the download with a progress bar; when it's done, **Play** is ready. **Play anyway** starts the version you have.
  A download already running shows its progress straight away. Turn it off in Settings > Games > Ask about updates
  before playing.
- **Streaming on the Multimedia page.** A **Streaming** button next to Spotify drops down Netflix, YouTube, Hulu, Prime
  Video and Crunchyroll, with their logos; there's also a Streaming tab with big tiles.
  - Each opens full screen inside SquareDeck and **stays on its own site**: links to anywhere else are blocked (a note
    says so) instead of opening. Sign-in pages a service needs (Google for YouTube, Amazon for Prime Video) are allowed.
    Sign-ins are kept between visits, in their own folder apart from Edge and the built-in browser.
  - Controller: d-pad moves between titles and buttons, A chooses, X plays / pauses, LB / RB skip 10 seconds,
    LT / RT scroll, Start goes full screen, the right stick moves a pointer (press it to click), Y opens a menu (home,
    reload, full screen, another service, close).
  - B goes back a page, out of full screen, then back to the launcher with the service **kept open and paused**. The
    Streaming tab and the dropdown show which service is open, with Resume and Close; opening another one asks before
    closing it. A service is closed when a game starts, to give the game its memory.
  - YouTube can use its **TV layout**, made for a controller (Y > Use YouTube's TV layout).
- **Open Terminal** (Settings > System > Tools): Command Prompt or PowerShell inside the launcher. Type with a keyboard
  or press A for the touch keyboard; Y has handy commands (network addresses, ping, disk space, running programs,
  battery report). Up / Down go through earlier commands, Start (or Ctrl+C) stops a command, View clears. Programs that
  draw their own full-screen display don't work in it, and nothing runs as administrator.
- **Storage speed test** (Settings > System > Maintenance): sequential and random 4 KB read and write speeds for the
  internal drive, a memory card or a USB drive, with live bars and a plain-words verdict. It uses a temporary file
  (256 MB, 512 MB or 1 GB) that's deleted afterwards, and reads past Windows' cache so the numbers are the drive's.
- **Background music makes way for your music.** With Settings > Sound > Pause it while your music plays on (the
  default), the launcher's background music pauses whenever something else is playing (the Multimedia page, Spotify,
  any media player) and comes back when it stops.

### Changed
- **The Music page is now Multimedia.**
- **SquareDeck rests while another app is in front:** the background music, moving background and animated cover pause,
  and the highlight stops pulsing, then carry on when you come back. Before, the music kept playing behind other apps.
- The pop-ups about a game (countdown, "Stop game?", "Closing software") follow the UI style's corners too.

## 5.9.0

### Added
- **10 more color themes:** Nord, Dracula, Solarized, Catppuccin, Evergreen, Ember, Abyss, Blossom, Synthwave and
  Royal, each in light and dark. The color theme page shows a small preview of each one.
- **My themes.** Save the look you've set up (UI style, light / dark, colors, highlight, wallpaper, moving background
  and blur, tile corners, sounds, menu music, button art and hero backgrounds) under a name, and switch between saved
  themes later. Each one can be used, updated with the current look, renamed or deleted. Settings > Appearance > My
  themes, looks and wallpaper.
- **8 new picture wallpapers:** Alone, Blue Moon, Cafe, Crimson, Gold Dust, Lavender, Next Door and Nostalgia. The
  wallpaper page shows bigger previews.
- **Headphone battery.** With Bluetooth headphones connected, their battery level shows beside the Bluetooth icon
  (orange at 15% or less). It's checked about once a minute and never while a game runs. Turn it off in Settings >
  Appearance > Top bar > Headphone battery. Only headphones that report their battery to Windows show a level.
- **IGN news.** The News page has IGN PC and IGN Games sections, from IGN's PC articles and all-games feeds. They're
  only downloaded and kept in memory while the News page is open; X refreshes the one you're on. The tabs wrap onto a
  second line if they don't fit, and "Patches and updates" is now just "Patches".
- **Disk cleanup** (Settings > System > Maintenance): measures and clears temporary files, the Recycle Bin, crash
  reports, shader caches, thumbnails, the browser's cache, cached cover art and Steam's download caches. Choose which
  ones; each shows its size first.
- **Network troubleshooting** (Settings > Network > Troubleshooting): a diagnosis (gateway, internet and name lookup
  checks), connection details, speed test, flush DNS, release and renew the IP address, re-register DNS, Winsock reset,
  TCP/IP reset and Windows' network reset. The ones that need administrator rights ask for them.
- **Ryzen Z1 / Z2 power settings** (Settings > Power and performance):
  - **Boost window:** short bursts above the limit (slow limit 1.1×, fast 1.25×, within what the chip allows).
  - **Power profile:** RyzenAdj's power-saving or maximum-performance mode.
  - **Energy preference (EPP)** and **CPU cores in use**, through Windows' power settings.
  - The power profile needs the updated TDP helper: an "Update the TDP helper" row appears when it's needed, and
    keeps your current limit.
- **48 more profile pictures** (56 in all), in groups: Classics, Creatures, Characters, Games, Hobbies and Nature.

### Changed
- **Friends, redesigned.** Friends in a game get big cards over that game's art, with an "Installed" badge when you
  have it here. "Popular with your friends" shows games several friends are in. Online friends are round pictures with
  a ring in their status color (in game, online, away, busy); offline friends show when they were last on.
  - Pin friends to the top with Y; filter with X (everyone, in a game, online, pinned).
  - A friend's options: play or install their game, join their game, message, pin, profile.
  - Friend pop-ups can be set to everyone, pinned friends only, or off. Buttons for Add friend and Steam chat.
- **Apps page, redesigned and sharper.** Microsoft Store apps now use their own logo files at the largest size they
  ship (usually 256 px, without the colored plate), instead of a small icon scaled up. Program icons that only exist
  small (32–64 px) are drawn smaller instead of blurry, on the home row too. The page shows the apps you used last
  first, then all of them A–Z under letter headings; LB / RB jump between letters and X finds an app by name.
- **Settings reorganized.** Sections are grouped more sensibly and nothing appears twice:
  - Appearance: Style, Colors, Your looks, Artwork, Top bar, Display.
  - Home screen: home row and buttons first, then animations, screen saver, startup and closing.
  - Sound: Interface sounds (with the game start chime), Background music, Music page, Sound output.
  - Controller: Buttons, Vibration and testing, Typing, Summon and quick menu, then the ROG Ally buttons.
  - Games: Launching, While playing, Library and stores, Achievements, play time and captures, Service keys.
  - Network: Connections, Troubleshooting, Web browser.
  - System: This PC, Notifications (moved from Updates and backup), Maintenance, Tools, Starting with Windows (now
    including Xbox mode home app).
- **Windows' touch keyboard is now the default for typing,** and the choice sticks: before, the launcher went back to
  its own keyboard after a restart. Existing setups switch to the Windows keyboard once; pick the SquareDeck keyboard
  again in Settings > Controller > Typing if you prefer it.

### Fixed
- **Pixel Dawn:** the game's details no longer run into the buttons at the bottom of the screen. They're placed above
  them and shrink to fit when there isn't room.

## 5.8.0

### Added
- **Captures: move through them with LB / RB.** Opening a screenshot or clip from the Captures page now shows a
  viewer that knows everything in the view you opened it from (All, Screenshots, Videos or one game's album), newest
  first as on the page. LB / RB or the on-screen buttons go to the previous and next one, wrapping around, with a
  "3 / 41" counter. The game's name and the date are shown at the top, and clips play as you reach them.
- **Share, built in.** The viewer has a Share button (also on the Options button). It opens SquareDeck's own share
  sheet, with the file at the top and tiles underneath:
  - **Copy:** the picture itself (paste into any chat or document), the file, or its path.
  - **Share using:** open in Photos or your video player, edit in Paint, show in its folder, or save a copy to
    OneDrive (Pictures > SquareDeck, which then uploads by itself), the desktop, Pictures / Videos, or a plugged-in
    USB drive.
  - A line at the bottom confirms what happened.
- **Delete from the viewer** (button, or the Details button) moves on to the next capture instead of closing, and the
  page behind is refreshed when you go back.

## 5.7.0

### Changed
- **The launch splash stays up 3 seconds after the game's window appears,** as a buffer over the black frames and
  logos of a game's first moments. The game gets the focus as soon as its window is up; the splash just stays in
  front of it for those seconds. B still hides it at any time.
- **New look for the countdown before a game starts.** A dark glass card lit in the game's own main color (taken
  from its cover): the cover or logo, the game's name, the seconds left inside a ring that empties smoothly, and
  "Start now" / "Cancel" as pill buttons. The highlighted button gets a white ring and grows slightly.
- **"Stop game?" and "Closing software" match it.** The confirmation shows the game's cover, the question and the
  two buttons in the game's color. While the game closes, its cover sits inside a turning ring. The "… is still
  running" question when you start a second game uses the same card.
- **First-time setup, rebuilt.**
  - **Steps down the left** (Welcome, Look, Background, Controller, Games, Ready) show where you are, with done steps
    ticked and a progress bar along the top. With touch or a mouse, tap a step to jump to it.
  - **Two ways in:** "Use recommended settings" goes straight to the end, "Customize" walks through the choices.
  - **Clear choices:** picture cards carry a tick and a highlighted edge when chosen; options are pills with the
    chosen one filled. The highlight stays on what you just picked instead of jumping back to the top.
  - **Look** offers six varied styles (including the newer layouts and a pixel-art one) with previews, and only asks
    light or dark for styles that support both.
  - **Controller** adds "How buttons are drawn" and a live sample of the hint bar that changes as you choose.
  - **Ready** lists what you chose (each row jumps back to its step) and four tips. "Start playing" has the
    highlight, so one press finishes.
  - RB / LB move between steps, as shown in the hints.

## 5.6.0

### Added
- **Start Steam games directly.** Settings > Games and library > Launching games > "Start Steam games directly" (off by default) starts a Steam
  game from its own .exe instead of the `steam://` link, so Steam's "Launching…" pop-up doesn't come up over the
  splash screen.
  - **Which program:** the one you chose for the game, else the one SquareDeck saw running the last time you played
    it (it notes this for every Steam game, with the setting on or off), else a best guess from the game's folder.
  - **Choosing it yourself:** game options > "Program to start directly" (shown while the setting is on) shows the
    current program and lets you pick another or go back to automatic.
  - **Steam's app id** is passed to the game the way Steam does it, so Steamworks games don't restart themselves
    through Steam.
  - **Fallback:** if Steam isn't running, or no program is found, the game starts through Steam as before.
  - **Running state:** a game started this way is tracked by its own processes, since Steam may never mark it as
    running.
  - **Limits:** launch options set in Steam aren't used, the Steam overlay may not appear, and some games (own
    launcher, copy protection) still restart through Steam.

## 5.5.2

### Fixed
- **When a game closes, SquareDeck comes to the front with the focus, so the controller works straight away.** It
  used to ask Windows politely, which Windows often refused (the closing game, its store or the desktop kept the
  focus), and the controller only drives SquareDeck while its window is active. Now:
  - It uses the same firm method as the launch splash, trying again a few times over about four seconds while Windows
    settles.
  - Once it's active, any button left "held" from the game is cleared and the highlight goes back to where it was on
    the current page.
  - While a game runs, SquareDeck checks for it closing every second instead of every two, so it's back sooner.
  - Still controlled by Settings > "Return to the launcher when a game closes" (on by default).

## 5.5.1

### Fixed
- **"Access is denied" when making SquareDeck the Xbox mode home app.** Both steps now try again with administrator
  rights when Windows refuses (Windows asks first):
  - **Registering:** if Windows' app installer is refused access, the registration runs again as administrator. It's
    still registered for your own account.
  - **Picking it as the home app:** some devices protect that setting. It's tried again as administrator. If Windows
    still won't allow it, SquareDeck explains the one step left (pick SquareDeck under "Choose home app" in Settings >
    Gaming > Xbox mode, where it's now listed) and opens that page.
  - Switching it off gives the previous home app back the same way.
  - At startup, when SquareDeck re-registers after being moved, it never asks for administrator rights. It only does
    what it can quietly, and the settings page does the rest.
  - Error messages say whether the prompt was declined or access was refused even as administrator (for example, a
    read-only, removable or network folder).

## 5.5.0

### Added
- **Use SquareDeck as the Xbox mode home app without AnyFSE.** Settings > System and tools > "Xbox mode home app"
  now does it directly:
  - **One switch:** registers this copy of SquareDeck with Windows as a home app and picks it, so Xbox mode (the full
    screen experience) opens SquareDeck and the Home button in Game Bar brings it back.
  - **Startup:** "Open Xbox mode when Windows starts" boots straight into SquareDeck.
  - **Shortcuts:** the page links to Developer Mode and to Windows' own Xbox mode settings.
  - **Switching off:** removes the registration and gives Windows back the home app it had (the Xbox app if there was
    none). If AnyFSE was set to start SquareDeck, that's switched off so it doesn't start it twice.
  - **How it works:** SquareDeck registers its own folder as a small package (`HomeApp\AppxManifest.xml` and
    `CustomCapability.SCCD`, copied next to `SquareDeck.exe`). The package declares the `windows.gamingApp` extension
    and the `gamingHome` capability Windows lists home apps by. Windows then starts the same `SquareDeck.exe`.
    Writes to AppData and the registry aren't redirected, so there's one set of settings however SquareDeck starts.
  - **Needs:** Windows 11 24H2 or later, an NTFS drive, and Developer Mode while registering, because the capability
    belongs to Microsoft. AnyFSE and other non-Microsoft home apps need the same. If SquareDeck's folder moves, it
    registers again from the new place when it starts.
  - **AnyFSE** stays available from the same page ("Use AnyFSE instead").

### Changed
- **The launch splash stays in front until the game's window appears.** It used to close as soon as the game counted
  as running, which is often before its window shows, so a store's window could come up first. Now:
  - It's put back on top of every window about twice a second.
  - Until the game is running, it takes the focus back from a store that grabs it.
  - It closes when the game shows a real window of its own (game-sized; small helper windows don't count). Games
    with no folder to watch, such as Xbox and Microsoft Store games, close it shortly after they start.
  - It steps aside at once when the game's window appears, and the game gets the focus rather than its store.
  - It still gives up after 90 seconds, and B hides it at any time, for example if a store asks you to sign in.

## 5.4.3

### Fixed
- **Home footer: with music playing and a game running, the pills ran into the button hints.** The pills on the
  left and the hints on the right were placed independently, so nothing stopped them overlapping once "Back to game"
  was added. The pills now give way to keep a gap before the hints:
  - The song title shortens first, then the running game's name (which scrolls when it doesn't fit), then any
    status message.
  - If it's still too tight, the song title is hidden, leaving its cover and play / pause.

  Everything goes back to full size when there's room again.

## 5.4.2

### Fixed
- **Pixel City: the buttons under the games were cut off at the bottom.** Its button row was 70 pixels tall, but
  square buttons are 88. The row is now 108 tall, taking the room from the space above it.
- **The button row now also shrinks to fit the height of its row,** not only its width, so a style can't cut its
  buttons off again. No existing style changes: all of them already fit.

## 5.4.1

### Fixed
- **The bottom of the selected game's ring was cut off in Switch and similar styles.** The list that holds the games
  has a built-in 1-pixel padding at its top and bottom, which the sizing didn't count. Each tile was laid out
  2 pixels shorter than it needs, so WPF clipped the bottom of the tile, ring included (about half the ring's
  thickness). That padding is now counted, with a little extra for rounding on scaled screens. Tiles in those styles
  are a hair smaller.

## 5.4.0

### Added
- **Four pixel-art UI styles.** Each comes with a pixel-art moving background, pixel-art buttons in the hints,
  square edges, typewriter type and its own pixel-art music:
  - **Pixel City**: a lo-fi city evening, the classic layout with hard shadows. Lofi City and "Coffee Shop".
  - **Pixel Rain**: a rainy cyberpunk street, with the game's logo big, tabs on top, posters below and scan lines.
    Cyberpunk Rain and "Gaming Vibes".
  - **Pixel Forest**: glowing mushrooms at night, with small tiles under the tabs. Mushroom Forest and "Meditation".
  - **Pixel Dawn**: sunrise over a skyline, with the highlighted game large in the middle. City Dawn and "Relaxing".

  With "Match theme, wallpaper, sounds and music to the style" on, choosing one also switches "How buttons are
  drawn" to Pixel art. Picking a different style afterwards puts it back to Simple, unless you changed it yourself.
- **Four new background music tracks:** Coffee Shop, Gaming Vibes, Meditation and Relaxing. They're listed with the
  other tracks wherever you choose menu music.
- **Style previews show the style's moving background,** so the pixel styles (and others with a moving background,
  like Frosted Dark and Tokyo Night) look like themselves on the UI style page.

### Changed
- **The game's logo instead of its name on the home screen** in Newsprint, Sphere, Desktop, Stage, Tokyo Night and
  Slate, as Cinema, Mono, Waves, Xenon and Grids already did. The name shows while the logo loads, or if
  SteamGridDB has none. Logos are sized to each layout: smaller in Stage, so the buttons stay in view.

## 5.3.2

### Fixed
- **The highlight around the selected game was cut off at the top or bottom in some UI styles.** The row kept a
  fixed 18 pixels around the selected tile for its ring. The ring's glow reaches about 20–24 pixels, and grows with
  the tile, so in styles with a glowing ring it ran past the edge of the row. The space is now worked out from each
  style's glow, ring, tile spacing and growth. It's about 26–38 pixels in Steam, Frosted, Sunset Drive, Ocean,
  Terminal, Carbon, Neon, Leonida, Waves, Xenon, Stage, Tokyo, Retro and the other glowing styles. Tiles in those
  styles may be a touch smaller where the row is tight. Styles without a glow are unchanged.

## 5.3.1

### Changed
- **Game options: the game's logo is centered** at the top, with the store name ("Steam") centered under it. The
  name, shown until the logo loads or when there's no logo, is centered too.

## 5.3.0

### Added
- **Choose the pages LB / RB switch between.** Settings > Controller and quick menu > "Pages LB / RB switch
  between" lists Home, Favorites, Apps, Game library, Captures, Music, Friends and Settings in their current order.
  - **Select** turns a page on or off. Pages that are off are skipped, but still open from their buttons.
  - **Details** moves the highlighted page up and **Options** moves it down, which changes the order LB and RB go
    through.
  - "Back to the original order, all on" undoes everything. At least two pages always stay on.
  - Swiping sideways on a touch screen follows the same order.

## 5.2.2

### Changed
- **Game library: six games in every row when the sidebar is shrunk to icons.** The tiles were a few pixels too
  wide for six, so rows stopped at five with a gap on the right. With the full sidebar it's still five per row.

## 5.2.1

### Fixed
- **Screenshots of games were named "SquareDeck" instead of the game.** Windows names each Game Bar capture after
  the app in front. Pressing the Xbox button (Guide) opened SquareDeck's quick menu straight away, so with Game
  Bar's controller shortcuts (Xbox + Y for a screenshot, Xbox + X to record) SquareDeck was in front at the moment
  of the capture.
  - The Guide button now opens the quick menu only when pressed and released on its own. Held with another button
    or a trigger, it's left to Game Bar.
  - The ROG Ally's screenshot and record keys close the quick menu first, if it's open.
  - As a safety net, a new capture named "SquareDeck …" while a game is running is renamed after the game, so it
    shows in that game's album and on its page. Recordings are renamed once they finish. Captures already named
    "SquareDeck" keep their names, since there's no telling which game they were from.
- **Music > My music and Spotify: the player controls were cut off at the bottom.** The album cover is smaller,
  with the title and artist beside it, so the seek bar, controls and volume all fit. With larger text, the player
  column scrolls.

## 5.2.0

### Added
- **Button pictures in the hints.** Settings > Controller and quick menu > "How buttons are drawn" has three choices:
  - **Simple**: the button's name on a pill, as before.
  - **Controller icons**: drawn button pictures.
  - **Pixel art**: pixel-art buttons, kept crisp when enlarged.

  Both picture sets have Xbox, PlayStation and Switch buttons, following "Button names shown on screen" and your
  button layout. They switch to keyboard keys when you use a keyboard. The styles that show a key next to Play use
  the picture too.
- **Seven pixel-art moving backgrounds:** Lofi City, City Dawn, Cyberpunk Rain, Night River, Mushroom Forest,
  Yellowstone and Chill Mario. They're in their own "Pixel art" group in Settings > Appearance > Moving background.

### Changed
- **The UI style page shows three styles per row,** so every row lines up the same way.

## 5.1.0

### Added
- **Pick a game's hero, logo and tall grid from SteamGridDB.** "Change cover image" in a game's options is now
  "Change artwork". Next to the square covers it has **Tall grids**, **Hero images** and **Logos**. Each shows every
  image SteamGridDB has of that kind, plus "Choose a file…" and "Use the default".
  - Your pick shows straight away and is used everywhere that kind of art appears: tall grids in the poster styles,
    heroes as the home background and on game pages, logos in the styles that show them.
  - Picks are kept with your own covers, so clearing the download cache doesn't remove them and backups include them.

### Fixed
- **Xenon, Waves and Grids: the game count ("83 games · 83 installed") stayed on screen over every other
  page.** It now sits behind pages, like the rest of the home screen.

## 5.0.2

### Changed
- **SquareDeck gives back much more memory while a game runs.** This is always on now, not only with "Free graphics
  memory for the game".
  - **The moving background is unloaded completely,** not just paused. A paused video still keeps its decoder and
    frames in memory. It starts again when the game closes, and can't be started by anything else while the game
    runs.
  - **The menu music player is closed.**
  - **A browser kept open in the background is closed.** Its web pages can use hundreds of megabytes. Its tabs
    aren't kept.
  - **Game artwork and the animated cover are let go,** the memory is compacted (large pictures included) and handed
    back to Windows.
  - Everything comes back when the game closes.

## 5.0.1

### Added
- **OLED UI style:**
  - True black background, so unlit pixels stay off.
  - Thin grey outlines on see-through buttons, no glow and no artwork behind.
  - White type with a cyan highlight.
  - The OLED theme comes with it.
- **Choosing a UI style also switches the theme (Light, Dark or OLED) to suit it.** For example, Leonida, Slate and
  Newsprint use Light, most others use Dark, and OLED uses OLED. Switch suits both, so it keeps yours. High contrast
  is never changed. The style page's switch is now "Match theme, wallpaper, sounds and music to the style".

### Fixed
- **The highlighted game's ring was cut off at the bottom** (and in Sphere and Tokyo Night, most of the grown tile).
  The games row clips anything past its edges.
  - Room is now kept above and below the tiles for the grown tile, its ring and glow.
  - Where the row is too short for that, the tiles are made slightly smaller to fit.
- **The game's details line ("Released 2009 · Last played…") was cut off** in styles that show the name beside the
  row (Steam Big Picture, Sunset Drive, Deep Ocean, Velvet, Aurora). That line always has room now.
- **The round buttons ran off both sides of the screen** in styles with a frosted button shelf. There are more
  buttons now than they were sized for, so the row now shrinks to fit the screen, and the shelf follows it.
- **The dot under the current view (Home / Favorites / Apps) sat on the shelf's edge;** the shelf now extends below
  it.
- **Tab buttons cut off long names** ("Web browse…") and the icon's left edge. They're wider now.

## 5.0.0

### Added
- **Music page with three tabs:** Now playing, My music and Spotify. Switch with LB / RB; the last tab is remembered.
  - **My music** plays songs from a folder you choose, including the folders inside it (MP3, M4A, AAC, WMA, WAV,
    FLAC).
    - Album art, title, artist and album come from the songs' tags.
    - Controls: play / pause, previous / next, a seek bar, shuffle, repeat (all / one), volume and an "up next" line.
    - Browse by Songs, Albums or Artists, with Play all and Shuffle all.
    - It plays through Windows' own media player, so it shows in Windows' media controls, answers media keys, and
      appears in the home screen's now-playing banner.
    - It keeps playing while you use the launcher and pauses when a game starts.
    - Controller: X play / pause, LT / RT previous / next, Y choose folder.
  - **Spotify**, built in:
    - What's playing, with cover art, progress, play / pause, previous / next, shuffle, repeat, ±15 seconds and volume.
    - "Playing on": choose the device, or open Spotify on this PC; playback moves to it automatically.
    - Browse Playlists, Liked songs, Recently played and Search. A playlist or album can be played or opened to pick
      a song.
    - A guided setup (create the free Spotify app, paste the Client ID, sign in). Signing in again is asked for once,
      to allow Liked songs and Recently played.
    - Controlling playback needs Spotify Premium (a Spotify rule). Without it, songs open in the Spotify app.
- **Ubisoft Connect and EA app libraries:** installed games from both are found and start through their launchers
  (`uplay://` and `origin2://` links). Both can be switched off with the other stores in Settings.
- **Every UI style comes with a matching wallpaper or moving background, interface sounds and menu music**, and
  uses its own highlight color. A new switch at the top of UI style, "Match wallpaper, sounds and music to the
  style", turns this off to keep your own.
- **Five new UI styles:**
  - **Aurora:** northern lights behind frosted glass.
  - **Mono:** a black-and-white magazine cover with the game's logo huge.
  - **Velvet:** wine, plum and gold, with a moving gold background.
  - **Tokyo Night:** neon city in the Sphere layout.
  - **Slate:** a clean light sidebar launcher.
- **Nine more page transitions:** Slide in from the side, Drop down, Rise, Pop, Spring, Bounce, Card flip, Swing
  and Come into focus.
- **Game options sheet:**
  - The game's logo (or its name) over its artwork, then the options in groups (Play, Your library, Customize, Steam,
    Manage), each with an icon.
  - Remove and uninstall options are in red.
  - New options: Return to game and Screenshots.
- **Your screenshots on the game details page:** a strip of the game's screenshots (Game Bar captures named after
  the game, and Steam's F12 screenshots); select one to see it full screen and move through them with LB / RB.
- **More Windows settings** (Settings > System > Windows):
  - Switches for dark mode, Game Mode, background recording, the Xbox button opening Game Bar, transparency effects,
    and the Sticky Keys shortcut.
  - A list that opens Windows Update, night light, graphics (HDR / VRR), battery saver, notifications, airplane
    mode, storage, startup apps, typing and mouse settings.
- **Low battery notification** (Settings > System > Battery): at 30 / 25 / 20 / 15 / 10%, and again at 5%. It shows
  over games too, without taking the controller away from the game.
- **Browser:**
  - Starter favorites: YouTube, Twitch, Reddit, Outlook, Loaded, Amazon, Steam Store, Discord, PCGamingWiki,
    HowLongToBeat, IsThereAnyDeal, Nexus Mods and IGN.
  - A limit on open tabs (Settings, default 6). At the limit, a link opens in the current tab instead.
- **Steam Web API key setup:**
  - "Get a key from Steam" opens Steam's key page in the browser.
  - A key you've copied is offered automatically when you come back.

### Changed
- **Existing UI styles polished:** more modern type, softer corners, glows and floating button shelves where they
  suit, and tidier layouts. The changes apply to Steam Big Picture, Sunset Drive, Blueprint, Deep Ocean, Sakura,
  Terminal, Carbon, Pastel, Neon, Minimal and Retro.
- **Settings:** changing a setting, or coming back from a page opened from one, keeps the highlight on that row
  instead of jumping to the top. Coming back to the list of sections keeps the highlight there.
- **Settings:** moving through the sections plays one sound, not a second one when the page changes.
- **Browser controls work on more sites:**
  - The d-pad now reaches links inside modern page parts (shadow DOM, used by YouTube and others) and same-site
    frames.
  - Scrolling (LT / RT and the right stick) moves the part of the page that actually scrolls (mail, chat and feeds).
  - A highlighted video plays or pauses with A, and text boxes that aren't plain inputs can be typed into.
  - The page gets the keyboard focus when you use the d-pad.

### Fixed
- **The launcher didn't fill the screen properly in full screen.** The window kept Windows' invisible 8-pixel resize
  border, which pushed the edges of the interface off screen. Full screen no longer has a resize border.
- **In Windows' Xbox full screen experience, the launch splash screen and the in-game quick menu didn't show.**
  Both are now forced on top of the game and the Xbox shell.
- **The "Closing software" pop-up (with the game's picture) was missing** when a game was stopped, especially from
  the in-game menu. It shows in front of the game again until the game has closed.
- **Browser button icons were squashed:** the round buttons ignored their padding, leaving no room for the icon.

## 4.1.1

### Changed
- **RyzenAdj now comes with SquareDeck** (`Tools\RyzenAdj`, copied next to the app when building).
  - "Set up TDP control" installs this copy, so no download is needed. It only falls back to downloading from
    GitHub if the folder is missing.
  - Included files: ryzenadj.exe, libryzenadj.dll, WinRing0x64.sys/.dll and inpoutx64.dll.
  - The folder's README.txt has the license note (LGPL v3) and a link to the source.

## 4.1.0

### Added
- **Six new UI styles** that rearrange the whole home screen (Settings > Appearance > UI style):
  - **Waves:** console-dashboard layout.
    - Tabs along the top bar.
    - The game's logo, "Continue where you left off", the last-played line and a blue Play button (showing the A /
      Enter key) large over the game's artwork.
    - "Your library" with the game count, and a row of posters along the bottom.
    - Soft flowing lines over the background.
  - **Xenon:** the same layout in electric cyan, with a glow and a cyan-to-blue Play button.
  - **Grids:** the same layout with square-cut posters, a fine grid over the artwork and lime highlights.
  - **Sphere:** media-console layout.
    - Small tiles right under the tabs; the highlighted one grows and its name sits under the row.
    - The artwork fills the screen, with the game's name, details, a wide Play button and "…" large at the bottom left.
  - **Desktop:** PC-launcher layout.
    - A sidebar with the SquareDeck name and every section as a list (icon and name).
    - The game's name, details, a glowing purple Play button, "…" and a favorite star.
    - A "Recently played" row of posters.
  - **Stage:** spotlight layout.
    - The highlighted game large in the middle over its blurred artwork, its neighbours either side.
    - The game's name and where it's from centered underneath.
    - Play / Add to favorites / More buttons, page dots and a details pill.
    - Tabs on top, with the current one as a filled pill.
- **Poster tiles:** Waves, Xenon, Grids, Desktop and Stage use tall 2:3 posters. Poster art is fetched from
  SteamGridDB for the games around the selection as you scroll, and the square cover shows until it arrives.
- **Controller navigation follows the layout:**
  - With tabs on top, Up from the games reaches the tabs; Up again reaches the status icons, and Down goes back.
  - In Desktop, Left from the first game reaches the sidebar; Up/Down move through it, Right goes back.
- **Per-game TDP on battery and when plugged in:** Game profile now has "TDP on battery" and "TDP when plugged
  in". It switches as the charger is connected or removed while the game runs.

## 4.0.0

### Added
- **TDP control for AMD handhelds** (new section: Settings > Power and performance). Made for the Ryzen Z2 Extreme;
  the Z1 Extreme, Z1, Z2, Z2 Go and Z2 A are recognised with their own ranges.
  - **TDP limit** from 5 to 35 W on the Z2 Extreme, with quick presets: Silent 8 W, Battery saver 12 W,
    Balanced 18 W, Performance 25 W and Turbo (the maximum).
  - **A different limit when plugged in.** It switches as the charger is connected or removed.
  - **Temperature limit:** device default, or 80–95 °C.
  - **CPU boost** on/off.
  - **Maximum CPU speed** (no limit, or 90% down to 50%).
  - **Windows power mode** (Best power efficiency / Balanced / Best performance).
  - **Keep re-applying** every 15 seconds, for devices whose own software (Armoury Crate, Legion Space) puts its
    limit back. The limit is also re-applied after the device wakes from sleep.
  - **Per-game power profiles:** a game's options (Y) > Game profile now has TDP limit, CPU boost, maximum CPU
    speed and Windows power mode. They're applied when the game starts and put back when it closes.
  - **TDP slider while playing:** in the Guide menu and Quick settings. It lasts until the game closes.
  - **How it's set up:** "Set up TDP control" downloads RyzenAdj from GitHub and installs it into Program Files,
    with one Windows permission prompt. It also adds a background task, "SquareDeck\ApplyTdp", that applies limits
    with administrator rights and no window, so there's no prompt each time. The task only accepts numbers in a
    safe range and only runs the RyzenAdj in Program Files. "Remove TDP control" takes both away.
- **Running game pill shows the game:** the bottom bar's "… is running" pill shows the game's icon or cover
  instead of a green dot, and a long name scrolls instead of being cut off.

### Changed
- Performance mode moved to Settings > Power and performance > Launcher performance.

### Fixed
- **The Windows touch keyboard squashed the launcher.** When the keyboard docked, Windows shrank the space for
  full-screen windows, and the whole interface resized. The launcher now keeps the full screen and the keyboard
  floats over it.

## 3.5.0

### Added
- **Leonida UI style** (Settings > Appearance > UI style): a sunset over a pastel beach skyline with palm trees.
  - Milky light glass panels, deep violet text and hot-pink highlights, with a floating shelf of buttons and a pill of hints.
  - The picture stays sharp on the home screen, under a light wash so text stays readable; pages show it blurred behind glass.
  - Choosing the style switches the wallpaper to the Leonida picture. You can pick another wallpaper afterwards.
- **Leonida wallpaper**: Themes > Wallpaper has built-in pictures now, starting with Leonida. They're kept in a new
  `Wallpapers` folder that's copied next to the app.
- **GTA VI color theme**, taken from the same picture: violet backgrounds, a pink-and-peach sunset for text and tiles,
  and a hot-pink highlight. A light version uses peach backgrounds with violet text.
  - **"GTA VI" ready-made look** in Themes: dark GTA VI colors, the Leonida wallpaper, round tiles and soft sounds.
  - **Themes > Color theme** switches between Standard and GTA VI on their own, with any wallpaper.
  - UI styles that have their own colors keep them; the color theme shows with Switch and the other styles that use
    the standard colors.

## 3.4.1

### Changed
- **Removed the "‹ Back" button from the top-right corner of pages.** Going back still works with B, Esc, the
  mouse's own Back button on its side (new), or by tapping or clicking "Back" in the hints at the bottom of the
  page (new).

## 3.4.0

### Added
- **Browser favorites:**
  - A star at the end of the address bar adds the page to your favorites, or removes it. It's filled in when the
    page is already a favorite.
  - Favorites show as chips under the address bar. Click one to open it, or right-click to rename it, move it,
    open it in a new tab, or remove it.
  - The browser's menu (Y) now opens with Favorites, Add to / Remove from favorites, and More… for everything else.
- **Performance mode** (Settings > Games and library > Performance): Off, On battery only, or Always on. It turns off:
  - drop shadows and glows;
  - live blurs (blurred artwork is loaded tiny and stretched, which softens it with almost no work);
  - blurred artwork behind pages;
  - the blur on a moving background;
  - the always-pulsing highlight;
  - animated covers.

  It also draws animations at 30 frames a second instead of 60; that part takes effect from the next start. "On
  battery only" switches itself on and off as the charger is unplugged and plugged in.

### Changed
- **SquareDeck steps aside while you play.** While a game is running and SquareDeck isn't the window in front (or
  whenever it's minimized), it stops drawing completely, checks the controller every 50 ms instead of every 16 ms
  (still quick for the Guide button), drops to below-normal CPU priority and hands memory back to Windows. It comes
  back the moment you switch to it. This is on all the time, not just in Performance mode.
- **Less graphics work in normal use:** the blurred artwork behind the home screen, the blurred wallpaper (Frosted
  styles) and the blurred artwork behind pages are now kept as ready-made pictures. Before, the pulsing highlight next
  to them made the graphics chip redo the full-screen blur every frame. A blurred moving background is now blurred at
  half size and stretched, about a quarter of the work.

### Fixed
- **Windows notifications showed a square instead of an icon.** The symbol used isn't in the icon font. They now
  show the sending app's own icon (saved the first time that app sends one), or a bell when Windows doesn't give
  one. This applies in the pop-up and in the notification center.

## 3.3.0

### Added
- **Skip songs from the home screen:** R2 plays the next song and L2 the previous one while something is playing.
  The music banner gives a little nudge. R1 / L1 still flip between pages.
- **Settings: the list of sections can shrink to icons only**, like the library's sidebar. Use the "Show icons only"
  row at the top; the choice is remembered. Your profile shows as its picture, Find a setting as a magnifier, and the
  section you're in has a stronger tint.

### Changed
- **Web browser reworked:**
  - **Tabs**, with each site's icon and title and a close button. "+" or Menu opens a new tab, LB / RB switch tabs,
    and links that open a new window open in a new tab.
  - **A rounded address bar** with a padlock for secure pages. Type straight into it with a keyboard, or press X.
  - Round back, forward, reload and home buttons; a zoom control showing the zoom level; a thin loading bar.
  - **The right stick is a mouse pointer.** Press the stick in to click, or press A just after moving the pointer. The
    d-pad still jumps between links, and LT / RT scroll a page at a time. Switch the stick back to scrolling in
    Settings or in the browser's menu.
  - **"Keep the browser open when you leave it"** (Settings > Network and browser; on by default). Leaving the browser
    no longer resets it: the tabs and pages are exactly as they were when you come back, and video and sound pause
    while you're away. "Close the browser" in its menu really closes it.
- **Windows touch keyboard:** with "Windows touch keyboard" chosen in Settings, Search, the library search bar, the
  browser's address bar and text boxes on web pages no longer pop up SquareDeck's typing box. You type straight
  into them with Windows' keyboard. Other questions that need typing (renaming, keys) still use a small box, now in
  the page's own colors instead of a white box.

### Fixed
- The library's hint bar said "View: Sidebar", but View always opens Quick settings. The hint is gone; the
  sidebar's top row shrinks and opens it.

## 3.2.1

### Changed
- **Choosing a moving background turns off "Hero background behind the home row"**, since the video takes the
  artwork's place. This happens in Settings and in the welcome pages. You can switch the artwork back on in
  Settings > Appearance, where the row now says it was turned off for the moving background. Picking a style built
  around game artwork (Steam Big Picture, Cinema, Frosted) no longer switches the artwork back on while a moving
  background is set.

## 3.2.0

### Added
- **Welcome pages** the first time SquareDeck opens, in six steps:
  - Welcome, with how many games were found.
  - Pick a look: eight style previews, plus light, dark, black or follow Windows.
  - A moving background (or none).
  - Your games: other launchers, and the optional Steam Web API key.
  - Your controller: button names, which button selects, and interface sounds.
  - Tips.

  Choices apply straight away. Menu or RB goes to the next step and B goes back. Run them again from Settings >
  Updates, backup and reset > Run first-time setup again. The old question-by-question setup is gone.
- **Song cover art in the home banner:** the music banner shows the song's cover instead of the play/pause symbol
  (dimmed while paused). The song name scrolls when it's too long for the banner.
- **Scrolling game names in the library:** a long name slides along while its tile is highlighted, and ends in "…" otherwise.
- **Switches for on/off settings:** rows that were "On" or "Off" now show a toggle switch, on every page.
- **Five more UI styles:**
  - **Frosted Light:** bright milky glass with dark text over soft pastel light.
  - **Sunset Drive:** a purple-to-orange synthwave sky with coral highlights.
  - **Blueprint:** white lines on blueprint blue with a drafting grid and outline buttons.
  - **Forest:** moss greens with an amber highlight, and names on the tiles.
  - **Newsprint:** cream paper, black ink, the game's name as a serif headline, and sections as tabs.

  Styles can now have their own background gradient behind everything (Sunset Drive, Forest) when there's no wallpaper.

### Changed
- **Settings reworked into two panes.** The sections are listed on the left; moving through them shows each one on
  the right straight away, and selecting one goes into it. B goes back to the list, then closes.
  - At the top of the list: your profile and **Find a setting**, a search over every setting that lands on the row.
  - Each section starts with its name and what's in it.
  - Accessibility is now one of the sections.

### Fixed
- **Cinema: the selected tile was cut off at the bottom.** It now grows less (18%) and grows upward into the space
  above the row.
- **Cinema (and other styles): the dot under the active button overlapped the button's name.** The name now sits
  below the dot.
- **Library sidebar in icons-only mode: the icons looked squashed.** The rows were built for text and squeezed the
  icon backgrounds. The icons are now proper square buttons, slightly wider. The section you're in is shown with a
  stronger tint.

## 3.1.1

### Fixed
- **Frosted: cover art was washed out on the home screen.** Tile corners are rounded with a mask painted in the tile
  color, and Frosted's see-through tile color made the covers see-through too. The mask now always has a solid color,
  so covers show at full strength in every style. The style previews no longer draw glass tiles washed out either.

### Added
- **Cinema shows the game's logo** (from SteamGridDB) in place of its name on the home screen. It fades in, and the
  name stays if a game has no logo.
- **Four more UI styles:**
  - **Frosted Dark:** smoked dark glass with a thin bright rim, the floating button shelf and hint pill, cool white
    highlights and a darker backdrop.
  - **Deep Ocean:** deep blue with aqua glow, soft round shapes, and the games along the bottom with their name underneath.
  - **Sakura:** cherry-blossom pink with rounded cards. Dark with the theme turns it into a night version.
  - **Terminal:** a green phosphor console with glowing text, scan lines, square edges and buttons as labelled commands.

## 3.1.0

### Added
- **Search everything** on the Start / Menu button (keyboard: F3 or /). One search box finds:
  - **Games:** installed, owned and wishlist games.
  - **Apps:** apps added to SquareDeck and everything in the Windows Start menu.
  - **Settings:** every settings page and row. Opening a setting lands on that row.
  - **Files:** files and folders in Desktop, Downloads, Documents, Pictures, Videos and Music.
  - **Steam store:** matching store pages, with prices or "In your library".
  - **Elsewhere:** links to search the Microsoft Store and the web.

  With a controller the on-screen keyboard opens straight away. Recent searches are kept. X on a game opens its
  details, and X on a file opens its folder in Explorer. Settings is still the gear button.
- **Moving backgrounds** (Settings > Appearance > Moving background): nine looping videos in two groups, Waves
  (Black, Blue, Gold, Silver) and Blades (Black, Green, Mint, Blurry, Fuzzy). The highlighted tile plays a preview.
  A **blur** level (None, Light, Medium, Strong, Very strong) applies live. The video shows behind the home screen
  and every page, replaces the still wallpaper while it's on, and pauses while a game runs or SquareDeck isn't in front.
- **Five new UI styles:**
  - **Cinema:** sharp game art, the title large in serif type, a gold Play button and darkened edges.
  - **Carbon:** carbon-fiber weave, red highlights, button cards and names on the tiles.
  - **Pastel:** lilac and mint, very round corners. It follows light or dark.
  - **Pop Art:** yellow, thick black outlines, hard comic shadows and halftone dots.
  - **Dot Matrix:** black and white with one red accent, a dot grid, outline buttons and light type.
- **Library search bar:** a real search box at the top of the library. Type on a keyboard and the games filter as
  you go. With a controller, select it for the on-screen keyboard. B clears it.
- **Library sidebar as a blade:** the sidebar can shrink to just icons with its top row, or with View (Q on a
  keyboard). Stores and categories show their initials, and names appear as tooltips. The choice is remembered.

### Changed
- **Glass is now Frosted** and was reworked:
  - Thicker frosted panels that are brighter at the top, with a bright rim.
  - The buttons sit together on one floating frosted shelf, and the hints on a floating pill.
  - Rounder tiles, softer shadows and a stronger blur behind.
  - It looks best with a moving background.
- **Removed the Xbox and PlayStation 5 styles.** Anyone using PlayStation 5 moves to Cinema, and anyone using
  Xbox moves to Switch.

## 3.0.0

### Added
- **UI styles** (Settings > Appearance > UI style). Each style changes the home screen and every page:
  colors, type, corners, the shape of the buttons, tile size, the game art behind it, the focus glow and pop-ups.
  The picker shows a small drawing of each style.
  - **Switch**: the original look.
  - **Xbox**: full-screen game art fading into the background, a row of smaller tiles where the selected one
    grows with its name across the bottom, buttons as labelled cards, and the Xbox green highlight.
  - **PlayStation 5**: small tiles across the top, the selected one much bigger, the game's title large at the
    lower left over its art, a "Play Game" button, and a white highlight.
  - **Steam Big Picture**: art across the screen, a "Recent games" label over the row, the game's name and details
    under it, the buttons as tabs, a dark hint bar along the bottom, and the Steam blue highlight.
  - **Neon** (new): near-black with glowing magenta edges, a faint cyan grid over everything, sharp corners and
    Bahnschrift type.
  - **Minimal** (new): flat with no shadows, small round buttons, more space between tiles, and one warm orange
    accent. It follows the light or dark theme.
  - **Retro** (new): an old amber monitor, with scan lines, chunky square edges and Consolas type.
  - **Glass**: frosted, see-through panels and buttons with bright edges over heavily blurred, colorful art. It
    uses the game's art, your wallpaper, or a built-in gradient.

  Choosing Xbox, PlayStation 5, Steam Big Picture or Glass turns on the artwork behind the home screen. A highlight
  color you picked yourself is kept. High-contrast themes still win over any style.
- **Pages show the game's art** in the Xbox, PlayStation, Steam, Neon and Glass styles: blurred and dimmed behind
  every page, the way the consoles do it.

### Changed
- **Explorer**, rebuilt along the lines of Handheld Explorer:
  - A sidebar with Quick access, Pinned folders and Drives, each drive with a bar showing how full it is.
  - A top bar with sidebar, back, up, a clickable breadcrumb path, filter, grid/list and menu.
  - Big tiles with picture thumbnails, or a list with date and size.
  - Tick several items with Y, then copy, cut, paste, delete, zip or check their properties together.
  - LB / RB go back and forward. LT / RT page. View switches grid and list. Menu opens the main menu.
  - New folder, natural sorting (by name, date, size or type), hidden files, pin folders, copy the path,
    show in Windows Explorer.
  - Updates when files change. Press and hold (touch) or right-click for actions.
  - The view, sort, hidden files and pins are remembered.
- **Task Manager**, rebuilt along the lines of Handheld Task Manager:
  - **Apps** shows programs with a window. **Processes** shows everything, grouped by program. **Performance**
    has CPU and memory with a minute of history, battery, network speed, storage and system.
  - CPU, RAM and battery are always in the top bar.
  - Actions: switch to, close (letting the app save), end task, priority, open file location, details.
  - X ends a task (asks first), Y changes the sort, LB / RB switch tabs, and the update speed can be changed.
  - The order holds still while you move through the list. Windows system processes and SquareDeck itself can't
    be ended from it.
- **News**: LB / RB move between Latest, Patches and updates, Announcements and Sales. Y still moves to the next one.

## 2.9.0

### Added
- **News button** on the home screen, next to Friends. The page has four sections:
  - **Latest:** recent posts from the Steam pages of the games you play (the 18 most recent, favorites and games with an update waiting first), plus Steam's own news. Grouped by day, each card has the game's cover.
  - **Patches and updates:** patch notes and update posts only.
  - **Announcements:** developers' own Steam announcements.
  - **Sales:** games on sale from your wishlist, then the Steam store's current specials, with the discount, old and new price, and when the sale ends.

  Posts open in the built-in reader, and sales open the Steam store page. Y moves to the next section and X refreshes. News is kept for 15 minutes, so opening it again is instant.
- **News button setting** in Settings > Home screen > Buttons under the games (on by default).

## 2.8.0

### Added
- **Launch splash screen** (Settings > Games and library > Launching games, on by default). The game's cover fills the screen over its blurred artwork, with "Starting…", from pressing Play until the game is running. B hides it early.
- **More animation options** (Settings > Home screen > Animations):
  - Home buttons: Grow and glow, Lift, Bounce, Pulse, Wiggle or Off.
  - Opening pages: Slide up, Fade, Zoom or None.
  - Selected game: Pulsing ring, Steady ring or Tile grows.
- **Countdown length**: the countdown before a game starts can be 3, 5 or 10 seconds.

### Changed
- The **countdown before a game starts** now sits at the top of Settings > Games and library, under Launching games. It was easy to miss lower down that page. The countdown pop-up has the new rounded look.

## 2.7.0

### Added
- **Home button animations.** The round buttons, the top-bar icons and the footer buttons grow with a soft glow in your highlight color when highlighted. They squash and spring back when pressed, with the controller, mouse or touch. Switch it off in Settings > Home screen > Home row ("Animate the home buttons"). Reduce motion also turns it off.
- **Xbox One sound pack** (by Dreamersouls) in Settings > Sound > Interface sounds.

### Fixed
- **The battery icon's highlight could stay lit** after moving away from it. Its label now reads "Battery" instead of "Bluetooth".

## 2.6.2

### Fixed
- **SteamGridDB icons were smaller than their tile.** Icons from SteamGridDB, chosen or automatic, now fill the whole tile like a cover, on the home screen, in the library, on the profile and details pages and in the notification panel. Icons taken from a program's own file stay centered, since they are too small to fill a tile.
- **Not installed games showed their name on top of a downloaded cover in the library.** The name now shows only until a cover arrives. Not installed covers are dimmed with a shade and a download symbol instead of being faded.

## 2.6.1

### Added
- **Windows touch keyboard** option (Settings > Controller and quick menu > Typing). Text boxes then open Windows' own touch keyboard instead of the built-in controller keyboard.
- **Per-store switches** for other game stores (Settings > Games and library > Other game stores): Epic Games, GOG Galaxy, Xbox, Battle.net and non-Steam games added to Steam can each be turned on or off.

### Changed
- **Only one SquareDeck runs at a time.** Starting it again brings the running copy to the front.
- The Power menu's **Exit to desktop** is now **Exit SquareDeck**.

### Fixed
- **Icons not found for some games** even though SteamGridDB has them. Icons in Windows .ico format are now included (using their largest size), and games SteamGridDB doesn't link to their Steam ID are looked up by title. A "nothing found" result is now checked again after a week.

## 2.6.0

### Added
- **Icons from SteamGridDB.** When SteamGridDB has no square image for a game, the cover picker shows the game's icons instead. Otherwise a "Search icons instead" tile lists them. A chosen icon is centered on a wash of its own colors.
- **Capture albums.** A new Albums filter on the Captures page groups screenshots and clips by game, using the game name in each file name. B goes back to the list of albums. Settings > Games and library can open Captures as albums by default.
- **Xbox mode home app** (Settings > System and tools). One switch points AnyFSE at SquareDeck, so SquareDeck opens when Windows' full screen experience (Xbox mode) starts. The page shows whether AnyFSE is the Home app in Windows, links to that setting, and adds "Open Xbox mode when Windows starts".
- **ROG Ally button remapping** (Settings > Controller, shown only on an Ally).
  - Covers the Armoury Crate, Command Center / Library and Mode+ buttons, plus the microphone, screenshot, keyboard and record keys.
  - Actions include opening SquareDeck, its quick menu or Task Manager, the task switcher, Game Bar, Command Center, Armoury Crate, the on-screen keyboard, screenshot, recording, Night light and show desktop.
  - The way the buttons are read is based on AnyFSE by Artem Shpynov (MIT licence). AnyFSE's injected filter for Armoury Crate SE is not included, so set those buttons to do nothing in Armoury Crate SE.
- **Night light** switch in Quick settings.

### Changed
- **Settings** shows your Steam account (profile name and Steam ID) instead of the Windows user name.
- **Notification panel** shows the game's cover or icon next to game notifications, with the achievement's own icon in the corner.
- **Apps row**: the app opened last comes first. With ten or more apps the row ends with More apps, and Add app is only inside More apps.

## 2.5.1

### Changed
- The **Continue where you left off** tile on the home row is larger and uses that wording. Its switch is in Settings > Home screen > Home row.

### Fixed
- The Open folder button and thumbnail badges on the Captures page showed squares instead of text.

## 2.5.0

### Changed
- **New look across all pages**:
  - List rows are rounded cards grouped by section, each with its icon in a tinted badge.
  - Page titles and section headings are bolder, and buttons are pill-shaped.
  - Tiles and pictures have rounded corners, and pop-ups are rounded with a shadow.
- **Settings home** is a profile card and a grid of category cards. It reopens on the last category used.
- **Captures** has All / Screenshots / Videos filters, a section per day, the game's name on each capture, badges on the thumbnails, and an Open folder button.

## 2.4.1

### Changed
- **Game details page redesigned**:
  - An edge-to-edge artwork banner that fades as you scroll.
  - Cover and title, with the game's logo on the right.
  - A large Play button with rounded action buttons.
  - Stat cards, an About card with facts, and card layouts for screenshots, news, DLC and achievements.
  - X opens collections and Y opens rating and notes.

## 2.4.0

### Added
- **Wishlist** section in the library, under Not installed, with prices and discounts.
- **Updates** and **Collections** in the library sidebar.
- **More apps**: the Apps row shows ten apps, then a tile that opens all of them.
- **Stores page**: Y hides a store or uninstalls it (through winget).
- **Night light** switch in Settings > Appearance > Screen.
- **Background music** (Settings > Sound): five built-in tracks, Shuffle all, and your own MP3s. It plays only while no game is running.
- **Music page search** (Settings > Sound > Music page). YouTube Music opens in the built-in browser. Spotify search and playlists need your own Spotify app's Client ID.
- **Profile page** redesigned as cards, with your Steam level, XP, badges and recently played games (needs the Steam Web API key).

### Changed
- The not-installed list and the wishlist are saved between runs, so they and their counts show immediately. Each refreshes in the background when its section is selected.
- Games with no square cover show their icon in the library as well as on the home screen.
- **Favorites**: a heart pops up once when a game is added, instead of a badge that stays on the tile.
- **LB / RB** skip the Music and Friends pages when their buttons are switched off.

### Fixed
- **Graphics memory**: while a game runs, the launcher stops using the graphics card and frees its pictures, so the game gets the video memory. Pictures are never decoded larger than needed.
- **More than one highlight at a time**, especially with touch. Moving the mouse now moves the focus, the library sidebar marks the open section with an accent bar, and the home ring hides while a button has the focus.
- The Updates and Collections library sections from 2.2 were missing from the sidebar.

## 2.3.0

### Added
- **Main display** setting: choose which display the launcher opens on.
- **Windows notifications** from other apps appear in the launcher's pop-ups and notification panel, when Windows allows it.
- **Profile page** behind the profile picture: latest games, Steam wishlist, achievements, play-time and library numbers.
- Friends' profile pictures on their pop-ups and in the notification panel.

### Changed
- The **quick menu** shows the running game's cover and takes its color from it.

### Fixed
- The **speed test** download result no longer shows "Failed"; it falls back through several test servers.

## 2.2.1

### Added
- **Restart to UEFI / BIOS setup** (Settings > This PC).

### Changed
- The session summary pop-up shows the game's icon.

## 2.2.0

### Added
- **Web browser** (WebView2) on the home screen, with back, forward, reload, home, address bar, bookmarks, zoom, search engine and home page settings.
- **Home** button on the home screen. Music, Friends, Web browser and Explorer can each be hidden in Settings.
- **Collections** in the library: create, rename, delete, and give each a 1:1 picture or an icon. Games are added from their options menu or details page.
- **Sound packs** from `SoundPacks`, in the Steam Deck audio-loader layout. Your own packs can go in `%LOCALAPPDATA%\SquareDeck\SoundPacks`.
- **Updates** from github.com/tetraguy/SquareDeck/releases, checked automatically or manually. The release notes are shown before installing.
- **Screen saver** on the home screen: a moving gradient or a game-artwork slideshow, after a time you choose.
- **Battery page** from the battery icon, and your **Steam avatar** as a profile picture.
- **Highlight color**: follow Windows, or pick any color from a gradient.
- More closing animations, a 16:10 screen shape, and Reset the launcher.

### Changed
- **Button hints** look the same on every page and switch to keyboard keys when a keyboard or mouse is used. Choose Xbox, PlayStation or Switch button names in Settings.
- **Notifications** open as a panel sliding in from the right.
- **Music page**: an Open "player" button for the app that is playing, tap the artwork to go to it, and players can be removed from the Open player list.
- The game details page always shows the title, with the logo beside the game info.
- Settings reorganized into categories.

## 2.1.0

### Added
- **Explorer**: a controller file explorer with copy, move, rename, new folder, delete to the Recycle Bin, ZIP and extract.
- **Task Manager**: Apps, Processes and Performance tabs. Windows' own processes are protected.
- **AnyFSE** page: download and run its installer, or open it if it is installed.
- **Accessibility**: text size 100 to 150%, high contrast, reduce motion, and a shortcut to Windows' color filters.
- **Interface sounds** page: eight sound sets, loudness, per-kind switches and test buttons.
- **News reader**: game news and updates open inside the launcher.
- Power menu icons, a closing animation on Exit to desktop, and six startup animations.
- Quick menu options: slide in from the left or right, choice of controller buttons, and an optional keyboard shortcut.
- Low-battery pop-up at 15% (and 5%), and a reminder when Windows is waiting to restart for an update.

### Changed
- A "Closing software" pop-up shows while a game is being stopped.
- The wallpaper shows behind every page, the Music page uses the blurred album art while something plays, and the library uses the full width.

### Fixed
- B on the home screen could start a game that had already closed.
- Interface sounds stopped working after a while.
- Picking a cover from SteamGridDB sometimes failed; downloads now retry and fall back to the preview image.

## 2.0.0

### Added
- **Controller file browser** for every "choose a file or folder" step.
- **Library page** with a sidebar (All, Favorites, Not installed, stores, categories, status), achievement progress, jump by letter, and Steam games you own but haven't installed, with an install queue.
- **Game profiles**: resolution, refresh rate, HDR, power plan and sound output applied when a game starts and put back when it closes.
- **Notification center**: achievements, session summaries, friends, finished downloads and low-battery warnings.
- Session summary, do not disturb while playing, launch countdown, in-game quick menu, and per-game highlight tint.
- **Now playing** controls in the footer, and a visualizer on the Music page.
- **Stores page**, with "Add more stores".
- Backup and restore, vibration strength, controller test, network speed test, clock options, boot video, cache size limit, an OLED theme, and first-run setup.
- Slide transitions on LB / RB, touch swipes, and the SquareDeck logo.

## 1.x

- First releases:
  - Switch-style home screen with square SteamGridDB covers for installed Steam games, apps, custom games and favorites.
  - Game details, captures and music pages.
  - Wi-Fi and Bluetooth settings, profile pictures, themes and highlight colors.
  - Running-game detection with Stop and return-to-game, play-time tracking, achievements and pop-ups.
  - Steam friends, a news feed, animated covers, interface sounds and controller vibration.
