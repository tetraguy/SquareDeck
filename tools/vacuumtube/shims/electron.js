// SquareDeck's stand-in for Electron's ipcRenderer. VacuumTube's page scripts were written to talk to VacuumTube's own
// Electron main process; inside SquareDeck they talk to SquareDeck instead (window.chrome.webview messages), and the
// answers that main process would give are given here.
const DEFAULTS = require('./defaults.js')

const boot = window.__sdvtBoot || {}
const listeners = {}
let config = Object.assign({}, DEFAULTS, boot.config || {})

function copy(o) { return JSON.parse(JSON.stringify(o)) }
function post(m) { try { window.chrome.webview.postMessage(m) } catch (e) { } }
function emit(channel, ...args) {
    for (const fn of (listeners[channel] || []).slice()) {
        try { fn({}, ...args) } catch (err) { console.error('[SquareDeck] listener failed', channel, err) }
    }
}

// SponsorBlock wants one id per person: made here the first time, then kept with the rest of the settings.
if (!config.sponsorblock_uuid) {
    config.sponsorblock_uuid = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2))
    post('vt:config|' + JSON.stringify(config))
}

window.addEventListener('focus', () => emit('focus'))
window.addEventListener('blur', () => emit('blur'))
window.__sdvtEmit = emit // SquareDeck calls this to send events in (a link to open, say)

function aboutInfo() {
    return {
        status: 'unsupported', // updates come with SquareDeck, not from VacuumTube's own updater
        currentVersion: boot.version || '',
        latestVersion: '',
        platform: 'win32',
        arch: 'x64',
        installationType: 'unknown',
        canAutoUpdate: false,
        progress: null
    }
}

const ipcRenderer = {
    on(channel, fn) { (listeners[channel] = listeners[channel] || []).push(fn); return ipcRenderer },
    once(channel, fn) { const wrap = (...a) => { ipcRenderer.removeListener(channel, wrap); fn(...a) }; return ipcRenderer.on(channel, wrap) },
    removeListener(channel, fn) { listeners[channel] = (listeners[channel] || []).filter(f => f !== fn); return ipcRenderer },
    send() { },
    sendSync(channel, arg) {
        switch (channel) {
            case 'get-config': return copy(config)
            case 'set-config':
                Object.assign(config, arg || {})
                post('vt:config|' + JSON.stringify(config))
                setTimeout(() => emit('config-update', copy(config)), 0)
                return copy(config)
            case 'can-auto-update': return false
        }
        return null
    },
    async invoke(channel) {
        switch (channel) {
            case 'is-steam': return false
            case 'get-deeplink': return boot.deeplink || null
            case 'reload':
            case 'relaunch-app': location.reload(); return null
            case 'set-fullscreen':
            case 'set-on-top': return null // SquareDeck is already full screen
            case 'get-userstyles': return []
            case 'get-about-info':
            case 'check-for-updates': return aboutInfo()
            case 'open-project-page': post('vt:open|https://github.com/shy1132/VacuumTube'); return null
            case 'open-release-page':
            case 'open-update-download': post('vt:open|https://github.com/shy1132/VacuumTube/releases'); return null
            case 'request-microphone-permission': return false
        }
        return null
    }
}

// Imported by two modules; links open in SquareDeck's browser only for VacuumTube's own pages.
const shell = {
    openExternal: async (url) => { post('vt:open|' + url) },
    openPath: async () => ''
}

module.exports = { ipcRenderer, shell }
