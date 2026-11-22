# VacuumTube bundle

SquareDeck's VacuumTube integration (Experimental) runs [VacuumTube](https://github.com/shy1132/VacuumTube)'s page
scripts inside its own WebView2 page, with SquareDeck playing the part of VacuumTube's Electron side. This folder
builds those scripts into `SquareDeck/VacuumTube/vacuumtube.js`.

The bundled version is VacuumTube 2.1.2 (commit d50fcf5). VacuumTube is MIT licensed, by shy; its licence is in
`SquareDeck/VacuumTube/LICENSE.txt` and at the top of the bundle.

To rebuild after a VacuumTube update:

```
git clone https://github.com/shy1132/VacuumTube tools/vacuumtube/VacuumTube
cd tools/vacuumtube
npm install esbuild@0.24.2 events@3.3.0
node build.js
```

Then update `VacuumTube.Version` in `SquareDeck/Services/VacuumTube.cs` (and the Cobalt version there if VacuumTube's
`src/ua.js` changed it).

`shims/` holds SquareDeck's stand-ins for what the scripts expect from Electron and Node: `electron.js` answers
VacuumTube's IPC calls (settings are saved by SquareDeck), `fs.js` serves the stylesheets, translations and icon
built into the bundle, and `h5vcc.js` replaces VacuumTube's h5vcc module without the DIAL casting server, which a
web page can't run. `files.js` and `defaults.js` are written by `build.js`.
