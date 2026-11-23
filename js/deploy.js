// Fall Weiss web — the deployment phase. Before the battle each side places its allotted troops on the map, and only what
// is placed fights. The computer places its side at the historical positions; you place yours — unit by unit, by dragging,
// or with Auto-deploy. Attackers deploy around their assembly area; defenders anywhere from the first position back.
// Defenders you place hold that ground until the position they guard falls, then fall back like the rest.
import { SIDE, NAMES, TANK, clamp, Rng } from './defs.js';

const LABEL = { rifle: 'Rifle section', mg: 'MG team', sniper: 'Marksman', at: 'AT gun', tank: 'Tank', cav: 'Cavalry troop', crew: 'Crew' };
const GLYPH = { rifle: 'R', mg: 'MG', sniper: 'S', at: 'AT', tank: 'T', cav: 'C', crew: 'R' };
const SPREAD = { rifle: 9, mg: 5, sniper: 2, at: 2, tank: 12, cav: 9 };
const TEAMCOL = ['#aab4ba', '#d4ac58'];

/** the troops a side brings: every force entry split into groups you can place (sections of up to ten riflemen, MG
 *  pairs, single guns, single tanks, single marksmen, troops of up to five riders), scaled by the battle size */
export function allotment(game, team) {
  const M = game.M, S = game.settings.size, list = (team === 0 ? M.forces0 : M.forces1) || [], out = []; let id = 0;
  for (const f of list) {
    const n = Math.max(1, Math.round(f.n * (f.type === 'tank' || f.type === 'at' ? Math.max(.5, S) : S)));
    const per = f.type === 'rifle' ? 10 : f.type === 'mg' ? 2 : f.type === 'cav' ? 5 : 1, k = Math.ceil(n / per);
    for (let i = 0; i < k; i++) { const m = Math.round(n * (i + 1) / k) - Math.round(n * i / k); if (m > 0) out.push({ id: team * 1000 + id++, team, type: f.type, n: m, loc: f.loc, sp: f.sp || 20, x: 0, z: 0, placed: false, hold: false }); }
  }
  return out;
}
export function groupLabel(g) {
  if (g.type === 'tank') return TANK[g.team].name;
  if (g.type === 'at') return NAMES[g.team].at + ' + crew';
  if (g.type === 'cav') return `${NAMES[g.team].cav} · ${g.n} riders`;
  if (g.type === 'sniper') return LABEL.sniper;
  return `${LABEL[g.type] || g.type} · ${g.n} ${g.n === 1 ? 'man' : 'men'}`;
}
/** deployment zones: attackers round their assembly area, defenders from just in front of position A back */
export function zoneOf(game) {
  const M = game.M, att = M.attacker, s = M.spawn[att], A = game.objectives[0] || { x: 0, z: 0, r: 20 };
  const dA = Math.hypot(A.x - s.x, A.z - s.z); return { att, s, attR: clamp(dA * .5, 70, 200), defD: dA - A.r - 25, half: M.half };
}
export function inZone(game, Z, team, x, z) {
  if (Math.abs(x) > Z.half - 6 || Math.abs(z) > Z.half - 6) return false;
  const d = Math.hypot(x - Z.s.x, z - Z.s.z); return team === Z.att ? d < Z.attR : d > Z.defD;
}
export function placeable(game, x, z) { return !game.world.blocked(x, z, 1.5) && game.world.waterDepth(x, z) < .3; }
/** where the troops historically stood: defenders round the position they belong to, attackers in a line abreast at the assembly area */
export function defaultPlace(game, groups, Z) {
  const M = game.M, R = new Rng(M.seed + 31), att = M.attacker; const line = new Map();
  for (const g of groups) {
    const anchor = g.loc >= 0 ? game.objectives[g.loc] : null; let x = 0, z = 0;
    for (let k = 0; k < 30; k++) {
      if (anchor) { const a = R.next() * 6.28, r = Math.sqrt(R.next()) * anchor.r * .55; x = anchor.x + Math.cos(a) * r; z = anchor.z + Math.sin(a) * r; }
      else { const s = M.spawn[g.team], e = M.spawn[1 - g.team], dx = e.x - s.x, dz = e.z - s.z, L = Math.hypot(dx, dz) || 1, fx = dx / L, fz = dz / L;
        const row = g.type === 'tank' ? 1 : g.type === 'at' || g.type === 'sniper' ? -1 : 0, idx = line.get(row) || 0; if (k === 0) line.set(row, idx + 1);
        const lat = ((idx % 2) ? 1 : -1) * Math.ceil(idx / 2) * 24 + (R.next() - .5) * 6, dep = row * 22 + (R.next() - .5) * 8 + k * 2;
        x = s.x - fz * lat + fx * dep; z = s.z + fx * lat + fz * dep; }
      if (placeable(game, x, z) && (g.team !== att || inZone(game, Z, g.team, x, z) || k > 20)) break;
    }
    g.x = x; g.z = z; g.placed = true; g.hold = false;
  }
}

export class Planner {
  constructor(game) { this.g = game; this.groups = []; this.sel = null; this.drag = null; this.memo = new Map(); }
  get mine() { return this.groups.filter(g => g.team === this.g.side); }
  /** show the planner for the battle that has just loaded; the computer's side is placed at once */
  open(onStart) {
    const g = this.g; this.onStart = onStart; this.Z = zoneOf(g);
    this.groups = [...allotment(g, 0), ...allotment(g, 1)]; defaultPlace(g, this.groups.filter(x => x.team !== g.side), this.Z);
    const key = `${g.mi}|${g.side}|${g.settings.size}`, old = this.memo.get(key); // your last placements on this battle come back
    if (old) for (const m of this.mine) { const o = old.find(q => q.id === m.id); if (o) Object.assign(m, { x: o.x, z: o.z, placed: o.placed, hold: o.hold }); }
    this.key = key; this.sel = this.mine.find(m => !m.placed) || null; this.build(); this.el.style.display = 'flex'; this.layout(); this.render(); this.list();
  }
  close() { if (this.el) this.el.style.display = 'none'; }
  build() {
    if (this.el) return; const el = this.el = document.querySelector('#plan');
    el.innerHTML = `<div class="pwrap"><div class="pside"><h2 id="ptitle"></h2><p class="small" id="phelp"></p><div id="plist"></div>
      <div class="row"><button id="pauto">Auto-deploy</button><button id="pclear">Clear</button><button id="prem">Remove</button></div>
      <div class="row"><button id="pstart" class="big">Start battle</button><button id="pquit">Quit</button></div><div id="pmsg" class="small"></div></div>
      <div class="pmapbox"><canvas id="pmap"></canvas></div></div>`;
    const $ = s => el.querySelector(s); this.cv = $('#pmap'); this.ctx = this.cv.getContext('2d');
    $('#pauto').onclick = () => { defaultPlace(this.g, this.mine.filter(m => !m.placed), this.Z); this.sel = null; this.say('Everything not yet placed went to its historical position.'); this.refresh(); };
    $('#pclear').onclick = () => { for (const m of this.mine) m.placed = false; this.sel = this.mine[0] || null; this.say(''); this.refresh(); };
    $('#prem').onclick = () => { if (this.sel) { this.sel.placed = false; this.refresh(); } };
    $('#pstart').onclick = () => this.start(); $('#pquit').onclick = () => { this.close(); this.g.leaveBattle(); };
    const pos = e => { const r = this.cv.getBoundingClientRect(), k = (2 * this.E) / r.width; return [-this.E + (e.clientX - r.left) * k, -this.E + (e.clientY - r.top) * k, k]; };
    this.cv.addEventListener('pointerdown', e => { e.preventDefault(); const [x, z, k] = pos(e); this.down(x, z, k * 22); this.cv.setPointerCapture?.(e.pointerId); });
    this.cv.addEventListener('pointermove', e => { if (!this.drag) return; const [x, z] = pos(e); this.move(x, z); });
    const up = () => { if (this.drag) { this.drag = null; this.list(); } }; this.cv.addEventListener('pointerup', up); this.cv.addEventListener('pointercancel', up);
    window.addEventListener('resize', () => { if (this.el.style.display === 'flex') { this.layout(); this.render(); } });
  }
  say(t) { this.el.querySelector('#pmsg').textContent = t; }
  refresh() { this.render(); this.list(); }
  /** tap: pick up a placed unit near the finger, or put the selected one down there */
  down(x, z, tol) {
    let best = null, bd = tol; for (const m of this.mine) if (m.placed) { const d = Math.hypot(m.x - x, m.z - z); if (d < bd) { bd = d; best = m; } }
    if (best) { this.sel = best; this.drag = best; this.refresh(); return; }
    const m = this.sel; if (!m) { this.say('Pick a unit from the list first.'); return; }
    if (!inZone(this.g, this.Z, m.team, x, z)) { this.say('That is outside your deployment zone (the bright area).'); return; }
    if (!placeable(this.g, x, z)) { this.say('Not there: a building or water is in the way.'); return; }
    m.x = x; m.z = z; m.placed = true; m.hold = true; this.say(''); this.sel = this.mine.find(q => !q.placed) || m; this.refresh();
  }
  move(x, z) { const m = this.drag; if (inZone(this.g, this.Z, m.team, x, z) && placeable(this.g, x, z)) { m.x = x; m.z = z; m.placed = true; m.hold = true; this.render(); } }
  start() {
    const mine = this.mine, n = mine.filter(m => m.placed).length;
    this.memo.set(this.key, mine.map(m => ({ id: m.id, x: m.x, z: m.z, placed: m.placed, hold: m.hold })));
    this.close(); this.onStart(this.groups.filter(g => g.placed), mine.length - n);
  }
  layout() {
    const box = this.el.querySelector('.pmapbox'), r = box.getBoundingClientRect(), S = Math.max(160, Math.floor(Math.min(r.width, r.height)));
    const dpr = Math.min(2, devicePixelRatio || 1); this.cv.style.width = this.cv.style.height = S + 'px'; this.cv.width = this.cv.height = Math.round(S * dpr); this.S = S; this.dpr = dpr;
    const g = this.g, M = g.M; this.el.querySelector('#ptitle').textContent = `${M.name} — deploy the ${SIDE[g.side]}`;
    this.el.querySelector('#phelp').textContent = `${g.side === M.attacker ? 'You attack: deploy round the assembly area.' : 'You defend: deploy anywhere from position A back.'} Pick a unit, then tap the bright part of the map; drag to move. Units you leave out stay out of the battle.`;
  }
  list() {
    const box = this.el.querySelector('#plist'), mine = this.mine, placed = mine.filter(m => m.placed).length;
    box.innerHTML = `<div class="small">${placed} of ${mine.length} placed</div>` + mine.map(m => `<button class="pc ${m.placed ? 'on' : ''} ${m === this.sel ? 'sel' : ''}" data-i="${m.id}"><span class="gl">${GLYPH[m.type] || '?'}</span>${groupLabel(m)}<small>${m.placed ? 'placed' : 'not placed'}</small></button>`).join('');
    box.querySelectorAll('.pc').forEach(b => b.onclick = () => { this.sel = mine.find(m => m.id === +b.dataset.i); this.say(this.sel.placed ? 'Drag it on the map, or tap Remove.' : 'Now tap the map where it should go.'); this.refresh(); });
    this.el.querySelector('#prem').disabled = !(this.sel && this.sel.placed);
  }
  render() {
    const c = this.ctx, g = this.g, hud = g.hud, S = this.S * this.dpr; if (!hud.mapImg) return; const E = this.E = hud.mapE, k = S / (2 * E);
    c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, S, S); c.drawImage(hud.mapImg, 0, 0, S, S);
    if (!this.zoneImg || this.zoneFor !== this.key) { // dim everything outside your zone
      const N = 160, zc = document.createElement('canvas'); zc.width = zc.height = N; const zg = zc.getContext('2d'), im = zg.createImageData(N, N);
      for (let j = 0; j < N; j++) for (let i = 0; i < N; i++) { const x = -E + (i + .5) * 2 * E / N, z = -E + (j + .5) * 2 * E / N, p = (j * N + i) * 4, ok = inZone(g, this.Z, g.side, x, z); im.data[p] = ok ? 255 : 0; im.data[p + 1] = ok ? 240 : 0; im.data[p + 2] = ok ? 200 : 0; im.data[p + 3] = ok ? 28 : 140; }
      zg.putImageData(im, 0, 0); this.zoneImg = zc; this.zoneFor = this.key; }
    c.imageSmoothingEnabled = true; c.drawImage(this.zoneImg, 0, 0, S, S);
    c.save(); c.scale(k, k); c.translate(E, E); const px = 1 / k * this.dpr;
    g.objectives.forEach((o, i) => { c.strokeStyle = TEAMCOL[o.owner]; c.fillStyle = TEAMCOL[o.owner] + '40'; c.lineWidth = 2 * px; c.beginPath(); c.arc(o.x, o.z, o.r, 0, 6.3); c.fill(); c.stroke(); c.fillStyle = '#fff'; c.font = `bold ${15 * px}px system-ui`; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(String.fromCharCode(65 + i), o.x, o.z); });
    const s = g.M.spawn[g.side]; c.strokeStyle = TEAMCOL[g.side]; c.setLineDash([6 * px, 4 * px]); c.lineWidth = 2 * px; c.strokeRect(s.x - 18, s.z - 18, 36, 36); c.setLineDash([]);
    for (const m of this.mine) { if (!m.placed) continue; const w = (m.type === 'tank' ? 34 : 30) * px, h = 20 * px, sel = m === this.sel;
      c.fillStyle = sel ? '#ffd76a' : TEAMCOL[g.side]; c.strokeStyle = sel ? '#fff' : '#111'; c.lineWidth = (sel ? 2.5 : 1.5) * px; c.beginPath(); c.roundRect ? c.roundRect(m.x - w / 2, m.z - h / 2, w, h, 4 * px) : c.rect(m.x - w / 2, m.z - h / 2, w, h); c.fill(); c.stroke();
      c.fillStyle = '#111'; c.font = `bold ${11 * px}px system-ui`; c.fillText((GLYPH[m.type] || '?') + (m.n > 1 ? ' ' + m.n : ''), m.x, m.z + .5 * px);
      c.strokeStyle = 'rgba(255,255,255,.35)'; c.lineWidth = px; c.beginPath(); c.arc(m.x, m.z, SPREAD[m.type] || 8, 0, 6.3); c.stroke(); }
    c.restore();
  }
  /** headless checks: put the first unplaced unit in the middle of the zone */
  testPlaceFirst() { const g = this.g, Z = this.Z, m = this.mine.find(q => !q.placed); if (!m) return false; const t = g.side === Z.att ? [Z.s.x, Z.s.z] : [g.objectives[1].x, g.objectives[1].z]; for (let i = 0; i < 40; i++) { const x = t[0] + (Math.random() - .5) * 40, z = t[1] + (Math.random() - .5) * 40; if (inZone(g, Z, g.side, x, z) && placeable(g, x, z)) { this.sel = m; this.down(x, z, 0); return true; } } return false; }
}
export { SPREAD };
