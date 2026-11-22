// The bits of Node's path module VacuumTube uses (POSIX style: the bundled files are kept under virtual paths).
function normalize(p) {
    const out = []
    for (const part of String(p).split('/')) {
        if (part === '' || part === '.') continue
        if (part === '..') out.pop(); else out.push(part)
    }
    return '/' + out.join('/')
}
module.exports = {
    sep: '/',
    normalize,
    join: (...parts) => normalize(parts.join('/')),
    dirname: (p) => normalize(p).replace(/\/[^/]*$/, '') || '/',
    basename: (p) => normalize(p).split('/').pop()
}
