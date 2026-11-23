// Fall Weiss: Poland 1939 — web edition. Game director: missions, forces, objectives, waves, support fire, ballistics, loop.
import * as THREE from './three.module.min.js';
import { loadModels } from './models.js';
import { buildTextures, TEX } from './textures.js';
import { World, modelMaterial } from './world.js';
import { Fx } from './fx.js';
import { Unit, Squad, RemotePlayer, classUnit } from './units.js';
import { Online, HOST_UNIT, HZ_SNAP, TYPES, GUNS, MATS, unpackUnits, esc } from './net/online.js';
import { Player } from './player.js';
import { Hud } from './hud.js';
import { I, initInput, setEnabled, isTouch, onGesture } from './input.js';
import { SIDE, CLASSES, NAMES, GRENADE, WEAPONS, clamp, lerp, sm, segD, Rng, angDiff } from './defs.js';
import * as SND from './audio.js';
import { patchScene, patchFog, FOGU, SkyLight, Post, Grass } from './look.js';
import { TEX as TEXS } from './textures.js';
const { Audio } = SND;

const $ = s => document.querySelector(s);
const _E = new THREE.Euler(), _V = new THREE.Vector3(), _V2 = new THREE.Vector3(), _R = new THREE.Vector3(), _U = new THREE.Vector3(), _C = new THREE.Vector3(), _UP = new THREE.Vector3(0, 1, 0), _T = new THREE.Color();
const VERSION = 'web 1.1 · ' + new Date(document.lastModified).toISOString().slice(0, 10);
const MV = { rifle: 760, atr: 1275, smg: 380, pistol: 350, lmg: 853, mg34: 755, mg30: 850 };

class Game {
  constructor() {
    this.timers = []; this.planes = []; this.settings = Object.assign({ quality: isTouch() ? 1 : 2, size: isTouch() ? .5 : .8, sens: 1, invert: false, volume: .9, length: 'short', fps: false }, JSON.parse(localStorage.getItem('fw_settings') || '{}'));
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
    this.remotes = new Map(); this.netById = new Map(); this.online = new Online(this); this.online.onChange(() => this.renderOnline()); try { this.online.connect(); } catch (e) { console.error(e); }
    window.addEventListener('resize', () => this.resize()); this.resize();
    this.clock = new THREE.Clock(); this.fpsA = []; this.renderer.setAnimationLoop(() => this.frame());
    document.addEventListener('visibilitychange', () => { if (document.hidden && this.state === 'play') this.pause(true); });
    this.menu();
  }
  /** the online session while in an online battle (host or client), otherwise null */
  get net() { return this.online && this.online.inBattle ? this.online : null; }
  resize() {
    const q = this.settings.quality, dpr = Math.min(devicePixelRatio || 1, [1, 1.5, 2][q]) * (this.touch ? [.6, .75, .9][q] : 1);
    this.renderer.setPixelRatio(dpr); this.renderer.setSize(innerWidth, innerHeight); this.camera.aspect = this.vmCamera.aspect = innerWidth / innerHeight;
    if (this.post) this.post.resize(); this.baseFov = innerWidth / innerHeight > 1.6 ? 62 : 70; this.camera.fov = this.baseFov; this.camera.updateProjectionMatrix(); this.vmCamera.updateProjectionMatrix(); this.hud.resize(); if (this.fx) this.fx.setScale(innerHeight * dpr);
    $('#rotate').style.display = this.touch && innerHeight > innerWidth && this.state !== 'menu' ? 'flex' : 'none';
  }
  saveSettings() { try { localStorage.setItem('fw_settings', JSON.stringify(this.settings)); } catch (e) { /* private mode */ } }
  lockPointer() { if (this.touch) return; try { const r = this.canvas.requestPointerLock?.(); if (r && r.catch) r.catch(() => { }); } catch (e) { /* needs a click first */ } }
  /** game-time timers: they stop while paused and are dropped when the battle is torn down */
  after(sec, fn) { this.timers.push({ t: this.time + sec, fn }); }
  runTimers() { const now = this.time, T = this.timers; for (let i = 0; i < T.length; i++) if (T[i].t <= now) { const f = T[i].fn; T.splice(i--, 1); try { f(); } catch (e) { console.error(e); } } }

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
    this.renderOnline(); this.drawMenuBackdrop();
  }
  /** the Play online card: relay status, your name, host / join, private room link */
  renderOnline() {
    const el = $('#online'); if (!el || this.state !== 'menu') return; const on = this.online; const games = on.openGames();
    if (!el.dataset.built) { el.dataset.built = 1; el.innerHTML = `<div class="lab">Play online</div><div id="on-status" class="small"></div>
      <div class="row"><input id="on-name" maxlength="16" placeholder="Your name"><button id="on-host">Host this battle online</button></div>
      <div id="on-games"></div>
      <div class="row"><button id="on-priv"></button><button id="on-copy">Copy invite link</button></div><div id="on-note" class="small"></div>`;
      const nm = $('#on-name'); nm.value = on.name; nm.onchange = () => on.setName(nm.value); nm.onkeydown = e => e.stopPropagation();
      $('#on-host').onclick = () => { on.setName(nm.value); Audio.init(); on.hostBattle(); this.start(); };
      $('#on-priv').onclick = () => { if (on.isPrivate) on.publicRoom(); else on.privateRoom(); };
      $('#on-copy').onclick = () => { const l = on.inviteLink(); (navigator.clipboard?.writeText(l) || Promise.reject()).then(() => { $('#on-note').textContent = 'Invite link copied: ' + l; }, () => { $('#on-note').textContent = 'Invite link: ' + l; }); };
    }
    $('#on-status').textContent = on.status() + (on.notice ? ' — ' + on.notice : '');
    $('#on-priv').textContent = on.isPrivate ? 'Back to the public room' : 'New private room';
    const ms = this.missions || [];
    const html = games.length ? games.map(g => `<div class="game"><div><b>${esc(ms[g.mi]?.name || g.mn)}</b><small>hosted by ${esc(g.host)} · ${g.n} player${g.n === 1 ? '' : 's'} · ${[0, 1].map(sd => `${SIDE[sd]} ${g.sides.filter(x => x === sd).length}`).join(' / ')}</small></div><button class="join" data-g="${g.gid}">Join as ${SIDE[this.side]}</button></div>`).join('')
      : `<div class="small">${on.online ? 'No battles hosted in this room right now. Host one, or open a friend\'s invite link.' : ''}</div>`;
    const gl = $('#on-games'); if (gl.innerHTML !== html) { gl.innerHTML = html; gl.querySelectorAll('.join').forEach(b => b.onclick = () => this.joinOnline(b.dataset.g)); }
  }
  joinOnline(gid) {
    const on = this.online; on.setName($('#on-name')?.value); const g = on.join(gid); if (!g) return;
    if (this.cls === 'atrifle' && this.side === 0) this.cls = 'rifleman';
    this.mi = g.mi; if (g.sz) this.settings.size = g.sz; if (g.ln) this.settings.length = g.ln; this.netBattle = g.b; Audio.init(); this.start();
  }
  /** leave whatever battle this is (online or not) and go back to the menu */
  leaveBattle() { for (const id of ['#deploy', '#pause']) $(id).style.display = 'none'; if (this.online.inBattle) this.online.leave(); this.cleanup(); this.menu(); }
  /** online client: the host went away */
  netLost(msg) { if (!this.online.client) return; this.online.leave(); this.online.notice = msg; this.leaveBattle(); this.hud.show(false); }
  drawMenuBackdrop() { this.renderer.setClearColor(0x14120e); this.renderer.clear(); }

  async start() {
    $('#menu').style.display = 'none'; $('#loading').style.display = 'flex'; this.state = 'loading'; const bar = $('#lbar'), lt = $('#ltext'); this.online.notice = '';
    if (this.online.host) this.online.newBattle(); if (!this.missions) { const D = await (await fetch('data/missions.json')).json(); this.missions = D.missions; }
    const step = (t, f) => { lt.textContent = t; bar.style.width = (f * 100).toFixed(0) + '%'; return new Promise(r => setTimeout(r, 0)); };
    try {
      if (!this.modelsLoaded) { await step('Loading soldiers, tanks and guns…', .02); await loadModels('data/models.bin', f => { bar.style.width = (f * 30).toFixed(0) + '%'; }); this.modelsLoaded = true; }
      if (this.texQ !== this.settings.quality) { await step('Painting textures…', .3); await buildTextures(this.settings.quality, this.renderer, f => { bar.style.width = (30 + f * 30).toFixed(0) + '%'; }); this.texQ = this.settings.quality; this.mats = null; }
      FOGU.fwNoise.value = TEXS.noise;
      await step('Building the battlefield…', .62); this.cleanup(); this.setupMission(this.missions[this.mi]);
      await step('Deploying forces…', .9); this.deployForces();
      await step('Lighting the sky…', .96); this.grass = new Grass(this.scene, this.world, this.settings.quality); for (const k in this.mats) patchFog(this.mats[k]); patchScene(this.scene); this.bakeSky(true);
      if (this.post) { this.post.dispose(); this.post = null; } if (this.settings.quality >= 1) { this.post = new Post(this.renderer, this.settings.quality); this.post.resize(); }
      await step('Ready', 1);
      this.renderer.compile(this.scene, this.camera);
    } catch (e) { console.error(e); lt.textContent = 'Error: ' + e.message; return; }
    $('#loading').style.display = 'none'; this.state = 'deploy'; this.showDeploy(true); if (new URLSearchParams(location.search).has('go')) $('#go').click();
  }
  cleanup() { this.remotes = new Map(); this.netById = new Map(); this.timers = []; for (const p of this.planes) p.done = true; this.planes = []; if (this.world) this.world.dispose(); if (this.units) for (const u of this.units) u.dispose(); if (this.player) this.player.dispose(); this.player = null; if (this.fx) this.fx.clear(); this.scene = null; SND.stopAmbience(); this.engines = 0; }

  // ================= mission =================
  setupMission(MU) {
    const q = this.settings.quality; this.quality = q; this.lodDist = this.touch ? [25, 38, 60][q] : [35, 55, 80][q];
    if (!this.mats) this.mats = { cloth: modelMaterial('cloth'), steel: modelMaterial('steel'), vm: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .6, metalness: .15 }), burnt: new THREE.MeshStandardMaterial({ color: 0x1c1a18, roughness: 1 }), proxy: new THREE.MeshLambertMaterial({ vertexColors: true }) };
    const sc = this.scene = new THREE.Scene(); this.world = new World(MU, q, sc); const M = this.M = this.world.M;
    this.fx = new Fx(sc, q); this.fx.setScale(innerHeight * this.renderer.getPixelRatio());
    // light: sun from the mission, hemisphere sky/ground, shadow frustum follows the camera
    this.sun = new THREE.DirectionalLight(0xffffff, 3); this.sun.castShadow = q > 0; const big = !this.touch && this.renderer.capabilities.maxTextureSize >= 4096; const ss = q === 0 ? 0 : q === 1 ? (big ? 2048 : 1024) : (big ? 4096 : 2048), ext = q === 1 ? (big ? 60 : 48) : (big ? 95 : 70);
    if (ss) { this.sun.shadow.mapSize.set(ss, ss); const c = this.sun.shadow.camera; c.left = c.bottom = -ext; c.right = c.top = ext; c.near = 1; c.far = 420; this.sun.shadow.bias = -.0003; this.sun.shadow.normalBias = .035; this.sun.shadow.radius = 2; }
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
    const fogK0 = A ? lerp(A.fog ?? 0, B.fog ?? 0, f) : 0; FOGU.fwCloud.value.set(clamp(.3 + rain * .62 + fogK0 * .2, 0, .95), .5 - rain * .22, 1400, 0); FOGU.fwSunDir.value.copy(dir); FOGU.fwSunCol.value.copy(sunC).multiplyScalar((1 - rain * .7) * (.8 + Math.min(1, elev * 3) * .4));
    this.sunDir = dir; this.sun.color.copy(sunC); this.sun.intensity = (3.0 + Math.min(1, elev * 3) * 1.6) * (1 - rain * .6); this.hemi.color.copy(top).lerp(new THREE.Color(0xffffff), .3); this.hemi.groundColor.set(0x5a4c36); this.hemi.intensity = .35 + rain * .3;
    this.scene.environmentIntensity = (.75 + rain * .3) * (.7 + Math.min(1, elev * 3) * .3);
    FOGU.fogSunDir.value.copy(dir); FOGU.fogSunCol.value.copy(sunC).multiplyScalar((1 - rain * .8) * (.6 + (1 - Math.min(1, elev * 2.5)) * .6));
    const ffar = M.fogFar * (1 - rain * .45); FOGU.fogP.value.set(.85 / ffar, .05, (this.groundBase ?? 0) - 1, 1.2 + rain * 1.5 + (A ? lerp(A.fog ?? 0, B.fog ?? 0, f) : 0) * 3);
    this.scene.fog.color.copy(hor); const fogK = (A ? lerp(A.fog ?? 0, B.fog ?? 0, f) : 0); this.scene.fog.near = M.fogFar * .55 * (1 - rain * .4); this.scene.fog.far = M.fogFar * 1.25 * (1 - rain * .4) * (1 - fogK * .3);
    this.fx.setFog(hor, this.scene.fog.near, this.scene.fog.far); this.renderer.toneMappingExposure = 1.05 + (1 - Math.min(1, elev * 3)) * .35; this.exposure = 1.3 + (1 - Math.min(1, elev * 3)) * .6 + rain * .25; this.todF = f;
    this.vmHemi.color.copy(this.hemi.color); this.vmSun.color.copy(sunC); this.vmSun.intensity = this.sun.intensity * .45; this.vmHemi.intensity = .9;
    this.fx.setRain(rain > .02, rain, this.camera); SND.setRain(rain); this.dry = rain < .05 && !M.hasRain;
    _T.copy(this.hemi.color).multiplyScalar(.62).add(new THREE.Color(sunC).multiplyScalar(.42 * (1 - rain * .6))); const tl = (_T.r + _T.g + _T.b) / 3 || 1; this.fx.setTint(_T.multiplyScalar(1 / tl));
  }

  // ================= forces =================
  spawnUnit(team, type, x, z, o = {}) { const u = new Unit(this, team, type, x, z, o); this.units.push(u); if (o.squad) o.squad.members.push(u); return u; }
  removeUnit(u, keepVisual) { const i = this.units.indexOf(u); if (i >= 0) this.units.splice(i, 1); if (!keepVisual || true) u.dispose(); }
  deployForces() {
    if (this.net?.client) { this.playerSquad = new Squad(this, this.side, 'rifle'); this.squads[this.side].push(this.playerSquad); return; }
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
      <p class="small">Reinforcements: ${Math.max(0, this.tickets[this.side])} · Deploying at ${sp.label}${this.net ? ` · Online: ${this.net.host ? 'you are hosting — keep this tab open' : 'playing in ' + esc(this.online.games.get(this.online.gid)?.host || 'the host') + '\'s battle'}` : ''}</p>
      <button id="go" class="big">${first ? 'Deploy' : 'Redeploy'}</button> <button id="quit">Quit to menu</button></div>`;
    d.querySelectorAll('.cls').forEach(b => b.onclick = () => { this.cls = b.dataset.k; this.showDeploy(first); });
    const go = () => { if (!first && this.redeployT > 0) return; d.style.display = 'none'; this.spawnPlayer(); this.state = 'play'; setEnabled(true); this.lockPointer(); if (first) this.hud.banner(M.name, M.date, 5); };
    d.querySelector('#go').onclick = go; d.querySelector('#quit').onclick = () => this.leaveBattle();
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
    const sq = this.playerSquad; sq.members = sq.members.filter(m => m.alive); const need = this.M.range || this.net?.client ? 0 : Math.max(0, Math.round(5 * Math.max(.6, this.settings.size)) - sq.alive.length);
    for (let i = 0; i < need && this.tickets[this.side] > 0; i++) { const u = this.spawnUnit(this.side, i === 1 ? 'mg' : 'rifle', x + (Math.random() - .5) * 10, z + (Math.random() - .5) * 10, { squad: sq, yaw: p.yaw }); this.tickets[this.side]--; }
    if (this.net?.client) { this.online.life++; this.online.act('sp', this.online.life); }
    sq.members.push(p); sq.setOrder(this.orderFor(this.side, sq)); this.hud.hint(this.touch ? 'Left: move · Right: look · FIRE · AIM' : 'WASD move · Mouse aim · LMB fire · RMB aim · C/Z stance · R reload · G grenade · M map', 6);
  }
  onPlayerDeath(from, how) {
    this.stats.deaths++; this.deathMsg = from && from.team !== undefined ? `${how === 'crushed' ? 'Crushed by' : how === 'blast' ? 'Blown up by' : 'Killed by'} ${esc(from.name || NAMES[from.team]?.[from.type] || 'the enemy')}${from.kind === 'inf' && !from.human && !from.isRemote && NAMES[from.team]?.[from.type] && from.name !== NAMES[from.team][from.type] ? ` (${NAMES[from.team][from.type]})` : ''}${how === 'headshot' ? ' — headshot' : ''}, ${Math.hypot(from.x - this.player.x, from.z - this.player.z) | 0} m` : how === 'bled out' ? 'Bled to death.' : how === 'blast' ? 'Killed by shellfire.' : how === 'crushed' ? 'Crushed by a tank.' : 'Killed.';
    this.hud.feed(`<b>You</b> ${how === 'bled out' ? 'bled out' : 'were killed'}`, '#e88'); this.redeployT = 6; this.state = 'dead'; setEnabled(false); if (document.pointerLockElement) document.exitPointerLock();
    this.deadCam = { x: this.player.x, y: this.player.y + .4, z: this.player.z, t: 0 };
    const kid = this.netIdOf(from); if (this.net?.client) { this.online.deaths++; this.online.act('d', kid, how || ''); } else if (this.net?.host) { this.online.deaths++; this.netEvent('kl', [HOST_UNIT, kid, how || '', this.side, 0]); }
    this.after(1.8, () => { if (this.state === 'dead') this.showDeploy(false); });
  }
  onDeath(u, from, how) {
    if (!this.M.range && !u.isRemote) this.tickets[u.team] -= u.kind === 'tank' ? 3 : u.kind === 'gun' ? 0 : 1;
    if (this.net?.host) this.netEvent('kl', [this.netIdOf(u), this.netIdOf(from), typeof how === 'string' ? how : '', u.team, u.kind === 'tank' ? 1 : u.kind === 'gun' ? 2 : 0]);
    if (from && from.isRemote && from.team === u.team) this.hud.feed(`<b>${esc(from.name)}</b> killed a friendly`, '#f80');
    if (u.dummy) this.after(3, () => { if (!this.M.range) return; const n = this.spawnUnit(u.team, u.type, u.spot[0], u.spot[1], { yaw: u.yaw }); n.dummy = u.dummy; n.spot = u.spot; if (u.dummy === 'walk') this.walker = n; this.hud.hint('Target reset', 1); });
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
          if (u.alive && u.team !== r.team && !u.net && !u.isPlayer && !u.isRemote) { const t = clamp(((u.x - ax) * (bx - ax) + (u.y + 1 - ay) * (by - ay) + (u.z - az) * (bz - az)) / L2, 0, 1); const d = Math.hypot(ax + (bx - ax) * t - u.x, ay + (by - ay) * t - u.y - 1, az + (bz - az) * t - u.z); if (d < 2.5 && !r.sup?.has(u)) { (r.sup || (r.sup = new Set())).add(u); u.supp = Math.min(1, u.supp + .3); u.target = u.target || r.from; u.tgtSeen = this.time - 1.5; } } }
        if (best && best.u.alive && (r.ghost || best.u.net || best.u.isRemote || best.u.isPlayer)) { // online: someone else's round, or a hit that the host must judge
          const u = best.u, hx = ax + (bx - ax) * bt, hy = ay + (by - ay) * bt, hz = az + (bz - az) * bt;
          if (!r.ghost) this.netHit(r, best, u, hx, hy, hz); else if (best.part === 'hull') this.fx.impact(hx, hy, hz, 'metal'); else this.fx.impact(hx, hy, hz, 'flesh');
          done = true;
        } else if (best && best.u.alive) {
          const u = best.u, hx = ax + (bx - ax) * bt, hy = ay + (by - ay) * bt, hz = az + (bz - az) * bt;
          if (best.part === 'hull') { if (r.w.pen100) { const d = Math.hypot(hx - (r.from?.x || hx), hz - (r.from?.z || hz)); this.armourHit(u, r.from, r.w.pen100 - r.w.penDrop * Math.max(0, d - 100) / 100, r.from?.x ?? ax, r.from?.z ?? az, true); } else this.impactFx(hx, hy, hz, 'metal', r.from?.isPlayer); }
          else { const speedK = Math.hypot(r.vx, r.vy, r.vz) / r.w.mv; const kill = u.hurt(r.w.dmg * Math.max(.4, speedK), r.from, best.part); if (r.from?.isPlayer) { this.stats.hits++; if (!kill) this.hud.hit(false); SND.hitMarker(); } }
          done = true;
        } else if (blk) { this.impactFx(blk.x, blk.y, blk.z, w.waterDepth(blk.x, blk.z) > .1 ? 'water' : blk.mat, !r.ghost && r.from?.isPlayer); done = true; }
        else { r.x = bx; r.y = by; r.z = bz; if (r.t > 3) done = true; }
      }
      if (done) { this.rounds.splice(i--, 1); }
    }
  }
  /** shell or AT-rifle bullet against plate: angle of the hit face decides front / side / rear armour, plus obliquity */
  armourCalc(t, pen, fx, fz, small) {
    const a = Math.atan2(-(fx - t.x), -(fz - t.z)); const rel = Math.abs(angDiff(t.yaw, a)); // 0 = shooter in front
    const T = t.T; let arm, face; if (t.kind === 'gun') { arm = 5; face = 'shield'; } else if (rel < .7) { arm = T.armF; face = 'front'; } else if (rel > 2.4) { arm = T.armR; face = 'rear'; } else { arm = T.armS; face = 'side'; }
    const obl = rel < .7 ? rel : rel > 2.4 ? Math.PI - rel : Math.abs(Math.PI / 2 - rel); const eff = arm / Math.max(.45, Math.cos(obl));
    const penetrated = pen + (Math.random() - .5) * 4 > eff; const dmg = small ? 60 + Math.random() * 60 : t.kind === 'gun' ? 200 : 170 + Math.random() * 140; return { pen: penetrated, dmg, face };
  }
  armourHit(t, from, pen, fx, fz, small) { if (!t.alive) return; this.armourApply(t, from, this.armourCalc(t, pen, fx, fz, small)); }
  armourApply(t, from, r) {
    if (!t.alive) return; const hx = t.x, hy = t.y + 1.4, hz = t.z;
    if (r.pen) { this.impactFx(hx, hy, hz, 'metal'); this.fx.flash(hx, hy, hz, .8); if (t.kind === 'gun') { for (const c of t.crew) if (c.alive && Math.random() < .6) c.hurt(100, from, 'chest'); } const killed = t.hurt(r.dmg, from, 'hull'); t.supp = 1;
      if (from?.isPlayer) { this.hud.hit(killed); this.hud.hint(killed ? `${NAMES[t.team][t.type]} knocked out` : `Penetration — ${r.face} armour`, 2); } }
    else { this.impactFx(hx, hy, hz, 'metal'); if (from?.isPlayer) this.hud.hint(`Ricochet — ${r.face} armour too thick at this range`, 2); }
  }
  explode(x, y, z, size, from, radius) {
    const R = radius || 4 * size; this.explodeFx(x, y, z, size, true, R);
    for (const u of this.queryUnits(x, z, R * 4)) {
      const d = Math.hypot(u.x - x, u.z - z, (u.y || 0) - y); if (u.isPlayer || u.isRemote) { if (!u.alive) continue; if (u.isPlayer && d < R * 4) { u.supp = 1; this.shake = Math.min(1.5, (this.shake || 0) + (1 - d / (R * 4)) * 1.5); } if (d < R * 2.2) { const cover = this.world.rayBlock(x, y + .5, z, u.x, u.y + 1, u.z, { noTrees: true }) ? .3 : 1; const dmg = (d < R * .6 ? 220 : d < R ? 120 : 60 * (1 - (d - R) / (R * 1.2))) * cover * (u.stance === 2 ? .6 : 1); if (dmg > 5) this.hurtHuman(u, dmg, from, 'chest', 'blast'); } continue; }
      if (!u.alive) continue; if (u.kind === 'tank') { if (d < 2.5) u.hurt(20, from, 'hull'); continue; }
      u.supp = Math.min(1, u.supp + Math.max(0, 1 - d / (R * 4)) * 1.2);
      if (d < R * 2.2) { const cover = this.world.rayBlock(x, y + .5, z, u.x, u.y + 1, u.z, { noTrees: true }) ? .25 : 1; const dmg = (d < R * .6 ? 300 : d < R ? 140 : 70 * (1 - (d - R) / (R * 1.2))) * cover * (u.stance === 2 ? .6 : 1) * (u.kind === 'gun' ? .4 : 1); if (dmg > 5) u.hurt(dmg, from, 'chest', { how: 'blast' }); }
    }
  }
  throwGrenade(u, tx, tz) { const d = Math.hypot(tx - u.x, tz - u.z), T = Math.max(.9, d / 14); const y0 = u.y + 1.6; const ty = this.world.H(tx, tz); this.throwGrenadeFrom(u, u.x, y0, u.z, (tx - u.x) / T, (ty - y0 + 4.9 * T * T) / T, (tz - u.z) / T, GRENADE[u.team].fuse - 1); }
  throwGrenadeFrom(u, x, y, z, vx, vy, vz, fuse, origin = '', visual = false) {
    const geo = this.nadeGeo || (this.nadeGeo = (() => { const g = new THREE.CylinderGeometry(.03, .03, .36, 6); g.rotateX(Math.PI / 2); return g; })()); const m = new THREE.Mesh(geo, this.mats.steel); m.position.set(x, y, z); this.scene.add(m);
    if (this.net?.client) { visual = true; if (u === this.player) this.online.act('n', x, y, z, vx, vy, vz, fuse); }
    else if (this.net?.host) this.netFx('nd', [origin, x, y, z, vx, vy, vz, fuse, u.team]);
    this.grenades.push({ x, y, z, vx, vy, vz, fuse: Math.max(.6, fuse), from: u, m, team: u.team, warned: false, visual });
  }
  updateGrenades(dt) {
    for (let i = 0; i < this.grenades.length; i++) { const n = this.grenades[i]; n.fuse -= dt; n.vy -= 9.81 * dt; let nx = n.x + n.vx * dt, ny = n.y + n.vy * dt, nz = n.z + n.vz * dt; const gy = this.world.H(nx, nz);
      const blk = this.world.rayBlock(n.x, n.y, n.z, nx, Math.max(ny, gy + .05), nz, { noTrees: true }); if (blk && blk.mat !== 'dirt') { n.vx *= -.35; n.vz *= -.35; nx = n.x; nz = n.z; }
      if (ny < gy + .05) { ny = gy + .05; n.vy *= -.25; n.vx *= .55; n.vz *= .55; if (Math.abs(n.vy) > 1) SND.impact(nx, ny, nz, 'wood'); }
      n.x = nx; n.y = ny; n.z = nz; n.m.position.set(nx, ny, nz); n.m.rotation.x += dt * 8;
      // men nearby who see it dive away
      if (!n.warned && !n.visual && n.fuse < 2.5) { n.warned = true; for (const u of this.queryUnits(n.x, n.z, 9, u => u.alive && u.kind === 'inf' && !u.isPlayer && !u.isRemote)) { const dx = u.x - n.x, dz = u.z - n.z, d = Math.hypot(dx, dz) || 1; u.wpt = [u.x + dx / d * 8, u.z + dz / d * 8]; u.stance = 0; u.supp = Math.max(u.supp, .5); u.mode = 'charge'; this.after(1.4, () => { if (u.alive) { u.stance = 2; u.mode = 'hold'; } }); } }
      if (n.fuse <= 0) { this.scene.remove(n.m); this.grenades.splice(i--, 1); if (!n.visual) this.explode(n.x, n.y, n.z, .6, n.from, GRENADE[n.team].kill); }
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
    this.netEvent('br', [this.objectives.indexOf(o), team]); this.hud.hint(team === this.side ? 'Friendly artillery on the objective' : 'Incoming artillery!', 3); this.markers.push({ x: o.x, z: o.z, r: o.r + 10, col: '#f84', t: this.time + 20 });
    for (let i = 0; i < n; i++) this.after(i * (.9 + Math.random() * .9), () => { const a = Math.random() * 6.28, d = Math.random() * (o.r + 15), x = o.x + Math.cos(a) * d, z = o.z + Math.sin(a) * d, y = this.world.H(x, z); this.whistle(x, y, z, 1.4); this.after(1.4, () => this.explode(x, y, z, 1.1, null, 5)); });
  }
  navalSalvo(o) { const M = this.M; this.sound('naval', M.shipX, 8, M.shipZ); for (let i = 0; i < 2; i++) this.after(2.5 + i * .7, () => { const x = o.x + (Math.random() - .5) * 50, z = o.z + (Math.random() - .5) * 50, y = this.world.H(x, z); this.whistle(x, y, z, 1.8); this.after(1.8, () => this.explode(x, y, z, 1.8, null, 8)); }); }
  stuka(o, visualOnly) {
    if (!this.stukaModel || !o) return; const plane = this.stukaModel.clone(); this.scene.add(plane);
    const P = { plane, o, t: 0, T: 14, start: { x: o.x - 900, y: 600, z: o.z + 300 }, dropped: !!visualOnly, pos: { x: 0, y: 0, z: 0, dive: 0 }, done: false }; this.planes.push(P);
    SND.stukaDive(() => P.done ? null : P.pos, P.T); this.hud.hint(this.side === 0 ? 'Stukas on the objective' : 'Stukas — take cover!', 3);
    if (!visualOnly) this.netEvent?.('stuka', [this.objectives.indexOf(o)]);
  }
  updatePlanes(dt) {
    for (let i = 0; i < this.planes.length; i++) { const P = this.planes[i], o = P.o, plane = P.plane; P.t += dt; const f = P.t / P.T; let x, y, z, dive = 0;
      if (f < .45) { const k = f / .45; x = lerp(P.start.x, o.x - 300, k); z = lerp(P.start.z, o.z + 100, k); y = 600; }
      else if (f < .75) { const k = (f - .45) / .3; dive = k; x = lerp(o.x - 300, o.x, k); z = lerp(o.z + 100, o.z, k); y = lerp(600, 120, k * k); }
      else { const k = (f - .75) / .25; x = lerp(o.x, o.x + 500, k); z = o.z - k * 100; y = lerp(120, 500, Math.sqrt(k)); dive = Math.max(0, 1 - k * 3); if (!P.dropped) { P.dropped = true; const bx = o.x + (Math.random() - .5) * 30, bz = o.z + (Math.random() - .5) * 30; this.after(1.5, () => this.explode(bx, this.world.H(bx, bz), bz, 2.2, null, 9)); } }
      const ox = plane.position.x, oy = plane.position.y, oz = plane.position.z; plane.position.set(x, y, z); if (P.t > .1) { plane.lookAt(x + (x - ox) * 10, y + (y - oy) * 10, z + (z - oz) * 10); plane.rotateY(Math.PI); }
      Object.assign(P.pos, { x, y, z, dive }); if (P.t >= P.T) { P.done = true; this.scene.remove(plane); this.planes.splice(i--, 1); } }
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
    if (this.over) return; this.over = true; const won = winner === this.side; this.netEvent?.('end', [winner]); this.after(4, () => {
      this.state = 'end'; setEnabled(false); if (document.pointerLockElement) document.exitPointerLock(); const s = this.stats;
      const d = $('#deploy'); d.style.display = 'flex'; d.innerHTML = `<div class="panel"><h2>${won ? 'Victory' : 'Defeat'}</h2><p>${winner === this.M.attacker ? `${SIDE[winner]} took every position.` : `${SIDE[winner]} held the line.`}</p>
        <p>Kills ${s.kills} · Deaths ${s.deaths} · Hits ${s.hits}/${s.shots} (${s.shots ? (s.hits / s.shots * 100 | 0) : 0}%) · Positions taken ${s.caps}</p>${this.net?.client ? '<p class="small">The host decides whether to play again.</p>' : '<button id="again" class="big">Play again</button> '}<button id="quit">Menu</button></div>`;
      const ag = d.querySelector('#again'); if (ag) ag.onclick = () => { d.style.display = 'none'; this.start(); }; d.querySelector('#quit').onclick = () => { d.style.display = 'none'; this.leaveBattle(); };
    }); this.hud.banner(won ? 'Victory' : 'Defeat', '', 6);
  }
  pause(v) {
    if (this.state !== 'play' && this.state !== 'paused') return; this.state = v ? 'paused' : 'play'; setEnabled(!v); $('#pause').style.display = v ? 'flex' : 'none'; if (v) { Audio.suspend(); if (document.pointerLockElement) document.exitPointerLock(); } else { Audio.resume(); this.lockPointer(); }
  }


  // ================= effects everyone in the battle sees and hears (the host also sends them to the other players) =================
  netFx(type, args, reach) { if (this.online.host && this.online.inBattle) this.online.fx(type, args, reach); }
  netEvent(type, args) { if (this.online.host && this.online.inBattle) this.online.event(type, args); }
  sound(kind, x, y, z) { SND.gunshot(kind, x, y, z); this.netFx('gs', [GUNS.indexOf(kind), x, y, z]); }
  whistle(x, y, z, dur) { SND.shellWhistle(x, y, z, dur); this.netFx('wh', [x, y, z, dur], 700); }
  explodeFx(x, y, z, size, ground = true, R) {
    this.fx.explosion(x, y, z, size, ground); SND.explosion(x, y, z, size); this.netFx('bm', [x, y, z, size, ground ? 1 : 0, R || 4 * size]);
    if (this.player && this.player.alive && Math.hypot(this.player.x - x, this.player.z - z) < 60) this.shake = Math.min(1.5, (this.shake || 0) + .4);
  }
  impactFx(x, y, z, mat, send = true) { this.fx.impact(x, y, z, mat); SND.impact(x, y, z, mat); if (send) this.netFx('im', [x, y, z, MATS.indexOf(mat)], 160); }
  /** an AI rifle / MG round: report, muzzle flash, tracer, the crack as it passes the player, and where it struck */
  shotFx(kind, x, y, z, dx, dy, dz, d, tracer, big, ix, iy, iz, mat, from, coax) {
    SND.gunshot(kind, x, y, z); const cam = this.camera.position;
    if (Math.hypot(cam.x - x, cam.z - z) < 250) this.fx.muzzle(x, y, z, dx, dy, dz, big);
    if (tracer) this.fx.tracer(x, y, z, dx, dy, dz, 700, 8, [1, .45, .2]);
    this.bulletNearPlayer(x, y, z, dx, dy, dz, d + 30, from, true);
    if (mat && (mat !== 'flesh' || !from?.g || this.net?.client) && Math.hypot(cam.x - ix, cam.z - iz) < 120) { this.fx.impact(ix, iy, iz, mat); if (mat !== 'dirt' && mat !== 'water') SND.impact(ix, iy, iz, mat); }
    if (this.online.host && this.online.inBattle) this.netFx('sh', [GUNS.indexOf(kind), x, y, z, dx * 1000, dy * 1000, dz * 1000, d, (tracer ? 1 : 0) | (big > .7 ? 2 : 0) | ((from?.team ?? 0) << 2), ix, iy, iz, mat ? MATS.indexOf(mat) : -1], 450);
  }
  /** tank or AT-gun main gun: report, flash, blast dust and the shell's tracer */
  cannonFx(snd, mx, my, mz, fx, fz, ix, iy, iz, d, gy) {
    SND.gunshot(snd, mx, my, mz); this.fx.muzzle(mx, my, mz, fx, 0, fz, 2.2); this.fx.smoke.emit(mx, gy + .3, mz, 0, .5, 0, 2.5, 2, 6, .55, .5, .42, .4);
    this.fx.tracer(mx, my, mz, (ix - mx) / d, (iy - my) / d, (iz - mz) / d, 760, 3, [1, .7, .35], d / 760 + .05);
    this.after(d / 760, () => this.bulletNearPlayer(mx, my, mz, (ix - mx) / d, (iy - my) / d, (iz - mz) / d, d, null, true, 3));
    this.netFx('cn', [GUNS.indexOf(snd), mx, my, mz, fx * 1000, fz * 1000, ix, iy, iz, d, gy]);
  }
  /** the local player fired: the other players hear and see it */
  playerShot(snd, x, y, z, dx, dy, dz, tracer) {
    if (this.net?.client) this.online.act('f', GUNS.indexOf(snd), x, y, z, dx * 1000, dy * 1000, dz * 1000, (tracer ? 1 : 0) | (this.side << 1));
    else if (this.net?.host) this.netFx('ps', [this.online.id, GUNS.indexOf(snd), x, y, z, dx * 1000, dy * 1000, dz * 1000, (tracer ? 1 : 0) | (this.side << 1)]);
  }
  /** someone else fired (another player): report, flash, tracer, crack */
  otherShot(snd, x, y, z, dx, dy, dz, fl) {
    const kind = GUNS[snd] || 'rifle'; SND.gunshot(kind, x, y, z); this.fx.flash(x + dx * 1.2, y, z + dz * 1.2, .3); this.fx.muzzle(x + dx * .8, y - .05, z + dz * .8, dx, dy, dz, .4);
    if (fl & 1) this.fx.tracer(x, y, z, dx, dy, dz, (MV[kind] || 760) * .9, 10, [1, .5, .22], 2);
    this.bulletNearPlayer(x, y, z, dx, dy, dz, 600, { team: (fl >> 1) & 1 }, (MV[kind] || 760) > 343);
  }

  // ================= online: who is who =================
  remoteHumans() { return [...this.remotes.values()]; }
  /** the id a unit goes by on the network (the host's own soldier has a fixed one) */
  netIdOf(u) { if (!u) return -1; if (u === this.player && this.online.host) return HOST_UNIT; if (u.net) return u.id; return typeof u.id === 'number' && u.id > 0 ? u.id % 65000 : -1; }
  unitByNetId(id) { if (id === HOST_UNIT) return this.player; if (id < 0) return null; for (const u of this.units) if (u.id % 65000 === id) return u; return null; }
  hurtHuman(u, dmg, from, part, how) { if (u.isPlayer) this.player.hurt(dmg, from, part, how); else if (u.isRemote) u.hurt(dmg, from, part, how); }
  /** host: a remote player was hit (by the AI, a blast, the host or another player): their page applies it */
  sendHurt(rp, dmg, from, part, how) { this.netEvent('dm', [rp.peer, dmg, part, this.netIdOf(from), from ? from.x : rp.x, from ? from.z : rp.z, how || '']); }
  /** host: keep a stand-in soldier for each remote player, a new one for each life */
  syncRemote(peer, p) {
    const last = p.buf[p.buf.length - 1]; if (!last || !this.scene || this.state === 'loading') return; let rp = this.remotes.get(peer);
    if (last.alive && (!rp || rp.life !== last.life)) {
      if (rp && rp.alive) this.removeUnit(rp);
      rp = new RemotePlayer(this, peer, p.d.sd ? 1 : 0, last.cls, last.x, last.z, last.life, String(p.d.n).slice(0, 16)); p.buf.splice(0, p.buf.length - 1); rp.nb = p.buf; this.units.push(rp); this.remotes.set(peer, rp);
    }
    if (rp && rp.alive && !last.alive) { rp.deadT = (rp.deadT || 0) + 1; if (rp.deadT > 18) rp.die(null, ''); } // their death report got lost: they are dead all the same
  }
  dropRemote(peer) { const rp = this.remotes.get(peer); if (rp) { this.removeUnit(rp); this.remotes.delete(peer); } }
  /** host: something a remote player did */
  netAction(peer, p, a) {
    if (!this.scene || this.state === 'loading') return; const type = a[1], v = a.slice(2), rp = this.remotes.get(peer); const side = p.d.sd ? 1 : 0;
    switch (type) {
      case 'sp': if (this.tickets[side] > 0 && !this.M.range) this.tickets[side]--; break;
      case 'f': { const [code, x, y, z, dx0, dy0, dz0, fl] = v; const dx = dx0 / 1000, dy = dy0 / 1000, dz = dz0 / 1000; this.otherShot(code, x, y, z, dx, dy, dz, fl);
        const kind = GUNS[code] || 'rifle', mv = MV[kind] || 760; if (rp) { rp.lastShotAt = this.time; this.fireRound({ x, y, z, vx: dx * mv, vy: dy * mv, vz: dz * mv, w: { mv, dmg: 0 }, from: rp, team: side, t: 0, ghost: true }); this.stats.shots--; }
        this.netFx('ps', [peer, code, x, y, z, dx0, dy0, dz0, fl]); break; }
      case 'h': { const [id, dmg, part, how] = v; const t = this.unitByNetId(id); if (!t || !t.alive) break; if (t.isPlayer || t.isRemote) this.hurtHuman(t, dmg, rp, part, how); else t.hurt(dmg, rp || null, part); break; }
      case 'ar': { const [id, pen, dmg, face] = v; const t = this.unitByNetId(id); if (t && t.alive && (t.kind === 'tank' || t.kind === 'gun')) this.armourApply(t, rp || null, { pen: !!pen, dmg, face }); break; }
      case 'n': { const [x, y, z, vx, vy, vz, fuse] = v; this.throwGrenadeFrom(rp || { team: side, x, z }, x, y, z, vx, vy, vz, fuse, peer); break; }
      case 'd': { const [kid, how] = v; if (rp && rp.alive) rp.die(this.unitByNetId(kid), how || ''); break; }
    }
  }
  /** online client: my round hit something that lives on the host's page (or, as host, hit another player) */
  netHit(r, best, u, hx, hy, hz) {
    const id = this.netIdOf(u), mine = r.from?.isPlayer;
    if (best.part === 'hull') {
      if (r.w.pen100) { const d = Math.hypot(hx - r.from.x, hz - r.from.z), res = this.armourCalc(u, r.w.pen100 - r.w.penDrop * Math.max(0, d - 100) / 100, r.from.x, r.from.z, true);
        this.fx.impact(hx, hy, hz, 'metal'); SND.impact(hx, hy, hz, 'metal'); if (res.pen) this.fx.flash(hx, hy, hz, .8);
        if (this.net?.client) this.online.act('ar', id, res.pen ? 1 : 0, res.dmg, res.face); else this.armourApply(u, r.from, res);
        if (mine) this.hud.hint(res.pen ? `Penetration — ${res.face} armour` : `Ricochet — ${res.face} armour too thick at this range`, 2); }
      else { this.fx.impact(hx, hy, hz, 'metal'); SND.impact(hx, hy, hz, 'metal'); }
      return;
    }
    const speedK = Math.hypot(r.vx, r.vy, r.vz) / r.w.mv, dmg = r.w.dmg * Math.max(.4, speedK); this.fx.impact(hx, hy, hz, 'flesh');
    if (this.net?.client) this.online.act('h', id, dmg, best.part, ''); else this.hurtHuman(u, dmg, r.from, best.part, '');
    if (mine) { this.stats.hits++; this.hud.hit(false); SND.hitMarker(); }
  }
  /** host: the battlefield for the snapshot — every unit, plus the host's own soldier */
  netUnitTable() {
    const out = [], now = this.time;
    for (const u of this.units) { const ti = Math.max(0, TYPES.indexOf(u.type)), sp = u.spotted;
      out.push({ id: u.id % 65000, code: (ti & 7) | (u.team << 3) | ((u.stance & 3) << 4) | (u.alive ? 64 : 0) | (u.leader ? 128 : 0), flags: (sp && sp[0] > now ? 1 : 0) | (sp && sp[1] > now ? 2 : 0) | (u.isRemote ? 4 : 0) | (u.reloading ? 16 : 0), x: u.x, z: u.z, yaw: u.yaw, extra: u.kind === 'tank' || u.kind === 'gun' ? u.turretYaw : (u.aimPitch || 0) }); }
    const p = this.player; if (p) { const C = classUnit(this.cls); out.push({ id: HOST_UNIT, code: TYPES.indexOf(C[0]) | (p.team << 3) | ((p.stance & 3) << 4) | (p.alive ? 64 : 0) | (C[1] ? 128 : 0), flags: 4 | (p.reloadT > 0 ? 16 : 0), x: p.x, z: p.z, yaw: p.yaw, extra: clamp(p.pitch * .6, -.4, .4) }); }
    return out;
  }
  /** online client: the host's latest snapshot */
  applySnap(d, now) {
    const on = this.online;
    if (d.b !== this.netBattle) { this.netBattle = d.b; this.mi = d.mi; if (d.sz) this.settings.size = d.sz; if (d.ln) this.settings.length = d.ln; if (this.state !== 'loading') this.start(); return; }
    if (!this.scene || this.state === 'loading' || this.state === 'menu') return;
    this.timeLeft = d.tl / 10; this.roundLen = d.rl || this.roundLen; this.tickets = d.tk.slice(0, 2);
    const oldAo = this.activeObj; this.activeObj = d.ao; d.ob.forEach((a, i) => { const o = this.objectives[i]; if (o) { o.owner = a[0]; o.prog = a[1] / 100; } });
    if (d.ao > oldAo && oldAo < this.objectives.length) { const o = this.objectives[oldAo], mine = this.M.attacker === this.side; this.hud.banner(`${o.n} ${mine ? 'captured' : 'lost'}`, d.ao < this.objectives.length ? (mine ? 'Regroup — the next position opens in 25 s' : 'Fall back to the next position!') : '', 4); }
    const names = new Map(); this.myUnit = -2; for (const id in d.pl) { const a = d.pl[id]; if (!Array.isArray(a)) continue; names.set(a[2], String(a[0])); if (id === on.id) this.myUnit = a[2]; }
    if (d.u) { const seen = new Set(), delay = 1700 / HZ_SNAP;
      for (const e of unpackUnits(d.u)) {
        if (e.id === this.myUnit && this.myUnit >= 0) continue; seen.add(e.id);
        const type = TYPES[e.code & 7], team = (e.code >> 3) & 1, stance = (e.code >> 4) & 3, alive = !!(e.code & 64), leader = !!(e.code & 128);
        let u = this.netById.get(e.id);
        if (u && (u.type !== type || u.team !== team || (alive && !u.alive))) { this.removeUnit(u); this.netById.delete(e.id); u = null; }
        if (!u) { u = this.spawnUnit(team, type, e.x, e.z, { yaw: e.yaw, leader }); u.net = true; u.id = e.id; u.nb = []; u.netDelay = delay; u.stance = stance; if (type === 'tank' || type === 'at') u.turretYaw = e.extra; if (!alive) u.netDie(true); this.netById.set(e.id, u); }
        u.nb.push({ t: now, x: e.x, z: e.z, yaw: e.yaw, ex: e.extra, st: stance, rl: e.flags & 16 }); if (u.nb.length > 12) u.nb.shift();
        if (u.alive && !alive) u.netDie(false);
        u.spotted = [e.flags & 1 ? this.time + 1 : 0, e.flags & 2 ? this.time + 1 : 0];
        if (e.flags & 4) { u.human = true; u.name = names.get(e.id) || u.name; }
      }
      for (const [id, u] of this.netById) if (!seen.has(id)) { this.removeUnit(u); this.netById.delete(id); } }
    if (on.fresh) { on.fresh = false; for (const e of d.ev) on.lastEv = Math.max(on.lastEv, e[0]); }
    else for (const e of d.ev) if (Array.isArray(e) && e[0] > on.lastEv) { on.lastEv = e[0]; try { this.onNetEvent(e); } catch (err) { console.error(err); } }
  }
  /** online client: one thing that happened on the host's page */
  onNetEvent(e) {
    const t = e[1], a = e.slice(2), me = this.online.id, p = this.player;
    switch (t) {
      case 'sh': { const [code, x, y, z, dx, dy, dz, d, fl, ix, iy, iz, mat] = a; this.shotFx(GUNS[code] || 'rifle', x, y, z, dx / 1000, dy / 1000, dz / 1000, d, fl & 1, fl & 2 ? .8 : .6, ix, iy, iz, mat >= 0 ? MATS[mat] : null, { team: (fl >> 2) & 1 }); break; }
      case 'ps': { const [org, code, x, y, z, dx, dy, dz, fl] = a; if (org !== me) this.otherShot(code, x, y, z, dx / 1000, dy / 1000, dz / 1000, fl); break; }
      case 'cn': { const [code, mx, my, mz, fx, fz, ix, iy, iz, d, gy] = a; this.cannonFx(GUNS[code] || 'cannon37', mx, my, mz, fx / 1000, fz / 1000, ix, iy, iz, d, gy); break; }
      case 'bm': { const [x, y, z, size, ground, R] = a; this.explodeFx(x, y, z, size, !!ground); if (p && p.alive) { const dd = Math.hypot(p.x - x, p.z - z); if (dd < R * 4) { p.supp = 1; this.shake = Math.min(1.5, (this.shake || 0) + (1 - dd / (R * 4)) * 1.5); } } break; }
      case 'im': { const [x, y, z, m] = a; const cam = this.camera.position; if (Math.hypot(cam.x - x, cam.z - z) < 120) { this.fx.impact(x, y, z, MATS[m] || 'dirt'); SND.impact(x, y, z, MATS[m] || 'dirt'); } break; }
      case 'wh': SND.shellWhistle(a[0], a[1], a[2], a[3]); break;
      case 'gs': SND.gunshot(GUNS[a[0]] || 'rifle', a[1], a[2], a[3]); break;
      case 'nd': { const [org, x, y, z, vx, vy, vz, fuse, team] = a; if (org !== me) this.throwGrenadeFrom({ team }, x, y, z, vx, vy, vz, fuse, '', true); break; }
      case 'stuka': this.stuka(this.objectives[a[0]], true); break;
      case 'br': { const o = this.objectives[a[0]]; if (o) { this.hud.hint(a[1] === this.side ? 'Friendly artillery on the objective' : 'Incoming artillery!', 3); this.markers.push({ x: o.x, z: o.z, r: o.r + 10, col: '#f84', t: this.time + 20 }); } break; }
      case 'kl': { const [vid, kid, how, vteam, vkind] = a; const v = this.netById.get(vid);
        if (kid === this.myUnit && kid >= 0 && vid !== this.myUnit) { if (vteam === this.side) this.hud.feed(`<b>You</b> killed a friendly${v?.human ? ': ' + esc(v.name) : ''}`, '#f80'); else { this.stats.kills++; this.online.kills++; if (p) p.score += vkind === 1 ? 50 : 10; this.hud.hit(true); this.hud.feed(`<b>You</b> ✠ ${vkind === 1 ? NAMES[vteam].tank : esc(v?.name || NAMES[vteam].rifle)}${how === 'headshot' ? ' (head)' : ''}`, '#fda'); } }
        else if (vkind === 1) this.hud.feed(`${NAMES[vteam].tank} knocked out`, vteam === this.side ? '#e88' : '#ad8');
        else if (v?.human) { const k = this.netById.get(kid); this.hud.feed(`<b>${esc(v.name)}</b> ${k?.human ? 'killed by <b>' + esc(k.name) + '</b>' : 'fell'}`, '#ccc'); }
        break; }
      case 'dm': { const [peer, dmg, part, fid, fx, fz, how] = a; if (peer !== me || !p || !p.alive) break; const from = this.netById.get(fid) || (fid < 0 ? { x: fx, z: fz } : { x: fx, z: fz, team: 1 - this.side, type: 'rifle', kind: 'inf', name: '' }); p.hurt(dmg, from, part, how || undefined); break; }
      case 'end': this.end(a[0]); break;
    }
  }
  /** online client: the host runs the battle; this page runs its own soldier and draws the rest */
  clientLogic(dt) {
    this.time += dt; if (this.redeployT > 0) this.redeployT -= dt; this.runTimers(); this.updatePlanes(dt);
    if (!this.over) this.timeLeft = Math.max(0, this.timeLeft - dt);
    this.todT = (this.todT || 0) - dt; if (this.todT <= 0) { this.todT = 2; this.applyTod(clamp(1 - this.timeLeft / this.roundLen, 0, 1)); this.bakeSky(false); }
    this.rebuildGrid();
    for (const u of [...this.units]) u.update(dt);
    if (this.player && this.player.alive && this.state === 'play') this.player.update(dt, I);
    this.updateRounds(dt); this.updateGrenades(dt);
    this.markers = this.markers.filter(m => m.t > this.time); this.shake = Math.max(0, (this.shake || 0) - dt * 2);
  }

  // ================= frame =================
  frame() {
    let dt = Math.min(.05, this.clock.getDelta()); try { this.online.tick(performance.now()); } catch (e) { console.error(e); } if (!this.scene || this.state === 'loading') { return; }
    if (I.pauseTap) { I.pauseTap = false; if (this.state === 'play') this.pause(true); else if (this.state === 'paused') this.pause(false); }
    if (I.mapTap) { I.mapTap = false; this.hud.toggleMap(); }
    const live = this.state === 'play' || this.state === 'dead' || this.state === 'deploy' || this.state === 'end';
    if (this.state === 'paused' && !this.net) dt = 0;
    if (live && dt > 0) this.logic(dt);
    // camera when not playing
    if (this.state === 'dead' && this.deadCam) { const c = this.deadCam; c.t += dt; this.camera.position.set(c.x, c.y + Math.min(1, c.t) * 2.5, c.z + Math.min(1, c.t) * 3); this.camera.lookAt(c.x, c.y, c.z); }
    if (this.state === 'deploy' || this.state === 'end') { this.camera.position.y += Math.sin(this.time * .2) * .01; }
    const cam = this.camera; const e = _E.setFromQuaternion(cam.quaternion, 'YXZ'); this.camYaw = e.y;
    Audio.setListener(cam.position.x, cam.position.y, cam.position.z, this.camYaw);
    SND.ambienceTick(dt, clamp(this.units.length / 80, 0, 1), this.frontDir);
    // shadows follow the camera
    if (this.sun) { const s = this.sunDir, sc = this.sun.shadow.camera, ext = sc.right || 50, texel = 2 * ext / (this.sun.shadow.mapSize.x || 1024);
      _V.set(0, 0, -1).applyQuaternion(cam.quaternion); _V.y = 0; if (_V.lengthSq() > 1e-6) _V.normalize(); _C.copy(cam.position).addScaledVector(_V, ext * .42);
      _R.crossVectors(_UP, s); if (_R.lengthSq() < 1e-6) _R.set(1, 0, 0); _R.normalize(); _U.crossVectors(s, _R).normalize();
      const u = Math.round(_C.dot(_R) / texel) * texel, v = Math.round(_C.dot(_U) / texel) * texel, w = _C.dot(s); _C.copy(_R).multiplyScalar(u).addScaledVector(_U, v).addScaledVector(s, w);
      this.sun.position.copy(_C).addScaledVector(s, 200); this.sun.target.position.copy(_C); this.sun.target.updateMatrixWorld(); }
    FOGU.fwTime.value = this.time; const wm = this.world.waterMat; if (wm && wm.normalMap) wm.normalMap.offset.set(this.time * .011, this.time * .006);
    this.world.sky.position.copy(cam.position); this.world.skyU.time.value = this.time;
    this.treeT = (this.treeT || 0) - dt; if (this.treeT <= 0) { this.treeT = .5; this.world.cullTrees(cam.position.x, cam.position.z, this.scene.fog.far * .85); }
    this.fx.update(dt, cam); if (this.grass) this.grass.update(cam, dt);
    if (this.state !== 'menu' && this.state !== 'loading') this.hud.update(dt);
    // render world, then the viewmodel over it with its own depth
    const r = this.renderer, P = this.post;
    if (P) { P.begin(); const sv = _V.copy(this.sunDir).multiplyScalar(1000).add(cam.position).project(cam); const fwd = _V2.set(0, 0, -1).applyQuaternion(cam.quaternion).dot(this.sunDir);
      P.u.sunPos.value.set((sv.x + 1) / 2, (sv.y + 1) / 2, fwd > 0 && Math.abs(sv.x) < 1.4 && Math.abs(sv.y) < 1.4 ? Math.min(1, fwd * 1.5) * (1 - (this.rainNow || 0)) : 0); P.u.sunCol.value.copy(FOGU.fogSunCol.value);
      P.u.exposure.value = this.exposure || 1.1; P.u.time.value = this.time % 100; P.u.supp.value = this.player && this.player.alive ? this.player.supp : 0; P.u.wet.value = this.rainNow || 0; }
    else r.clear();
    r.render(this.scene, cam);
    if (this.player && this.player.alive && this.state !== 'dead') { r.clearDepth(); r.render(this.vmScene, this.vmCamera); }
    if (P) P.end();
    if (this.settings.fps) { this.fpsA.push(dt); if (this.fpsA.length > 30) this.fpsA.shift(); $('#fps').textContent = (30 / this.fpsA.reduce((a, b) => a + b, 0)).toFixed(0) + ' fps · ' + r.info.render.calls + ' calls'; }
  }
  logic(dt) {
      if (this.net?.client) return this.clientLogic(dt);
      this.time += dt; if (this.redeployT > 0) this.redeployT -= dt; this.runTimers(); this.updatePlanes(dt);
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
  spotting() { for (const u of this.units) if (u.alive && u.target && u.target.team !== u.team && this.time - (u.tgtSeen || 0) < 3) (u.target.spotted || (u.target.spotted = [0, 0]))[u.team] = this.time + 6; }
}
function segSphere(ax, ay, az, bx, by, bz, cx, cy, cz, r) { const dx = bx - ax, dy = by - ay, dz = bz - az, fx = ax - cx, fy = ay - cy, fz = az - cz; const a = dx * dx + dy * dy + dz * dz, b = 2 * (fx * dx + fy * dy + fz * dz), c = fx * fx + fy * fy + fz * fz - r * r, disc = b * b - 4 * a * c; if (disc < 0) return null; const t = (-b - Math.sqrt(disc)) / (2 * a); return t >= 0 && t <= 1 ? t : null; }

// boot
const game = new Game(); window.FW = game;
$('#resume').onclick = () => game.pause(false); $('#pmenu').onclick = () => game.leaveBattle();
$('#pvol').oninput = e => { game.settings.volume = +e.target.value; Audio.setVolume(+e.target.value); game.saveSettings(); };
$('#pfps').onchange = e => { game.settings.fps = e.target.checked; $('#fps').style.display = e.target.checked ? 'block' : 'none'; game.saveSettings(); };
// Stuka for air raids, built once models load
const waitModels = setInterval(async () => { if (game.modelsLoaded) { clearInterval(waitModels); const { spawnModel } = await import('./models.js'); game.stukaModel = spawnModel('stuka', game.mats ? game.mats.steel : new THREE.MeshStandardMaterial({ vertexColors: true }), false); } }, 500);
