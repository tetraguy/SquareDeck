// Bundles VacuumTube's page scripts (src/preload) into one script SquareDeck adds to its YouTube Leanback page.
const fs = require('fs')
const path = require('path')
const esbuild = require('esbuild')

// VacuumTube's repo: a clone next to this script (tools/vacuumtube/VacuumTube), or VACUUMTUBE_SRC.
const VT = process.env.VACUUMTUBE_SRC || path.join(__dirname, 'VacuumTube')
const PRE = path.join(VT, 'src/preload')
const out = process.argv[2] || path.join(__dirname, '../../SquareDeck/VacuumTube/vacuumtube.js')
const pkg = require(path.join(VT, 'package.json'))

// --- the files VacuumTube reads at run time, kept under the same paths relative to its repo
const files = {}
const add = (abs, b64) => {
    const key = '/' + path.relative(VT, abs).split(path.sep).join('/')
    files[key] = b64 ? { b64: fs.readFileSync(abs).toString('base64') } : { text: fs.readFileSync(abs, 'utf-8') }
}
for (const f of fs.readdirSync(path.join(VT, 'locale'))) if (f.endsWith('.json')) add(path.join(VT, 'locale', f))
add(path.join(VT, 'assets/icon.png'), true)
;(function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, e.name)
        if (e.isDirectory()) walk(p); else if (e.name.endsWith('.css')) add(p)
    }
})(PRE)
fs.writeFileSync(path.join(__dirname, 'shims/files.js'), 'module.exports = ' + JSON.stringify(files) + '\n')

// --- VacuumTube's default settings, from its own config.js (the SponsorBlock id is made per person at run time)
const cfg = fs.readFileSync(path.join(VT, 'src/config.js'), 'utf-8')
const body = cfg.slice(cfg.indexOf('const defaults = {') + 'const defaults = '.length)
let depth = 0, end = 0
for (let i = 0; i < body.length; i++) { if (body[i] === '{') depth++; else if (body[i] === '}' && --depth === 0) { end = i + 1; break } }
const defaults = new Function('crypto', 'return (' + body.slice(0, end) + ')')({ randomUUID: () => '' })
defaults.sponsorblock_uuid = ''
defaults.fullscreen = true
fs.writeFileSync(path.join(__dirname, 'shims/defaults.js'), 'module.exports = ' + JSON.stringify(defaults, null, 1) + '\n')

// --- the entry: what preload/index.js does, with the module list fixed at build time (sorted like Windows lists them)
const mods = fs.readdirSync(path.join(PRE, 'modules'), { withFileTypes: true })
    .filter(e => e.isDirectory() || e.name.endsWith('.js'))
    .map(e => e.name).sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()))
const requires = mods.map(name => name === 'h5vcc'
    ? `require('./shims/h5vcc.js')`
    : `require('__VT__/modules/${name}${name.endsWith('.js') ? '' : '/index.js'}')`)
fs.writeFileSync(path.join(__dirname, 'entry.js'), `
if (location.host === 'www.youtube.com' && location.pathname === '/tv' && !window.__sdvtLoaded) {
    window.__sdvtLoaded = true
    const xhrModifiers = require('__VT__/util/xhrModifiers')
    xhrModifiers.block()
    const modules = [
        ${requires.join(',\n        ')}
    ]
    for (const module of modules) {
        try { module() } catch (err) { console.error('a module experienced failure while loading', err) }
    }
    xhrModifiers.unblock()

    // SquareDeck: while this page reads the controller itself (VacuumTube's controller support), it says so every
    // second, and SquareDeck keeps its own controller handling out of the way.
    const configManager = require('__VT__/config')
    setInterval(() => {
        try {
            const pads = navigator.getGamepads ? Array.from(navigator.getGamepads()).filter(p => p && p.connected) : []
            if (pads.length && configManager.get().controller_support && document.hasFocus() && document.visibilityState === 'visible')
                window.chrome.webview.postMessage('sd:alive')
        } catch (e) { }
    }, 1000)

    // SquareDeck: ways back out of VacuumTube, besides YouTube's own Exit.
    const leave = () => { try { window.chrome.webview.postMessage('sd:leave') } catch (e) { } }

    // View + Menu together on the controller (the two small buttons in the middle), from anywhere.
    let comboDown = false
    ;(function watchCombo() {
        try {
            const pads = navigator.getGamepads ? Array.from(navigator.getGamepads()).filter(p => p && p.connected) : []
            const both = pads.some(p => p.buttons[8] && p.buttons[8].pressed && p.buttons[9] && p.buttons[9].pressed)
            if (both && !comboDown) leave()
            comboDown = both
        } catch (e) { }
        requestAnimationFrame(watchCombo)
    })()

    // Ctrl+Q on a keyboard.
    window.addEventListener('keydown', (e) => {
        if (e.ctrlKey && (e.key === 'q' || e.key === 'Q')) { e.preventDefault(); e.stopImmediatePropagation(); leave() }
    }, true)

    // With touch or a mouse: a "SquareDeck" button in the corner, shown for a few seconds after you touch or move.
    const addButton = () => {
        if (!document.body || document.getElementById('sd-leave')) return
        const b = document.createElement('div')
        b.id = 'sd-leave'
        b.textContent = '\u2190  SquareDeck'
        b.style.cssText = 'position:fixed;top:24px;left:24px;z-index:2147483647;padding:14px 26px;border-radius:32px;' +
            'background:rgba(20,20,20,.82);color:#fff;font:600 22px/1 "YouTube Sans",Roboto,Arial,sans-serif;' +
            'box-shadow:0 4px 18px rgba(0,0,0,.45);cursor:pointer;opacity:0;pointer-events:none;transition:opacity .25s;user-select:none'
        const stop = (e) => { e.preventDefault(); e.stopPropagation() }
        b.addEventListener('pointerdown', stop, true)
        b.addEventListener('click', (e) => { stop(e); leave() }, true)
        document.body.appendChild(b)
        let hideAt = 0
        const show = () => {
            b.style.opacity = '1'; b.style.pointerEvents = 'auto'
            clearTimeout(hideAt)
            hideAt = setTimeout(() => { b.style.opacity = '0'; b.style.pointerEvents = 'none' }, 3500)
        }
        window.addEventListener('pointermove', (e) => { if (e.pointerType !== 'mouse' || e.movementX || e.movementY) show() }, true)
        window.addEventListener('pointerdown', show, true)
    }
    if (document.body) addButton(); else document.addEventListener('DOMContentLoaded', addButton)

    // A reminder once Leanback is up.
    setTimeout(() => {
        try { require('__VT__/util/ui').toast('SquareDeck', 'Press View + Menu together to go back to SquareDeck') } catch (e) { }
    }, 6000)
}
`)

// Settings that don't apply inside SquareDeck are hidden from VacuumTube's settings page.
const hidden = [ 'hardware_decoding', 'userstyles', 'device_discoverability', 'fullscreen', 'no_window_decorations', 'keep_on_top', 'auto_update' ]

const plugin = {
    name: 'squaredeck',
    setup(build) {
        build.onResolve({ filter: /^__VT__\// }, a => ({ path: path.join(PRE, a.path.slice('__VT__/'.length)) + (a.path.endsWith('.js') ? '' : '.js') }))
        build.onResolve({ filter: /^(electron|fs|path|os)$/ }, a => ({ path: path.join(__dirname, 'shims', a.path + '.js') }))
        build.onResolve({ filter: /^events$/ }, () => ({ path: require.resolve('events/events.js') }))
        build.onResolve({ filter: /^(dgram|http|crypto)$/ }, a => ({ path: a.path, namespace: 'missing' }))
        build.onLoad({ filter: /.*/, namespace: 'missing' }, a => ({ contents: `throw new Error('${a.path} is not available in SquareDeck')`, loader: 'js' }))
        build.onLoad({ filter: /\/preload\/.*\.js$/ }, async a => {
            let src = await fs.promises.readFile(a.path, 'utf-8')
            if (a.path.endsWith('/settings/schema.js'))
                src = src.split('\n').map(line => {
                    const key = hidden.find(k => line.includes(`key: '${k}'`))
                    if (!key) return line
                    // a row that already decides when it's hidden: hidden always
                    if (line.includes('hide:')) return line.replace(/hide:[^,}]*/, 'hide: true')
                    return line.replace(`key: '${key}'`, `key: '${key}', hide: true`)
                }).join('\n')
            const dir = '/' + path.relative(VT, path.dirname(a.path)).split(path.sep).join('/')
            return { contents: `var __dirname = ${JSON.stringify(dir)};\n` + src, loader: 'js' }
        })
    }
}

const license = fs.readFileSync(path.join(VT, 'LICENSE'), 'utf-8').trim()
esbuild.build({
    entryPoints: [ path.join(__dirname, 'entry.js') ],
    bundle: true, format: 'iife', platform: 'browser', target: 'chrome120', outfile: out,
    define: { 'process.platform': '"win32"', 'process.arch': '"x64"' },
    plugins: [ plugin ], logLevel: 'warning', legalComments: 'none',
    banner: { js: `/*\n VacuumTube ${pkg.version} (https://github.com/shy1132/VacuumTube), its page scripts bundled for SquareDeck.\n Changes for SquareDeck: Electron and Node calls answered by SquareDeck, no DIAL casting server, settings that\n don't apply hidden. Built by tools/vacuumtube/build.js.\n\n ${license.split('\n').join('\n ')}\n*/` }
}).then(() => {
    const size = fs.statSync(out).size
    console.log('built', out, Math.round(size / 1024) + ' KB', mods.length + ' modules', Object.keys(files).length + ' files')
}).catch(e => { console.error(e.message); process.exit(1) })
