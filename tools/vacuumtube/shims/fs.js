// VacuumTube reads its stylesheets, translations and icon from disk; in SquareDeck they're built into this script.
const FILES = require('./files.js')
const path = require('./path.js')

function readFileSync(p) {
    const key = path.normalize(p)
    const file = FILES[key]
    if (!file) throw new Error('ENOENT: ' + key)
    if (file.b64 !== undefined) return { toString: (enc) => enc === 'base64' ? file.b64 : atob(file.b64) }
    return file.text
}

module.exports = {
    readFileSync,
    existsSync: (p) => !!FILES[path.normalize(p)],
    promises: {
        readdir: async (p) => Object.keys(FILES).filter(k => path.dirname(k) === path.normalize(p)).map(k => path.basename(k)),
        readFile: async (p, enc) => readFileSync(p, enc)
    }
}
