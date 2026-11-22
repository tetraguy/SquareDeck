// VacuumTube's h5vcc module, without DIAL: casting to the device from a phone needs a local network server, which a
// page can't run. Leanback still gets the runtime (start-up links) and the video size override it asks the TV apps for.
const { ipcRenderer } = require('electron')
const configManager = require('__VT__/config')
const config = configManager.get()

function getMaxResolution() {
    const resolutions = [ '256x144', '426x240', '640x360', '854x480', '1280x720', '1920x1080', '2560x1440', '3840x2160', '7680x4320' ]
    const w = Math.max(window.screen.width, window.screen.height) * (window.devicePixelRatio || 1)
    const h = Math.min(window.screen.width, window.screen.height) * (window.devicePixelRatio || 1)
    for (const r of resolutions) {
        const [ rw, rh ] = r.split('x').map(Number)
        if (w <= rw && h <= rh) return r
    }
    return `${Math.round(w)}x${Math.round(h)}`
}

function createOnDeepLink() {
    const listeners = []
    const pending = []
    ipcRenderer.on('deeplink', (event, link) => {
        if (listeners.length === 0) { pending.push(link); return }
        for (const l of listeners) { try { l(link) } catch (err) { console.error('[h5vcc] Deeplink listener failed', err) } }
    })
    return {
        addListener: (listener) => {
            listeners.push(listener)
            while (pending.length > 0) { try { listener(pending.shift()) } catch (err) { console.error(err) } }
        }
    }
}

module.exports = async () => {
    const initialDeepLink = await ipcRenderer.invoke('get-deeplink')
    const maxResolution = getMaxResolution()
    window.h5vcc = {
        runtime: { initialDeepLink, onDeepLink: createOnDeepLink() },
        system: { getVideoContainerSizeOverride: () => config.unlock_resolution ? '7680x4320' : maxResolution }
    }
}
