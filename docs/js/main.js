// Fall Weiss: Poland 1939 — web edition. Game director: missions, forces, objectives, waves, support fire, ballistics, loop.
import * as THREE from './three.module.min.js';
import { loadModels } from './models.js';
import { buildTextures, TEX } from './textures.js';
import { World, modelMaterial } from './world.js';
import { Fx } from './fx.js';
import { Unit, Squad } from './units.js';
import { Player } from './player.js';
import { Hud } from './hud.js';
import { I, initInput, setEnabled, isTouch, onGesture } from './input.js';
import { SIDE, CLASSES, NAMES, GRENADE, clamp, lerp, sm, segD, Rng, angDiff } from './defs.js';
import * as SND from './audio.js';
import { patchScene, patchFog, FOGU, SkyLight, Post, Grass } from './look.js';
const { Audio } = SND;

const $ = s => document.querySelector(s);
const VERSION = 'web 1.0 · ' + new Date(document.lastModified).toISOString().slice(0, 10);

class Game {
  constructor() {
    this.settings = Object.assign({ quality: isTouch() ? 1 : 2, size: isTouch() ? .5 : .8, sens: 1, invert: false, volume: .9, length: 'short', fps: false }, JSON.parse(localStorage.getItem('fw_settings') || '{}'));
    this.touch = isTouch(); this.state = 'menu'; this.side = 0; this.cls = 'rifleman'; const QP = new URLSearchParams(location.search); this.mi = QP.has('m') ? +QP.get('m') : 1; if (QP.has('side')) this.side = +QP.get('side'); this.autoGo = QP.has('go');
    const cv = $('#c'); this.canvas = cv;
    this.renderer = new THREE.WebGLRenderer({ canvas: cv, antialias: this.settings.quality >= 2, powerPreference: 'high-performance' });
    this.renderer.outputColorSpace = THREE.SRGBColorSpace; this.renderer.toneMapping = THREE.ACESFilmicToneMapping; this.renderer.toneMappingExposure = 1.15; this.renderer.autoClear = false;
    this.renderer.shadowMap.enabled = true; this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.camera = new THREE.PerspectiveCamera(70, 1, .08, 3500); this.baseFov = 70; this.camYaw = 0;
    this.vmScene = new THREE.Scene(); this.vmCamera = new THREE.PerspectiveCamera(52, 1, .01, 10);
    this.vmScene.add(this.vmHemi = new THREE.HemisphereLight(0xffffff, 0x554433, 1.4)); this.vmSun = new THREE.DirectionalLight(0xffffff, 2); this.vmSun.position.set(.4, 1, .3); this.vmScene.add(this.vmSun);
    this.touchUI = initInput(cv, $('#app')); onGesture(() => Audio.init());
    this.hud = new Hud(this, $('#app')); this.hud.show(false);
    window.addEventListener('resize', () => this.resize()); this.resize();
    this.clock = new THREE.Clock(); this.fpsA = []; this.renderer.setAnimationLoop(() => this.frame());
    document.addEventListener('visibilitychange', () => { if (document.hidden && this.state === 'play') this.pause(true); });
    this.menu();
  }
  resize() {
    const q = this.settings.quality, dpr = Math.min(devicePixelRatio || 1, [1, 1.5, 2][q]) * (this.touch ? [.6, .75, .9][q] : 1);
    this.renderer.setPixelRatio(dpr); this.renderer.setSize(innerWidth, innerHeight); this.camera.aspect = this.vmCamera.aspect = innerWidth / innerHeight;
    if (this.post) this.post.resize(); this.baseFov = innerWidth / innerHeight > 1.6 ? 62 : 70; this.camera.fov = this.baseFov; this.camera.updateProjectionMatrix(); this.vmCamera.updateProjectionMatrix(); this.hud.resize(); if (this.fx) this.fx.setScale(innerHeight * dpr);
    $('#rotate').style.display = this.touch && innerHeight > innerWidth && this.state !== 'menu' ? 'flex' : 'none';
  }
  saveSettings() { localStorage.setItem('fw_settings', JSON.stringify(this.settings)); }

  // ================= menus =================
  async menu() {
    this.state = 'menu'; setEnabled(false); this.hud.show(false); $('#menu').style.display = 'flex';
    if (!this.missions) { const D = await (await fetch('data/missions.json')).json(); this.missions = D.missions; }
    const ms = this.missions; $('#ver').textContent = VERSION;
    $('#missions').innerHTML = ms.map((m, i) => `<button class="mis ${i === this.mi ? 'sel' : ''}" data-i="${i}"><b>${m.name}</b><small>${m.date}</small></button>`).join('');
    $('#missions').querySelectorAll('.mis').forEach(b => b.onclick = () => { this.mi = +b.dataset.i; this.menu(); });
    const m = ms[this.mi]; const att = m.attacker;
    $('#brief').innerHTML = `<h2>${m.name}</h2><div class="date">${m.date}</div><p>${m.brief}</p><div class="sides">Attacking: <b>${SIDE[att]}</b> · Defending: <b>${SIDE[1 - att]}</b></div>`;
    $('#sides').innerHTML = [0, 1].map(s => `<button class="side ${s === this.side ? 'sel' : ''}" data-s="${s}">${SIDE[s]}<small>${s === att ? 'attack' : 'defend'}</small></button>`).join('');
    $('#sides').querySelectorAll('.side').forEach(b => b.onclick = () => { this.side = +b.dataset.s; if (this.cls === 'atrifle' && this.side === 0) this.cls = 'rifleman'; this.menu(); });
    $('#classes').innerHTML = CLASSES.filter(c => c.team === undefined || c.team === this.side).map(c => `<button class="cls ${c.key === this.cls ? 'sel' : ''}" data-k="${c.key}">${c.label}<small>${(c.weps[this.side] || []).map(id => id).join(' · ')}</small></button>`).join('');
    $('#classes').querySelectorAll('.cls').forEach(b => b.onclick = () => { this.cls = b.dataset.k; this.menu(); });
    const S = this.settings; const opt = (id, vals, cur, cb) => { $(id).innerHTML = vals.map(([v, l]) => `<button class="${v === cur ? 'sel' : ''}" data-v="${v}">${l}</button>`).join(''); $(id).querySelectorAll('button').forEach(b => b.onclick = () => { cb(b.dataset.v); this.saveSettings(); this.menu(); }); };
    opt('#o-q', [[0, 'Low'], [1, 'Medium'], [2, 'High']], S.quality, v => { S.quality = +v; this.resize(); });
    opt('#o-size', [[.35, 'Skirmish'], [.5, 'Company'], [.8, 'Battalion'], [1, 'Historical']], S.size, v => S.size = +v);
    opt('#o-len', [['short', '15 min'], ['full', 'Historical clock']], S.length, v => S.length = v);
    opt('#o-sens', [[.6, 'Low'], [1, 'Normal'], [1.5, 'High'], [2.2, 'Very high']], S.sens, v => S.sens = +v);
    $('#start').onclick = () => { Audio.init(); this.start(); }; if (this.autoGo) { this.autoGo = false; this.start(); }
    $('#fs').onclick = () => { const e = document.documentElement; (e.requestFullscreen || e.webkitRequestFullscreen)?.call(e).catch?.(() => { }); screen.orientation?.lock?.('landscape').catch(() => { }); };
    this.drawMenuBackdrop();
  }
  drawMenuBackdrop() { this.renderer.setClearColor(0x14120e); this.renderer.clear(); }

  async start() {
    $('#menu').style.display = 'none'; $('#loading').style.display = 'flex'; this.state = 'loading'; const bar = $('#lbar'), lt = $('#ltext');
    const step = (t, f) => { lt.textContent = t; bar.style.width = (f * 100).toFixed(0) + '%'; return new Promise(r => setTimeout(r, 0)); };
    try {
      if (!this.modelsLoaded) { await step('Loading soldiers, tanks and guns…', .02); await loadModels('data/models.bin', f => { bar.style.width = (f * 30).toFixed(0) + '%'; }); this.modelsLoaded = true; }
      if (this.texQ !== this.settings.quality) { await step('Painting textures…', .3); await buildTextures(this.settings.quality, this.renderer, f => { bar.style.width = (30 + f * 30).toFixed(0) + '%'; }); this.texQ = this.settings.quality; this.mats = null; }
      await step('Building the battlefield…', .62); this.cleanup(); this.setupMission(this.missions[this.mi]);
      await step('Deploying forces…', .9); this.deployForces();
      await step('Lighting the sky…', .96); this.grass = new Grass(this.scene, this.world, this.settings.quality); for (const k in this.mats) patchFog(this.mats[k]); patchScene(this.scene); this.bakeSky(true);
      if (this.post) { this.post.dispose(); this.post = null; } if (this.settings.quality >= 1) { this.post = new Post(this.renderer, this.settings.quality); this.post.resize(); }
      await step('Ready', 1);
      this.renderer.compile(this.scene, this.camera);
    } catch (e) { console.error(e); lt.textContent = 'Error: ' + e.message; return; }
    $('#loading').style.display = 'none'; this.state = 'deploy'; this.showDeploy(true); if (new URLSearchParams(location.search).has('go')) $('#go').click();
  }
  cleanup() { if (this.world) this.world.dispose(); if (this.units) for (const u of this.units) u.dispose(); if (this.player) this.player.dispose(); this.player = null; if (this.fx) this.fx.clear(); this.scene = null; SND.stopAmbience(); this.engines = 0; }

  // ================= mission =================
  setupMission(MU) {
    const q = this.settings.quality; this.quality = q; this.lodDist = this.touch ? [25, 38, 60][q] : [35, 55, 80][q];
    if (!this.mats) this.mats = { cloth: modelMaterial('cloth'), steel: modelMaterial('steel'), vm: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .6, metalness: .15 }), burnt: new THREE.MeshStandardMaterial({ color: 0x1c1a18, roughness: 1 }), proxy: new THREE.MeshLambertMaterial({ vertexColors: true }) };
    const sc = this.scene = new THREE.Scene(); this.world = new World(MU, q, sc); const M = this.M = this.world.M;
    this.fx = new Fx(sc, q); this.fx.setScale(innerHeight * this.renderer.getPixelRatio());
    // light: sun from the mission, hemisphere sky/ground, shadow frustum follows the camera
    this.sun = new THREE.DirectionalLight(0xffffff, 3); this.sun.castShadow = q > 0; const ss = [0, 1024, 2048][q]; if (ss) { this.sun.shadow.mapSize.set(ss, ss); const c = this.sun.shadow.camera; c.left = c.bottom = -[0, 45, 70][q]; c.right = c.top = [0, 45, 70][q]; c.near = 1; c.far = 400; this.sun.shadow.bias = -.0004; this.sun.shadow.normalBias = .04; }
    sc.add(this.sun); sc.add(this.sun.target); this.hemi = new THREE.HemisphereLight(0xffffff, 0x4a4030, .5); sc.add(this.hemi); this.skyLight = this.skyLight || new SkyLight(this.renderer);
    sc.fog = new THREE.Fog(new THREE.Color(M.skyHor), M.fogNear, M.fogFar);
    this.dry = !M.hasRain; this.time = 0; this.night = false; this.markers = []; this.grenades = []; this.rounds = []; this.engines = 0;
    this.roundLen = this.settings.length === 'short' ? Math.min(M.time, 900) : M.time; this.timeLeft = this.roundLen;
    this.objectives = M.points.map((p, i) => ({ ...p, r: p.w > 0 && p.d > 0 ? Math.max(p.w, p.d) / 2 : p.r > 0 ? p.r : 14, owner: 1 - M.attacker, prog: 0, i }));
    this.activeObj = 0; this.lockT = 0; this.over = false; this.stats = { kills: 0, deaths: 0, caps: 0, shots: 0, hits: 0 };
    const base = M.tickets || 200; this.tickets = [0, 0]; this.tickets[M.attacker] = Math.round(base * this.settings.size); this.tickets[1 - M.attacker] = Math.round(base * .75 * this.settings.size);
    this.units = []; this.grid = new Map(); this.squads = [[], []]; this.waveT = (M.waves || []).map(w => w.t0); this.supportT = 40; this.shipT = 20; this.barrageLeft = M.attacker === 0 ? Math.round((M.barrage || 0) * .6) : Math.round((M.barrage || 0) * .4);
    this.rearSpawn = [M.spawn[0], M.spawn[1]].map(s => ({ x: s.x, z: s.z }));
    for (const f of this.world.fires) this.fx.fire(f.x, f.y, f.z, f.big);
    SND.startAmbience({}); this.hud.buildMap(); this.frontDir = Math.atan2(M.spawn[1 - M.attacker].x - M.spawn[M.attacker].x, -(M.spawn[1 - M.attacker].z - M.spawn[M.attacker].z));
    if (M.range) { this.tickets = [999, 999]; }
    this.groundBase = this.objectives.reduce((a, o) => a + this.world.H(o.x, o.z), 0) / Math.max(1, this.objectives.length);
    this.applyTod(0);
  }
  bakeSky(force) { if (!force && Math.abs((this.todF || 0) - (this.bakedF ?? -1)) < .04) return; this.bakedF = this.todF || 0; this.scene.environment = this.skyLight.bake(this.world.sky, new THREE.Color(this.M.ground).multiplyScalar(.5)); }
  /** time of day: lerp mission todFrom → todTo over the round (dawn at Westerplatte, dusk and rain on the Bzura) */
  applyTod(f) {
    const M = this.M, w = this.world; const A = M.hasTod ? M.todFrom : null, B = M.hasTod ? M.todTo : null;
    const mixC = (a, b) => new THREE.Color(a).lerp(new THREE.Color(b), f);
    const top = A ? mixC(A.top, B.top) : new THREE.Color(M.skyTop), hor = A ? mixC(A.hor, B.hor) : new THREE.Color(M.skyHor), sunC = A ? mixC(A.sun, B.sun) : new THREE.Color(M.sun);
    const sd = M.sunDir; const elev = A ? lerp(A.dirY, B.dirY, f) : sd[1]; const dir = new THREE.Vector3(sd[0], Math.max(.06, elev), -sd[2]).normalize();
    let rain = 0; if (M.hasRain) { const t = f; rain = t < M.rainStart || t > M.rainEnd ? 0 : sm(M.rainStart, M.rainPeak, t) * (1 - sm(M.rainPeak + .05, M.rainEnd, t)); } this.rainNow = rain;
    const dim = 1 - rain * .45; top.multiplyScalar(dim); hor.lerp(new THREE.Color(0x8a8e92), rain * .6);
    w.skyU.top.value.copy(top); w.skyU.hor.value.copy(hor); w.skyU.sunCol.value.copy(sunC).multiplyScalar(1 - rain * .7); w.skyU.sunDir.value.copy(dir); w.skyU.cover.value = .35 + rain * .55;
    this.sunDir = dir; this.sun.color.copy(sunC); this.sun.intensity = (3.0 + Math.min(1, elev * 3) * 1.6) * (1 - rain * .6); this.hemi.color.copy(top).lerp(new THREE.Color(0xffffff), .3); this.hemi.groundColor.set(0x5a4c36); this.hemi.intensity = .35 + rain * .3;
    this.scene.environmentIntensity = (.75 + rain * .3) * (.7 + Math.min(1, elev * 3) * .3);
    FOGU.fogSunDir.value.copy(dir); FOGU.fogSunCol.value.copy(sunC).multiplyScalar((1 - rain * .8) * (.6 + (1 - Math.min(1, elev * 2.5)) * .6));
    const ffar = M.fogFar * (1 - rain * .45); FOGU.fogP.value.set(1.15 / ffar, .05, (this.groundBase ?? 0) - 1, 1.2 + rain * 1.5 + (A ? lerp(A.fog ?? 0, B.fog ?? 0, f) : 0) * 3);
    this.scene.fog.color.copy(hor); const fogK = (A ? lerp(A.fog ?? 0, B.fog ?? 0, f) : 0); this.scene.fog.near = M.fogFar * .55 * (1 - rain * .4); this.scene.fog.far = M.fogFar * 1.25 * (1 - rain * .4) * (1 - fogK * .3);
    this.fx.setFog(hor, this.scene.fog.near, this.scene.fog.far); this.renderer.toneMappingExposure = 1.05 + (1 - Math.min(1, elev * 3)) * .35; this.exposure = 1.3 + (1 - Math.min(1, elev * 3)) * .6 + rain * .25; this.todF = f;
    this.vmHemi.color.copy(this.hemi.color); this.vmSun.color.copy(sunC); this.vmSun.intensity = this.sun.intensity * .45; this.vmHemi.intensity = .9;
    this.fx.setRain(rain > .02, rain, this.camera); SND.setRain(rain); this.dry = rain < .05 && !M.hasRain;
  }

  // ================= forces =================
  spawnUnit(team, type, x, z, o = {}) { const u = new Unit(this, team, type, x, z, o); this.units.push(u); if (o.squad) o.squad.members.push(u); return u; }
  removeUnit(u, keepVisual) { const i = this.units.indexOf(u); if (i >= 0) this.units.splice(i, 1); if (!keepVisual || true) u.dispose(); }
  deployForces() {
    const M = this.M, w = this.world, S = this.settings.size, def = 1 - M.attacker, R = new Rng(M.seed + 7);
    const facing = (x, z, tx, tz) => Math.atan2(-(tx - x), -(tz - z));
    const place = (team, list, isDef) => {
      for (const f of list) {
        const n = Math.max(f.type === 'tank' || f.type === 'at' ? 1 : 1, Math.round(f.n * (f.type === 'tank' || f.type === 'at' ? Math.max(.5, S) : S))); if (f.type === 'tank' && n < 1) continue;
        const anchor = f.loc >= 0 && this.objectives[f.loc] ? this.objectives[f.loc] : null; const sp = anchor ? anchor : M.spawn[team];
        const sq = new Squad(this, team, f.type); sq.home = anchor ? f.loc : -1; this.squads[team].push(sq);
        const enemySp = M.spawn[1 - team];
        for (let i = 0; i < n; i++) {
          let x, z; const tries = 20;
          for (let k = 0; k < tries; k++) { const a = R.next() * 6.28, r = Math.sqrt(R.next()) * (anchor ? anchor.r * .9 : f.sp || 20); x = sp.x + Math.cos(a) * r; z = sp.z + Math.sin(a) * r; if (!w.blocked(x, z, 1) && w.waterDepth(x, z) < .3) break; }
          if (f.type === 'at') { const g = this.spawnUnit(team, 'at', x, z, { squad: sq, yaw: facing(x, z, enemySp.x, enemySp.z) }); for (let c = 0; c < 2; c++) { const cu = this.spawnUnit(team, 'crew', x + (c ? 1.5 : -1.5), z + 1.2, { squad: sq, yaw: g.yaw }); cu.stance = 1; g.crew.push(cu); cu.gun = g; } continue; }
          this.spawnUnit(team, f.type, x, z, { squad: sq, yaw: facing(x, z, enemySp.x, enemySp.z), leader: i === 0 && f.type === 'rifle' });
        }
        if (anchor && isDef) sq.setOrder({ kind: 'defend', x: anchor.x, z: anchor.z, r: anchor.r }); else sq.setOrder({ kind: 'move', x: sp.x, z: sp.z, r: 20 });
      }
    };
    place(0, M.forces0 || [], def === 0); place(1, M.forces1 || [], def === 1);
    if (M.range) { const s = M.spawn[this.side], t = M.points[0] || { x: s.x, z: s.z - 200 }, dx = t.x - s.x, dz = t.z - s.z, L = Math.hypot(dx, dz) || 1, ux = dx / L, uz = dz / L; this.rangeDummies = [];
      for (const d of [10, 25, 50, 100, 200]) for (const off of [-3, 3]) { const x = s.x + ux * d - uz * off, z = s.z + uz * d + ux * off; const u = this.spawnUnit(1 - this.side, 'rifle', x, z, { yaw: Math.atan2(ux, uz) }); u.dummy = true; u.spot = [x, z]; this.rangeDummies.push(u); }
      for (const d of [120, 180]) { const x = s.x + ux * d + uz * 25, z = s.z + uz * d - ux * 25; const u = this.spawnUnit(1 - this.side, 'tank', x, z, { yaw: Math.atan2(-uz, ux) }); u.dummy = true; u.spot = [x, z]; }
      const w = this.spawnUnit(1 - this.side, 'rifle', s.x + ux * 60, s.z + uz * 60); w.dummy = 'walk'; w.spot = [s.x + ux * 60, s.z + uz * 60]; this.walker = w; }
    this.playerSquad = new Squad(this, this.side, 'rifle'); this.squads[this.side].push(this.playerSquad);
    this.commander(true);
  }
  showDeploy(first) {
    setEnabled(false); this.hud.show(true); const d = $('#deploy'); d.style.display = 'flex'; const M = this.M;
    const cs = CLASSES.filter(c => c.team === undefined || c.team === this.side);
    const sp = this.spawnPoint(this.side);
    d.innerHTML = `<div class="panel"><h2>${first ? M.name : 'Killed in action'}</h2><p>${first ? `${SIDE[this.side]} — ${this.side === M.attacker ? 'take the positions in order' : 'hold the positions until the clock runs out'}.` : (this.deathMsg || '')}</p>
      <div class="row">${cs.map(c => `<button class="cls ${c.key === this.cls ? 'sel' : ''}" data-k="${c.key}">${c.label}<small>${c.weps[this.side].join(' · ')}</small></button>`).join('')}</div>
      <p class="small">Reinforcements: ${Math.max(0, this.tickets[this.side])} · Deploying at ${sp.label}</p>
      <button id="go" class="big">${first ? 'Deploy' : 'Redeploy'}</button> <button id="quit">Quit to menu</button></div>`;
    d.querySelectorAll('.cls').forEach(b => b.onclick = () => { this.cls = b.dataset.k; this.showDeploy(first); });
    const go = () => { if (!first && this.redeployT > 0) return; d.style.display = 'none'; this.spawnPlayer(); this.state = 'play'; setEnabled(true); if (!this.touch) this.canvas.requestPointerLock?.(); if (first) this.hud.banner(M.name, M.date, 5); };
    d.querySelector('#go').onclick = go; d.querySelector('#quit').onclick = () => { d.style.display = 'none'; this.cleanup(); this.menu(); };
    if (!first) { const b = d.querySelector('#go'); const tick = () => { if (this.redeployT > 0) { b.textContent = `Redeploy in ${Math.ceil(this.redeployT)}`; b.disabled = true; setTimeout(tick, 200); } else { b.textContent = 'Redeploy'; b.disabled = false; } }; tick(); }
    // spectate from above the player's spawn while choosing
    const s = this.spawnPoint(this.side); this.camera.position.set(s.x, this.world.H(s.x, s.z) + 40, s.z + 30); this.camera.lookAt(this.objectives[Math.min(this.activeObj, this.objectives.length - 1)].x, 0, this.objectives[Math.min(this.activeObj, this.objectives.length - 1)].z);
  }
  /** spawn: rear spawn, or forward near the last captured objective (attackers) / behind the active one (defenders) */
  spawnPoint(team) {
    const M = this.M, att = M.attacker; let p = this.rearSpawn[team], label = 'the assembly area';
    if (team === att && this.activeObj > 0) { const o = this.objectives[this.activeObj - 1]; const s = M.spawn[att]; const dx = s.x - o.x, dz = s.z - o.z, d = Math.hypot(dx, dz) || 1; p = { x: o.x + dx / d * 60, z: o.z + dz / d * 60 }; label = o.n + ' (rear)'; }
    if (team !== att && this.activeObj < this.objectives.length) { const o = this.objectives[Math.min(this.objectives.length - 1, this.activeObj + 1)], a = this.objectives[this.activeObj]; const s = M.spawn[att]; const dx = a.x - s.x, dz = a.z - s.z, d = Math.hypot(dx, dz) || 1; const tgt = this.activeObj + 1 < this.objectives.length ? o : a; p = { x: tgt.x + dx / d * 40, z: tgt.z + dz / d * 40 }; label = tgt.n; }
    if (this.world.blocked(p.x, p.z, 2) || this.world.waterDepth(p.x, p.z) > .2) p = this.rearSpawn[team];
    return { ...p, label };
  }
  spawnPlayer() {
    const sp = this.spawnPoint(this.side); let x = sp.x + (Math.random() - .5) * 8, z = sp.z + (Math.random() - .5) * 8; [x, z] = this.world.collide(x, z, .5);
    if (this.player) this.player.dispose(); const p = this.player = new Player(this, this.side, this.cls, x, z); if (this.tickets[this.side] > 0 && !this.M.range) this.tickets[this.side]--;
    const o = this.objectives[Math.min(this.activeObj, this.objectives.length - 1)]; p.yaw = Math.atan2(-(o.x - x), -(o.z - z));
    // a section of men deploys with you
    const sq = this.playerSquad; sq.members = sq.members.filter(m => m.alive); const need = Math.max(0, Math.round(5 * Math.max(.6, this.settings.size)) - sq.alive.length);
    for (let i = 0; i < need && this.tickets[this.side] > 0; i++) { const u = this.spawnUnit(this.side, i === 1 ? 'mg' : 'rifle', x + (Math.random() - .5) * 10, z + (Math.random() - .5) * 10, { squad: sq, yaw: p.yaw }); this.tickets[this.side]--; }
    sq.members.push(p); sq.setOrder(this.orderFor(this.side, sq)); this.hud.hint(this.touch ? 'Left: move · Right: look · FIRE · AIM' : 'WASD move · Mouse aim · LMB fire · RMB aim · C/Z stance · R reload · G grenade · M map', 6);
  }
  onPlayerDeath(from, how) {
    this.stats.deaths++; this.deathMsg = from ? `${how === 'crushed' ? 'Crushed by' : 'Killed by'} ${from.name || NAMES[from.team][from.type] || 'the enemy'}${from.kind === 'inf' ? ` (${NAMES[from.team][from.type] || ''})` : ''}${how === 'headshot' ? ' — headshot' : ''}, ${Math.hypot(from.x - this.player.x, from.z - this.player.z) | 0} m` : how === 'bled out' ? 'Bled to death.' : 'Killed.';
    this.hud.feed(`<b>You</b> ${how === 'bled out' ? 'bled out' : 'were killed'}`, '#e88'); this.redeployT = 6; this.state = 'dead'; setEnabled(false); if (document.pointerLockElement) document.exitPointerLock();
    this.deadCam = { x: this.player.x, y: this.player.y + .4, z: this.player.z, t: 0 };
    setTimeout(() => { if (this.state === 'dead') this.showDeploy(false); }, 1800);
  }
  onDeath(u, from, how) {
    if (!this.M.range) this.tickets[u.team] -= u.kind === 'tank' ? 3 : u.kind === 'gun' ? 0 : 1;
    if (u.dummy) setTimeout(() => { if (!this.scene || !this.M.range) return; const n = this.spawnUnit(u.team, u.type, u.spot[0], u.spot[1], { yaw: u.yaw }); n.dummy = u.dummy; n.spot = u.spot; this.hud.hint('Target reset', 1); }, 3000);
    if (from && from.isPlayer) { if (from.team === u.team) { this.hud.feed(`<b>You</b> killed a friendly: ${u.name}`, '#f80'); } else { this.stats.kills++; this.player.score += u.kind === 'tank' ? 50 : 10; this.hud.hit(true); this.hud.feed(`<b>You</b> ✠ ${u.kind === 'tank' ? NAMES[u.team].tank : u.name}${how === 'headshot' ? ' (head)' : ''}`, '#fda'); } }
    else if (u.kind === 'tank') this.hud.feed(`${NAMES[u.team].tank} knocked out`, u.team === this.side ? '#e88' : '#ad8');
    if (u.gun) { /* crewman */ }
  }

  // ================= AI commanders =================
  orderFor(team, sq) {
    const M = this.M, att = M.attacker, a = Math.min(this.activeObj, this.objectives.length - 1), o = this.objectives[a];
    if (team === att) { // attack the active objective; spread squads across its width so they don't all funnel down the road
      const s = M.spawn[att], dx = o.x - s.x, dz = o.z - s.z, d = Math.hypot(dx, dz) || 1, px = -dz / d, pz = dx / d; const lane = ((this.squads[team].indexOf(sq) % 3) - 1) * Math.min(45, o.r + 15);
      return { kind: 'attack', x: o.x + px * lane * .6, z: o.z + pz * lane * .6, r: Math.max(10, o.r * .6), run: false };
    }
    // defenders: own home point if still held, otherwise the active objective
    const home = sq.home >= 0 && sq.home >= a ? this.objectives[sq.home] : o;
    return { kind: 'defend', x: home.x, z: home.z, r: home.r, run: home !== this.objectives[sq.home] };
  }
  commander(force) {
    for (const team of [0, 1]) for (const sq of this.squads[team]) { if (!sq.alive.length) continue; const o = this.orderFor(team, sq); const cur = sq.order; if (force || !cur || cur.kind !== o.kind || Math.hypot(cur.x - o.x, cur.z - o.z) > 8) sq.setOrder(o); }
  }
  /** cover spots near (x,z): trench points, sandbags, walls, building corners, preferring the side away from the enemy */
  coverSpots(x, z, r, team, n) {
    const w = this.world, M = this.M, es = M.spawn[1 - team], out = [];
    const cand = [];
    for (const t of M.trenches || []) for (let i = 0; i + 3 < t.pts.length; i += 2) { const ax = t.pts[i], az = t.pts[i + 1], bx = t.pts[i + 2], bz = t.pts[i + 3], L = Math.hypot(bx - ax, bz - az); for (let s = 0; s < L; s += 3) { const px = ax + (bx - ax) * s / L, pz = az + (bz - az) * s / L; if (Math.hypot(px - x, pz - z) < r) cand.push([px, pz, 3]); } }
    for (const c of w.covers) { if (Math.hypot(c.x - x, c.z - z) < r) { const dx = es.x - c.x, dz = es.z - c.z, d = Math.hypot(dx, dz) || 1; cand.push([c.x - dx / d * 1.2, c.z - dz / d * 1.2, 2]); } }
    for (const o of w.obst) { if (o.low || o.kind === 'wreck') continue; const cx = (o.x0 + o.x1) / 2, cz = (o.z0 + o.z1) / 2; if (Math.hypot(cx - x, cz - z) > r + 10) continue; for (const [px, pz] of [[o.x0 - 1, o.z0 - 1], [o.x1 + 1, o.z0 - 1], [o.x0 - 1, o.z1 + 1], [o.x1 + 1, o.z1 + 1]]) if (Math.hypot(px - x, pz - z) < r) cand.push([px, pz, 1.5]); }
    for (let i = 0; i < 10; i++) { const a = Math.random() * 6.28, d = Math.sqrt(Math.random()) * r; cand.push([x + Math.cos(a) * d, z + Math.sin(a) * d, 0]); }
    cand.sort((a, b) => b[2] - a[2] + (Math.random() - .5) * 1.5);
    for (const c of cand) { if (out.length >= n) break; if (out.some(o => Math.hypot(o[0] - c[0], o[1] - c[1]) < 2.5)) continue; if (w.blocked(c[0], c[1], .4) || w.waterDepth(c[0], c[1]) > .3) continue; out.push([c[0], c[1]]); }
    while (out.length < n) out.push([x + (Math.random() - .5) * r, z + (Math.random() - .5) * r]);
    return out;
  }
  queryUnits(x, z, r, f) {
    const out = [], c = 25, x0 = Math.floor((x - r) / c), x1 = Math.floor((x + r) / c), z0 = Math.floor((z - r) / c), z1 = Math.floor((z + r) / c);
    for (let i = x0; i <= x1; i++) for (let j = z0; j <= z1; j++) { const l = this.grid.get(i * 10007 + j); if (l) for (const u of l) if (Math.abs(u.x - x) < r && Math.abs(u.z - z) < r && Math.hypot(u.x - x, u.z - z) < r && (!f || f(u))) out.push(u); }
    return out;
  }
  rebuildGrid() { this.grid.clear(); const c = 25; const add = u => { const k = Math.floor(u.x / c) * 10007 + Math.floor(u.z / c); let l = this.grid.get(k); if (!l) this.grid.set(k, l = []); l.push(u); }; for (const u of this.units) if (u.alive) add(u); if (this.player && this.player.alive) add(this.player); }

  // ================= combat helpers =================
  get playerHitMul() { return .55; } // the player is a squad of one: AI hits on you are toned down (RO2 bots are similarly forgiving)
  bulletNearPlayer(ox, oy, oz, dx, dy, dz, len, from, supersonic, scale = 1) {
    const p = this.player; if (!p || !p.alive || (from && from.team === p.team && !from.isPlayer && Math.random() < .7)) return;
    const hx = p.x - ox, hy = p.y + p.eyeH() - oy, hz = p.z - oz; const t = hx * dx + hy * dy + hz * dz; if (t < 2 || t > len) return;
    const cx = ox + dx * t - p.x, cy = oy + dy * t - (p.y + p.eyeH()), cz = oz + dz * t - p.z, d = Math.hypot(cx, cy, cz);
    if (d < 5 * scale) { const side = Math.sin(Math.atan2(cx, -cz) + this.camYaw); SND.bulletPass(d / scale, supersonic, Math.max(-.8, Math.min(.8, side))); p.supp = Math.min(1, p.supp + (.35 - d * .05) * scale); if (d < 1.5) this.shake = Math.min(1, (this.shake || 0) + .3); }
  }
  fireRound(r) { this.rounds.push(r); this.stats.shots++; if (r.tracer) this.fx.tracer(r.x, r.y, r.z, r.vx / r.w.mv, r.vy / r.w.mv, r.vz / r.w.mv, r.w.mv * .9, 10, [1, .5, .22], 2); }
  updateRounds(dt) {
    const w = this.world;
    for (let i = 0; i < this.rounds.length; i++) {
      const r = this.rounds[i]; let rem = dt; let done = false;
      while (rem > 0 && !done) {
        const h = Math.min(rem, .02); rem -= h; r.t += h; const ax = r.x, ay = r.y, az = r.z; r.vy -= 9.81 * h; const drag = Math.pow(.9985, h * 60); r.vx *= drag; r.vy *= drag; r.vz *= drag;
        const bx = ax + r.vx * h, by = ay + r.vy * h, bz = az + r.vz * h;
        // world first
        const blk = w.rayBlock(ax, ay, az, bx, by, bz); let tW = blk ? blk.t : 2;
        // men
        let best = null, bt = tW; const L2 = (bx - ax) ** 2 + (by - ay) ** 2 + (bz - az) ** 2; const mxp = (ax + bx) / 2, mzp = (az + bz) / 2, rad = Math.sqrt(L2) / 2 + 3;
        for (const u of this.queryUnits(mxp, mzp, rad)) { if (u === r.from || u.isPlayer) continue;
          if (u.kind === 'tank' || u.kind === 'gun') { const hh = u.hullHit(ax, ay, az, bx, by, bz); if (hh && hh.t < bt) { bt = hh.t; best = { u, part: 'hull', face: hh.face }; } continue; }
          if (!u.alive && u.dead > 2) continue;
          for (const s of u.spheres()) { const t = segSphere(ax, ay, az, bx, by, bz, s[0], s[1], s[2], s[3]); if (t != null && t < bt) { bt = t; best = { u, part: s[4] }; } }
          // near miss: suppress
          if (u.alive && u.team !== r.team) { const t = clamp(((u.x - ax) * (bx - ax) + (u.y + 1 - ay) * (by - ay) + (u.z - az) * (bz - az)) / L2, 0, 1); const d = Math.hypot(ax + (bx - ax) * t - u.x, ay + (by - ay) * t - u.y - 1, az + (bz - az) * t - u.z); if (d < 2.5 && !r.sup?.has(u)) { (r.sup || (r.sup = new Set())).add(u); u.supp = Math.min(1, u.supp + .3); u.target = u.target || r.from; u.tgtSeen = this.time - 1.5; } } }
        if (best && best.u.alive) {
          const u = best.u, hx = ax + (bx - ax) * bt, hy = ay + (by - ay) * bt, hz = az + (bz - az) * bt;
          if (best.part === 'hull') { if (r.w.pen100) { const d = Math.hypot(hx - (r.from?.x || hx), hz - (r.from?.z || hz)); this.armourHit(u, r.from, r.w.pen100 - r.w.penDrop * Math.max(0, d - 100) / 100, r.from?.x ?? ax, r.from?.z ?? az, true); } else { this.fx.impact(hx, hy, hz, 'metal'); SND.impact(hx, hy, hz, 'metal'); } }
          else { const speedK = Math.hypot(r.vx, r.vy, r.vz) / r.w.mv; const kill = u.hurt(r.w.dmg * Math.max(.4, speedK), r.from, best.part); if (r.from?.isPlayer) { this.stats.hits++; if (!kill) this.hud.hit(false); SND.hitMarker(); } }
          done = true;
        } else if (blk) { this.fx.impact(blk.x, blk.y, blk.z, w.waterDepth(blk.x, blk.z) > .1 ? 'water' : blk.mat); SND.impact(blk.x, blk.y, blk.z, blk.mat); done = true; }
        else { r.x = bx; r.y = by; r.z = bz; if (r.t > 3) done = true; }
      }
      if (done) { this.rounds.splice(i--, 1); }
    }
  }
  /** shell or AT-rifle bullet against plate: angle of the hit face decides front / side / rear armour, plus obliquity */
  armourHit(t, from, pen, fx, fz, small) {
    if (!t.alive) return; const a = Math.atan2(-(fx - t.x), -(fz - t.z)); const rel = Math.abs(angDiff(t.yaw, a)); // 0 = shooter in front
    const T = t.T; let arm, face; if (t.kind === 'gun') { arm = 5; face = 'shield'; } else if (rel < .7) { arm = T.armF; face = 'front'; } else if (rel > 2.4) { arm = T.armR; face = 'rear'; } else { arm = T.armS; face = 'side'; }
    const obl = rel < .7 ? rel : rel > 2.4 ? Math.PI - rel : Math.abs(Math.PI / 2 - rel); const eff = arm / Math.max(.45, Math.cos(obl));
    const hx = t.x, hy = t.y + 1.4, hz = t.z; const penetrated = pen + (Math.random() - .5) * 4 > eff;
    if (penetrated) { const dmg = small ? 60 + Math.random() * 60 : t.kind === 'gun' ? 200 : 170 + Math.random() * 140; this.fx.impact(hx, hy, hz, 'metal'); SND.impact(hx, hy, hz, 'metal'); this.fx.flash(hx, hy, hz, .8); if (t.kind === 'gun') { for (const c of t.crew) if (c.alive && Math.random() < .6) c.hurt(100, from, 'chest'); } const killed = t.hurt(dmg, from, 'hull'); t.supp = 1;
      if (from?.isPlayer) { this.hud.hit(killed); this.hud.hint(killed ? `${NAMES[t.team][t.type]} knocked out` : `Penetration — ${face} armour`, 2); } }
    else { this.fx.impact(hx, hy, hz, 'metal'); SND.impact(hx, hy, hz, 'metal'); if (from?.isPlayer) this.hud.hint(`Ricochet — ${face} armour too thick at this range`, 2); }
  }
  explode(x, y, z, size, from, radius) {
    this.fx.explosion(x, y, z, size); SND.explosion(x, y, z, size);
    const R = radius || 4 * size;
    for (const u of this.queryUnits(x, z, R * 4)) {
      const d = Math.hypot(u.x - x, u.z - z, (u.y || 0) - y); if (u.isPlayer) { if (d < R * 4) { u.supp = 1; this.shake = Math.min(1.5, (this.shake || 0) + (1 - d / (R * 4)) * 1.5); } if (d < R * 2.2) { const cover = this.world.rayBlock(x, y + .5, z, u.x, u.y + 1, u.z, { noTrees: true }) ? .3 : 1; const dmg = (d < R * .6 ? 220 : d < R ? 120 : 60 * (1 - (d - R) / (R * 1.2))) * cover * (u.stance === 2 ? .6 : 1); if (dmg > 5) u.hurt(dmg, from, 'chest', 'blast'); } continue; }
      if (!u.alive) continue; if (u.kind === 'tank') { if (d < 2.5) u.hurt(20, from, 'hull'); continue; }
      u.supp = Math.min(1, u.supp + Math.max(0, 1 - d / (R * 4)) * 1.2);
      if (d < R * 2.2) { const cover = this.world.rayBlock(x, y + .5, z, u.x, u.y + 1, u.z, { noTrees: true }) ? .25 : 1; const dmg = (d < R * .6 ? 300 : d < R ? 140 : 70 * (1 - (d - R) / (R * 1.2))) * cover * (u.stance === 2 ? .6 : 1) * (u.kind === 'gun' ? .4 : 1); if (dmg > 5) u.hurt(dmg, from, 'chest', { how: 'blast' }); }
    }
    if (this.player && this.player.alive && Math.hypot(this.player.x - x, this.player.z - z) < 60) this.shake = Math.min(1.5, (this.shake || 0) + .4);
  }
  throwGrenade(u, tx, tz) { const d = Math.hypot(tx - u.x, tz - u.z), T = Math.max(.9, d / 14); const y0 = u.y + 1.6; const ty = this.world.H(tx, tz); this.throwGrenadeFrom(u, u.x, y0, u.z, (tx - u.x) / T, (ty - y0 + 4.9 * T * T) / T, (tz - u.z) / T, GRENADE[u.team].fuse - 1); SND.mech; }
  throwGrenadeFrom(u, x, y, z, vx, vy, vz, fuse) {
    const geo = this.nadeGeo || (this.nadeGeo = (() => { const g = new THREE.CylinderGeometry(.03, .03, .36, 6); g.rotateX(Math.PI / 2); return g; })()); const m = new THREE.Mesh(geo, this.mats.steel); m.position.set(x, y, z); this.scene.add(m);
    this.grenades.push({ x, y, z, vx, vy, vz, fuse: Math.max(.6, fuse), from: u, m, team: u.team, warned: false });
  }
  updateGrenades(dt) {
    for (let i = 0; i < this.grenades.length; i++) { const n = this.grenades[i]; n.fuse -= dt; n.vy -= 9.81 * dt; let nx = n.x + n.vx * dt, ny = n.y + n.vy * dt, nz = n.z + n.vz * dt; const gy = this.world.H(nx, nz);
      const blk = this.world.rayBlock(n.x, n.y, n.z, nx, Math.max(ny, gy + .05), nz, { noTrees: true }); if (blk && blk.mat !== 'dirt') { n.vx *= -.35; n.vz *= -.35; nx = n.x; nz = n.z; }
      if (ny < gy + .05) { ny = gy + .05; n.vy *= -.25; n.vx *= .55; n.vz *= .55; if (Math.abs(n.vy) > 1) SND.impact(nx, ny, nz, 'wood'); }
      n.x = nx; n.y = ny; n.z = nz; n.m.position.set(nx, ny, nz); n.m.rotation.x += dt * 8;
      // men nearby who see it dive away
      if (!n.warned && n.fuse < 2.5) { n.warned = true; for (const u of this.queryUnits(n.x, n.z, 9, u => u.alive && u.kind === 'inf' && !u.isPlayer)) { const dx = u.x - n.x, dz = u.z - n.z, d = Math.hypot(dx, dz) || 1; u.wpt = [u.x + dx / d * 8, u.z + dz / d * 8]; u.stance = 0; u.supp = Math.max(u.supp, .5); u.mode = 'charge'; setTimeout(() => { if (u.alive) { u.stance = 2; u.mode = 'hold'; } }, 1400); } }
      if (n.fuse <= 0) { this.scene.remove(n.m); this.grenades.splice(i--, 1); this.explode(n.x, n.y, n.z, .6, n.from, GRENADE[n.team].kill); }
    }
  }

  // ================= support fire: Stukas, artillery, the Schleswig-Holstein =================
  updateSupport(dt) {
    const M = this.M; if (M.range) return; const att = M.attacker, o = this.objectives[this.activeObj]; if (!o) return;
    // opening barrage on the first positions, then periodic fire missions on the active objective
    this.supportT -= dt;
    if (this.supportT <= 0 && this.barrageLeft > 0 && this.lockT <= 0) {
      this.supportT = 55 + Math.random() * 50; const shells = Math.min(this.barrageLeft, 4 + (Math.random() * 4 | 0)); this.barrageLeft -= shells;
      const attackersNear = this.queryUnits(o.x, o.z, o.r + 35, u => u.alive && u.team === att).length; if (attackersNear > 2) { this.barrageLeft += shells; this.supportT = 20; return; } // don't shell your own men
      if (att === 0 && Math.random() < .5) this.stuka(o); else this.barrage(o, shells, att);
    }
    if (M.hasShip && att === 0) { this.shipT -= dt; if (this.shipT <= 0) { this.shipT = 30 + Math.random() * 30; const t = this.objectives[Math.min(this.objectives.length - 1, this.activeObj + (Math.random() < .5 ? 1 : 0))]; this.navalSalvo(t); } }
  }
  barrage(o, n, team) {
    this.hud.hint(team === this.side ? 'Friendly artillery on the objective' : 'Incoming artillery!', 3); this.markers.push({ x: o.x, z: o.z, r: o.r + 10, col: '#f84', t: this.time + 20 });
    for (let i = 0; i < n; i++) setTimeout(() => { if (!this.scene) return; const a = Math.random() * 6.28, d = Math.random() * (o.r + 15), x = o.x + Math.cos(a) * d, z = o.z + Math.sin(a) * d, y = this.world.H(x, z); SND.shellWhistle(x, y, z, 1.4); setTimeout(() => this.scene && this.explode(x, y, z, 1.1, null, 5), 1400); }, i * (900 + Math.random() * 900));
  }
  navalSalvo(o) { const M = this.M; SND.gunshot('naval', M.shipX, 8, M.shipZ); for (let i = 0; i < 2; i++) setTimeout(() => { if (!this.scene) return; const x = o.x + (Math.random() - .5) * 50, z = o.z + (Math.random() - .5) * 50, y = this.world.H(x, z); SND.shellWhistle(x, y, z, 1.8); setTimeout(() => this.scene && this.explode(x, y, z, 1.8, null, 8), 1800); }, 2500 + i * 700); }
  stuka(o) {
    if (!this.stukaModel) return; const plane = this.stukaModel.clone(); this.scene.add(plane); const start = { x: o.x - 900, y: 600, z: o.z + 300 }; let t = 0; const T = 14; let dropped = false; const pos = { x: 0, y: 0, z: 0, dive: 0 };
    const tick = () => { if (!this.scene) return; t += 1 / 30; const f = t / T; let x, y, z, dive = 0;
      if (f < .45) { const k = f / .45; x = lerp(start.x, o.x - 300, k); z = lerp(start.z, o.z + 100, k); y = 600; }
      else if (f < .75) { const k = (f - .45) / .3; dive = k; x = lerp(o.x - 300, o.x, k); z = lerp(o.z + 100, o.z, k); y = lerp(600, 120, k * k); }
      else { const k = (f - .75) / .25; x = lerp(o.x, o.x + 500, k); z = o.z - k * 100; y = lerp(120, 500, Math.sqrt(k)); dive = Math.max(0, 1 - k * 3); if (!dropped) { dropped = true; const bx = o.x + (Math.random() - .5) * 30, bz = o.z + (Math.random() - .5) * 30; setTimeout(() => this.scene && this.explode(bx, this.world.H(bx, bz), bz, 2.2, null, 9), 1500); } }
      const ox = plane.position.x, oy = plane.position.y, oz = plane.position.z; plane.position.set(x, y, z); if (t > .1) plane.lookAt(x + (x - ox) * 10, y + (y - oy) * 10, z + (z - oz) * 10); plane.rotateY(Math.PI);
      Object.assign(pos, { x, y, z, dive }); if (t < T) setTimeout(tick, 33); else this.scene.remove(plane); };
    tick(); SND.stukaDive(() => pos, T); this.hud.hint(this.side === 0 ? 'Stukas on the objective' : 'Stukas — take cover!', 3);
  }

  // ================= objectives =================
  inObj(o, x, z) { if (o.w > 0 && o.d > 0) { const c = Math.cos(o.rot || 0), s = Math.sin(o.rot || 0), dx = x - o.x, dz = z - o.z; const lx = dx * c + dz * s, lz = -dx * s + dz * c; return Math.abs(lx) < o.w / 2 && Math.abs(lz) < o.d / 2; } return Math.hypot(x - o.x, z - o.z) < o.r; }
  updateObjectives(dt) {
    const M = this.M, att = M.attacker; if (this.over || M.range) return; this.lockT -= dt;
    const o = this.objectives[this.activeObj]; if (!o || this.lockT > 0) return;
    let na = 0, nd = 0; const all = [...this.units, ...(this.player && this.player.alive ? [this.player] : [])];
    for (const u of all) if (u.alive && (u.kind === 'inf') && this.inObj(o, u.x, u.z)) { if (u.team === att) na++; else nd++; } // only infantry capture (RE-CAP)
    o.na = na; o.nd = nd;
    if (na > nd) o.prog = Math.min(1, o.prog + dt * Math.min(6, na - nd) / 48); else if (nd > 0 && na === 0) o.prog = Math.max(0, o.prog - dt / 20);
    if (o.prog >= 1) {
      o.owner = att; o.prog = 1; this.activeObj++; this.stats.caps++; this.lockT = 25; const mine = att === this.side;
      this.hud.banner(`${o.n} ${mine ? 'captured' : 'lost'}`, this.activeObj < this.objectives.length ? (mine ? 'Regroup — the next position opens in 25 s' : 'Fall back to the next position!') : '', 4);
      if (this.player && this.inObj(o, this.player.x, this.player.z) && mine) this.player.score += 50;
      this.tickets[att] += Math.round(15 * this.settings.size); // reinforcements for a capture
      this.commander(true);
      if (this.activeObj >= this.objectives.length) this.end(att);
    }
  }
  updateWaves(dt) {
    const M = this.M; if (M.range) return;
    (M.waves || []).forEach((wv, team) => { if (team > 1) return; this.waveT[team] -= dt; if (this.waveT[team] > 0) return; this.waveT[team] = wv.every;
      if (this.tickets[team] <= 0) return; const alive = this.units.filter(u => u.alive && u.team === team).length; if (alive > 70 * this.settings.size + 15) return;
      const sp = this.spawnPoint(team); const sq = new Squad(this, team, 'wave'); sq.home = -1; this.squads[team].push(sq);
      for (const c of wv.comp) { const n = Math.max(c.type === 'tank' ? 1 : 1, Math.round(c.n * Math.max(.5, this.settings.size))); for (let i = 0; i < n && this.tickets[team] > 0; i++) { let x = sp.x + (Math.random() - .5) * 24, z = sp.z + (Math.random() - .5) * 24; [x, z] = this.world.collide(x, z, 1.5); this.spawnUnit(team, c.type, x, z, { squad: sq, leader: i === 0 }); this.tickets[team] -= c.type === 'tank' ? 3 : 1; } }
      sq.setOrder(this.orderFor(team, sq)); });
  }
  end(winner) {
    if (this.over) return; this.over = true; const won = winner === this.side; setTimeout(() => {
      this.state = 'end'; setEnabled(false); if (document.pointerLockElement) document.exitPointerLock(); const s = this.stats;
      const d = $('#deploy'); d.style.display = 'flex'; d.innerHTML = `<div class="panel"><h2>${won ? 'Victory' : 'Defeat'}</h2><p>${winner === this.M.attacker ? `${SIDE[winner]} took every position.` : `${SIDE[winner]} held the line.`}</p>
        <p>Kills ${s.kills} · Deaths ${s.deaths} · Hits ${s.hits}/${s.shots} (${s.shots ? (s.hits / s.shots * 100 | 0) : 0}%) · Positions taken ${s.caps}</p><button id="again" class="big">Play again</button> <button id="quit">Menu</button></div>`;
      d.querySelector('#again').onclick = () => { d.style.display = 'none'; this.start(); }; d.querySelector('#quit').onclick = () => { d.style.display = 'none'; this.cleanup(); this.menu(); };
    }, 4000); this.hud.banner(won ? 'Victory' : 'Defeat', '', 6);
  }
  pause(v) {
    if (this.state !== 'play' && this.state !== 'paused') return; this.state = v ? 'paused' : 'play'; setEnabled(!v); $('#pause').style.display = v ? 'flex' : 'none'; if (v) { Audio.suspend(); if (document.pointerLockElement) document.exitPointerLock(); } else { Audio.resume(); if (!this.touch) this.canvas.requestPointerLock?.(); }
  }

  // ================= frame =================
  frame() {
    let dt = Math.min(.05, this.clock.getDelta()); if (!this.scene) { return; }
    if (I.pauseTap) { I.pauseTap = false; if (this.state === 'play') this.pause(true); else if (this.state === 'paused') this.pause(false); }
    if (I.mapTap) { I.mapTap = false; this.hud.toggleMap(); }
    const live = this.state === 'play' || this.state === 'dead' || this.state === 'deploy' || this.state === 'end';
    if (this.state === 'paused') dt = 0;
    if (live && dt > 0) this.logic(dt);
    // camera when not playing
    if (this.state === 'dead' && this.deadCam) { const c = this.deadCam; c.t += dt; this.camera.position.set(c.x, c.y + Math.min(1, c.t) * 2.5, c.z + Math.min(1, c.t) * 3); this.camera.lookAt(c.x, c.y, c.z); }
    if (this.state === 'deploy' || this.state === 'end') { this.camera.position.y += Math.sin(this.time * .2) * .01; }
    const cam = this.camera; const e = new THREE.Euler().setFromQuaternion(cam.quaternion, 'YXZ'); this.camYaw = e.y;
    Audio.setListener(cam.position.x, cam.position.y, cam.position.z, this.camYaw);
    SND.ambienceTick(dt, clamp(this.units.length / 80, 0, 1), this.frontDir);
    // shadows follow the camera
    if (this.sun) { const s = this.sunDir; this.sun.position.set(cam.position.x + s.x * 150, cam.position.y + s.y * 150, cam.position.z + s.z * 150); this.sun.target.position.set(cam.position.x, cam.position.y, cam.position.z); this.sun.target.updateMatrixWorld(); }
    this.world.sky.position.copy(cam.position); this.world.skyU.time.value = this.time;
    this.treeT = (this.treeT || 0) - dt; if (this.treeT <= 0) { this.treeT = .5; this.world.cullTrees(cam.position.x, cam.position.z, this.scene.fog.far * .85); }
    this.fx.update(dt, cam); if (this.grass) this.grass.update(cam, dt);
    if (this.state !== 'menu' && this.state !== 'loading') this.hud.update(dt);
    // render world, then the viewmodel over it with its own depth
    const r = this.renderer, P = this.post;
    if (P) { P.begin(); const sv = new THREE.Vector3().copy(this.sunDir).multiplyScalar(1000).add(cam.position).project(cam); const fwd = new THREE.Vector3(0, 0, -1).applyQuaternion(cam.quaternion).dot(this.sunDir);
      P.u.sunPos.value.set((sv.x + 1) / 2, (sv.y + 1) / 2, fwd > 0 && Math.abs(sv.x) < 1.4 && Math.abs(sv.y) < 1.4 ? Math.min(1, fwd * 1.5) * (1 - (this.rainNow || 0)) : 0); P.u.sunCol.value.copy(FOGU.fogSunCol.value);
      P.u.exposure.value = this.exposure || 1.1; P.u.time.value = this.time % 100; P.u.supp.value = this.player && this.player.alive ? this.player.supp : 0; P.u.wet.value = this.rainNow || 0; }
    else r.clear();
    r.render(this.scene, cam);
    if (this.player && this.player.alive && this.state !== 'dead') { r.clearDepth(); r.render(this.vmScene, this.vmCamera); }
    if (P) P.end();
    if (this.settings.fps) { this.fpsA.push(dt); if (this.fpsA.length > 30) this.fpsA.shift(); $('#fps').textContent = (30 / this.fpsA.reduce((a, b) => a + b, 0)).toFixed(0) + ' fps · ' + r.info.render.calls + ' calls'; }
  }
  logic(dt) {
      this.time += dt; if (this.redeployT > 0) this.redeployT -= dt;
      if (!this.over && !this.M.range) { this.timeLeft -= dt; if (this.timeLeft <= 0) this.end(1 - this.M.attacker); const aAlive = this.units.some(u => u.alive && u.team === this.M.attacker) || (this.player?.alive && this.side === this.M.attacker); if (this.tickets[this.M.attacker] <= 0 && !aAlive) this.end(1 - this.M.attacker); }
      this.todT = (this.todT || 0) - dt; if (this.todT <= 0) { this.todT = 2; this.applyTod(clamp(1 - this.timeLeft / this.roundLen, 0, 1)); this.bakeSky(false); }
      this.rebuildGrid();
      this.cmdT = (this.cmdT || 0) - dt; if (this.cmdT <= 0) { this.cmdT = 3; this.commander(false); this.spotting(); }
      for (const u of [...this.units]) u.update(dt);
      if (this.player && this.player.alive && this.state === 'play') this.player.update(dt, I);
      this.updateRounds(dt); this.updateGrenades(dt); this.updateObjectives(dt); this.updateWaves(dt); this.updateSupport(dt);
      this.markers = this.markers.filter(m => m.t > this.time);
      this.shake = Math.max(0, (this.shake || 0) - dt * 2);
  }
  /** fast-forward the battle without rendering (testing) */
  sim(sec, dt = .05) { const t0 = performance.now(); for (let t = 0; t < sec; t += dt) { this.logic(dt); this.fx.update(dt, this.camera); } return performance.now() - t0; }
  /** spotting: enemies seen by any friendly in the last few seconds show on the map (RO2 squad leader spotting, simplified) */
  spotting() { const me = this.side; for (const u of this.units) if (u.alive && u.team === me && u.target && u.target.team !== me && this.time - (u.tgtSeen || 0) < 3) u.target.spottedT = this.time + 6; }
}
function segSphere(ax, ay, az, bx, by, bz, cx, cy, cz, r) { const dx = bx - ax, dy = by - ay, dz = bz - az, fx = ax - cx, fy = ay - cy, fz = az - cz; const a = dx * dx + dy * dy + dz * dz, b = 2 * (fx * dx + fy * dy + fz * dz), c = fx * fx + fy * fy + fz * fz - r * r, disc = b * b - 4 * a * c; if (disc < 0) return null; const t = (-b - Math.sqrt(disc)) / (2 * a); return t >= 0 && t <= 1 ? t : null; }

// boot
const game = new Game(); window.FW = game;
$('#resume').onclick = () => game.pause(false); $('#pmenu').onclick = () => { $('#pause').style.display = 'none'; game.state = 'menu'; game.cleanup(); game.menu(); };
$('#pvol').oninput = e => { game.settings.volume = +e.target.value; Audio.setVolume(+e.target.value); game.saveSettings(); };
$('#pfps').onchange = e => { game.settings.fps = e.target.checked; $('#fps').style.display = e.target.checked ? 'block' : 'none'; game.saveSettings(); };
// Stuka for air raids, built once models load
const waitModels = setInterval(async () => { if (game.modelsLoaded) { clearInterval(waitModels); const { spawnModel } = await import('./models.js'); game.stukaModel = spawnModel('stuka', game.mats ? game.mats.steel : new THREE.MeshStandardMaterial({ vertexColors: true }), false); } }, 500);
