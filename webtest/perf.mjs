// Phone performance probe: emulates a phone held sideways, times each loading phase, profiles the game logic with the
// Chrome CPU profiler (top self-time functions) and counts the GPU work of one frame (draw calls, triangles, render
// passes, pixels). Headless Chrome renders on the CPU (SwiftShader), so GPU *timings* are meaningless here; the counts
// are what matter. Needs docs/ served on 127.0.0.1:8765.
// Usage: node webtest/perf.mjs [mission=1] [q=1] [size=0.5] [sim=30] [profile=1]
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
const args = Object.fromEntries(process.argv.slice(2).map(a => a.split('=')));
const BASE = args.base || 'http://127.0.0.1:8765/', MI = +(args.mission ?? 1), Q = +(args.q ?? 1), SIZE = +(args.size ?? .5), SIM = +(args.sim ?? 30);
const FAKE = `window.mqtt = { connect() { const h = {}; const c = { on(e, f) { (h[e] = h[e] || []).push(f); return c; }, subscribe(t, o, cb) { setTimeout(() => cb && cb(null), 0); }, publish() {}, end() {} }; setTimeout(() => (h.connect || []).forEach(f => f()), 20); return c; } };`;
const b = await chromium.launch({ executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-background-timer-throttling'] });
const CTXO = { viewport: { width: 915, height: 412 }, deviceScaleFactor: 2.625, isMobile: true, hasTouch: true, userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36' };
// udd=<dir>: keep a browser profile between runs (tests the texture cache: run twice)
const ctx = args.udd ? await chromium.launchPersistentContext(args.udd, { ...CTXO, executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] }) : await b.newContext(CTXO);
await ctx.addInitScript(`localStorage.setItem('fw_settings', JSON.stringify({ quality: ${Q}, size: ${SIZE}, length: 'short', planMode: 'auto', place: 'auto' }));
  window.__phase = []; const _l = console.log; window.__mark = n => window.__phase.push([n, performance.now()]);`);
const p = await ctx.newPage(); const errs = [];
p.on('pageerror', e => errs.push('PAGEERROR: ' + e.message)); p.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
await p.route('**/mqtt.min.js', r => r.fulfill({ body: FAKE, contentType: 'application/javascript' }));
const t0 = Date.now();
await p.goto(BASE + `?m=${MI}&go=1`);
// record the loading screen's text changes as phase marks
await p.evaluate(() => { const lt = document.querySelector('#ltext'); let last = ''; new MutationObserver(() => { if (lt.textContent !== last) { last = lt.textContent; __mark(last); } }).observe(lt, { childList: true, characterData: true, subtree: true }); });
await p.waitForFunction(() => window.FW && FW.state === 'play', null, { timeout: 300000 });
const load = Date.now() - t0;
const phases = await p.evaluate(() => { const P = __phase; const out = []; for (let i = 0; i < P.length; i++) out.push([P[i][0], Math.round((P[i + 1] ? P[i + 1][1] : performance.now()) - P[i][1])]); return out; });
console.log('load total', load, 'ms'); for (const [n, ms] of phases) console.log('  ', String(ms).padStart(6), 'ms', n);
// GPU work for one frame
const gpu = await p.evaluate(() => { const r = FW.renderer; const info = r.info; info.autoReset = false; info.reset(); FW.frame(); const o = { calls: info.render.calls, tris: info.render.triangles, lines: info.render.lines, points: info.render.points, textures: info.memory.textures, geometries: info.memory.geometries, programs: info.programs?.length };
  info.autoReset = true; const s = r.getDrawingBufferSize(new (FW.camera.position.constructor)(0, 0)); o.buffer = `${r.domElement.width}x${r.domElement.height}`; o.pixelRatio = +r.getPixelRatio().toFixed(2); o.units = FW.units.length; o.trees = FW.world.trees.length; o.grass = FW.grass?.mesh?.count; o.post = !!FW.post; o.shadowMap = FW.sun.castShadow ? FW.sun.shadow.mapSize.x : 0; return o; });
console.log('frame', JSON.stringify(gpu));
// logic profile
const cdp = await ctx.newCDPSession(p);
if (args.profile !== '0') { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 200 }); await cdp.send('Profiler.start'); }
const simMs = await p.evaluate(([sec]) => { const g = FW; g.god = true; let ms = 0; const per = []; for (let t = 0; t < sec; t += 1) { const a = performance.now(); g.sim(1, 1 / 30); const d = performance.now() - a; ms += d; per.push(d / 30); } per.sort((a, b) => a - b); return { avg: +(ms / sec / 30).toFixed(2), p95: +per[Math.floor(per.length * .95)].toFixed(2), max: +per[per.length - 1].toFixed(2), alive: g.units.filter(u => u.alive).length }; }, [SIM]);
console.log('logic ms per 30 Hz tick', JSON.stringify(simMs));
if (args.profile !== '0') {
  const { profile } = await cdp.send('Profiler.stop'); const self = new Map(); const byId = new Map(profile.nodes.map(n => [n.id, n])); const dt = {};
  for (let i = 0; i < profile.samples.length; i++) { const id = profile.samples[i]; dt[id] = (dt[id] || 0) + (profile.timeDeltas[i] || 0); }
  let tot = 0; for (const n of profile.nodes) { const t = (dt[n.id] || 0) / 1000; tot += t; const f = n.callFrame; const k = `${f.functionName || '(anon)'} ${f.url.split('/').pop()}:${f.lineNumber + 1}`; self.set(k, (self.get(k) || 0) + t); }
  console.log('top self time (ms, of', Math.round(tot), ')'); [...self.entries()].sort((a, b) => b[1] - a[1]).slice(0, +(args.top || 18)).forEach(([k, v]) => console.log('  ', v.toFixed(0).padStart(6), (v / tot * 100).toFixed(1).padStart(5) + '%', k));
}
if (args.udd) { console.log('saved', await p.evaluate(async () => { const T = (await import('./js/textures.js')).TEX; return T.saving ? await T.saving : 'n/a'; })); console.log('texture cache used this run:', await p.evaluate(async () => (await import('./js/textures.js')).TEX.fromCache)); }
console.log(errs.length ? 'ERRORS ' + errs.slice(0, 5).join(' | ') : 'no errors');
await ctx.close(); await b.close();
