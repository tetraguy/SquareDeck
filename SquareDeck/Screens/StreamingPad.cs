namespace SquareDeck.Screens;

/// <summary>
/// The controller inside a streaming service's page. The page reads the controller itself (the browser's Gamepad
/// API, the same way cloud games get it), so it keeps working while the page has the focus. The d-pad or left stick
/// moves a highlight between links, buttons and tiles. A chooses, B goes back (then out to SquareDeck), X plays or
/// pauses, and LB / RB skip 10 seconds. While a video is playing full screen, the d-pad seeks and changes the volume.
/// LT / RT scroll, Start goes full screen, the right stick moves a pointer (A or R3 clicks), Y opens the menu and View
/// opens quick settings. It also reports what's playing (for the pill on the home screen), and says it's alive, so
/// SquareDeck's own controller handling takes over again if the page ever stops hearing the controller.
/// </summary>
public static class StreamingPad
{
    public static string For(string accentHex) => Script.Replace("__ACCENT__", accentHex);

    private const string Script = """
(function () {
  if (window.top !== window || window.__sdpad) return;
  var ACCENT = '__ACCENT__';
  var pad = window.__sdpad = { last: {}, cur: null, repeatAt: {}, cursor: null, cx: -1, cy: -1, cursorAt: 0, alive: 0 };
  function post(m) { try { window.chrome.webview.postMessage(m); } catch (e) { } }

  // ---- what can be chosen
  var SEL = 'a[href],button,input:not([type=hidden]),select,textarea,[role=button],[role=link],[role=tab],[role=menuitem],[role=option],[role=checkbox],[role=switch],[role=radio],[tabindex]:not([tabindex="-1"]),[onclick],[data-uia*=button],[data-uia*=tile],summary';
  function visible(el, r) {
    if (r.width < 6 || r.height < 6) return false;
    var s = getComputedStyle(el);
    if (s.visibility === 'hidden' || s.display === 'none' || +s.opacity < 0.05 || s.pointerEvents === 'none') return false;
    var x = Math.min(Math.max(r.left + r.width / 2, 0), innerWidth - 1), y = Math.min(Math.max(r.top + r.height / 2, 0), innerHeight - 1);
    if (r.left + r.width / 2 >= 0 && r.left + r.width / 2 < innerWidth && r.top + r.height / 2 >= 0 && r.top + r.height / 2 < innerHeight) {
      var hit = document.elementFromPoint(x, y);
      if (hit && hit !== el && !el.contains(hit) && !hit.contains(el)) {
        // Covered by something else (a dialog, a menu): only the cover counts.
        var cover = hit.closest(SEL);
        if (cover && cover !== el && !el.contains(cover)) return false;
      }
    }
    return true;
  }
  function candidates() {
    var list = [], seen = new Set();
    document.querySelectorAll(SEL).forEach(function (el) {
      // A clickable inside a clickable: keep the outer one.
      var outer = el.parentElement && el.parentElement.closest(SEL);
      if (outer && outer.getBoundingClientRect().width < innerWidth * 0.9) el = outer;
      if (seen.has(el)) return; seen.add(el);
      var r = el.getBoundingClientRect();
      if (r.right < -innerWidth || r.left > innerWidth * 2 || r.bottom < -innerHeight || r.top > innerHeight * 2) return;
      if (visible(el, r)) list.push({ el: el, r: r });
    });
    return list;
  }

  // ---- the highlight
  var ring = document.createElement('div');
  ring.style.cssText = 'position:fixed;z-index:2147483646;pointer-events:none;border-radius:10px;border:4px solid ' + ACCENT +
    ';box-shadow:0 0 0 3px rgba(0,0,0,.55),0 0 24px ' + ACCENT + ';transition:all .14s ease-out;display:none;';
  var toastBox = document.createElement('div');
  toastBox.style.cssText = 'position:fixed;z-index:2147483647;left:50%;bottom:6%;transform:translateX(-50%);padding:12px 22px;border-radius:24px;' +
    'background:rgba(10,12,18,.85);color:#fff;font:600 18px Segoe UI,sans-serif;pointer-events:none;opacity:0;transition:opacity .2s;white-space:nowrap;';
  var dot = document.createElement('div');
  dot.style.cssText = 'position:fixed;z-index:2147483647;width:22px;height:22px;margin:-11px 0 0 -11px;border-radius:50%;border:3px solid #fff;background:' + ACCENT +
    ';box-shadow:0 0 10px rgba(0,0,0,.6);pointer-events:none;display:none;';
  function attach() {
    if (!document.body) return;
    if (!ring.isConnected) document.documentElement.appendChild(ring);
    if (!toastBox.isConnected) document.documentElement.appendChild(toastBox);
    if (!dot.isConnected) document.documentElement.appendChild(dot);
  }
  var toastTimer = 0;
  pad.toast = function (text) {
    attach(); toastBox.textContent = text; toastBox.style.opacity = '1';
    clearTimeout(toastTimer); toastTimer = setTimeout(function () { toastBox.style.opacity = '0'; }, 1800);
  };
  function show(el) {
    pad.cur = el;
    if (!el) { ring.style.display = 'none'; return; }
    attach();
    var r = el.getBoundingClientRect();
    ring.style.display = 'block';
    ring.style.left = (r.left - 6) + 'px'; ring.style.top = (r.top - 6) + 'px';
    ring.style.width = (r.width + 4) + 'px'; ring.style.height = (r.height + 4) + 'px';
  }
  function keepRing() { if (pad.cur && pad.cur.isConnected && !pad.cursor) show(pad.cur); else if (pad.cur && !pad.cur.isConnected) show(null); }

  // ---- moving
  function move(dir) {
    var list = candidates();
    if (!list.length) { scrollBy(dir); return; }
    var from;
    if (pad.cur && pad.cur.isConnected) from = pad.cur.getBoundingClientRect();
    else {
      // Nothing chosen yet: the first thing near the top left that's on screen.
      var onScreen = list.filter(function (c) { return c.r.top >= 0 && c.r.bottom <= innerHeight && c.r.left >= 0 && c.r.right <= innerWidth; });
      var start = (onScreen.length ? onScreen : list).sort(function (a, b) { return (a.r.top - b.r.top) * 2 + (a.r.left - b.r.left); })[0];
      focusOn(start.el); return;
    }
    var fx = from.left + from.width / 2, fy = from.top + from.height / 2, best = null, bestScore = 1e9;
    list.forEach(function (c) {
      if (c.el === pad.cur || c.el.contains(pad.cur) || pad.cur.contains(c.el)) return;
      var r = c.r, x = r.left + r.width / 2, y = r.top + r.height / 2, along, across, gap;
      if (dir === 'right') { along = x - fx; across = Math.abs(y - fy); gap = r.left - from.right; }
      else if (dir === 'left') { along = fx - x; across = Math.abs(y - fy); gap = from.left - r.right; }
      else if (dir === 'down') { along = y - fy; across = Math.abs(x - fx); gap = r.top - from.bottom; }
      else { along = fy - y; across = Math.abs(x - fx); gap = from.top - r.bottom; }
      if (along <= 4 || gap < -Math.min(from.width, from.height) * 0.5) return;
      // Same row / column first: things straight ahead beat things off to the side.
      var overlap = (dir === 'left' || dir === 'right') ? Math.min(r.bottom, from.bottom) - Math.max(r.top, from.top) : Math.min(r.right, from.right) - Math.max(r.left, from.left);
      var score = Math.max(gap, 0) + across * (overlap > 0 ? 0.6 : 3) + along * 0.2;
      if (score < bestScore) { bestScore = score; best = c.el; }
    });
    if (best) focusOn(best);
    else {
      // Nothing that way: a row's "next" arrow, else scroll the page.
      if (dir === 'right' || dir === 'left') {
        var arrow = Array.prototype.find.call(document.querySelectorAll('[aria-label],[class*=handle],[class*=arrow],[class*=Arrow]'), function (a) {
          var l = ((a.getAttribute('aria-label') || '') + ' ' + a.className).toLowerCase();
          var r = a.getBoundingClientRect();
          return r.width > 0 && Math.abs((r.top + r.height / 2) - fy) < from.height && (dir === 'right' ? /next|right|forward|more/.test(l) : /prev|left|back/.test(l));
        });
        if (arrow) { click(arrow); setTimeout(function () { move(dir); }, 450); return; }
      }
      scrollBy(dir);
    }
  }
  function scrollBy(dir) {
    var dy = dir === 'down' ? innerHeight * 0.6 : dir === 'up' ? -innerHeight * 0.6 : 0;
    var dx = dir === 'right' ? innerWidth * 0.5 : dir === 'left' ? -innerWidth * 0.5 : 0;
    window.scrollBy({ left: dx, top: dy, behavior: 'smooth' });
  }
  function focusOn(el) {
    try { el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' }); } catch (e) { }
    try { el.focus({ preventScroll: true }); } catch (e) { }
    // Hover too: many tiles open their preview on hover.
    var r = el.getBoundingClientRect();
    ['pointerover', 'mouseover', 'pointerenter', 'mouseenter'].forEach(function (t) {
      el.dispatchEvent(new MouseEvent(t, { bubbles: t.indexOf('enter') < 0, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
    });
    show(el);
    setTimeout(keepRing, 300);
  }
  function click(el) {
    var r = el.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2, o = { bubbles: true, cancelable: true, clientX: x, clientY: y, view: window, button: 0 };
    try { el.focus({ preventScroll: true }); } catch (e) { }
    if (el.matches('input,textarea,select')) { el.focus(); return; }
    el.dispatchEvent(new PointerEvent('pointerdown', o)); el.dispatchEvent(new MouseEvent('mousedown', o));
    el.dispatchEvent(new PointerEvent('pointerup', o)); el.dispatchEvent(new MouseEvent('mouseup', o));
    el.click();
  }

  // ---- the video
  function video() {
    var vs = Array.prototype.slice.call(document.querySelectorAll('video')).filter(function (v) { var r = v.getBoundingClientRect(); return r.width > 200 && r.height > 100; });
    return vs.filter(function (v) { return !v.paused; })[0] || vs.sort(function (a, b) { return b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight; })[0] || null;
  }
  function watching() {
    var v = video(); if (!v) return null;
    var r = v.getBoundingClientRect();
    // Only on the player itself, not a big preview on a browse page.
    var player = document.fullscreenElement || /\/(watch|video|play|player)(\/|$)/i.test(location.pathname);
    return player && r.width * r.height > innerWidth * innerHeight * 0.55 ? v : null;
  }
  function seek(v, by) { try { v.currentTime = Math.max(0, Math.min(v.duration || 1e9, v.currentTime + by)); pad.toast((by > 0 ? '+' : '') + by + ' s'); } catch (e) { } }
  function playPause() {
    var v = video(); if (!v) return false;
    if (v.paused) { v.play(); pad.toast('▶  Playing'); } else { v.pause(); pad.toast('❚❚  Paused'); }
    return true;
  }
  function back() {
    if (document.fullscreenElement) { document.exitFullscreen(); return; }
    var esc = new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true });
    (document.activeElement || document.body).dispatchEvent(esc);
    if (history.length > 1 && location.href !== pad.home) history.back(); else post('sd:leave');
  }
  pad.home = location.href;

  // ---- buttons (standard mapping)
  function pressed(gp, i) { var b = gp.buttons[i]; return !!b && (b.pressed || b.value > 0.5); }
  function edge(name, now) {
    var t = performance.now(), was = pad.last[name];
    pad.last[name] = now;
    if (!now) { pad.repeatAt[name] = 0; return false; }
    if (!was) { pad.repeatAt[name] = t + 380; return true; }
    if (/^(up|down|left|right)$/.test(name) && pad.repeatAt[name] && t >= pad.repeatAt[name]) { pad.repeatAt[name] = t + 110; return true; }
    return false;
  }
  function frame() {
    var gps = navigator.getGamepads ? navigator.getGamepads() : [];
    var gp = null;
    for (var i = 0; i < gps.length; i++) if (gps[i] && gps[i].connected) { gp = gps[i]; break; }
    if (gp && document.hasFocus()) {
      var t = performance.now();
      if (t - pad.alive > 1000) { pad.alive = t; post('sd:alive'); }
      var ax = gp.axes[0] || 0, ay = gp.axes[1] || 0, rx = gp.axes[2] || 0, ry = gp.axes[3] || 0;
      var up = pressed(gp, 12) || ay < -0.6, down = pressed(gp, 13) || ay > 0.6, left = pressed(gp, 14) || ax < -0.6, right = pressed(gp, 15) || ax > 0.6;
      var v = watching();
      // The right stick: a pointer that clicks with A (or R3).
      if (Math.abs(rx) > 0.15 || Math.abs(ry) > 0.15) {
        if (pad.cx < 0) { pad.cx = innerWidth / 2; pad.cy = innerHeight / 2; }
        pad.cx = Math.max(0, Math.min(innerWidth - 1, pad.cx + rx * Math.abs(rx) * 18));
        pad.cy = Math.max(0, Math.min(innerHeight - 1, pad.cy + ry * Math.abs(ry) * 18));
        attach(); dot.style.display = 'block'; dot.style.left = pad.cx + 'px'; dot.style.top = pad.cy + 'px';
        pad.cursor = true; pad.cursorAt = t; ring.style.display = 'none';
        var under = document.elementFromPoint(pad.cx, pad.cy);
        if (under) under.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: pad.cx, clientY: pad.cy }));
      } else if (pad.cursor && t - pad.cursorAt > 2500) { pad.cursor = false; dot.style.display = 'none'; keepRing(); }

      if (edge('up', up)) { if (v && !pad.cursor) { v.volume = Math.min(1, v.volume + 0.1); pad.toast('Volume ' + Math.round(v.volume * 100) + '%'); } else move('up'); }
      if (edge('down', down)) { if (v && !pad.cursor) { v.volume = Math.max(0, v.volume - 0.1); pad.toast('Volume ' + Math.round(v.volume * 100) + '%'); } else move('down'); }
      if (edge('left', left)) { if (v && !pad.cursor) seek(v, -10); else move('left'); }
      if (edge('right', right)) { if (v && !pad.cursor) seek(v, 10); else move('right'); }
      var aDown = edge('a', pressed(gp, 0)), r3Down = edge('r3', pressed(gp, 11));
      if (aDown || r3Down) {
        if (pad.cursor) { var el = document.elementFromPoint(pad.cx, pad.cy); if (el) click(el); }
        else if (v) playPause();
        else if (pad.cur && pad.cur.isConnected) click(pad.cur);
        else move('down');
      }
      if (edge('b', pressed(gp, 1))) back();
      if (edge('x', pressed(gp, 2))) { if (!playPause()) pad.toast('Nothing playing'); }
      if (edge('y', pressed(gp, 3))) post('sd:menu');
      if (edge('lb', pressed(gp, 4))) { var v1 = video(); if (v1) seek(v1, -10); }
      if (edge('rb', pressed(gp, 5))) { var v2 = video(); if (v2) seek(v2, 10); }
      if (edge('lt', pressed(gp, 6))) window.scrollBy({ top: -innerHeight * 0.8, behavior: 'smooth' });
      if (edge('rt', pressed(gp, 7))) window.scrollBy({ top: innerHeight * 0.8, behavior: 'smooth' });
      if (edge('view', pressed(gp, 8))) post('sd:quick');
      if (edge('start', pressed(gp, 9))) {
        var v3 = video();
        if (document.fullscreenElement) document.exitFullscreen(); else if (v3 && v3.requestFullscreen) v3.requestFullscreen();
      }
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  addEventListener('scroll', function () { if (pad.cur) setTimeout(keepRing, 60); }, true);
  addEventListener('resize', keepRing);
  // The mouse or a finger: the ring steps aside.
  addEventListener('pointerdown', function (e) { if (e.isTrusted && e.pointerType !== '') { show(null); } }, true);

  // ---- what's playing, for the pill on the home screen
  setInterval(function () {
    var v = video(), md = navigator.mediaSession && navigator.mediaSession.metadata;
    var info = {
      title: (md && md.title) || '', artist: (md && (md.artist || md.album)) || '',
      page: document.title || '', playing: !!(v && !v.paused && !v.ended), hasVideo: !!v,
      position: v ? Math.floor(v.currentTime || 0) : 0, duration: v && isFinite(v.duration) ? Math.floor(v.duration) : 0
    };
    post('sd:media|' + JSON.stringify(info));
  }, 3000);
})();

""";
}
