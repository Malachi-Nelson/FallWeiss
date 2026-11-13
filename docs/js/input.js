// Fall Weiss web — phone (twin-stick touch) and desktop (keyboard + pointer-lock mouse) controls in one state object.
export const I = { mx: 0, my: 0, lookX: 0, lookY: 0, fire: false, firePressed: false, adsTap: false, adsHold: null, sprint: false, crouchTap: false, proneTap: false, reloadTap: false, nadeTap: false, switchTap: false, bandageTap: false, mapTap: false, mapHeld: false, pauseTap: false, any: false };
const keys = new Set();
let touchUI = null, enabled = false, onFirstGesture = null;

export function setEnabled(v) { enabled = v; if (touchUI) touchUI.style.display = v && isTouch() ? 'block' : 'none'; if (!v) { I.fire = false; I.mx = I.my = 0; } }
export function isTouch() { return matchMedia('(pointer: coarse)').matches || 'ontouchstart' in window; }
export function onGesture(fn) { onFirstGesture = fn; }

export function initInput(canvas, root) {
  const gest = () => { if (onFirstGesture) onFirstGesture(); };
  window.addEventListener('pointerdown', gest, { capture: true }); window.addEventListener('keydown', gest, { capture: true });
  // ---------- keyboard / mouse
  const kmap = () => { I.mx = (keys.has('KeyD') ? 1 : 0) - (keys.has('KeyA') ? 1 : 0); I.my = (keys.has('KeyW') ? 1 : 0) - (keys.has('KeyS') ? 1 : 0); I.sprint = keys.has('ShiftLeft') || keys.has('ShiftRight'); };
  window.addEventListener('keydown', e => {
    if (e.repeat) return; keys.add(e.code); if (!enabled) { if (e.code === 'Escape') I.pauseTap = true; return; }
    switch (e.code) { case 'KeyC': case 'ControlLeft': I.crouchTap = true; break; case 'KeyZ': I.proneTap = true; break; case 'KeyR': I.reloadTap = true; break; case 'KeyG': I.nadeTap = true; break;
      case 'Digit1': case 'Digit2': case 'KeyQ': I.switchTap = true; break; case 'KeyB': I.bandageTap = true; break; case 'KeyM': case 'Tab': I.mapTap = true; e.preventDefault(); break; case 'Escape': case 'KeyP': I.pauseTap = true; break; }
    kmap();
  });
  window.addEventListener('keyup', e => { keys.delete(e.code); kmap(); });
  canvas.addEventListener('mousedown', e => {
    if (isTouchEvent) return; if (!enabled) return;
    if (document.pointerLockElement !== canvas) { canvas.requestPointerLock?.(); return; }
    if (e.button === 0) { I.fire = true; I.firePressed = true; } if (e.button === 2) I.adsHold = true;
  });
  window.addEventListener('mouseup', e => { if (e.button === 0) I.fire = false; if (e.button === 2) I.adsHold = false; });
  canvas.addEventListener('contextmenu', e => e.preventDefault());
  window.addEventListener('mousemove', e => { if (document.pointerLockElement === canvas && enabled) { I.lookX += e.movementX; I.lookY += e.movementY; } });
  document.addEventListener('pointerlockchange', () => { if (!document.pointerLockElement && enabled && !isTouch()) I.pauseTap = true; });

  // ---------- touch: floating move stick on the left, drag-to-look anywhere on the right, buttons on the right edge
  let isTouchEvent = false;
  touchUI = document.createElement('div'); touchUI.id = 'touch'; root.appendChild(touchUI);
  touchUI.innerHTML = `
  <div id="tz-move"></div><div id="tz-look"></div>
  <div id="stick"><div id="knob"></div></div>
  <button class="tb" id="b-fire" style="right:3%;bottom:20%;width:84px;height:84px">FIRE</button>
  <button class="tb" id="b-fire2" style="left:3%;top:46%;width:58px;height:58px;opacity:.7">FIRE</button>
  <button class="tb" id="b-ads" style="right:calc(3% + 96px);bottom:30%">AIM</button>
  <button class="tb" id="b-reload" style="right:3%;bottom:calc(20% + 96px)">R</button>
  <button class="tb" id="b-crouch" style="right:calc(3% + 70px);bottom:6%">⤓</button>
  <button class="tb" id="b-prone" style="right:calc(3% + 140px);bottom:6%">▁</button>
  <button class="tb" id="b-nade" style="right:calc(3% + 96px);bottom:calc(30% + 70px)">💣</button>
  <button class="tb" id="b-switch" style="right:3%;bottom:6%">⇄</button>
  <button class="tb" id="b-band" style="left:calc(50% - 30px);bottom:22%;display:none;background:rgba(150,30,20,.55)">BANDAGE</button>
  <button class="tb small" id="b-map" style="left:12px;top:12px">MAP</button>
  <button class="tb small" id="b-pause" style="right:12px;top:12px">❚❚</button>`;
  const stick = touchUI.querySelector('#stick'), knob = touchUI.querySelector('#knob');
  let moveId = null, mox = 0, moy = 0; const looks = new Map(); const R = 56;
  const tz = touchUI.querySelector('#tz-move'), tl = touchUI.querySelector('#tz-look');
  tz.addEventListener('touchstart', e => { isTouchEvent = true; e.preventDefault(); for (const t of e.changedTouches) if (moveId === null) { moveId = t.identifier; mox = t.clientX; moy = t.clientY; stick.style.display = 'block'; stick.style.left = (mox - R) + 'px'; stick.style.top = (moy - R) + 'px'; knob.style.transform = 'translate(0px,0px)'; } }, { passive: false });
  const lookStart = (e, el) => { isTouchEvent = true; e.preventDefault(); for (const t of e.changedTouches) looks.set(t.identifier, { x: t.clientX, y: t.clientY, el }); };
  tl.addEventListener('touchstart', e => lookStart(e, 'look'), { passive: false });
  window.addEventListener('touchmove', e => {
    if (!enabled) return;
    for (const t of e.changedTouches) {
      if (t.identifier === moveId) { let dx = t.clientX - mox, dy = t.clientY - moy; const l = Math.hypot(dx, dy); if (l > R) { dx *= R / l; dy *= R / l; } knob.style.transform = `translate(${dx}px,${dy}px)`; I.mx = dx / R; I.my = -dy / R; if (l > R * 1.25) { /* drag the stick base along: comfortable sprint */ mox += (t.clientX - mox) * (1 - R * 1.25 / l); moy += (t.clientY - moy) * (1 - R * 1.25 / l); stick.style.left = (mox - R) + 'px'; stick.style.top = (moy - R) + 'px'; } }
      const lk = looks.get(t.identifier); if (lk) { I.lookX += (t.clientX - lk.x) * 1.15; I.lookY += (t.clientY - lk.y) * 1.15; lk.x = t.clientX; lk.y = t.clientY; }
    }
  }, { passive: false });
  const end = e => { for (const t of e.changedTouches) { if (t.identifier === moveId) { moveId = null; I.mx = I.my = 0; stick.style.display = 'none'; } const lk = looks.get(t.identifier); if (lk) { if (lk.el === 'fire') I.fire = false; looks.delete(t.identifier); } } };
  window.addEventListener('touchend', end); window.addEventListener('touchcancel', end);
  // buttons: fire buttons also steer the aim while held (thumb stays on FIRE and drags)
  const btn = (id, down, up, look) => { const b = touchUI.querySelector(id); b.addEventListener('touchstart', e => { isTouchEvent = true; e.preventDefault(); e.stopPropagation(); b.classList.add('on'); down(); if (look) for (const t of e.changedTouches) looks.set(t.identifier, { x: t.clientX, y: t.clientY, el: 'fire' }); }, { passive: false });
    b.addEventListener('touchend', e => { e.preventDefault(); b.classList.remove('on'); if (up) up(); }, { passive: false }); return b; };
  btn('#b-fire', () => { I.fire = true; I.firePressed = true; }, () => { I.fire = false; }, true);
  btn('#b-fire2', () => { I.fire = true; I.firePressed = true; }, () => { I.fire = false; }, true);
  const adsB = btn('#b-ads', () => { I.adsTap = true; });
  btn('#b-reload', () => { I.reloadTap = true; }); btn('#b-crouch', () => { I.crouchTap = true; }); btn('#b-prone', () => { I.proneTap = true; });
  btn('#b-nade', () => { I.nadeTap = true; }); btn('#b-switch', () => { I.switchTap = true; }); btn('#b-band', () => { I.bandageTap = true; });
  btn('#b-map', () => { I.mapTap = true; }); btn('#b-pause', () => { I.pauseTap = true; });
  touchUI.style.display = 'none';
  return { setBandage(v) { touchUI.querySelector('#b-band').style.display = v ? 'block' : 'none'; }, setAds(v) { adsB.classList.toggle('lit', v); }, setStance(s) { touchUI.querySelector('#b-crouch').classList.toggle('lit', s === 1); touchUI.querySelector('#b-prone').classList.toggle('lit', s === 2); } };
}
