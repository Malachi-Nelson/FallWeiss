// Fall Weiss web — synthesized battlefield audio (WebAudio). No sample files: every report is built from layered noise and
// oscillators, delayed by the speed of sound, filtered by distance (air absorption), panned by bearing and sent to an outdoor
// reverb. A master compressor + limiter keeps MG bursts and cannon from clipping on phone speakers.
const C = 343;
let ctx = null, master, comp, sfx, amb, rev, revSend, noiseBuf, brownBuf, listener = { x: 0, y: 0, z: 0, yaw: 0 }, active = 0, muted = false;
let volume = 0.9;
const MAXV = 30;

function mkNoise(sec, brown) {
  const n = ctx.sampleRate * sec, b = ctx.createBuffer(1, n, ctx.sampleRate), d = b.getChannelData(0);
  let last = 0;
  for (let i = 0; i < n; i++) { const w = Math.random() * 2 - 1; if (brown) { last = (last + .02 * w) / 1.02; d[i] = last * 3.5; } else d[i] = w; }
  return b;
}
function mkIR(sec, decay) {
  const n = ctx.sampleRate * sec, b = ctx.createBuffer(2, n, ctx.sampleRate);
  for (let c = 0; c < 2; c++) {
    const d = b.getChannelData(c);
    for (let i = 0; i < n; i++) {
      const t = i / ctx.sampleRate; let v = (Math.random() * 2 - 1) * Math.pow(1 - i / n, decay);
      // a few discrete slap-back echoes off tree lines and buildings
      if (Math.abs(t - .09 - c * .013) < .002 || Math.abs(t - .21 + c * .02) < .003 || Math.abs(t - .43) < .004) v *= 5;
      d[i] = v * (t < .012 ? t / .012 : 1);
    }
  }
  return b;
}

export const Audio = {
  get ready() { return !!ctx; },
  init() {
    if (ctx) { if (ctx.state !== 'running') ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext; if (!AC) return;
    ctx = new AC({ latencyHint: 'interactive' });
    master = ctx.createGain(); master.gain.value = volume;
    comp = ctx.createDynamicsCompressor(); comp.threshold.value = -16; comp.knee.value = 8; comp.ratio.value = 5; comp.attack.value = .002; comp.release.value = .18;
    const lim = ctx.createDynamicsCompressor(); lim.threshold.value = -2; lim.knee.value = 0; lim.ratio.value = 20; lim.attack.value = .001; lim.release.value = .06;
    master.connect(comp); comp.connect(lim); lim.connect(ctx.destination);
    sfx = ctx.createGain(); sfx.connect(master); amb = ctx.createGain(); amb.gain.value = .55; amb.connect(master);
    rev = ctx.createConvolver(); rev.buffer = mkIR(2.6, 3.2); revSend = ctx.createGain(); revSend.gain.value = .55; revSend.connect(rev); rev.connect(master);
    noiseBuf = mkNoise(2, false); brownBuf = mkNoise(4, true);
    if (ctx.state !== 'running') ctx.resume();
  },
  setVolume(v) { volume = v; if (master) master.gain.value = muted ? 0 : v; },
  mute(m) { muted = m; if (master) master.gain.value = m ? 0 : volume; },
  suspend() { if (ctx) ctx.suspend(); }, resume() { if (ctx) ctx.resume(); },
  setListener(x, y, z, yaw) { listener.x = x; listener.y = y; listener.z = z; listener.yaw = yaw; },
  get time() { return ctx ? ctx.currentTime : 0; },
};

// ---------- spatial helper: returns {out(node), t0, dist} or null
function spatial(x, y, z, opts = {}) {
  if (!ctx || ctx.state !== 'running') return null;
  const dx = x - listener.x, dy = y - listener.y, dz = z - listener.z, d = Math.sqrt(dx * dx + dy * dy + dz * dz);
  const maxD = opts.maxD || 1500; if (d > maxD) return null;
  if (active > MAXV && d > 60 && !opts.force) return null;
  const near = opts.near || 6;
  let g = (opts.gain || 1) * Math.min(1, near / Math.max(near, d)) ;
  g *= Math.pow(Math.max(0, 1 - d / maxD), .7);
  if (g < .004) return null;
  // bearing relative to the view: three.js forward is -z, yaw rotates about +y
  const ang = Math.atan2(dx, -dz) + listener.yaw; let pan = Math.sin(ang) * Math.min(1, d / 4);
  const t0 = ctx.currentTime + (opts.noDelay ? 0 : Math.min(3, d / C));
  const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = Math.max(500, 18000 / (1 + d / 90)); lp.Q.value = .5;
  const gn = ctx.createGain(); gn.gain.value = g;
  const pn = ctx.createStereoPanner ? ctx.createStereoPanner() : null;
  lp.connect(gn); if (pn) { pn.pan.value = Math.max(-.9, Math.min(.9, pan)); gn.connect(pn); pn.connect(opts.bus || sfx); } else gn.connect(opts.bus || sfx);
  // far sounds are mostly reverb (the "rolling" report over open fields)
  const rs = ctx.createGain(); rs.gain.value = (opts.rev ?? .35) * Math.min(1.6, .25 + d / 220) * g; gn.connect(rs); rs.connect(revSend);
  return { inp: lp, t0, d, g };
}
function track(src, dur) { active++; setTimeout(() => active--, (dur + .2) * 1000 + 3500); }

function noiseBurst(dest, t0, dur, type, freq, q, a = .001, peak = 1, startOff) {
  const s = ctx.createBufferSource(); s.buffer = noiseBuf; s.loop = true;
  const f = ctx.createBiquadFilter(); f.type = type; f.frequency.value = freq; f.Q.value = q;
  const e = ctx.createGain(); e.gain.setValueAtTime(0, t0); e.gain.linearRampToValueAtTime(peak, t0 + a); e.gain.exponentialRampToValueAtTime(.0008, t0 + a + dur);
  s.connect(f); f.connect(e); e.connect(dest); s.start(t0, startOff ?? Math.random() * 1.5); s.stop(t0 + a + dur + .05); return { s, f, e };
}
function thump(dest, t0, f0, f1, dur, peak) {
  const o = ctx.createOscillator(); o.type = 'sine'; o.frequency.setValueAtTime(f0, t0); o.frequency.exponentialRampToValueAtTime(f1, t0 + dur);
  const e = ctx.createGain(); e.gain.setValueAtTime(peak, t0); e.gain.exponentialRampToValueAtTime(.0008, t0 + dur);
  o.connect(e); e.connect(dest); o.start(t0); o.stop(t0 + dur + .05);
}
function click(dest, t0, f, peak = .3, dur = .012) { noiseBurst(dest, t0, dur, 'bandpass', f, 6, .0005, peak); }

// Weapon report profiles: crack (supersonic N-wave), body (muzzle blast), boom (low thump), tail (length of field echo)
const GUN = {
  rifle:  { crack: 1, body: 1, bf: 900, boom: 1, bo: 95, tail: .55, g: 1.0 },
  atr:    { crack: 1.3, body: 1.3, bf: 700, boom: 1.4, bo: 70, tail: .8, g: 1.25 },
  smg:    { crack: .15, body: .55, bf: 1200, boom: .5, bo: 120, tail: .3, g: .55 },
  pistol: { crack: .1, body: .45, bf: 1500, boom: .35, bo: 140, tail: .25, g: .45 },
  lmg:    { crack: .8, body: .8, bf: 1000, boom: .8, bo: 100, tail: .45, g: .85 },
  mg34:   { crack: .8, body: .75, bf: 1050, boom: .75, bo: 105, tail: .4, g: .8 },
  mg30:   { crack: .85, body: .8, bf: 950, boom: .8, bo: 95, tail: .45, g: .85 },
  mg:     { crack: .8, body: .75, bf: 1050, boom: .75, bo: 105, tail: .4, g: .8 },
  kwk20:  { crack: .9, body: 1.2, bf: 600, boom: 1.6, bo: 60, tail: .9, g: 1.3 },
  cannon37: { crack: 1, body: 1.6, bf: 450, boom: 2.4, bo: 48, tail: 1.6, g: 1.9 },
  naval:  { crack: .5, body: 2.2, bf: 220, boom: 3.2, bo: 32, tail: 3.5, g: 3.2 },
};

export function gunshot(kind, x, y, z, own = false) {
  const P = GUN[kind] || GUN.rifle;
  const sp = spatial(x, y, z, { gain: P.g * (own ? .9 : 1), near: own ? 3 : 8, maxD: kind === 'naval' ? 4000 : 1800, rev: .3 + P.tail * .3, noDelay: own });
  if (!sp) return; const { inp, t0, d } = sp; const jit = .9 + Math.random() * .2;
  if (P.crack > 0) noiseBurst(inp, t0, .012 + .01 * Math.min(1, d / 200), 'highpass', 2400 * jit, .7, .0003, P.crack * (d < 300 ? 1 : .4));
  noiseBurst(inp, t0, .07 + P.tail * .08, 'bandpass', P.bf * jit, .9, .001, P.body * 1.1);
  noiseBurst(inp, t0 + .004, .16 + P.tail * .35, 'lowpass', 600 * jit, .7, .004, P.body * .5);
  thump(inp, t0, P.bo * 2.2 * jit, P.bo * .6, .12 + P.boom * .1, P.boom * .9);
  if (P.tail > .5) { const b = ctx.createBufferSource(); b.buffer = brownBuf; const e = ctx.createGain(); e.gain.setValueAtTime(P.tail * .5, t0 + .02); e.gain.exponentialRampToValueAtTime(.001, t0 + .4 + P.tail); b.connect(e); e.connect(inp); b.start(t0 + .02, Math.random() * 2); b.stop(t0 + .5 + P.tail); }
  track(null, .3 + P.tail);
}

export function explosion(x, y, z, size = 1) {
  const sp = spatial(x, y, z, { gain: 1.6 * size, near: 10, maxD: 3000, rev: .7, force: true }); if (!sp) return; const { inp, t0, d } = sp;
  noiseBurst(inp, t0, .05, 'highpass', 1800, .7, .0005, d < 120 ? .9 : .2);
  noiseBurst(inp, t0, .5 * size + .3, 'lowpass', 900, .6, .002, 1.4);
  thump(inp, t0, 110, 28, .7 + .3 * size, 1.8 * size);
  const b = ctx.createBufferSource(); b.buffer = brownBuf; const e = ctx.createGain(); e.gain.setValueAtTime(1.2 * size, t0); e.gain.exponentialRampToValueAtTime(.001, t0 + 1.8 + size);
  b.connect(e); e.connect(inp); b.start(t0, Math.random() * 2); b.stop(t0 + 2.2 + size);
  // falling debris patter for close blasts
  if (d < 60) for (let i = 0; i < 10; i++) click(inp, t0 + .35 + Math.random() * .9, 900 + Math.random() * 1600, .12);
  track(null, 2.5);
}

/** Supersonic round passing the listener: the sharp "crack-thump" every RO2 player knows; subsonic 9 mm gives a whiz. */
export function bulletPass(missDist, supersonic, side) {
  if (!ctx || ctx.state !== 'running') return; const t0 = ctx.currentTime;
  const g = Math.max(.1, 1 - missDist / 6) * .9; const pn = ctx.createStereoPanner(); pn.pan.value = side; pn.connect(sfx);
  if (supersonic) { noiseBurst(pn, t0, .006, 'highpass', 3500, .8, .0002, g * 1.3); noiseBurst(pn, t0 + .002, .03, 'bandpass', 1400, 2, .001, g * .5); }
  else { const s = ctx.createBufferSource(); s.buffer = noiseBuf; const f = ctx.createBiquadFilter(); f.type = 'bandpass'; f.Q.value = 8; f.frequency.setValueAtTime(3000, t0); f.frequency.exponentialRampToValueAtTime(900, t0 + .18); const e = ctx.createGain(); e.gain.setValueAtTime(0, t0); e.gain.linearRampToValueAtTime(g * .6, t0 + .06); e.gain.exponentialRampToValueAtTime(.001, t0 + .2); s.connect(f); f.connect(e); e.connect(pn); s.start(t0, Math.random()); s.stop(t0 + .25); }
}
export function impact(x, y, z, mat) {
  const sp = spatial(x, y, z, { gain: .35, near: 3, maxD: 120, rev: .2 }); if (!sp) return; const { inp, t0 } = sp;
  if (mat === 'metal') { const o = ctx.createOscillator(); o.type = 'triangle'; o.frequency.setValueAtTime(2600 + Math.random() * 1500, t0); o.frequency.exponentialRampToValueAtTime(1800, t0 + .25); const e = ctx.createGain(); e.gain.setValueAtTime(.5, t0); e.gain.exponentialRampToValueAtTime(.001, t0 + .3); o.connect(e); e.connect(inp); o.start(t0); o.stop(t0 + .32); click(inp, t0, 4000, .6); }
  else if (mat === 'stone') { click(inp, t0, 2600, .7, .02); noiseBurst(inp, t0 + .005, .08, 'bandpass', 1200, 1, .001, .3); if (Math.random() < .35) ricochet(inp, t0 + .01); }
  else if (mat === 'wood') { noiseBurst(inp, t0, .05, 'bandpass', 700, 2, .001, .6); }
  else if (mat === 'flesh') { noiseBurst(inp, t0, .06, 'lowpass', 420, 1, .001, .9); }
  else { noiseBurst(inp, t0, .07, 'lowpass', 900, .8, .001, .5); }
}
function ricochet(dest, t0) {
  const o = ctx.createOscillator(); o.type = 'sine'; const f0 = 2800 + Math.random() * 1600; o.frequency.setValueAtTime(f0, t0); o.frequency.exponentialRampToValueAtTime(f0 * .45, t0 + .5);
  const e = ctx.createGain(); e.gain.setValueAtTime(.18, t0); e.gain.exponentialRampToValueAtTime(.001, t0 + .55); o.connect(e); e.connect(dest); o.start(t0); o.stop(t0 + .6);
}
export function hitMarker() { if (!ctx) return; const t0 = ctx.currentTime; noiseBurst(sfx, t0, .05, 'lowpass', 300, 1, .001, .6); }
export function playerHurt() { if (!ctx) return; const t0 = ctx.currentTime; thump(sfx, t0, 90, 40, .25, .8); noiseBurst(sfx, t0, .12, 'lowpass', 500, 1, .002, .6); }

// ---------- weapon handling (own weapon only, no delay)
export function mech(kind) {
  if (!ctx || ctx.state !== 'running') return; const t = ctx.currentTime, o = sfx;
  const k = (dt, f, p, du) => click(o, t + dt, f, p, du);
  switch (kind) {
    case 'bolt': k(.02, 1800, .35, .02); k(.12, 2600, .25); k(.22, 2200, .28, .02); k(.34, 1500, .4, .025); break; // lift, back, forward, down
    case 'clip': k(0, 2000, .3); k(.25, 1400, .25, .05); noiseBurst(o, t + .3, .25, 'bandpass', 2800, 2, .01, .12); k(.6, 3000, .2); k(.9, 1700, .35); break; // stripper clip pressed in
    case 'mag': k(0, 1600, .35, .02); k(.5, 900, .3, .03); k(1.2, 1800, .4, .02); k(1.6, 2400, .35); break;
    case 'belt': k(0, 1200, .4, .03); k(.8, 2000, .3); noiseBurst(o, t + 1.2, .4, 'bandpass', 3200, 3, .02, .1); k(2.4, 1500, .4, .03); k(3.1, 2200, .4); break;
    case 'dry': k(0, 3200, .35, .008); break;
    case 'switch': noiseBurst(o, t, .18, 'bandpass', 900, 1, .02, .15); k(.2, 2000, .2); break;
    case 'pin': k(0, 3500, .3, .01); k(.15, 2400, .25); break;
    case 'throw': noiseBurst(o, t, .2, 'bandpass', 600, 1, .05, .25); break;
  }
}

// ---------- movement
export function footstep(x, y, z, surf, own, run) {
  const sp = spatial(x, y, z, { gain: own ? (run ? .35 : .22) : .25, near: 2, maxD: own ? 10 : 45, rev: .05, noDelay: true }); if (!sp) return; const { inp, t0 } = sp;
  if (surf === 'road' || surf === 'stone') { click(inp, t0, 1500 + Math.random() * 600, .5, .025); noiseBurst(inp, t0 + .01, .05, 'bandpass', 2600, 1, .002, .25); }
  else if (surf === 'water') { noiseBurst(inp, t0, .2, 'bandpass', 900, .7, .02, .8); }
  else if (surf === 'wood') { noiseBurst(inp, t0, .07, 'bandpass', 420, 2, .002, .7); }
  else { noiseBurst(inp, t0, .09, 'bandpass', 1400 + Math.random() * 500, .6, .008, .55); noiseBurst(inp, t0 + .03, .07, 'lowpass', 400, 1, .004, .3); }
  if (own && Math.random() < .3) click(inp, t0 + .05, 3000, .06); // kit rattle
}

// ---------- tank engines: one looping voice per nearby tank
export class Engine {
  constructor(big) {
    this.on = false; this.big = big;
    if (!ctx) return;
    this.o1 = ctx.createOscillator(); this.o1.type = 'sawtooth'; this.o2 = ctx.createOscillator(); this.o2.type = 'square';
    this.n = ctx.createBufferSource(); this.n.buffer = brownBuf; this.n.loop = true;
    this.f = ctx.createBiquadFilter(); this.f.type = 'lowpass'; this.f.frequency.value = 380; this.f.Q.value = 2;
    this.g = ctx.createGain(); this.g.gain.value = 0; this.pn = ctx.createStereoPanner();
    const ng = ctx.createGain(); ng.gain.value = 1.4; this.n.connect(ng); ng.connect(this.f);
    const og = ctx.createGain(); og.gain.value = .25; this.o1.connect(og); this.o2.connect(og); og.connect(this.f);
    this.f.connect(this.g); this.g.connect(this.pn); this.pn.connect(sfx);
    this.o1.start(); this.o2.start(); this.n.start(); this.on = true; this.clank = 0;
  }
  update(x, y, z, speed, dt) {
    if (!this.on) return; const dx = x - listener.x, dz = z - listener.z, d = Math.hypot(dx, dz, y - listener.y);
    const rpm = .55 + Math.min(1, Math.abs(speed) / 6) * .6, base = this.big ? 31 : 37;
    this.o1.frequency.value = base * rpm; this.o2.frequency.value = base * rpm * 1.5 + 1.3;
    this.f.frequency.value = Math.max(160, (240 + rpm * 420) / (1 + d / 250));
    this.g.gain.value = .5 * Math.min(1, 14 / Math.max(14, d)) * Math.max(0, 1 - d / 700);
    this.pn.pan.value = Math.max(-.9, Math.min(.9, Math.sin(Math.atan2(dx, -dz) + listener.yaw) * Math.min(1, d / 5)));
    this.clank -= dt * Math.abs(speed) * 1.3;
    if (this.clank < 0 && d < 160 && Math.abs(speed) > .5) { this.clank = 1 + Math.random(); const sp = spatial(x, y, z, { gain: .25, near: 6, maxD: 160, noDelay: true, rev: .15 }); if (sp) { click(sp.inp, sp.t0, 700 + Math.random() * 500, .5, .03); click(sp.inp, sp.t0 + .06, 2000, .2); } }
  }
  stop() { if (!this.on) return; this.on = false; try { this.o1.stop(); this.o2.stop(); this.n.stop(); this.g.disconnect(); } catch (e) { } }
}

// ---------- air: Stuka dive with Jericho siren, artillery whistle
export function stukaDive(getPos, dur) {
  if (!ctx || ctx.state !== 'running') return; const t0 = ctx.currentTime;
  const sir = ctx.createOscillator(); sir.type = 'sawtooth'; const sir2 = ctx.createOscillator(); sir2.type = 'triangle';
  const f = ctx.createBiquadFilter(); f.type = 'bandpass'; f.Q.value = 3; f.frequency.value = 900;
  const eng = ctx.createBufferSource(); eng.buffer = brownBuf; eng.loop = true; const ef = ctx.createBiquadFilter(); ef.type = 'lowpass'; ef.frequency.value = 500; const eo = ctx.createOscillator(); eo.type = 'sawtooth'; eo.frequency.value = 72;
  const g = ctx.createGain(); g.gain.value = 0; const pn = ctx.createStereoPanner();
  const sg = ctx.createGain(); sg.gain.value = .5; sir.connect(sg); sir2.connect(sg); sg.connect(f); f.connect(g);
  const eg = ctx.createGain(); eg.gain.value = 1; eng.connect(ef); const eog = ctx.createGain(); eog.gain.value = .12; eo.connect(eog); eog.connect(ef); ef.connect(eg); eg.connect(g);
  g.connect(pn); pn.connect(sfx); const rs = ctx.createGain(); rs.gain.value = .3; g.connect(rs); rs.connect(revSend);
  sir.start(); sir2.start(); eng.start(); eo.start(); active += 2;
  const iv = setInterval(() => {
    const t = ctx.currentTime - t0, p = getPos(); if (!p || t > dur) { clearInterval(iv); g.gain.setTargetAtTime(0, ctx.currentTime, .3); setTimeout(() => { try { sir.stop(); sir2.stop(); eng.stop(); eo.stop(); } catch (e) { } active -= 2; }, 1500); return; }
    const dx = p.x - listener.x, dz = p.z - listener.z, d = Math.hypot(dx, dz, p.y - listener.y);
    const dive = p.dive || 0; // 0..1 dive progress: siren rises with airspeed
    sir.frequency.value = 180 + dive * 420; sir2.frequency.value = (180 + dive * 420) * 1.505;
    sg.gain.value = .6 * dive;
    g.gain.setTargetAtTime(Math.min(1.1, 90 / Math.max(60, d)), ctx.currentTime, .1);
    pn.pan.value = Math.max(-.8, Math.min(.8, Math.sin(Math.atan2(dx, -dz) + listener.yaw)));
  }, 50);
}
export function shellWhistle(x, y, z, dur) {
  const sp = spatial(x, y, z, { gain: .7, near: 30, maxD: 600, noDelay: true, rev: .3 }); if (!sp) return; const { inp, t0 } = sp;
  const o = ctx.createOscillator(); o.type = 'sine'; o.frequency.setValueAtTime(1900, t0); o.frequency.exponentialRampToValueAtTime(380, t0 + dur);
  const e = ctx.createGain(); e.gain.setValueAtTime(.001, t0); e.gain.exponentialRampToValueAtTime(.35, t0 + dur * .9); e.gain.linearRampToValueAtTime(0, t0 + dur);
  o.connect(e); e.connect(inp); o.start(t0); o.stop(t0 + dur + .02);
  noiseBurst(inp, t0 + dur * .5, dur * .5, 'bandpass', 1200, 3, dur * .4, .2);
}

// ---------- ambience: wind, distant battle, rain, fire crackle
let ambNodes = null, distantT = 0;
export function startAmbience(opts) {
  stopAmbience(); if (!ctx) return; ambNodes = {};
  const w = ctx.createBufferSource(); w.buffer = noiseBuf; w.loop = true; const wf = ctx.createBiquadFilter(); wf.type = 'bandpass'; wf.frequency.value = 420; wf.Q.value = .6;
  const wg = ctx.createGain(); wg.gain.value = .18; const lfo = ctx.createOscillator(); lfo.frequency.value = .07; const lg = ctx.createGain(); lg.gain.value = .1; lfo.connect(lg); lg.connect(wg.gain);
  const lfo2 = ctx.createOscillator(); lfo2.frequency.value = .13; const lg2 = ctx.createGain(); lg2.gain.value = 180; lfo2.connect(lg2); lg2.connect(wf.frequency);
  w.connect(wf); wf.connect(wg); wg.connect(amb); w.start(); lfo.start(); lfo2.start(); ambNodes.wind = [w, lfo, lfo2];
  const r = ctx.createBufferSource(); r.buffer = noiseBuf; r.loop = true; const rf = ctx.createBiquadFilter(); rf.type = 'highpass'; rf.frequency.value = 1800; const rg = ctx.createGain(); rg.gain.value = 0;
  r.connect(rf); rf.connect(rg); rg.connect(amb); r.start(); ambNodes.rain = rg; ambNodes.rs = r;
  ambNodes.fireLevel = 0; distantT = 2;
}
export function setRain(v) { if (ambNodes) ambNodes.rain.gain.setTargetAtTime(v * .22, ctx.currentTime, .5); }
export function stopAmbience() { if (!ambNodes) return; try { ambNodes.wind.forEach(n => n.stop()); ambNodes.rs.stop(); } catch (e) { } ambNodes = null; }
/** Off-map war: far artillery rumbles and rifle/MG pops from beyond the map edge, louder toward the front. */
export function ambienceTick(dt, intensity, frontDir) {
  if (!ambNodes || !ctx || ctx.state !== 'running') return; distantT -= dt;
  if (distantT > 0) return; distantT = .6 + Math.random() * (3.5 - intensity * 2.2);
  const a = frontDir + (Math.random() - .5) * 2.2, d = 700 + Math.random() * 1400, x = listener.x + Math.sin(a) * d, z = listener.z - Math.cos(a) * d;
  const r = Math.random();
  if (r < .3) explosion(x, 0, z, .8 + Math.random());
  else if (r < .5) { const n = 3 + (Math.random() * 8 | 0); for (let i = 0; i < n; i++) setTimeout(() => gunshot('mg', x, 2, z), i * 85); }
  else gunshot(Math.random() < .2 ? 'cannon37' : 'rifle', x, 2, z);
}
