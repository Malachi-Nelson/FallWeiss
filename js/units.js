// Fall Weiss web — soldiers, cavalry, tanks and AT guns with milsim AI (RO2-style ranges, suppression, cover, bounding advance).
import * as THREE from './three.module.min.js';
import { spawnModel, findNode, mergeUnder } from './models.js';
import { UNITS, TANK, ATGUN, NAMES, RANKS, SURNAMES, clamp, lerp, angDiff, sm } from './defs.js';
import * as SND from './audio.js';

const TMP = new THREE.Vector3(), TMP2 = new THREE.Vector3();
let uid = 0;
const proxyMats = {};
function proxyFor(team, kind) { // low-poly stand-in drawn past the LOD distance
  const key = team + kind; if (proxyMats[key]) return proxyMats[key];
  const uni = new THREE.MeshLambertMaterial({ color: team === 0 ? 0x4e5442 : 0x5e5638 }), helm = new THREE.MeshLambertMaterial({ color: team === 0 ? 0x3c4236 : 0x4a4a34 }), skin = new THREE.MeshLambertMaterial({ color: 0x9a7a62 });
  const parts = [], col = (g, c) => { const n = g.attributes.position.count, a = new Float32Array(n * 3), cc = new THREE.Color(c); for (let i = 0; i < n; i++) { a[i * 3] = cc.r; a[i * 3 + 1] = cc.g; a[i * 3 + 2] = cc.b; } g.setAttribute('color', new THREE.BufferAttribute(a, 3)); parts.push(g.index ? g.toNonIndexed() : g); };
  const U = team === 0 ? 0x5a6050 : 0x6e6444, Hm = team === 0 ? 0x464c40 : 0x55553c;
  let b = new THREE.BoxGeometry(.42, .62, .26); b.translate(0, 1.18, 0); col(b, U); b = new THREE.BoxGeometry(.34, .86, .22); b.translate(0, .43, 0); col(b, U); b = new THREE.BoxGeometry(.2, .22, .22); b.translate(0, 1.6, 0); col(b, 0xa07e66);
  b = new THREE.SphereGeometry(.17, 6, 3, 0, Math.PI * 2, 0, Math.PI / 2); b.translate(0, 1.67, 0); col(b, Hm); b = new THREE.BoxGeometry(.05, .06, 1.1); b.translate(.12, 1.35, -.35); col(b, 0x3a2e22);
  let n = 0; for (const p of parts) n += p.attributes.position.count; const P = new Float32Array(n * 3), N = new Float32Array(n * 3), C = new Float32Array(n * 3); let o = 0;
  for (const p of parts) { const q = p.toNonIndexed ? p : p; P.set(q.attributes.position.array, o * 3); N.set(q.attributes.normal.array, o * 3); C.set(q.attributes.color.array, o * 3); o += q.attributes.position.count; }
  const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(P, 3)); geo.setAttribute('normal', new THREE.BufferAttribute(N, 3)); geo.setAttribute('color', new THREE.BufferAttribute(C, 3));
  return proxyMats[key] = { geo };
}

export class Unit {
  constructor(game, team, type, x, z, o = {}) {
    this.g = game; this.id = ++uid; this.team = team; this.type = type; this.def = UNITS[type] || UNITS.rifle; this.kind = this.def.kind;
    this.x = x; this.z = z; this.y = game.world.H(x, z); this.yaw = o.yaw ?? 0; this.hp = this.def.hp; this.alive = true; this.isPlayer = false;
    this.stance = 0; this.stanceV = 0; this.supp = 0; this.morale = 1; this.speed = 0; this.phase = Math.random() * 6; this.fireT = 1 + Math.random() * 2; this.thinkT = Math.random() * .5; this.burst = 0;
    this.goal = null; this.target = null; this.lastShotAt = 0; this.squad = o.squad || null; this.leader = !!o.leader; this.bleed = 0; this.grenades = type === 'rifle' ? 1 + (Math.random() < .5 ? 1 : 0) : 0; this.nadeT = 10 + Math.random() * 10;
    this.name = (RANKS[team][o.leader ? 2 + (Math.random() < .3 ? 1 : 0) : Math.random() < .25 ? 1 : 0]) + ' ' + SURNAMES[team][Math.floor(Math.random() * 16)];
    this.dead = 0; this.home = o.home || null; this.cover = null; this.mode = 'move'; this.modeT = 0; this.path = null; this.wpt = null; this.seen = 0;
    if (this.kind === 'tank') { const T = TANK[team]; this.T = T; this.hp = T.hp; this.turretYaw = 0; this.engine = null; this.reloadT = 2; this.coaxT = 0; this.ammoAP = true; }
    if (this.kind === 'gun') { this.A = ATGUN[team]; this.turretYaw = 0; this.crew = []; }
    this.build();
  }
  get eye() { return this.kind === 'tank' ? 2.2 : this.kind === 'cav' ? 2.4 : this.kind === 'gun' ? 1.0 : [1.6, 1.1, .4][this.stance]; }
  get infantry() { return this.kind === 'inf'; }
  build() {
    const g = this.g, t = this.team; let name;
    if (this.kind === 'inf') name = `inf_${t}_${this.type === 'mg' ? 'mg' : this.type === 'sniper' ? 'sniper' : this.leader ? 'assault' : this.type === 'tcrew' ? 'pistol' : 'rifle'}`;
    else if (this.kind === 'cav') name = `cav_${t}`; else if (this.kind === 'tank') name = `tank_${t}`; else name = `at_${t}`;
    this.root = new THREE.Group(); g.scene.add(this.root);
    this.model = spawnModel(name, this.kind === 'tank' || this.kind === 'gun' ? g.mats.steel : g.mats.cloth, g.quality > 0);
    this.root.add(this.model); this.n = {};
    const mat = this.kind === 'tank' || this.kind === 'gun' ? g.mats.steel : g.mats.cloth, sh = g.quality > 0;
    if (this.kind === 'inf') mergeUnder(this.model, name, ['upper', 'knee0', 'knee1', 'leg0', 'leg1'], mat, sh);
    else if (this.kind === 'tank' || this.kind === 'gun') { mergeUnder(this.model, name, ['turret'], mat, sh); const body = []; this.model.children.forEach(c => { if (c.isMesh) body.push(c); }); }
    else if (this.kind === 'cav') mergeUnder(this.model, name, ['leg0', 'leg1', 'leg2', 'leg3'], mat, sh);
    for (const k of ['lay', 'hip', 'upper', 'head', 'gear', 'weapon', 'muzzle', 'arm0', 'arm1', 'leg0', 'leg1', 'knee0', 'knee1', 'leg2', 'leg3', 'turret']) this.n[k] = findNode(this.model, k);
    if (this.kind === 'inf') { const P = proxyFor(t, 'inf'); const pr = new THREE.Mesh(P.geo, g.mats.proxy); pr.visible = false; this.root.add(pr); this.proxy = pr; }
    if (this.kind === 'tank') { this.tdust = 0; }
    this.root.position.set(this.x, this.y, this.z); this.root.rotation.y = this.yaw;
  }
  dispose() { this.g.scene.remove(this.root); this.stopEngine(); }
  stopEngine() { if (this.engine) { this.engine.stop(); this.engine = null; this.g.engines = Math.max(0, this.g.engines - 1); } }

  // ---------- hit volumes for real projectiles (player rounds, grenades): [x,y,z,r,part]
  spheres() {
    const x = this.x, y = this.y, z = this.z;
    if (this.kind === 'tank' || this.kind === 'gun') return [];
    if (this.kind === 'cav') return [[x, y + 2.35, z, .16, 'head'], [x, y + 1.9, z, .28, 'chest'], [x, y + 1.25, z, .55, 'horse'], [x - Math.sin(this.yaw) * .9, y + 1.3, z - Math.cos(this.yaw) * .9, .45, 'horse']];
    const s = this.dead ? 3 : this.stance;
    if (s >= 2) { const fx = -Math.sin(this.yaw), fz = -Math.cos(this.yaw); return [[x + fx * 1.55, y + .3, z + fz * 1.55, .16, 'head'], [x + fx * 1.15, y + .22, z + fz * 1.15, .24, 'chest'], [x + fx * .75, y + .2, z + fz * .75, .22, 'belly'], [x + fx * .2, y + .15, z + fz * .2, .2, 'leg']]; }
    const k = s === 1 ? .7 : 1; return [[x, y + 1.62 * k, z, .14, 'head'], [x, y + 1.3 * k, z, .24, 'chest'], [x, y + 1.0 * k, z, .22, 'belly'], [x, y + .5 * k, z, .2, 'leg']];
  }
  /** tank / gun hull box for projectile & shell tests */
  hullHit(ax, ay, az, bx, by, bz) {
    if (this.kind !== 'tank' && this.kind !== 'gun') return null; const L = this.kind === 'tank' ? [1.15, 2.5, 2.4] : [.9, 1.3, 1.2];
    // transform segment into hull space
    const c = Math.cos(-this.yaw), s = Math.sin(-this.yaw); const tx = (px, pz) => [(px - this.x) * c + (pz - this.z) * s, -(px - this.x) * s + (pz - this.z) * c];
    const [ax2, az2] = tx(ax, az), [bx2, bz2] = tx(bx, bz); const ay2 = ay - this.y, by2 = by - this.y; let t0 = 0, t1 = 1;
    for (const [o, d, lo, hi] of [[ax2, bx2 - ax2, -L[0], L[0]], [ay2, by2 - ay2, 0, L[2]], [az2, bz2 - az2, -L[1], L[1]]]) { if (Math.abs(d) < 1e-9) { if (o < lo || o > hi) return null; continue; } let a = (lo - o) / d, b = (hi - o) / d; if (a > b) [a, b] = [b, a]; t0 = Math.max(t0, a); t1 = Math.min(t1, b); if (t0 > t1) return null; }
    // which face: compare entry direction with hull axes
    const dx = bx2 - ax2, dz = bz2 - az2, L2 = Math.hypot(dx, dz) || 1; const along = dz / L2; // hull forward is -z
    const face = along > .7 ? 'front' : along < -.7 ? 'rear' : 'side';
    return { t: t0, face };
  }

  // ---------- damage
  hurt(dmg, from, part, opts = {}) {
    if (!this.alive) return false;
    if (this.kind === 'inf' || this.kind === 'cav') {
      let m = part === 'head' ? 2.2 : part === 'chest' ? 1.05 : part === 'belly' ? .85 : part === 'horse' ? .5 : .45; this.hp -= dmg * m;
      if (part === 'belly' || part === 'chest') this.bleed = Math.max(this.bleed, 3); this.supp = Math.min(1, this.supp + .5);
      if (part === 'horse' && this.kind === 'cav' && this.hp > 0) this.dismount();
      this.g.fx.impact(this.x, this.y + (this.stance === 2 ? .3 : 1.2), this.z, 'flesh');
    } else this.hp -= dmg;
    if (from && from.team !== this.team) { this.target = from; this.alert = 1; }
    if (this.hp <= 0) { this.die(from, opts.how || (part === 'head' ? 'headshot' : '')); return true; }
    return false;
  }
  die(from, how) {
    if (!this.alive) return; this.alive = false; this.dead = 0.001; this.hp = 0; this.speed = 0; this.g.onDeath(this, from, how);
    if (this.kind === 'tank') this.g.explodeFx(this.x, this.y + 1.5, this.z, 1.3, false); else if (this.kind === 'gun') this.g.explodeFx(this.x, this.y + .6, this.z, .8, false);
    this.deathVisual();
  }
  /** what a death looks like: burnt-out hull and a wreck to drive round, or a man falling */
  deathVisual() {
    if (this.kind === 'tank') { this.fire = this.g.fx.fire(this.x, this.y + 1.5, this.z, false); this.stopEngine(); this.model.traverse(o => { if (o.material) o.material = this.g.mats.burnt; }); if (this.n.turret) { this.n.turret.rotation.z = .3; this.n.turret.position.y += .25; }
      const w = this.g.world, o = w.addObst(this.x - 1.3, this.x + 1.3, this.z - 2.4, this.z + 2.4, 2.2, 'wreck'); o.y0 = this.y; o.y1 = this.y + 2.4; w.gridAdd(w.ogrid, o, 20); this.wreck = o; }
    else if (this.kind === 'gun') this.model.traverse(o => { if (o.material) o.material = this.g.mats.burnt; });
    else { this.deathSide = Math.random() < .5 ? 1 : -1; this.deathRoll = (Math.random() - .5) * .8; if (this.kind === 'cav') this.deathSide = 1; }
  }
  /** a mirrored unit (online client) dies because the host says so */
  netDie(already) { if (!this.alive) return; this.alive = false; this.dead = already ? 30 : .001; this.hp = 0; this.speed = 0; this.deathVisual(); }
  dismount() { // uhlans fight on foot: the rider becomes a rifleman; the horse is led away
    if (this.kind !== 'cav' || !this.alive) return; const u = this.g.spawnUnit(this.team, 'rifle', this.x + Math.cos(this.yaw) * 1, this.z - Math.sin(this.yaw) * 1, { squad: this.squad, yaw: this.yaw });
    u.goal = this.goal; u.hp = this.hp; this.alive = false; this.g.fx.dust(this.x, this.y, this.z, 1.2); this.led = 1; this.g.removeUnit(this, true);
  }

  // ---------- per-frame
  update(dt) {
    if (this.net) { this.netUpdate(dt); return; }
    if (!this.alive) { this.updateDead(dt); return; }
    if (this.bleed > 0) { this.bleed -= dt * .25; this.hp -= dt * 2.2; if (this.hp <= 0) { this.die(null, 'bled out'); return; } }
    this.supp = Math.max(0, this.supp - dt * (this.supp > .7 ? .12 : .2));
    if (this.dummy) { this.supp = 0; if (this.dummy === 'walk') { this.walkT = (this.walkT || 0) + dt; const a = Math.sin(this.walkT * .25) * 12; this.x = this.spot[0] + a; this.speed = Math.abs(Math.cos(this.walkT * .25)) * 3; this.yaw = Math.cos(this.walkT * .25) > 0 ? -Math.PI / 2 : Math.PI / 2; this.y = this.g.world.H(this.x, this.z); } this.pose(dt); return; }
    if (!this.isPlayer) { this.thinkT -= dt; if (this.thinkT <= 0) { this.thinkT = .35 + Math.random() * .35; this.think(); } this.act(dt); }
    this.pose(dt);
  }
  updateDead(dt) {
    this.dead += dt;
    if (this.kind === 'inf' || this.kind === 'cav') { const t = Math.min(1, this.dead / .55); const lay = this.n.lay; if (lay) { lay.rotation.x = this.deathSide * Math.PI / 2 * (t * t); lay.rotation.z = this.deathRoll * t; lay.position.y = .12 * t; }
      if (this.kind === 'cav') this.root.rotation.z = t * 1.4;
      if (this.proxy && this.proxy.visible) this.proxy.rotation.x = this.deathSide * Math.PI / 2 * t; }
    if (!this.net && this.dead > (this.g.quality === 0 ? 45 : 90) && this.kind !== 'tank') this.g.removeUnit(this);
  }

  /** follow the host's snapshots, drawn a moment in the past so movement is smooth between them */
  netUpdate(dt) {
    const b = this.nb;
    if (this.alive && b && b.length) {
      const t = performance.now() - (this.netDelay || 240); let i = b.length - 1; while (i > 0 && b[i - 1].t > t) i--;
      const hi = b[i], lo = b[Math.max(0, i - 1)], f = hi.t > lo.t ? clamp((t - lo.t) / (hi.t - lo.t), 0, 1) : 1, ox = this.x, oz = this.z;
      this.x = lerp(lo.x, hi.x, f); this.z = lerp(lo.z, hi.z, f); this.yaw = lo.yaw + angDiff(lo.yaw, hi.yaw) * f;
      if (hi.ex !== undefined) { if (this.kind === 'tank' || this.kind === 'gun') this.turretYaw = lo.ex + angDiff(lo.ex, hi.ex) * f; else this.aimPitch = lerp(lo.ex, hi.ex, f); }
      const s = f > .5 ? hi : lo; if (s.st !== undefined) this.stance = s.st; this.reloading = !!s.rl;
      const sp = Math.hypot(this.x - ox, this.z - oz) / Math.max(dt, 1e-3); this.speed += (Math.min(sp, 14) - this.speed) * Math.min(1, dt * 6);
      this.y = this.g.world.H(this.x, this.z);
      // the footsteps, hooves, dust and engine noise the host's AI code would have made
      if (this.kind === 'inf' && this.camD < 40 && this.speed > .5) { this.stepT = (this.stepT || 0) - dt * this.speed / 1.4; if (this.stepT <= 0) { this.stepT = 1; SND.footstep(this.x, this.y, this.z, this.g.world.surf(this.x, this.z), false, this.speed > 2.9); } }
      if (this.kind === 'cav' && this.camD < 200 && this.speed > .5) { this.stepT = (this.stepT || 0) - dt * this.speed / 2; if (this.stepT <= 0) { this.stepT = 1; SND.footstep(this.x, this.y, this.z, 'grass', false, true); if (this.g.dry) this.g.fx.dust(this.x, this.y, this.z, .5); } }
      if (this.kind === 'tank') this.tankFx(dt, this.speed);
    }
    if (!this.alive) this.updateDead(dt); else this.pose(dt);
  }

  // ---------- animation
  pose(dt) {
    this.root.position.set(this.x, this.y, this.z); this.root.rotation.y = this.yaw;
    const cam = this.g.camera.position, d = Math.hypot(cam.x - this.x, cam.z - this.z); this.camD = d;
    if (this.kind === 'inf') {
      const far = d > this.g.lodDist; if (this.proxy) { this.proxy.visible = far && !this.isPlayer; this.model.visible = !far && !this.isPlayer; }
      this.stanceV += (this.stance - this.stanceV) * Math.min(1, dt * 6); const s = this.stanceV;
      if (far) { this.proxy.rotation.x = s > 1.5 ? -Math.PI / 2 : 0; this.proxy.position.set(0, s > 1.5 ? .2 : 0, s > 1.5 ? .9 : 0); this.proxy.scale.y = s > .5 && s < 1.5 ? .72 : 1; return; }
      const n = this.n; const mv = Math.min(1, this.speed / 2.4); this.phase += dt * (this.speed * 2.6 + (this.speed > .05 ? 1.2 : 0)); const sw = Math.sin(this.phase) * mv;
      const crouch = clamp(s, 0, 1), prone = clamp(s - 1, 0, 1);
      if (n.hip) n.hip.position.y = .92 - crouch * .36 + Math.abs(Math.cos(this.phase)) * .04 * mv;
      if (n.leg0) { n.leg0.rotation.x = sw * .6 * (1 - prone) + crouch * (1.25) * (1 - prone); n.leg1.rotation.x = -sw * .6 * (1 - prone) + crouch * (-.15) * (1 - prone); }
      if (n.knee0) { n.knee0.rotation.x = -Math.max(0, -sw) * .9 * (1 - prone) - crouch * 1.35 * (1 - prone); n.knee1.rotation.x = -Math.max(0, sw) * .9 * (1 - prone) - crouch * 1.6 * (1 - prone); }
      if (n.lay) { n.lay.rotation.x = -prone * Math.PI / 2; n.lay.position.y = prone * .12; n.lay.position.z = prone * .95; }
      if (n.upper) { n.upper.rotation.x = prone * 1.3 + (this.aimPitch || 0); n.upper.rotation.y = this.kind === 'inf' && prone < .5 ? sw * .08 : 0; }
      if (n.arm0) n.arm0.rotation.x = (this.reloading ? -.4 : 0);
    } else if (this.kind === 'cav') {
      this.phase += dt * (2 + this.speed * 1.3); const gal = Math.min(1, this.speed / 5); const n = this.n;
      for (let i = 0; i < 4; i++) { const leg = n['leg' + i]; if (leg) leg.rotation.x = Math.sin(this.phase + (i < 2 ? 0 : 1.6) + (i % 2) * .4) * .7 * gal; }
      this.model.position.y = Math.abs(Math.sin(this.phase)) * .12 * gal;
    } else if (this.kind === 'tank' || this.kind === 'gun') {
      if (this.n.turret) this.n.turret.rotation.y = this.turretYaw;
      const n = this.g.world; const fx = -Math.sin(this.yaw), fz = -Math.cos(this.yaw); const hf = n.H(this.x + fx * 2, this.z + fz * 2), hb = n.H(this.x - fx * 2, this.z - fz * 2);
      this.root.rotation.x = Math.atan2(hf - hb, 4) * (this.kind === 'tank' ? 1 : .6); this.root.rotation.order = 'YXZ';
    }
  }

  // ---------- AI
  enemiesNear(r) { return this.g.queryUnits(this.x, this.z, r, u => u.team !== this.team && u.alive); }
  canSee(u) {
    const key = u.id; this.losCache = this.losCache || new Map(); const c = this.losCache.get(key), now = this.g.time;
    if (c && now - c.t < .9) return c.v;
    const v = this.g.world.los(this.x, this.y + this.eye, this.z, u.x, u.y + (u.isPlayer ? u.eyeH() : u.eye) * .85, u.z); this.losCache.set(key, { v, t: now }); if (this.losCache.size > 40) this.losCache.clear(); return v;
  }
  think() {
    const g = this.g, D = this.def;
    if (this.kind === 'gun') return this.thinkGun();
    if (this.kind === 'tank') return this.thinkTank();
    // target acquisition: nearest visible enemy within sight (concealment shortens it for prone / covered men)
    let best = null, bd = 1e9; const sight = D.sight * (g.night ? .55 : 1) * (g.rainNow > .3 ? .8 : 1);
    for (const u of this.enemiesNear(sight)) {
      if (!u.alive) continue; let d = Math.hypot(u.x - this.x, u.z - this.z);
      const st = u.isPlayer ? u.stance : u.stance; const conceal = (st === 2 ? .45 : st === 1 ? .75 : 1) * (1 - g.world.coverAt(u.x, u.z) * .35) * (u.lastShotAt && g.time - u.lastShotAt < 3 ? 1.8 : 1);
      if (d > sight * conceal && u.kind === 'inf') continue;
      const pri = d * (u.kind === 'tank' ? (this.type === 'rifle' ? 2.5 : 1.5) : 1) * (u === this.target ? .7 : 1);
      if (pri < bd && this.canSee(u)) { bd = pri; best = u; }
    }
    if (best) { if (best !== this.target) this.fireT = Math.max(this.fireT, .6 + Math.random() * 1.2); this.target = best; this.tgtSeen = g.time; this.squadAlert(best); }
    else if (this.target && (!this.target.alive || g.time - (this.tgtSeen || 0) > 6)) this.target = null;
    // stance: under fire or with a target in the open → crouch / prone; pinned stays down
    const threat = this.supp > .55 || (this.target && Math.hypot(this.target.x - this.x, this.target.z - this.z) < D.range * .9);
    if (this.kind === 'inf') {
      if (this.supp > .8 && this.mode !== 'charge') this.stance = 2;
      else if (this.mode === 'hold' || this.mode === 'fire') this.stance = this.type === 'mg' || this.type === 'sniper' ? 2 : (g.world.coverAt(this.x, this.z) > .5 ? 1 : (threat ? 2 : 1));
      else this.stance = threat && this.mode !== 'charge' ? 1 : 0;
    }
    this.modeT -= .5;
    if (this.squad) this.squad.think(this);
    // grenades: an enemy close and behind cover / inside a building
    this.nadeT -= .5;
    if (this.grenades > 0 && this.nadeT <= 0 && this.target && this.kind === 'inf') { const d = Math.hypot(this.target.x - this.x, this.target.z - this.z); if (d > 9 && d < 30 && (g.world.coverAt(this.target.x, this.target.z) > .4 || Math.random() < .25)) { this.grenades--; this.nadeT = 18 + Math.random() * 10; g.throwGrenade(this, this.target.x + (Math.random() - .5) * 4, this.target.z + (Math.random() - .5) * 4); } }
  }
  squadAlert(t) { if (!this.squad) return; for (const m of this.squad.members) if (m !== this && m.alive && !m.target && Math.hypot(m.x - this.x, m.z - this.z) < 60) { m.target = t; m.tgtSeen = this.g.time - 3; } }

  act(dt) {
    const g = this.g, D = this.def;
    if (this.kind === 'gun') return this.actGun(dt);
    if (this.kind === 'tank') return this.actTank(dt);
    // movement toward wpt
    let tx = null, tz = null; if (this.wpt) { tx = this.wpt[0]; tz = this.wpt[1]; }
    const pinned = this.supp > .8 && this.mode !== 'charge';
    let sp = 0;
    if (tx != null && !pinned) {
      const dx = tx - this.x, dz = tz - this.z, d = Math.hypot(dx, dz);
      if (d > .8) {
        const want = Math.atan2(-dx, -dz); sp = D.speed * (this.stance === 2 ? .25 : this.stance === 1 ? .6 : 1) * (this.mode === 'charge' ? 1.6 : this.run ? 1.45 : 1) * (this.hp < 50 ? .7 : 1);
        if (this.stance === 2 && this.mode !== 'hold') { this.stance = 1; sp = D.speed * .6; }
        if (this.kind === 'cav') sp = D.speed * (this.run ? 1.2 : .8);
        if (!this.target || this.kind === 'cav' || this.mode === 'charge') this.yaw += angDiff(this.yaw, want) * Math.min(1, dt * 5);
        const step = Math.min(d, sp * dt); let nx = this.x + Math.sin(want + Math.PI) * step * 0 + (dx / d) * step, nz = this.z + (dz / d) * step;
        const wd = g.world.waterDepth(nx, nz); if (wd > 1.4 && this.kind !== 'cav') { nx = this.x; nz = this.z; } else if (wd > .2) { nx = this.x + (nx - this.x) * .45; nz = this.z + (nz - this.z) * .45; }
        [nx, nz] = g.world.collide(nx, nz, this.kind === 'cav' ? .8 : .35, this.y + 1);
        // separation from nearby friends
        const near = g.queryUnits(nx, nz, 1.6, u => u !== this && u.alive && u.kind !== 'tank'); for (const u of near) { const ex = nx - u.x, ez = nz - u.z, e = Math.hypot(ex, ez) || .01; if (e < 1.1) { nx += ex / e * (1.1 - e) * .5; nz += ez / e * (1.1 - e) * .5; } }
        const moved = Math.hypot(nx - this.x, nz - this.z); if (moved < step * .2) { this.stuck = (this.stuck || 0) + dt; if (this.stuck > 1.2) { this.stuck = 0; this.wpt = [this.x + (Math.random() - .5) * 10, this.z + (Math.random() - .5) * 10]; } } else this.stuck = 0;
        this.x = nx; this.z = nz; sp = moved / dt;
        if (this.camD < 40 && this.speed > .5) { this.stepT = (this.stepT || 0) - dt * this.speed / 1.4; if (this.stepT <= 0) { this.stepT = 1; SND.footstep(this.x, this.y, this.z, g.world.surf(this.x, this.z), false, this.speed > 2.9); } }
        if (this.kind === 'cav' && this.camD < 200) { this.stepT = (this.stepT || 0) - dt * this.speed / 2; if (this.stepT <= 0) { this.stepT = 1; SND.footstep(this.x, this.y, this.z, 'grass', false, true); if (g.dry) g.fx.dust(this.x, this.y, this.z, .5); } }
      } else this.wpt = null;
    }
    this.speed = sp; this.y = g.world.H(this.x, this.z);
    if (this.kind === 'cav' && this.target && Math.hypot(this.target.x - this.x, this.target.z - this.z) < 130) { this.dismount(); return; }
    // aim & fire
    const t = this.target;
    if (t && t.alive) {
      const dx = t.x - this.x, dz = t.z - this.z, d = Math.hypot(dx, dz); const want = Math.atan2(-dx, -dz); this.yaw += angDiff(this.yaw, want) * Math.min(1, dt * 4);
      this.aimPitch = clamp(Math.atan2((t.y + 1) - (this.y + 1.4), d) * .6, -.4, .4);
      this.fireT -= dt;
      const aimed = Math.abs(angDiff(this.yaw, want)) < .2, inRange = d < D.range * (this.type === 'sniper' ? 1.2 : 1);
      if (this.fireT <= 0 && aimed && inRange && (sp < .5 || this.type === 'mg' && this.stance === 2 || sp < 2.5 && Math.random() < .35) && g.time - (this.tgtSeen || 0) < 2.5) {
        if (D.burst) { this.burst = this.burst || D.burst + Math.floor(Math.random() * 4); this.shoot(t, d); this.burst--; this.fireT = this.burst > 0 ? D.rof : D.pause * (1 + Math.random() * .6) * (1 + this.supp); }
        else { this.shoot(t, d); this.fireT = D.rof * (.8 + Math.random() * .7) * (1 + this.supp * 1.5); }
      }
    } else this.aimPitch = 0;
  }
  /** resolve an AI shot: hit chance from range, stance, cover, movement and suppression (RO2 numbers from FWCore) */
  shoot(t, d) {
    const g = this.g, D = this.def; this.lastShotAt = g.time; const my = this.y + this.eye - .1, human = t.isPlayer || t.isRemote;
    const mz = this.n.muzzle ? this.n.muzzle.getWorldPosition(TMP) : TMP.set(this.x, my, this.z);
    const ex = t.x, ey = t.y + (t.isPlayer ? t.eyeH() * .8 : t.eye * .75), ez = t.z; let dx = ex - mz.x, dy = ey - mz.y, dz = ez - mz.z; const L = Math.hypot(dx, dy, dz); dx /= L; dy /= L; dz /= L;
    const kind = D.snd === 'mg' ? (this.team === 0 ? 'mg34' : 'mg30') : 'rifle', tracer = this.type === 'mg' && Math.random() < .35;
    // hit probability
    let p = D.acc * Math.pow(clamp(75 / (d + 8), .08, 2.6), 1.05);
    const tst = t.stance ?? 0; p *= tst === 2 ? .45 : tst === 1 ? .75 : 1; p *= 1 - g.world.coverAt(t.x, t.z) * (tst ? .6 : .3);
    p *= (t.speed > 2.8 ? .7 : 1) * (1 - this.supp * .6) * (this.stance === 2 ? 1.2 : this.stance === 1 ? 1.08 : .9) * (this.speed > .5 ? .5 : 1);
    if (t.kind === 'cav') p *= 1.3; if (human) p *= g.playerHitMul;
    let ix = t.x, iy = t.y + 1, iz = t.z, mat = null;
    if (t.kind === 'tank' || t.kind === 'gun') { ix = t.x + (Math.random() - .5) * 2; iy = t.y + 1 + Math.random(); iz = t.z + (Math.random() - .5) * 2; mat = 'metal'; }
    else if (Math.random() < p) {
      // blocked by something in between? (wall the target ducks behind)
      const blk = g.world.rayBlock(mz.x, mz.y, mz.z, ex, ey, ez, { noTrees: false });
      if (blk && blk.t < .97) { ix = blk.x; iy = blk.y; iz = blk.z; mat = blk.mat; if (!t.isRemote) t.supp = Math.min(1, (t.supp || 0) + .25); }
      else { const r = Math.random(), part = r < .12 ? 'head' : r < .5 ? 'chest' : r < .75 ? 'belly' : 'leg'; mat = 'flesh'; iy = t.y + (t.stance === 2 ? .3 : 1.2);
        if (human) g.hurtHuman(t, D.dmg * (part === 'head' ? 2 : part === 'leg' ? .45 : part === 'belly' ? .85 : 1.05), this, part); else t.hurt(D.dmg, this, part); }
    } else {
      ix = t.x + (Math.random() - .5) * (2 + d * .02); iz = t.z + (Math.random() - .5) * (2 + d * .02); iy = g.world.H(ix, iz); mat = g.world.surf(ix, iz) === 'water' ? 'water' : 'dirt';
      if (!human) t.supp = Math.min(1, (t.supp || 0) + (this.type === 'mg' ? .12 : .2));
    }
    g.shotFx(kind, mz.x, mz.y, mz.z, dx, dy, dz, d, tracer, this.type === 'mg' ? .8 : .6, ix, iy, iz, mat, this);
  }

  // ---------- tanks
  thinkTank() {
    const g = this.g; let best = null, bd = 1e9;
    for (const u of this.enemiesNear(this.def.sight)) { if (!u.alive) continue; const d = Math.hypot(u.x - this.x, u.z - this.z); const pri = d * (u.kind === 'gun' ? .4 : u.kind === 'tank' ? .5 : u.type === 'mg' ? .8 : 1.1); if (pri < bd && this.canSee(u)) { bd = pri; best = u; } }
    this.target = best; if (best) this.tgtSeen = g.time;
    if (this.squad) this.squad.think(this);
  }
  actTank(dt) {
    const g = this.g, T = this.T; let sp = 0;
    // drive to waypoint; stop to fire when a gun or tank is in sight (short halt)
    const halt = this.target && (this.target.kind === 'gun' || this.target.kind === 'tank') && Math.hypot(this.target.x - this.x, this.target.z - this.z) < 350;
    if (this.wpt && !halt) {
      const dx = this.wpt[0] - this.x, dz = this.wpt[1] - this.z, d = Math.hypot(dx, dz);
      if (d > 4) { let want = Math.atan2(-dx, -dz);
        // feel ahead for buildings / water and steer around
        for (const off of [0, .5, -.5, 1, -1, 1.6, -1.6]) { const a = want + off, px = this.x - Math.sin(a) * 9, pz = this.z - Math.cos(a) * 9; if (!g.world.blocked(px, pz, 2) && g.world.waterDepth(px, pz) < .8 && !this.tankAt(px, pz)) { want = a; break; } }
        const turn = angDiff(this.yaw, want); this.yaw += clamp(turn, -dt * .45, dt * .45);
        sp = T.speed * (Math.abs(turn) > .6 ? .25 : 1) * (this.supp > .5 ? .8 : 1);
        let nx = this.x - Math.sin(this.yaw) * sp * dt, nz = this.z - Math.cos(this.yaw) * sp * dt;
        if (g.world.blocked(nx, nz, 1.6)) { nx = this.x; nz = this.z; sp = 0; this.stuck = (this.stuck || 0) + dt; if (this.stuck > 3) { this.yaw += 1.2; this.stuck = 0; } }
        // run over men in the way
        for (const u of g.queryUnits(nx, nz, 2.2, u => u.alive && u.kind === 'inf' && u !== this)) { if (u.isPlayer || u.isRemote) g.hurtHuman(u, 200, this, 'chest', 'crushed'); else u.hurt(500, this, 'chest', { how: 'crushed' }); }
        this.x = nx; this.z = nz;
      } else this.wpt = null;
    }
    this.speed = sp; this.y = g.world.H(this.x, this.z); this.tankFx(dt, sp);
    // turret: hand-cranked traverse ~20°/s (compromise D-TUR)
    const t = this.target;
    if (t && t.alive) {
      const dx = t.x - this.x, dz = t.z - this.z, d = Math.hypot(dx, dz), want = angDiff(this.yaw, Math.atan2(-dx, -dz)); const err = angDiff(this.turretYaw, want);
      this.turretYaw += clamp(err, -dt * .35, dt * .35); this.reloadT -= dt; this.coaxT -= dt;
      if (Math.abs(err) < .05 && this.reloadT <= 0 && d < this.def.range && g.time - (this.tgtSeen || 0) < 2) { this.fireMain(t, d); this.reloadT = (this.burstLeft = (this.burstLeft || T.burst) - 1) > 0 ? .25 : T.rof * (1 + Math.random() * .5); if (this.burstLeft <= 0) this.burstLeft = T.burst; }
      if (t.kind === 'inf' && d < 180 && Math.abs(err) < .15 && this.coaxT <= 0) { this.coaxBurst = (this.coaxBurst || 0) + 1; this.coaxT = this.coaxBurst % 8 === 0 ? 2 + Math.random() * 2 : .085; this.coax(t, d); }
    } else { this.turretYaw += angDiff(this.turretYaw, 0) * dt * .3; }
  }
  /** dust behind the tracks and engine audio for the nearest few tanks */
  tankFx(dt, sp) {
    const g = this.g; if (!this.alive) return;
    if (sp > 1 && g.dry && this.camD < 300) { this.tdust = (this.tdust || 0) - dt; if (this.tdust < 0) { this.tdust = .15; g.fx.dust(this.x + Math.sin(this.yaw) * 2.4, this.y, this.z + Math.cos(this.yaw) * 2.4, 1.2); } }
    if (this.camD < 450) { if (!this.engine && g.engines < 3) { this.engine = new SND.Engine(this.team === 1); g.engines++; } } else this.stopEngine();
    if (this.engine) this.engine.update(this.x, this.y + 1, this.z, sp, dt);
  }
  tankAt(x, z) { for (const u of this.g.queryUnits(x, z, 5, u => u.kind === 'tank' && u !== this && u.alive)) return true; return false; }
  muzzleTank() { const a = this.yaw + this.turretYaw, L = this.kind === 'tank' ? 2.7 : 1.9, h = this.kind === 'tank' ? 1.75 : .95; return [this.x - Math.sin(a) * L, this.y + h, this.z - Math.cos(a) * L, -Math.sin(a), -Math.cos(a)]; }
  fireMain(t, d) {
    const g = this.g, [mx, my, mz, fx, fz] = this.muzzleTank(), P = this.kind === 'tank' ? this.T : this.A;
    const vs = t.kind === 'tank' || t.kind === 'gun'; const acc = (this.kind === 'gun' ? .85 : .8) * clamp(1.2 - d / 650, .3, 1) * (this.speed > .5 ? .55 : 1);
    const hit = Math.random() < acc; const ix = t.x + (hit ? 0 : (Math.random() - .5) * (6 + d * .03)), iz = t.z + (hit ? 0 : (Math.random() - .5) * (6 + d * .03)); const iy = g.world.H(ix, iz) + (hit && vs ? 1.2 : 0);
    g.cannonFx(P.snd, mx, my, mz, fx, fz, ix, iy, iz, d, this.y);
    g.after(d / 760, () => {
      if (vs && hit && t.alive) g.armourHit(t, this, P.pen100 - P.penDrop * Math.max(0, d - 100) / 100, mx, mz);
      else { g.explode(ix, iy, iz, this.kind === 'tank' && this.team === 0 ? .7 : 1.0, this, P.splash || (this.kind === 'gun' ? 2.5 : 4)); }
    });
  }
  coax(t, d) {
    const g = this.g, [mx, my0, mz] = this.muzzleTank(), my = my0 - .2, human = t.isPlayer || t.isRemote;
    const dx = (t.x - mx) / d, dy = (t.y + 1 - my) / d, dz = (t.z - mz) / d;
    let p = .16 * Math.pow(clamp(75 / (d + 8), .08, 2.6), 1.05); const st = t.stance ?? 0; p *= st === 2 ? .45 : st === 1 ? .75 : 1; p *= 1 - g.world.coverAt(t.x, t.z) * .6; if (human) p *= g.playerHitMul;
    let ix, iy, iz, mat;
    if (Math.random() < p && !g.world.rayBlock(mx, my, mz, t.x, t.y + 1, t.z, { noTrees: true })) { const part = Math.random() < .5 ? 'chest' : 'leg'; if (human) g.hurtHuman(t, 100, this, part); else t.hurt(100, this, part); ix = t.x; iy = t.y + 1; iz = t.z; mat = 'flesh'; }
    else { ix = t.x + (Math.random() - .5) * 4; iz = t.z + (Math.random() - .5) * 4; iy = g.world.H(ix, iz); mat = 'dirt'; if (!human) t.supp = Math.min(1, t.supp + .15); }
    g.shotFx(this.team === 0 ? 'mg34' : 'mg30', mx, my, mz, dx, dy, dz, d, this.coaxBurst % 3 === 0, .5, ix, iy, iz, mat, this, true);
  }
  // ---------- AT guns
  thinkGun() {
    const g = this.g; if (!this.crew.some(c => c.alive)) { this.target = null; return; }
    let best = null, bd = 1e9; for (const u of this.enemiesNear(this.def.sight)) { if (!u.alive || u.kind !== 'tank' && u.kind !== 'gun') continue; const d = Math.hypot(u.x - this.x, u.z - this.z); if (d < bd && this.canSee(u)) { bd = d; best = u; } }
    if (!best) for (const u of this.enemiesNear(220)) { if (!u.alive || u.kind !== 'inf') continue; const d = Math.hypot(u.x - this.x, u.z - this.z); if (d < bd && this.canSee(u)) { bd = d; best = u; } }
    this.target = best; if (best) this.tgtSeen = g.time;
  }
  actGun(dt) {
    const t = this.target; this.reloadT = (this.reloadT ?? 2) - dt; const crew = this.crew.filter(c => c.alive).length; if (!crew) return;
    if (t && t.alive) { const dx = t.x - this.x, dz = t.z - this.z, d = Math.hypot(dx, dz); const want = Math.atan2(-dx, -dz); const err = angDiff(this.yaw + this.turretYaw, want);
      // the trail limits traverse to ±30°: beyond it the crew heaves the carriage round
      if (Math.abs(this.turretYaw + err) < .52) this.turretYaw += clamp(err, -dt * .5, dt * .5); else this.yaw += clamp(err, -dt * .12 * crew, dt * .12 * crew);
      if (Math.abs(err) < .04 && this.reloadT <= 0 && d < this.def.range) { this.fireMain(t, d); this.reloadT = 60 / 12 * (3 / Math.max(1, crew)); this.recoil = .25; } }
    if (this.recoil > 0) { this.recoil -= dt; this.model.position.z = Math.max(0, this.recoil) * .8; }
  }
}

// ---------- squads: leader-follower, bounding overwatch, objective orders
export class Squad {
  constructor(game, team, role) { this.g = game; this.team = team; this.role = role; this.members = []; this.order = null; this.bound = 0; this.boundT = 0; this.id = Math.random(); }
  get alive() { return this.members.filter(m => m.alive); }
  get leader() { return this.members.find(m => m.alive && !m.isPlayer) || null; }
  /** order: {kind:'attack'|'defend'|'move', x, z, r} */
  setOrder(o) { this.order = o; this.formation(); }
  formation() {
    const o = this.order; if (!o) return; const ms = this.alive.filter(m => !m.isPlayer); const g = this.g;
    if (o.kind === 'defend') { // spread to cover spots around the position, facing the attacker's approach
      const covers = g.coverSpots(o.x, o.z, o.r, this.team, ms.length);
      ms.forEach((m, i) => { const c = covers[i % covers.length]; m.wpt = [c[0] + (Math.random() - .5) * 1.5, c[1] + (Math.random() - .5) * 1.5]; m.mode = 'hold'; m.homeSpot = m.wpt.slice(); m.run = !!o.run; });
    } else { const fx = o.x - this.cx(), fz = o.z - this.cz(), fl = Math.hypot(fx, fz) || 1; const px = -fz / fl, pz = fx / fl;
      ms.forEach((m, i) => { const lat = (i - (ms.length - 1) / 2) * 6, back = (i % 2) * 4; const tx = o.x + px * lat - fx / fl * back + (Math.random() - .5) * o.r * .6, tz = o.z + pz * lat - fz / fl * back + (Math.random() - .5) * o.r * .6; m.goal = [tx, tz]; m.mode = 'move'; m.run = !!o.run; }); }
  }
  cx() { const a = this.alive; return a.length ? a.reduce((s, m) => s + m.x, 0) / a.length : 0; }
  cz() { const a = this.alive; return a.length ? a.reduce((s, m) => s + m.z, 0) / a.length : 0; }
  /** per-member decision (called from Unit.think) */
  think(m) {
    const o = this.order, g = this.g; if (!o || m.isPlayer) return;
    if (m.kind === 'tank') { // tanks support: advance to a firing line short of the objective, never into it
      const d = Math.hypot(o.x - m.x, o.z - m.z), stand = o.kind === 'defend' ? 25 : 70; if (d > stand) { const k = (d - stand) / d; m.wpt = [m.x + (o.x - m.x) * Math.min(1, k), m.z + (o.z - m.z) * Math.min(1, k)]; if (m.target && Math.random() < .3) m.wpt = null; } else m.wpt = null; return; }
    if (o.kind === 'defend') { if (m.homeSpot && Math.hypot(m.homeSpot[0] - m.x, m.homeSpot[1] - m.z) > 1.5) m.wpt = m.homeSpot; else if (!m.homeSpot) this.formation(); m.mode = 'hold';
      if (m.supp > .9 && Math.random() < .05) { const c = g.coverSpots(m.x, m.z, 10, m.team, 1)[0]; if (c) { m.wpt = c; m.homeSpot = c; } } return; }
    // attack / move: bounding overwatch. Half the squad moves while the other half fires; switch every few seconds.
    const goal = m.goal || [o.x, o.z]; const dGoal = Math.hypot(goal[0] - m.x, goal[1] - m.z);
    const contact = m.target && m.target.alive && g.time - (m.tgtSeen || 0) < 4;
    if (!contact) { m.mode = 'move'; m.wpt = dGoal > 3 ? this.stepToward(m, goal, 40) : null; if (dGoal < 3) m.mode = 'hold'; return; }
    const dT = Math.hypot(m.target.x - m.x, m.target.z - m.z);
    if (dT < 18 && m.supp < .6) { m.mode = 'charge'; m.wpt = [m.target.x, m.target.z]; return; } // close the last metres
    const idx = this.members.indexOf(m); this.boundT -= .5 / Math.max(1, this.alive.length); if (this.boundT <= 0) { this.boundT = 6 + Math.random() * 4; this.bound ^= 1; }
    const moving = (idx % 2) === this.bound && m.supp < .65 && m.type !== 'mg';
    if (moving && dGoal > 6) { m.mode = 'move'; m.run = true; const c = g.coverSpots(m.x + (goal[0] - m.x) * Math.min(1, 25 / dGoal), m.z + (goal[1] - m.z) * Math.min(1, 25 / dGoal), 12, m.team, 1)[0]; m.wpt = c || this.stepToward(m, goal, 20); }
    else { m.mode = 'fire'; m.run = false; if (!m.wpt || Math.hypot(m.wpt[0] - m.x, m.wpt[1] - m.z) < 1) m.wpt = null; }
  }
  stepToward(m, goal, len) { const dx = goal[0] - m.x, dz = goal[1] - m.z, d = Math.hypot(dx, dz); if (d < len) return goal; return [m.x + dx / d * len, m.z + dz / d * len]; }
}

// ---------- another human in an online battle, as the host's AI sees them: positions come from their page, damage goes back to it
const CLASS_UNIT = { rifleman: ['rifle', false], assault: ['rifle', true], gunner: ['mg', false], sniper: ['sniper', false], atrifle: ['rifle', false] };
export function classUnit(cls) { return CLASS_UNIT[cls] || CLASS_UNIT.rifleman; }
export class RemotePlayer extends Unit {
  constructor(game, peer, team, cls, x, z, life, name) { const C = classUnit(cls); super(game, team, C[0], x, z, { leader: C[1] }); this.isRemote = true; this.peer = peer; this.life = life; this.name = name; this.cls = cls; this.hp = 100; this.netDelay = 140; this.grenades = 0; }
  update(dt) { this.supp = 0; this.netUpdate(dt); }
  /** their page owns their health: send the hit there */
  hurt(dmg, from, part, how) { if (!this.alive) return false; this.g.sendHurt(this, dmg, from, part, typeof how === 'string' ? how : how?.how || ''); return false; }
}
