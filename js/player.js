// Fall Weiss web — the first-person soldier: RO2 pace and stances, real projectiles with drop, bolt cycling, stripper clips,
// sway / recoil / suppression, grenades with fuses, bleeding and bandages, and the original first-person weapon models.
import * as THREE from './three.module.min.js';
import { spawnModel, findNode, hasModel } from './models.js';
import { WEAPONS, GRENADE, CLASSES, G, clamp, lerp, angDiff } from './defs.js';
import * as SND from './audio.js';

const EYE = [1.62, 1.08, .36];
const V = new THREE.Vector3(), V2 = new THREE.Vector3(), Q = new THREE.Quaternion(), E = new THREE.Euler(0, 0, 0, 'YXZ');

export class Player {
  constructor(game, team, cls, x, z) {
    this.g = game; this.team = team; this.isPlayer = true; this.kind = 'inf'; this.type = 'rifle'; this.alive = true; this.id = -1;
    this.x = x; this.z = z; this.y = game.world.H(x, z); this.yaw = 0; this.pitch = 0; this.kick = 0; this.stance = 0; this.stanceV = 0; this.speed = 0; this.vx = 0; this.vz = 0;
    this.hp = 100; this.bleed = 0; this.stamina = 1; this.supp = 0; this.lastShotAt = 0; this.cls = cls; this.def = { sight: 200 }; this.kills = 0; this.score = 0;
    const C = CLASSES.find(c => c.key === cls) || CLASSES[0]; const weps = C.weps[team] || C.weps[0];
    this.slots = weps.map(id => ({ id, w: WEAPONS[id], mag: WEAPONS[id].clip, mags: WEAPONS[id].mags, ready: true })); this.cur = 0; this.nades = C.gren; this.nadeType = GRENADE[team];
    this.ads = false; this.adsV = 0; this.fireT = 0; this.boltT = 0; this.reloadT = 0; this.switchT = 0; this.bobT = 0; this.stepT = 0; this.swayT = Math.random() * 10; this.bandT = 0; this.nadeT = 0;
    this.buildVM();
  }
  get w() { return this.slots[this.cur].w; } get slot() { return this.slots[this.cur]; }
  eyeH() { return lerp(EYE[Math.floor(this.stanceV)], EYE[Math.min(2, Math.floor(this.stanceV) + 1)], this.stanceV % 1); }
  get eye() { return this.eyeH(); }

  // ---------- viewmodel
  buildVM() {
    const g = this.g; if (this.vm) g.vmScene.remove(this.vm);
    const w = this.w; let name = `vm_${this.team}_${w.vm}`; if (w.vm === 'mg' && this.team === 1) name = 'vm_1_ckm'; if (!hasModel(name)) name = `vm_${this.team}_rifle`;
    this.vm = new THREE.Group(); this.vmModel = spawnModel(name, g.mats.vm, false); this.vm.add(this.vmModel); g.vmScene.add(this.vm);
    this.vmn = {}; for (const k of ['bolt', 'clip', 'round', 'mag', 'chandle', 'toggle', 'cover', 'drum', 'belt', 'arms', 'gunmesh']) this.vmn[k] = findNode(this.vmModel, k);
    this.vmBase = {}; for (const k in this.vmn) if (this.vmn[k]) this.vmBase[k] = this.vmn[k].position.clone();
    if (this.vmn.clip) this.vmn.clip.visible = false;
    this.flashSprite = this.flashSprite || new THREE.Mesh(new THREE.PlaneGeometry(.18, .18), new THREE.MeshBasicMaterial({ map: g.fx.smoke.uniforms.tex.value, color: 0xffb060, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
    this.vm.add(this.flashSprite); this.flashSprite.visible = false;
    // where the muzzle sits in the weapon model
    const len = { rifle: .85, sniper: .85, smg: .62, bar: .9, mg: .95, ckm: .95, pistol: .25 }[w.vm] || .8; this.flashSprite.position.set(0, -.01, -len);
    this.vmHip = w.vm === 'pistol' ? new THREE.Vector3(.14, -.15, -.38) : w.vm === 'mg' || w.vm === 'ckm' ? new THREE.Vector3(.17, -.2, -.44) : new THREE.Vector3(.2, -.22, -.4);
    this.vmAds = w.vm === 'pistol' ? new THREE.Vector3(0, -.035, -.34) : new THREE.Vector3(0, -.016, -.2);
  }

  // ---------- per-frame
  update(dt, I) {
    const g = this.g; if (!this.alive) return;
    // look
    const sens = g.settings.sens * (this.ads ? (this.w.scope ? .25 : .55) : 1);
    this.yaw -= I.lookX * sens * .0032; this.pitch -= I.lookY * sens * .0032 * (g.settings.invert ? -1 : 1); I.lookX = I.lookY = 0;
    this.pitch = clamp(this.pitch, this.stance === 2 ? -.5 : -1.45, this.stance === 2 ? .6 : 1.45);
    // stance
    if (I.crouchTap) { this.stance = this.stance === 1 ? 0 : 1; I.crouchTap = false; }
    if (I.proneTap) { this.stance = this.stance === 2 ? 0 : 2; I.proneTap = false; if (this.stance === 2) this.ads = false; }
    this.stanceV += (this.stance - this.stanceV) * Math.min(1, dt * (this.stance === 2 || this.stanceV > 1.2 ? 2.2 : 7));
    // move (RO2 pace: 3.6 m/s walk, ~6.1 sprint, crouch 2.1, crawl 0.9)
    let mx = I.mx, mz = I.my; const ml = Math.hypot(mx, mz); if (ml > 1) { mx /= ml; mz /= ml; }
    const sprint = (I.sprint || ml > .97 && g.touch) && this.stance === 0 && mz > .6 && this.stamina > .05 && !this.ads && this.reloadT <= 0;
    if (sprint && this.stance !== 0) this.stance = 0;
    let base = this.stance === 2 ? .9 : this.stance === 1 ? 2.1 : sprint ? 6.1 : 3.6; if (this.ads) base *= .55; if (this.hp < 40) base *= .75;
    const slope = (g.world.H(this.x - Math.sin(this.yaw) * mz * .8, this.z - Math.cos(this.yaw) * mz * .8) - this.y) / .8; base *= clamp(1 - Math.max(0, slope) * .9, .45, 1.1);
    const wd = g.world.waterDepth(this.x, this.z); if (wd > .3) base *= .5;
    const fx = -Math.sin(this.yaw), fz = -Math.cos(this.yaw), rx = Math.cos(this.yaw), rz = -Math.sin(this.yaw);
    const tvx = (fx * mz + rx * mx) * base, tvz = (fz * mz + rz * mx) * base; const acc = Math.min(1, dt * (this.stance === 2 ? 4 : 9)); this.vx += (tvx - this.vx) * acc; this.vz += (tvz - this.vz) * acc;
    let nx = this.x + this.vx * dt, nz = this.z + this.vz * dt; if (g.world.waterDepth(nx, nz) > 1.35) { nx = this.x; nz = this.z; }
    [nx, nz] = g.world.collide(nx, nz, .35, this.y + 1);
    for (const u of g.queryUnits(nx, nz, 1.5, u => u.alive && u.kind === 'inf')) { const ex = nx - u.x, ez = nz - u.z, e = Math.hypot(ex, ez) || .01; if (e < .7) { nx += ex / e * (.7 - e); nz += ez / e * (.7 - e); } }
    for (const u of g.queryUnits(nx, nz, 4, u => u.alive && (u.kind === 'tank' || u.kind === 'gun'))) { const ex = nx - u.x, ez = nz - u.z, e = Math.hypot(ex, ez) || .01, rr = u.kind === 'tank' ? 2.4 : 1.4; if (e < rr) { nx += ex / e * (rr - e); nz += ez / e * (rr - e); } }
    this.speed = Math.hypot(nx - this.x, nz - this.z) / Math.max(dt, 1e-4); this.x = nx; this.z = nz;
    const gy = g.world.H(this.x, this.z); this.y += (gy - this.y) * Math.min(1, dt * 14);
    this.stamina = clamp(this.stamina + (sprint ? -dt / 9 : dt / (this.speed > .5 ? 7 : 4)), 0, 1);
    // footsteps
    if (this.speed > .6) { this.stepT -= dt * this.speed / (this.stance === 2 ? 1.2 : 1.55); if (this.stepT <= 0) { this.stepT = 1; SND.footstep(this.x, this.y, this.z, g.world.surf(this.x, this.z), true, sprint); } }
    // bleeding & bandage (RO2)
    if (this.bleed > 0 && this.bandT <= 0) { this.hp -= dt * this.bleed; if (this.hp <= 0) { this.die(null, 'bled out'); return; } }
    if (I.bandageTap) { I.bandageTap = false; if (this.bleed > 0 && this.bandT <= 0) { this.bandT = 3.2; SND.mech('switch'); } }
    if (this.bandT > 0) { this.bandT -= dt; if (this.bandT <= 0) this.bleed = 0; }
    this.supp = Math.max(0, this.supp - dt * .35);
    // weapon handling
    this.weapon(dt, I, sprint);
    this.camera(dt, sprint);
  }
  weapon(dt, I, sprint) {
    const g = this.g, s = this.slot, w = s.w; this.fireT -= dt; this.switchT -= dt; this.nadeT -= dt;
    if (I.adsTap) { I.adsTap = false; this.ads = !this.ads; } if (I.adsHold !== undefined && I.adsHold !== null) this.ads = I.adsHold; if (sprint || this.bandT > 0) this.ads = false;
    this.adsV += ((this.ads ? 1 : 0) - this.adsV) * Math.min(1, dt * (w.scope ? 7 : 10));
    if (I.switchTap && this.reloadT <= 0) { I.switchTap = false; this.cur = (this.cur + 1) % this.slots.length; this.switchT = .6; this.boltT = 0; this.ads = false; SND.mech('switch'); this.buildVM(); }
    if (I.reloadTap) { I.reloadTap = false; if (this.reloadT <= 0 && s.mag < w.clip && s.mags > 0 && this.bandT <= 0) { this.reloadT = w.clipLoad && s.mag > 0 ? w.reload * .75 : w.reload; this.ads = false; SND.mech(w.mg ? 'belt' : w.clipLoad ? 'clip' : 'mag'); } }
    if (this.reloadT > 0) { this.reloadT -= dt; if (this.reloadT <= 0) { s.mags--; s.mag = w.clipLoad ? Math.min(w.clip, s.mag + 5) : w.clip; } }
    if (this.boltT > 0) { this.boltT -= dt; }
    // grenade: tap to throw (the fuse is lit as the arm comes back)
    if (I.nadeTap) { I.nadeTap = false; if (this.nades > 0 && this.nadeT <= 0 && this.reloadT <= 0) { this.nades--; this.nadeT = 1.4; SND.mech('pin'); g.after(.45, () => { if (!this.alive || g.player !== this) return; SND.mech('throw'); const d = this.aimDir(); g.throwGrenadeFrom(this, this.x + d.x * .5, this.y + this.eyeH() + .1, this.z + d.z * .5, d.x * 15, d.y * 15 + 3.5, d.z * 15, this.nadeType.fuse - .9); }); } }
    // fire
    const busy = this.reloadT > 0 || this.switchT > 0 || this.boltT > 0 || sprint || this.bandT > 0 || this.nadeT > .8;
    const trig = w.auto ? I.fire : I.firePressed; I.firePressed = false;
    if (trig && !busy && this.fireT <= 0) {
      if (s.mag <= 0) { if (I.fireEdge !== false) SND.mech('dry'); this.fireT = .3; if (s.mags > 0) I.reloadTap = true; }
      else this.shoot();
    }
  }
  aimDir() { E.set(this.pitch + this.kick, this.yaw, 0); return V.set(0, 0, -1).applyEuler(E).clone(); }
  shoot() {
    const g = this.g, s = this.slot, w = s.w; s.mag--; this.fireT = w.rof; if (w.bolt) this.boltT = w.rof; this.lastShotAt = g.time;
    const cam = g.camera; const d = this.aimDir();
    // spread: hip fire is wild, ADS settles; movement, MG unrested and suppression add more; prone / crouched steady it (RO2)
    const rested = w.mg && this.stance === 2; let sp = w.spread + (1 - this.adsV) * (w.vm === 'pistol' ? .025 : .035) + Math.min(this.speed, 4) * .006 + this.supp * .01 + (w.mg && !rested ? .02 : 0);
    sp *= this.stance === 2 ? .6 : this.stance === 1 ? .8 : 1;
    const sw = this.sway(); d.x += (Math.random() - .5) * sp * 2 + sw[0] * .01; d.y += (Math.random() - .5) * sp * 2 + sw[1] * .01; d.z += (Math.random() - .5) * sp * 2; d.normalize();
    // zeroed at 100 m: launch slightly up so the round crosses the sight line there
    const zero = G * 100 / (2 * w.mv * w.mv); d.y += zero;
    const ox = cam.position.x, oy = cam.position.y - .03, oz = cam.position.z;
    const tracer = w.mg && s.mag % 4 === 0; g.fireRound({ x: ox, y: oy, z: oz, vx: d.x * w.mv, vy: d.y * w.mv, vz: d.z * w.mv, w, from: this, team: this.team, t: 0, tracer });
    g.playerShot(w.snd, ox, oy, oz, d.x, d.y, d.z, tracer);
    SND.gunshot(w.snd, ox, oy, oz, true); g.fx.flash(ox + d.x * 1.2, oy, oz + d.z * 1.2, .3);
    g.fx.smoke.emit(ox + d.x * 1.3, oy - .1, oz + d.z * 1.3, d.x * 2, .3, d.z * 2, 1, .15, .9, .8, .78, .75, .18, .95);
    // recoil: muzzle climb (partly recovered) + yaw wander; bipod / prone tame it
    const r = w.recoil * (rested ? .35 : this.stance === 2 ? .6 : this.stance === 1 ? .8 : 1) * (this.adsV > .5 ? .85 : 1.1);
    this.kick += r * (.8 + Math.random() * .4); this.yaw += (Math.random() - .5) * r * .5; this.vmKick = 1; this.flashT = .05;
    if (w.bolt) setTimeout(() => SND.mech('bolt'), 260);
    g.hud.ammoFlash();
  }
  sway() { const t = this.swayT, k = (1 - this.adsV * .6) * (this.stance === 2 ? .35 : this.stance === 1 ? .65 : 1) * (1 + (1 - this.stamina) * 2.5 + this.supp * 3) * (this.w.scope ? 1.4 : 1); return [Math.sin(t * .9) * Math.cos(t * .37) * k, Math.sin(t * 1.3 + 1) * .7 * k]; }
  camera(dt, sprint) {
    const g = this.g, cam = g.camera; this.swayT += dt;
    // kick recovers ~70 % (RO2: the muzzle comes back most of the way, not all)
    const rec = this.kick * Math.min(1, dt * 7); this.kick -= rec; this.pitch += rec * .3;
    const bob = this.speed > .5 ? Math.sin(this.bobT += dt * this.speed * 2.2) : 0; const bobAmp = (1 - this.adsV * .85) * Math.min(1, this.speed / 4) * (sprint ? .055 : .028);
    const sw = this.sway(); const shake = this.supp * .006 * (Math.random() - .5) + (g.shake || 0) * (Math.random() - .5) * .04;
    cam.position.set(this.x, this.y + this.eyeH() + bob * bobAmp, this.z);
    E.set(this.pitch + this.kick + sw[1] * .006 + shake, this.yaw + sw[0] * .006 + shake, sprint ? Math.sin(this.bobT * .5) * .015 : 0); cam.quaternion.setFromEuler(E);
    const fov = lerp(g.baseFov, this.w.scope ? this.w.ads * .9 : this.w.ads * (g.baseFov / 70) * 1.25, this.adsV); if (Math.abs(cam.fov - fov) > .01) { cam.fov = fov; cam.updateProjectionMatrix(); }
    // viewmodel
    const vm = this.vm; this.vmKick = Math.max(0, (this.vmKick || 0) - dt * 9);
    const hip = this.vmHip, ads = this.vmAds; const p = V2.copy(hip).lerp(ads, this.adsV);
    p.x += Math.sin(this.bobT) * bobAmp * .6; p.y += Math.abs(Math.cos(this.bobT)) * bobAmp * .5 - (sprint ? .06 : 0); p.z += this.vmKick * .05;
    let rx = this.vmKick * .06 + (sprint ? -.35 : 0), ry = (sprint ? .6 : 0) + (1 - this.adsV) * .09, rz = (1 - this.adsV) * -.12;
    if (this.reloadT > 0) { const k = Math.sin(Math.min(1, (this.w.reload - this.reloadT) / this.w.reload) * Math.PI); p.y -= k * .12; rx -= k * .25; rz += k * .5; if (this.vmn.clip) this.vmn.clip.visible = this.w.clipLoad && k > .3; }
    else if (this.vmn.clip) this.vmn.clip.visible = false;
    if (this.switchT > 0) p.y -= this.switchT * .4; if (this.bandT > 0) p.y -= .4; if (this.nadeT > .5) { p.y -= .3; }
    if (this.w.scope && this.adsV > .8) vm.visible = false; else vm.visible = true;
    vm.position.copy(p); vm.rotation.set(rx + sw[1] * .01 * (1 - this.adsV * .5), ry + sw[0] * .01, rz);
    // bolt: lift → back → forward → down
    if (this.vmn.bolt && this.w.bolt) { const t = this.boltT > 0 ? 1 - this.boltT / this.w.rof : 1; const ph = t < .25 ? 0 : t < .75 ? (t - .25) / .5 : 1; const back = Math.sin(ph * Math.PI) * .09; this.vmn.bolt.position.z = this.vmBase.bolt.z + back; this.vmn.bolt.rotation.z = (t > .15 && t < .85) ? -.9 : 0; }
    if (this.vmn.chandle && this.vmKick > .5) this.vmn.chandle.position.z = this.vmBase.chandle.z + .03; else if (this.vmn.chandle) this.vmn.chandle.position.z = this.vmBase.chandle.z;
    this.flashT = (this.flashT || 0) - dt; this.flashSprite.visible = this.flashT > 0 && !(this.w.scope && this.adsV > .8); if (this.flashSprite.visible) { this.flashSprite.rotation.z = Math.random() * 6; this.flashSprite.scale.setScalar(.8 + Math.random() * .8); }
    g.vmCamera.fov = 52 - this.adsV * 8; g.vmCamera.updateProjectionMatrix();
  }
  hurt(dmg, from, part, how) {
    if (!this.alive || this.g.god) return; const m = part === 'head' ? 2 : part === 'leg' ? .45 : 1; this.hp -= dmg * m * .9; this.g.hud.damage(from, this); SND.playerHurt(); this.supp = 1;
    if (part !== 'leg' && this.hp > 0) this.bleed = Math.max(this.bleed, 2.2); if (part === 'leg') this.bleed = Math.max(this.bleed, 1.2);
    if (this.hp <= 0) this.die(from, how || (part === 'head' ? 'headshot' : ''));
  }
  die(from, how) { if (!this.alive) return; this.alive = false; this.hp = 0; this.vm.visible = false; this.g.onPlayerDeath(from, how); }
  dispose() { if (this.vm) this.g.vmScene.remove(this.vm); }
}
