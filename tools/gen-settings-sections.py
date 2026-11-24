import re, sys
import os
root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SquareDeck")
src = open(root + '/Screens/SettingsScreens.cs', encoding='utf-8').read()
settings_src = open(root + '/Services/Settings.cs', encoding='utf-8').read()
props = set(re.findall(r'public\s+[\w<>\[\]?,\s]+?\s+(\w+)\s*\{\s*get;\s*set;', settings_src))

def method_body(name):
    m = re.search(r'\n    private (?:async )?\w+(?:<[^>]+>)? ' + name + r'\(Settings s[^)]*\)\s*\n    \{', src)
    if not m: return ''
    i = m.end(); depth = 1
    while depth:
        c = src[i]
        if c == '{': depth += 1
        elif c == '}': depth -= 1
        i += 1
    return src[m.end():i-1]

subs = ['BuildTdp', 'BuildTdpHelper', 'BuildAllyHardware', 'BuildAdvancedStyles', 'BuildAdrenalineHook']
def expand(body, seen=()):
    for sname in subs:
        if sname in seen: continue
        if re.search(sname + r'\(s\b', body):
            inner = expand(method_body(sname), seen + (sname,))
            body = re.sub(r'\b' + sname + r'\(s\b[^;]*;', lambda m: inner, body, count=1)
    return body

parts = {'looks':'BuildLooks','look':'BuildLook','home':'BuildHome','sound':'BuildSound','controller':'BuildController',
         'power':'BuildPower','apps':'BuildApps','games':'BuildGames','access':'BuildAccess','network':'BuildNetwork',
         'system':'BuildSystem','data':'BuildData','experimental':'BuildExperimental','handheld':'BuildHandheld'}
skip = re.compile(r'Key|Token|Secret|Password|ClientId')
content = {'Avatar','BrowserFavorites','CustomStyles','SavedThemes','ExplorerPins','FtpServers','PinnedFriends','RecentSearches',
           'UnlockedStyles','MusicFolder','BackupFolder','SetupDone','KeyboardChosen','SkinVariants','HiddenPlayers','WidgetInstallTried',
           'SkippedVersion','WhatsNewSeen','HealthDismissed','UnlocksChecked','LabsGraduated72','SteelDefault','SwitchRingDefault',
           'BrowserFavoritesSeeded','PreviousHomeApp','LabValues','RetroDatFolder','RaUser','SoundFolder','LaunchChimeFile','LockScreenSource'}
out = {}
for part, meth in parts.items():
    body = expand(method_body(meth))
    group = ''
    for line in body.split('\n'):
        h = re.search(r'\bAddHeader\("([^"]+)"\)', line)
        if h: group = h.group(1)
        for p in re.findall(r'(?:\bs|Settings|Host\.Settings)\.([A-Z]\w+)\s*(?:=(?!=)|\+=|-=|\.(?:Add|Remove|Clear|Insert)\()', line):
            if p in props and not skip.search(p) and p not in content:
                out.setdefault(part, {}).setdefault(group, set()).add(p)

lines = ['// Generated from SettingsScreens.cs: which settings each part and group of Settings shows (the properties its',
         '// rows read and write). Regenerate after adding settings rows (see SettingsSections.cs).',
         'namespace SquareDeck.Services;', '', 'public static partial class SettingsSections', '{',
         '    private static readonly Dictionary<string, Dictionary<string, string[]>> Map = new()', '    {']
for part in sorted(out):
    lines.append(f'        ["{part}"] = new()')
    lines.append('        {')
    for g in sorted(out[part]):
        names = ', '.join('"%s"' % n for n in sorted(out[part][g]))
        lines.append(f'            ["{g}"] = new[] {{ {names} }},')
    lines.append('        },')
lines += ['    };', '}', '']
open(root + '/Services/SettingsSections.Map.cs', 'w', encoding='utf-8').write('\n'.join(lines))
print(sum(len(v) for p in out.values() for v in p.values()), 'entries;', {p: len(set().union(*out[p].values())) for p in out})
