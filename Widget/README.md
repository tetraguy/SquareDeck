# SquareDeck Game Bar widget

SquareDeck's companion inside Xbox Game Bar (Win + G, or the Xbox / Guide button on a handheld). While you play it gives you:

- **Power**: a dial for the TDP with minus / plus, four steps across your chip's range (Quiet, Balanced, Performance, Max) and *Back to profile* to drop a quick change.
- **Game** (while one runs): its picture and icon, hours played and this session, **Per-game profile** (on: the game keeps its own TDP and the dial edits it) and **Close game** (press twice).
- **Live**: processor, graphics, fan speed and battery, each with its last half minute.
- **ROG Ally** (on an Ally): operating mode (Silent / Performance / Turbo), fan curve, lights and controller / mouse mode.
- **Quick**: performance overlay, refresh rate, night light, HDR, do not disturb, play / pause, Wi-Fi, Bluetooth, keyboard, Task Manager, AMD overlay, screen off, microphone, show desktop, AMD metrics.
- Its colors follow **Settings > Controller > Widget colors** in SquareDeck (SquareDeck teal, the UI style, or the game), and **Widget cards** chooses which cards show. The header shows the battery and the time left.
- A rail with **brightness** and **volume** (with a controller: A grabs a slider, up / down changes it, B lets go), mute, and a button that opens SquareDeck.

## How it works

Game Bar widgets are UWP apps and run in a sandbox, so the widget can't change the TDP itself. SquareDeck does it:
it opens a named pipe (`\\.\pipe\SquareDeck.Widget`) that sandboxed apps may use, sends the widget what's going on
about once a second, and carries out what the widget asks (see `SquareDeck/Services/WidgetBridge.cs` and
`SquareDeck/MainWindow.Widget.cs`). The TDP goes through whatever SquareDeck already uses (ASUS firmware on the
ROG Ally, RyzenAdj, and so on), so the widget needs SquareDeck running, set up for TDP, with
**Settings > Controller > Xbox Game Bar widget** on (it is by default).

If SquareDeck isn't running, the widget says so and offers **Start SquareDeck**, which opens `squaredeck:background`
(SquareDeck registers that link for your Windows user) and starts it minimized, so the game stays in front.

Reaching a desktop app from a Game Bar widget through a pipe follows
[GoTweaks](https://github.com/corando98/GoTweaks) by corando98 (MIT).

## Building

1. Visual Studio 2022 with the **Universal Windows Platform development** workload (and the Windows 11 SDK 10.0.22621).
2. Open `Widget\SquareDeck.Widget.sln`. NuGet restores `Microsoft.Gaming.XboxGameBar` and `Microsoft.NETCore.UniversalWindowsPlatform`.
3. Pick **x64** (ROG Ally and most handhelds) and **Debug** or **Release**.

### Trying it from Visual Studio

Turn on **Developer Mode** (Settings > System > For developers), then **Debug > Start without debugging** deploys it.
Open Game Bar: SquareDeck is in the widget menu (and starts pinned the first time). To debug inside Game Bar, set the
project's Debug option *Do not launch, but debug my code when it starts*, press F5, then open the widget from Game Bar.

### A package to install

The manifest's Publisher (in `Package.appxmanifest`) has to match the certificate you sign with. The script makes one with that name:

```powershell
# Once: a code-signing certificate for CN=SquareDeck, trusted on this PC
.\New-WidgetCertificate.ps1
```

Then in Visual Studio: **Project > Publish > Create App Packages… > Sideloading**, choose the certificate
(`SquareDeck.Widget.pfx`), x64, Release. Install the `.msix` from the output folder by double-clicking it
(on another PC, trust the `.cer` first: `Import-Certificate -FilePath SquareDeck.Widget.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`, as administrator).

Keep the `.pfx` and its password out of the repository.

### Shipping it with SquareDeck

SquareDeck installs the widget by itself. Put the contents of the AppPackages output (the `.msix`, its `.cer` and
the `Dependencies` folder) in a folder called `Widget` next to `SquareDeck.exe`; the installer should do the same.
A little after SquareDeck starts, if the widget isn't installed or an older one is, SquareDeck:

1. trusts the `.cer` for the PC (Local Machine > Trusted People), which takes one administrator prompt the first time,
2. installs the `.msix` with its dependencies for the current Windows user (no administrator needed).

A declined prompt isn't asked again for the same version; **Settings > Controller > Install the widget** does it on
demand. Building SquareDeck copies whatever is in `Widget\SquareDeck.Widget\AppPackages` into that folder, so a
Debug run installs your latest package.

## Messages

One JSON object per line, both ways.

The widget sends `{"op": ...}`:
`hello`, `tdp` (`watts`), `tdpstep` (`delta`), `tdpreset`, `volume` (`value`), `mute` (`on`, optional), `brightness` (`value`),
`asusmode` (`id`: silent / performance / turbo), `fan` (`id`: quiet / balanced / cool / max), `refresh` (`hz`),
`gameprofile` (`on`), `closegame`, `art` (`key`), `action` (`id`: mic, desktop, perf, refresh, nightlight, hdr, dnd, wifi, bluetooth, keyboard, taskmanager, lights, padmouse, playpause, screenoff, amdoverlay, amdmetrics, home), `open`.

SquareDeck sends `{"type": "state", ...}` (device, game, tdp {ready, min, max, presets, watts, session, profile, method, chip},
cpu, gpu, fps, battery, plugged, volume, muted, brightness, ally {asus, mode, fan, lights, mouse, rpm},
toggles {perf, nightlight, hdr, dnd}, refresh, rates), `{"type": "art", "key", "icon", "hero"}` (base64 pictures) and `{"type": "note", "text": ...}`.
