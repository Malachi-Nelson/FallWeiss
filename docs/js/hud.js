// Fall Weiss web — HUD: objectives, tickets & clock, compass, minimap / full map (hillshaded from the terrain), kill feed,
// friendly markers, suppression and damage vignettes, scope, ammo.
import * as THREE from './three.module.min.js';
import { SIDE, SIDE_SHORT } from './defs.js';

const TEAMCOL = ['#9aa3a8', '#c8a050'];
const V = new THREE.Vector3();
export class Hud {
  constructor(game, root) {
    this.g = game; this.root = root; const el = this.el = document.createElement('div'); el.id = 'hud'; root.appendChild(el);
    el.innerHTML = `
      <canvas id="ov"></canvas>
      <div id="vig"></div><div id="dmg"></div><div id="supp"></div>
      <div id="topbar"><div id="tk0"></div><div id="objs"></div><div id="tk1"></div></div>
      <div id="clock"></div>
      <div id="compass"><div id="cstrip"></div><div id="cmark"></div></div>
      <canvas id="mini" width="180" height="180"></canvas>
      <div id="feed"></div><div id="banner"></div><div id="hint"></div>
      <div id="hm">✕</div>
      <div id="ammo"><div id="wname"></div><div id="rounds"></div><div id="mags"></div><div id="nades"></div></div>
      <div id="status"><div id="hpbar"><div id="hpfill"></div></div><div id="stance"></div><div id="bleed">BLEEDING — bandage (B)</div></div>
      <div id="bigmap"><canvas id="mapc" width="720" height="720"></canvas><div id="maplegend"></div></div>`;
    this.$ = id => el.querySelector('#' + id);
    this.ov = this.$('ov'); this.ctx = this.ov.getContext('2d'); this.mini = this.$('mini').getContext('2d'); this.feedItems = []; this.bannerT = 0; this.hintT = 0; this.dmgV = 0; this.hmT = 0;
    // compass strip
    const cs = this.$('cstrip'); let h = ''; for (let i = -360; i <= 720; i += 15) { const d = ((i % 360) + 360) % 360; const lab = { 0: 'N', 90: 'E', 180: 'S', 270: 'W', 45: 'NE', 135: 'SE', 225: 'SW', 315: 'NW' }[d]; h += `<span style="left:${(i + 360) * 3}px" class="${lab ? 'maj' : ''}">${lab || '·'}</span>`; } cs.innerHTML = h;
    this.resize();
  }
  resize() { const dpr = Math.min(2, devicePixelRatio || 1); this.ov.width = innerWidth * dpr; this.ov.height = innerHeight * dpr; this.ov.style.width = innerWidth + 'px'; this.ov.style.height = innerHeight + 'px'; this.dpr = dpr; }
  show(v) { this.el.style.display = v ? 'block' : 'none'; }
  /** pre-render the battlefield map: hillshade, woods, water, roads, buildings */
  buildMap() {
    const w = this.g.world, M = w.M, S = 720, E = M.half + 40, c = document.createElement('canvas'); c.width = c.height = S; const g = c.getContext('2d'); const im = g.createImageData(S, S), d = im.data;
    const base = new THREE.Color(M.ground); const k = 2 * E / S;
    for (let j = 0; j < S; j++) for (let i = 0; i < S; i++) {
      const x = -E + i * k, z = -E + j * k, h = w.Hraw(x, z), hx = w.Hraw(x + k, z), hz = w.Hraw(x, z + k); const shade = Math.max(.35, Math.min(1.25, 1 + ((h - hx) * .7 + (h - hz) * .5) * 1.2)); const p = (j * S + i) * 4;
      let r = base.r * 255 * 1.6, gg = base.g * 255 * 1.5, b = base.b * 255 * 1.6; if (w.WL != null && h < w.WL) { r = 60; gg = 84; b = 100; }
      d[p] = Math.min(255, r * shade); d[p + 1] = Math.min(255, gg * shade); d[p + 2] = Math.min(255, b * shade); d[p + 3] = 255;
    }
    g.putImageData(im, 0, 0); const X = x => (x + E) / k, Z = z => (z + E) / k;
    g.fillStyle = 'rgba(30,55,25,.55)'; for (const t of w.trees) { g.beginPath(); g.arc(X(t.x), Z(t.z), 2.4, 0, 6.3); g.fill(); }
    g.lineCap = 'round'; for (const r of [...M.roads, ...M.lanes]) { g.strokeStyle = M.city ? 'rgba(90,85,78,.9)' : 'rgba(180,160,120,.9)'; g.lineWidth = Math.max(2, r.w / k); g.beginPath(); g.moveTo(X(r.x1), Z(r.z1)); g.lineTo(X(r.x2), Z(r.z2)); g.stroke(); }
    for (const r of M.rails) { g.strokeStyle = '#3a3430'; g.lineWidth = 2; g.setLineDash([5, 3]); g.beginPath(); g.moveTo(X(r.x1), Z(r.z1)); g.lineTo(X(r.x2), Z(r.z2)); g.stroke(); g.setLineDash([]); }
    g.strokeStyle = 'rgba(60,40,20,.85)'; g.lineWidth = 2; for (const t of M.trenches || []) { g.beginPath(); for (let i = 0; i < t.pts.length; i += 2) (i ? g.lineTo : g.moveTo).call(g, X(t.pts[i]), Z(t.pts[i + 1])); g.stroke(); }
    g.fillStyle = '#4a3c34'; g.strokeStyle = '#1c1612'; g.lineWidth = 1; for (const o of w.obst) if (!o.low && o.kind !== 'wreck') { g.fillRect(X(o.x0), Z(o.z0), (o.x1 - o.x0) / k, (o.z1 - o.z0) / k); g.strokeRect(X(o.x0), Z(o.z0), (o.x1 - o.x0) / k, (o.z1 - o.z0) / k); }
    this.mapImg = c; this.mapE = E; this.mapK = k;
  }
  ammoFlash() { this.ammoT = 1.2; }
  damage(from, p) { this.dmgV = Math.min(1, this.dmgV + .6); if (from) { this.dmgDir = Math.atan2(from.x - p.x, from.z - p.z); this.dmgDirT = 1.5; } }
  hit(kill) { this.hmT = .25; this.$('hm').style.color = kill ? '#ff5040' : '#fff'; }
  banner(t, sub = '', dur = 3.5) { this.$('banner').innerHTML = `<div>${t}</div>${sub ? `<small>${sub}</small>` : ''}`; this.bannerT = dur; }
  hint(t, dur = 3) { this.$('hint').textContent = t; this.hintT = dur; }
  feed(txt, col) { this.feedItems.unshift({ txt, col, t: 6 }); if (this.feedItems.length > 6) this.feedItems.pop(); this.renderFeed(); }
  renderFeed() { this.$('feed').innerHTML = this.feedItems.map(f => `<div style="opacity:${Math.min(1, f.t)};color:${f.col || '#ddd'}">${f.txt}</div>`).join(''); }
  toggleMap(v) { this.mapOpen = v ?? !this.mapOpen; this.$('bigmap').style.display = this.mapOpen ? 'flex' : 'none'; }

  update(dt) {
    const g = this.g, p = g.player, cam = g.camera;
    // objectives
    const ob = g.objectives; this.$('objs').innerHTML = ob.map((o, i) => { const own = o.owner, act = i === g.activeObj, prog = act ? o.prog : 0; const L = String.fromCharCode(65 + i);
      return `<div class="obj ${act ? 'act' : ''}" style="--c:${TEAMCOL[own]};--p:${(prog * 100).toFixed(0)}%;--pc:${TEAMCOL[g.M.attacker]}" title="${o.n}"><span>${L}</span></div>`; }).join('');
    const myT = g.player ? g.player.team : g.side;
    this.$('tk0').innerHTML = `<b style="color:${TEAMCOL[0]}">${SIDE_SHORT[0]}</b> ${Math.max(0, g.tickets[0] | 0)}`; this.$('tk1').innerHTML = `${Math.max(0, g.tickets[1] | 0)} <b style="color:${TEAMCOL[1]}">${SIDE_SHORT[1]}</b>`;
    const tl = Math.max(0, g.timeLeft); this.$('clock').textContent = `${Math.floor(tl / 60)}:${String(Math.floor(tl % 60)).padStart(2, '0')}` + (g.activeObj < ob.length ? ` · ${ob[g.activeObj].n}` : '');
    // compass (north = -z)
    const hdg = ((-g.camYaw * 180 / Math.PI) % 360 + 360) % 360; this.$('cstrip').style.transform = `translateX(${-(hdg + 360) * 3 + 150}px)`;
    // ammo
    if (p && p.alive) { const s = p.slot, w = s.w; this.$('wname').textContent = w.name; const pips = w.clip <= 10 ? '▮'.repeat(s.mag) + '▯'.repeat(w.clip - s.mag) : `<span class="mg">${s.mag > w.clip * .5 ? 'Heavy' : s.mag > w.clip * .15 ? 'Light' : s.mag > 0 ? 'Nearly empty' : 'Empty'}</span>`;
      this.$('rounds').innerHTML = p.reloadT > 0 ? '<span class="mg">Reloading…</span>' : pips; this.$('mags').textContent = (w.clipLoad ? 'Clips ' : w.mg ? 'Belts ' : 'Mags ') + s.mags; this.$('nades').textContent = '🞄'.repeat(p.nades) ? `Grenades ${p.nades}` : '';
      this.$('hpfill').style.width = Math.max(0, p.hp) + '%'; this.$('hpfill').style.background = p.hp > 60 ? '#9bb07a' : p.hp > 30 ? '#c9a24a' : '#b0412e';
      this.$('stance').textContent = ['Standing', 'Crouched', 'Prone'][p.stance] + (p.stamina < .25 ? ' · winded' : ''); this.$('bleed').style.display = p.bleed > 0 && p.bandT <= 0 ? 'block' : 'none'; if (p.bandT > 0) { this.$('bleed').style.display = 'block'; this.$('bleed').textContent = 'Bandaging…'; } else this.$('bleed').textContent = g.touch ? 'BLEEDING — tap BANDAGE' : 'BLEEDING — bandage (B)';
      g.touchUI?.setBandage(p.bleed > 0 && p.bandT <= 0); g.touchUI?.setAds(p.ads); g.touchUI?.setStance(p.stance);
    }
    // vignettes
    this.dmgV = Math.max(0, this.dmgV - dt * .8); const lowHp = p && p.alive ? Math.max(0, (50 - p.hp) / 50) : 0;
    this.$('dmg').style.opacity = Math.min(1, this.dmgV + lowHp * .5); this.$('supp').style.opacity = p ? Math.min(.85, p.supp * .9) : 0;
    this.hmT -= dt; this.$('hm').style.opacity = this.hmT > 0 ? 1 : 0;
    this.bannerT -= dt; this.$('banner').style.opacity = Math.max(0, Math.min(1, this.bannerT)); this.hintT -= dt; this.$('hint').style.opacity = Math.max(0, Math.min(1, this.hintT));
    let ch = false; for (const f of this.feedItems) { const o = f.t; f.t -= dt; if ((o > 1) !== (f.t > 1) || f.t < 1) ch = true; } this.feedItems = this.feedItems.filter(f => f.t > 0); if (ch) this.renderFeed();
    this.drawOverlay(dt); this.miniT = (this.miniT || 0) - dt; if (this.miniT <= 0) { this.miniT = .1; this.drawMini(this.mini, 180, 70, true); } if (this.mapOpen) { this.mapT = (this.mapT || 0) - dt; if (this.mapT <= 0) { this.mapT = .2; this.drawBig(); } }
  }
  drawOverlay(dt) {
    const g = this.g, c = this.ctx, W = this.ov.width, H = this.ov.height, d = this.dpr, p = g.player, cam = g.camera; c.clearRect(0, 0, W, H);
    if (!p || !p.alive) return;
    // scope (ZF 39 / wz.29 sniper): black surround + post reticle
    if (p.w.scope && p.adsV > .85) { const r = Math.min(W, H) * .46; c.fillStyle = '#000'; c.beginPath(); c.rect(0, 0, W, H); c.arc(W / 2, H / 2, r, 0, 6.3, true); c.fill();
      c.strokeStyle = '#000'; c.lineWidth = 3 * d; c.beginPath(); c.moveTo(W / 2 - r, H / 2); c.lineTo(W / 2 - r * .08, H / 2); c.moveTo(W / 2 + r * .08, H / 2); c.lineTo(W / 2 + r, H / 2); c.moveTo(W / 2, H / 2 + r); c.lineTo(W / 2, H / 2 + 2 * d); c.stroke(); c.lineWidth = 1.2 * d; c.beginPath(); c.moveTo(W / 2, H / 2); c.lineTo(W / 2, H / 2 + 6 * d); c.stroke();
      const gr = c.createRadialGradient(W / 2, H / 2, r * .7, W / 2, H / 2, r); gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(0,0,0,.8)'); c.fillStyle = gr; c.beginPath(); c.arc(W / 2, H / 2, r, 0, 6.3); c.fill(); }
    // friendly markers within 60 m (RO2 nametags), objective marker
    c.font = `${11 * d}px system-ui`; c.textAlign = 'center';
    for (const u of g.units) { if (!u.alive || u.team !== p.team || u.kind === 'gun') continue; const dd = Math.hypot(u.x - p.x, u.z - p.z); if (dd > 70 || dd < 2) continue; V.set(u.x, u.y + (u.kind === 'tank' ? 3.4 : u.stance === 2 ? .9 : u.stance === 1 ? 1.6 : 2.15), u.z).project(cam); if (V.z > 1) continue;
      const x = (V.x + 1) / 2 * W, y = (1 - V.y) / 2 * H; c.globalAlpha = Math.min(1, (70 - dd) / 20) * .85; c.fillStyle = u.squad === g.playerSquad ? '#9fe08a' : '#7fb0ff'; c.beginPath(); c.moveTo(x - 5 * d, y - 6 * d); c.lineTo(x + 5 * d, y - 6 * d); c.lineTo(x, y); c.fill();
      if (dd < 12 && Math.abs(x - W / 2) < W * .12 && Math.abs(y - H / 2) < H * .2) c.fillText(u.name, x, y - 9 * d); }
    c.globalAlpha = 1;
    const o = g.objectives[g.activeObj]; if (o) { V.set(o.x, g.world.H(o.x, o.z) + 6, o.z).project(cam); if (V.z < 1) { const x = (V.x + 1) / 2 * W, y = (1 - V.y) / 2 * H; const dd = Math.hypot(o.x - p.x, o.z - p.z); c.fillStyle = 'rgba(255,230,160,.85)'; c.font = `bold ${13 * d}px system-ui`; c.fillText(String.fromCharCode(65 + g.activeObj) + `  ${dd | 0} m`, x, y); } }
    // damage direction
    if (this.dmgDirT > 0) { this.dmgDirT -= dt; const a = this.dmgDir - g.camYaw - Math.PI; c.save(); c.translate(W / 2, H / 2); c.rotate(-a); c.fillStyle = `rgba(200,30,20,${Math.min(.8, this.dmgDirT)})`; c.beginPath(); c.moveTo(-40 * d, -120 * d); c.lineTo(40 * d, -120 * d); c.lineTo(0, -150 * d); c.fill(); c.restore(); }
  }
  drawMini(c, S, range, rotate) {
    const g = this.g, w = g.world, p = g.player || { x: g.camera.position.x, z: g.camera.position.z }; c.clearRect(0, 0, S, S); if (!this.mapImg) return;
    c.save(); c.beginPath(); c.arc(S / 2, S / 2, S / 2 - 2, 0, 6.3); c.clip(); c.fillStyle = '#222'; c.fillRect(0, 0, S, S);
    c.translate(S / 2, S / 2); if (rotate) c.rotate(g.camYaw); const sc = S / (2 * range); c.scale(sc, sc); c.translate(-p.x, -p.z);
    const E = this.mapE, k = this.mapK; c.drawImage(this.mapImg, -E, -E, 2 * E, 2 * E);
    this.drawMarks(c, 1 / sc); c.restore();
    c.strokeStyle = 'rgba(220,210,180,.6)'; c.lineWidth = 2; c.beginPath(); c.arc(S / 2, S / 2, S / 2 - 2, 0, 6.3); c.stroke();
    c.fillStyle = '#fff'; c.beginPath(); c.moveTo(S / 2, S / 2 - 7); c.lineTo(S / 2 - 5, S / 2 + 5); c.lineTo(S / 2 + 5, S / 2 + 5); c.fill();
  }
  drawMarks(c, px) {
    const g = this.g, myT = g.player ? g.player.team : g.side;
    g.objectives.forEach((o, i) => { c.strokeStyle = TEAMCOL[o.owner]; c.fillStyle = TEAMCOL[o.owner] + '44'; c.lineWidth = 2 * px * (i === g.activeObj ? 2 : 1); c.beginPath(); c.arc(o.x, o.z, o.r, 0, 6.3); c.fill(); c.stroke(); c.fillStyle = '#fff'; c.font = `bold ${14 * px}px system-ui`; c.textAlign = 'center'; c.fillText(String.fromCharCode(65 + i), o.x, o.z + 5 * px); });
    for (const u of g.units) { if (!u.alive) continue; const mine = u.team === myT; if (!mine && !(u.spottedT > g.time)) continue; c.fillStyle = mine ? (u.squad === g.playerSquad ? '#9fe08a' : '#7fb0ff') : '#ff5a40'; const r = (u.kind === 'tank' ? 5 : u.kind === 'gun' ? 4 : 2.6) * px; if (u.kind === 'tank' || u.kind === 'gun') c.fillRect(u.x - r, u.z - r, 2 * r, 2 * r); else { c.beginPath(); c.arc(u.x, u.z, r, 0, 6.3); c.fill(); } }
    for (const s of g.markers) { c.strokeStyle = s.col; c.lineWidth = 2 * px; c.beginPath(); c.arc(s.x, s.z, s.r, 0, 6.3); c.stroke(); }
  }
  drawBig() {
    const g = this.g, c = this.$('mapc').getContext('2d'), S = 720, E = this.mapE; c.clearRect(0, 0, S, S); c.drawImage(this.mapImg, 0, 0, S, S);
    c.save(); c.scale(S / (2 * E), S / (2 * E)); c.translate(E, E); const px = 2 * E / S; this.drawMarks(c, px * 1.3);
    if (g.player && g.player.alive) { c.fillStyle = '#fff'; c.save(); c.translate(g.player.x, g.player.z); c.rotate(-g.player.yaw); c.beginPath(); c.moveTo(0, -12 * px); c.lineTo(-7 * px, 7 * px); c.lineTo(7 * px, 7 * px); c.fill(); c.restore(); }
    for (const s of g.M.spawn) { c.strokeStyle = TEAMCOL[g.M.spawn.indexOf(s)]; c.lineWidth = 3 * px; c.strokeRect(s.x - 15, s.z - 15, 30, 30); }
    c.restore();
    this.$('maplegend').innerHTML = `<b>${g.M.name}</b> — ${g.M.date}<br>${g.objectives.map((o, i) => `<span style="color:${TEAMCOL[o.owner]}">${String.fromCharCode(65 + i)}: ${o.n}</span>`).join('<br>')}<br><br><small>Tap MAP / press M to close</small>`;
  }
}
