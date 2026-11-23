// Headless smoke test for the web edition: opens every battle, deploys, runs the battle for a while, renders a few
// frames, and reports console errors, page errors and timings. Needs the docs folder served over http (fetch of
// data/*.json does not work from file://):  python3 -m http.server 8765 --bind 127.0.0.1  (inside docs/)
// Usage: node webtest/smoke.mjs [missions=0,1,..] [simSeconds=60] [quality=1] [shots=dir]
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
const FAKE_MQTT = `window.mqtt = { connect() { const h = {}; const c = { on(e, f) { (h[e] = h[e] || []).push(f); return c; }, subscribe(t, o, cb) { setTimeout(() => cb && cb(null), 0); }, publish() {}, end() {} }; setTimeout(() => (h.connect || []).forEach(f => f()), 20); return c; } };`;
const args = Object.fromEntries(process.argv.slice(2).map(a => a.split('=')));
const BASE = args.base || 'http://127.0.0.1:8765/';
const missions = (args.missions || '0,1,2,3,4,5,6,7').split(',').map(Number);
const simSec = +(args.sim || 60), quality = +(args.q ?? 1), shots = args.shots || null, size = args.size || '0.5';
const b = await chromium.launch({ executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--autoplay-policy=no-user-gesture-required'] });
const ctx = await b.newContext({ viewport: { width: +(args.w || 960), height: +(args.h || 540) } });
await ctx.addInitScript(`localStorage.setItem('fw_settings', JSON.stringify({ quality: ${quality}, size: ${size}, length: 'short' }));`);
let bad = 0;
for (const mi of missions) {
  const p = await ctx.newPage(); const errs = [];
  p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text().slice(0, 300)); });
  p.on('pageerror', e => errs.push('PAGEERROR: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 4).join(' | ')));
  await p.route('**/mqtt.min.js', r => r.fulfill({ body: FAKE_MQTT, contentType: 'application/javascript' }));
  const t0 = Date.now();
  await p.goto(BASE + `?m=${mi}&go=1${args.side ? '&side=' + args.side : ''}`);
  try { await p.waitForFunction(() => window.FW && (FW.state === 'play' || /Error/.test(document.querySelector('#ltext')?.textContent || '')), null, { timeout: 120000 }); }
  catch (e) { errs.push('TIMEOUT waiting for play; ltext=' + await p.evaluate(() => document.querySelector('#ltext')?.textContent)); }
  const load = Date.now() - t0;
  const info = await p.evaluate(async ([simSec]) => {
    const g = FW; if (g.state !== 'play') return { state: g.state, ltext: document.querySelector('#ltext')?.textContent };
    const n0 = g.units.length; let ms = 0, steps = 0; const t0 = performance.now();
    for (let t = 0; t < simSec && performance.now() - t0 < 90000; t += 5) { ms += g.sim(5, 1 / 20); steps++; if (g.player && !g.player.alive && g.state === 'dead') { g.state = 'play'; g.spawnPlayer(); } }
    const alive = [0, 1].map(s => g.units.filter(u => u.alive && u.team === s).length);
    return { state: g.state, units0: n0, alive, tickets: g.tickets.map(t => t | 0), activeObj: g.activeObj, prog: +(g.objectives[g.activeObj]?.prog || 0).toFixed(2), simMs: Math.round(ms), simulated: steps * 5, kills: g.stats, over: g.over, rounds: g.rounds.length, nades: g.grenades.length, calls: g.renderer.info.render.calls };
  }, [simSec]);
  // render some frames for real (optionally turned round: yaw=radians)
  if (args.yaw) await p.evaluate(y => { if (FW.player) FW.player.yaw += y; }, +args.yaw);
  const fr = await p.evaluate(async () => { const t = performance.now(); let n = 0; await new Promise(r => { const f = () => { n++; if (n < (+window.__frames || 4)) requestAnimationFrame(f); else r(); }; requestAnimationFrame(f); }); return { frames: n, ms: Math.round(performance.now() - t), calls: FW.renderer.info.render.calls, tris: FW.renderer.info.render.triangles }; });
  if (shots) await p.screenshot({ path: `${shots}/m${mi}${args.tag || ''}.png`, timeout: 600000 });
  const realErrs = errs.filter(e => !/GPU stall|swiftshader|WebGL: INVALID_ENUM|Automatic fallback|GroupMarkerNotSet|AudioContext was not allowed/.test(e));
  if (realErrs.length) bad++;
  console.log(JSON.stringify({ mi, load, ...info, render: fr, errors: realErrs.slice(0, 12) }));
  await p.close();
}
await b.close();
process.exit(bad ? 1 : 0);
