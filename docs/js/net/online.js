// Fall Weiss web — online battles. One page hosts: it runs the whole battle (every AI soldier, tank and gun, the
// objectives, tickets, artillery and Stukas) and sends a snapshot of the battlefield about seven times a second. Every
// other player runs their own soldier on their own page, sends where they are and what they did twelve times a second,
// and draws what the host's snapshots say. Shots are judged by the shooter's page (what you hit on your screen is what
// you hit); damage, deaths, captures and the end of the battle are decided by the host.
// Humans pick a side in the menu, so the same battle can be co-op (everyone on one side) or versus (both sides), with
// AI filling the rest of both armies.
import { Relay } from './relay.js';

const PROTO = 1;
export const HZ_SNAP = 7, HZ_STATE = 12;
export const TYPES = ['rifle', 'mg', 'sniper', 'crew', 'cav', 'at', 'tank', 'tcrew'];
export const CLS = ['rifleman', 'assault', 'gunner', 'sniper', 'atrifle'];
export const GUNS = ['rifle', 'atr', 'smg', 'pistol', 'lmg', 'mg34', 'mg30', 'mg', 'kwk20', 'cannon37', 'naval'];
export const MATS = ['dirt', 'stone', 'wood', 'metal', 'water', 'flesh'];
export const HOST_UNIT = 65535;
const r1 = v => Math.round(v * 10) / 10;
const num = v => typeof v === 'number' && Number.isFinite(v);

// ---------- unit table: 10 bytes a unit, base64 ----------
export function packUnits(list) {
  const b = new Uint8Array(list.length * 10), dv = new DataView(b.buffer);
  list.forEach((u, i) => { const o = i * 10; dv.setUint16(o, u.id, true); b[o + 2] = u.code; b[o + 3] = u.flags;
    dv.setInt16(o + 4, Math.max(-32767, Math.min(32767, Math.round(u.x * 10))), true); dv.setInt16(o + 6, Math.max(-32767, Math.min(32767, Math.round(u.z * 10))), true);
    dv.setInt8(o + 8, Math.round(wrap(u.yaw) * 127 / Math.PI)); dv.setInt8(o + 9, Math.round(Math.max(-Math.PI, Math.min(Math.PI, wrap(u.extra || 0))) * 127 / Math.PI)); });
  let s = ''; for (let i = 0; i < b.length; i += 8192) s += String.fromCharCode.apply(null, b.subarray(i, i + 8192)); return btoa(s);
}
export function unpackUnits(str) {
  const s = atob(str), b = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) b[i] = s.charCodeAt(i);
  const dv = new DataView(b.buffer), out = []; for (let o = 0; o + 10 <= b.length; o += 10) out.push({ id: dv.getUint16(o, true), code: b[o + 2], flags: b[o + 3], x: dv.getInt16(o + 4, true) / 10, z: dv.getInt16(o + 6, true) / 10, yaw: dv.getInt8(o + 8) * Math.PI / 127, extra: dv.getInt8(o + 9) * Math.PI / 127 }); return out;
}
function wrap(a) { a = a % (Math.PI * 2); if (a > Math.PI) a -= Math.PI * 2; if (a < -Math.PI) a += Math.PI * 2; return a; }

export class Online {
  constructor(game) {
    this.g = game; this.mode = 'menu'; this.relay = null; this.peers = new Map(); this.games = new Map();
    this.name = (localStorage.getItem('fw_name') || '').slice(0, 16) || 'Soldat ' + (100 + Math.random() * 900 | 0);
    this.gid = null; this.hostId = null; this.battle = 0; this.evSeq = 0; this.evq = []; this.lastEv = 0; this.acts = []; this.actSeq = 0; this.lastAct = new Map(); this.ackFor = {};
    this.sendT = 0; this.snapT = 0; this.lastSnap = null; this.lastSnapAt = 0; this.joinedAt = 0; this.life = 0; this.kills = 0; this.deaths = 0; this.notice = '';
    this.room = this.roomFromHash(); this.listeners = new Set();
    window.addEventListener('hashchange', () => { const r = this.roomFromHash(); if (r !== this.room && this.mode === 'menu') { this.room = r; this.reconnect(); } });
  }
  get host() { return this.mode === 'host'; }
  get client() { return this.mode === 'client'; }
  get inBattle() { return this.mode === 'host' || this.mode === 'client'; }
  get online() { return !!(this.relay && this.relay.online); }
  roomFromHash() { const h = (location.hash || '').replace(/^#/, '').toLowerCase().replace(/[^a-z0-9]/g, '').slice(0, 12); return h || 'public'; }
  get isPrivate() { return this.room !== 'public'; }
  setName(n) { this.name = String(n || '').trim().slice(0, 16) || this.name; try { localStorage.setItem('fw_name', this.name); } catch (e) { } this.changed(); }
  connect() { if (this.relay) return; this.relay = new Relay(`fw/${PROTO}/${this.room}/`, (s, d, f) => this.onMsg(s, d, f), () => this.changed()); this.id = this.relay.id; this.relay.start(); }
  reconnect() { if (this.relay) { try { this.relay.bye(); for (const k of this.relay.conns) { k.gen++; clearTimeout(k.timer); try { k.c && k.c.end(true); } catch (e) { } } } catch (e) { } } this.relay = null; this.peers.clear(); this.games.clear(); this.connect(); this.changed(); }
  privateRoom() { location.hash = Math.random().toString(36).slice(2, 8); }
  publicRoom() { history.replaceState(null, '', location.pathname + location.search); this.room = 'public'; this.reconnect(); }
  inviteLink() { return location.origin + location.pathname + '#' + this.room; }
  onChange(fn) { this.listeners.add(fn); }
  changed() { for (const f of this.listeners) try { f(); } catch (e) { console.error(e); } }
  status() {
    if (!this.relay) return 'Offline';
    const n = this.relay.upCount; if (!n) return 'Connecting to the relays…';
    const here = [...this.peers.values()].filter(p => performance.now() - p.t < 6000).length;
    return `Online (${n}/${this.relay.total} relays) · ${this.isPrivate ? 'private room ' + this.room : 'public room'} · ${here} other player${here === 1 ? '' : 's'} here`;
  }
  openGames() { const now = performance.now(), out = []; for (const [gid, g] of this.games) if (now - g.t < 5000 && gid !== this.gid) out.push({ gid, ...g }); return out.sort((a, b) => b.t - a.t); }

  // ---------------------------------------------------------------- joining and leaving
  hostBattle() { this.mode = 'host'; this.gid = Math.random().toString(36).slice(2, 8); this.hostId = this.id; this.battle = 1; this.evq = []; this.evSeq = 0; this.lastAct.clear(); this.ackFor = {}; this.kills = this.deaths = 0; this.sendT = 0; this.changed(); }
  newBattle() { if (this.host) { this.battle++; this.evq = []; } }
  join(gid) {
    const g = this.games.get(gid); if (!g) return null;
    this.mode = 'client'; this.gid = gid; this.hostId = g.h; this.battle = g.b; this.lastSnap = null; this.lastSnapAt = performance.now(); this.joinedAt = performance.now();
    this.acts = []; this.actSeq = 0; this.lastEv = 0; this.fresh = true; this.kills = this.deaths = 0; this.life = 0; this.myUnit = -1; this.sendT = 0; this.changed();
    return g;
  }
  leave() { const was = this.mode; this.mode = 'menu'; this.gid = null; this.hostId = null; this.lastSnap = null; this.acts = []; this.evq = []; if (was !== 'menu') { this.sendT = 0; this.publish(); } this.changed(); }

  // ---------------------------------------------------------------- every frame
  tick(now) {
    if (!this.relay) return; const dt = Math.min(.25, Math.max(0, (now - (this.lastT || now)) / 1000)); this.lastT = now;
    this.sendT -= dt;
    if (this.sendT <= 0) { this.sendT = this.mode === 'menu' ? 1.5 : 1 / HZ_STATE; this.publish(); }
    if (this.host) { this.snapT -= dt; if (this.snapT <= 0) { this.snapT = 1 / HZ_SNAP; this.publishSnap(); } }
    for (const [id, p] of this.peers) if (now - p.t > 8000) this.dropPeer(id);
    for (const [gid, g] of this.games) if (now - g.t > 6000) { this.games.delete(gid); this.changed(); }
    if (this.client) this.watchHost(now);
  }
  /** my presence (all modes) and, in a battle, my soldier and what I did since the host last acknowledged */
  publish() {
    if (!this.online) return; const g = this.g, p = g.player, inB = this.inBattle;
    const s = inB && p ? [Math.round(p.x * 10), Math.round(p.y * 10), Math.round(p.z * 10), Math.round(wrap(p.yaw) * 1000), Math.round(p.pitch * 1000), p.stance, p.alive ? 1 : 0, this.life, CLS.indexOf(g.cls), p.reloadT > 0 ? 1 : 0] : null;
    this.relay.pub('p/' + this.id, { n: this.name, g: inB ? this.gid : null, sd: g.side, c: CLS.indexOf(g.cls), s, a: this.client ? this.acts.slice(-40) : [], k: this.kills, d: this.deaths });
  }
  /** client: something happened that the host must hear about; it is repeated until acknowledged */
  act(type, ...args) { if (!this.client) return; this.acts.push([++this.actSeq, type, ...args.map(v => typeof v === 'number' ? r1(v) : v)]); if (this.acts.length > 60) this.acts.shift(); if (this.sendT > .02) this.sendT = .02; }

  // ---------------------------------------------------------------- host: effects and events for everyone else
  /** an effect other players should see / hear; cosmetic ones far from every remote player are not sent */
  fx(type, args, reach = 0) {
    if (!this.host) return; const rem = this.g.remoteHumans(); if (!rem.length) return;
    if (reach > 0) { const o = type === 'sh' ? 1 : 0, x = args[o], z = args[o + 2]; let near = false; for (const u of rem) if (Math.abs(u.x - x) < reach && Math.abs(u.z - z) < reach) { near = true; break; } if (!near) return; }
    this.evq.push({ e: [++this.evSeq, type, ...args.map(v => typeof v === 'number' ? r1(v) : v)], n: 0 });
    if (this.evq.length > 400) this.evq.splice(0, this.evq.length - 400);
  }
  event(type, args) { if (!this.host) return; this.evq.push({ e: [++this.evSeq, type, ...args.map(v => typeof v === 'number' ? r1(v) : v)], n: 0 }); }
  publishSnap() {
    const g = this.g; if (!this.online || !g.scene || !g.objectives) return;
    const units = g.netUnitTable(), pl = {}; pl[this.id] = [this.name, g.side, HOST_UNIT, this.kills, this.deaths, CLS.indexOf(g.cls)];
    for (const [id, p] of this.peers) if (p.d && p.d.g === this.gid) { const rp = g.remotes.get(id); pl[id] = [String(p.d.n).slice(0, 16), p.d.sd | 0, rp && rp.alive ? rp.id % 65000 : -1, p.d.k | 0, p.d.d | 0, p.d.c | 0]; }
    const ev = []; for (const q of this.evq) { ev.push(q.e); q.n++; } this.evq = this.evq.filter(q => q.n < 2); // every event goes out in two snapshots in a row
    const M = g.M; const s = { g: this.gid, h: this.id, b: this.battle, mi: g.mi, mn: M.name, sz: g.settings.size, ln: g.settings.length, t: Math.round(g.time * 10), tl: Math.round(g.timeLeft * 10), rl: g.roundLen, ao: g.activeObj, ov: g.over ? 1 : 0,
      ob: g.objectives.map(o => [o.owner, Math.round(o.prog * 100)]), tk: g.tickets.map(t => Math.round(t)), u: packUnits(units), pl, ev, ak: this.ackFor };
    this.relay.pub('g/' + this.gid, s);
  }

  // ---------------------------------------------------------------- what arrives
  onMsg(suffix, d, from) {
    const now = performance.now();
    if (suffix.startsWith('p/')) {
      const id = suffix.slice(2); if (d === null) return this.dropPeer(id);
      if (id !== from || id === this.id || typeof d.n !== 'string') return;
      let p = this.peers.get(id); const isNew = !p; if (!p) { p = { buf: [] }; this.peers.set(id, p); }
      const wasIn = p.d && p.d.g === this.gid; p.d = d; p.t = now; if (isNew) this.changed();
      if (this.host) {
        if (d.g === this.gid) {
          if (!wasIn) this.g.hud?.feed(`<b>${esc(d.n)}</b> joined the battle (${['Wehrmacht', 'Polish Army'][d.sd | 0]})`, '#cde');
          if (Array.isArray(d.s) && d.s.length >= 8 && d.s.every(num)) { p.buf.push({ t: now, x: d.s[0] / 10, y: d.s[1] / 10, z: d.s[2] / 10, yaw: d.s[3] / 1000, pitch: d.s[4] / 1000, st: d.s[5] | 0, alive: !!d.s[6], life: d.s[7] | 0, cls: CLS[d.s[8] | 0] || 'rifleman', rl: !!d.s[9] }); if (p.buf.length > 16) p.buf.shift(); }
          let last = this.lastAct.get(id) || 0; if (Array.isArray(d.a)) for (const a of d.a) if (Array.isArray(a) && num(a[0]) && a[0] > last) { last = a[0]; try { this.g.netAction(id, p, a); } catch (e) { console.error(e); } }
          this.lastAct.set(id, last); this.ackFor[id] = last; this.g.syncRemote(id, p);
        } else if (wasIn || this.g.remotes?.has(id)) { this.g.dropRemote(id); delete this.ackFor[id]; this.g.hud?.feed(`<b>${esc(d.n)}</b> left the battle`, '#cde'); }
      }
      return;
    }
    if (suffix.startsWith('g/')) {
      const gid = suffix.slice(2); if (!d || d.g !== gid || d.h !== from || !num(d.mi) || !num(d.b) || typeof d.u !== 'string') return;
      const had = this.games.has(gid); const n = d.pl && typeof d.pl === 'object' ? Object.keys(d.pl).length : 1;
      this.games.set(gid, { h: d.h, b: d.b, mi: d.mi, mn: String(d.mn || ''), n, t: now, host: d.pl && d.pl[d.h] ? String(d.pl[d.h][0]) : 'someone', sz: d.sz, ln: d.ln, sides: d.pl ? Object.values(d.pl).map(a => a[1] | 0) : [] });
      if (!had) this.changed();
      if (gid !== this.gid) return;
      if (this.host) { if (d.h < this.id) { /* two hosts on one id: cannot happen with random ids */ } return; }
      if (!this.client || d.h !== this.hostId) return;
      this.lastSnap = d; this.lastSnapAt = now;
      try { this.g.applySnap(d, now); } catch (e) { console.error(e); }
      if (d.ak && num(d.ak[this.id])) { const ak = d.ak[this.id]; if (this.acts.length) this.acts = this.acts.filter(a => a[0] > ak); }
    }
  }
  dropPeer(id) { if (!this.peers.delete(id)) return; if (this.host && this.g.remotes?.has(id)) { this.g.dropRemote(id); delete this.ackFor[id]; } this.changed(); }
  watchHost(now) {
    if (!this.lastSnap) { if (now - this.joinedAt > 9000) { this.notice = 'That battle did not answer.'; this.g.netLost(this.notice); } return; }
    const silent = now - this.lastSnapAt, hp = this.peers.get(this.hostId), gone = hp && hp.d && hp.d.g !== this.gid;
    if (gone || silent > 9000) { this.notice = gone ? 'The host ended the battle.' : 'Lost contact with the host.'; this.g.netLost(this.notice); }
  }
}
export function esc(s) { return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
