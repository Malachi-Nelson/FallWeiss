// Phone check: emulates a phone held sideways (touch, small screen, high pixel density), screenshots the menu, the
// deployment planner and the battle HUD, and drives the touch controls (move stick, look drag, buttons).
// Needs docs/ served on 127.0.0.1:8765.  Usage: node webtest/phone.mjs [device=pixel|se] [mission=1] [shots=/tmp/shots]
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
const args = Object.fromEntries(process.argv.slice(2).map(a => a.split('=')));
const BASE = args.base || 'http://127.0.0.1:8765/', MI = +(args.mission ?? 1), shots = args.shots || '/tmp/shots';
const DEV = { pixel: { viewport: { width: 915, height: 412 }, deviceScaleFactor: 2.625 }, se: { viewport: { width: 667, height: 375 }, deviceScaleFactor: 2 } }[args.device || 'pixel'];
const FAKE = `window.mqtt = { connect() { const h = {}; const c = { on(e, f) { (h[e] = h[e] || []).push(f); return c; }, subscribe(t, o, cb) { setTimeout(() => cb && cb(null), 0); }, publish() {}, end() {} }; setTimeout(() => (h.connect || []).forEach(f => f()), 20); return c; } };`;
const b = await chromium.launch({ executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const ctx = await b.newContext({ ...DEV, isMobile: true, hasTouch: true, userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36' });
await ctx.addInitScript(`localStorage.setItem('fw_settings', JSON.stringify(Object.assign({ size: 0.35, length: 'short' }, ${args.settings || '{}'})));`);
const p = await ctx.newPage(); const errs = [];
p.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 300)); });
p.on('pageerror', e => errs.push('PAGEERROR: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' | ')));
await p.route('**/mqtt.min.js', r => r.fulfill({ body: FAKE, contentType: 'application/javascript' }));
await p.goto(BASE + `?m=${MI}`); await p.waitForFunction(() => window.FW && FW.missions && document.querySelector('#missions button'), null, { timeout: 60000 });
const tag = args.device || 'pixel'; const shot = async n => p.screenshot({ path: `${shots}/${tag}_${n}.png`, timeout: 600000 });
await shot('menu');
const menuFit = await p.evaluate(() => { const m = document.querySelector('#menu'); return { scrollH: m.scrollHeight, clientH: m.clientHeight, start: document.querySelector('#start').getBoundingClientRect().top | 0 }; });
console.log('menu', JSON.stringify(menuFit));
await p.tap('#start', { force: true, timeout: 120000 });
await p.waitForFunction(() => FW.state === 'plan' || FW.state === 'deploy', null, { timeout: 180000 });
const st = await p.evaluate(() => FW.state); console.log('after loading:', st);
if (st === 'plan') {
  await shot('plan');
  const info = await p.evaluate(() => ({ groups: FW.planner.groups.filter(g => g.team === FW.side).length, placed: FW.planner.groups.filter(g => g.team === FW.side && g.placed).length }));
  console.log('planner', JSON.stringify(info));
  // tap the first unplaced unit, then tap the middle of the zone on the map
  await p.evaluate(() => FW.planner.testPlaceFirst());
  await shot('plan2'); await p.tap('#pstart', { force: true, timeout: 120000 }); await p.waitForFunction(() => FW.state === 'deploy', null, { timeout: 60000 });
}
await p.tap('#go', { force: true, timeout: 120000 }); await p.waitForFunction(() => FW.state === 'play', null, { timeout: 60000 });
await p.evaluate(() => new Promise(r => setTimeout(r, 1500)));
await shot('play');
// touch: drag the move stick forward for a second, drag to look, tap reload / crouch / fire
const before = await p.evaluate(() => ({ x: FW.player.x, z: FW.player.z, yaw: FW.player.yaw }));
const touch = async (type, id, x, y) => p.evaluate(([type, id, x, y]) => {
  const el = document.elementFromPoint(x, y) || document.body; const t = new Touch({ identifier: id, target: el, clientX: x, clientY: y });
  (type === 'touchstart' ? el : window.__tgt?.[id] || el).dispatchEvent(new TouchEvent(type, { touches: type === 'touchend' ? [] : [t], changedTouches: [t], bubbles: true, cancelable: true }));
  (window.__tgt = window.__tgt || {})[id] = type === 'touchstart' ? el : window.__tgt[id]; }, [type, id, x, y]);
const W = DEV.viewport.width, H = DEV.viewport.height;
await touch('touchstart', 1, W * .15, H * .7); for (let i = 1; i <= 6; i++) await touch('touchmove', 1, W * .15, H * .7 - i * 10);
await p.evaluate(() => new Promise(r => setTimeout(r, 1500)));
const mid = await p.evaluate(() => ({ x: FW.player.x, z: FW.player.z, mx: window.__I?.mx, my: window.__I?.my }));
await touch('touchend', 1, W * .15, H * .7 - 60);
await touch('touchstart', 2, W * .6, H * .4); for (let i = 1; i <= 5; i++) await touch('touchmove', 2, W * .6 + i * 16, H * .4); await touch('touchend', 2, W * .6 + 80, H * .4);
await p.evaluate(() => new Promise(r => setTimeout(r, 600)));
const after = await p.evaluate(() => ({ x: FW.player.x, z: FW.player.z, yaw: FW.player.yaw, stance: FW.player.stance }));
console.log('moved', Math.hypot(after.x - before.x, after.z - before.z).toFixed(2), 'm; turned', (after.yaw - before.yaw).toFixed(3), 'rad');
// buttons must not overlap each other or the HUD read-outs
const overlap = await p.evaluate(() => {
  const els = [...document.querySelectorAll('#touch .tb')].filter(e => e.offsetParent !== null).map(e => ({ id: e.id, r: e.getBoundingClientRect() }));
  const hud = ['#ammo', '#status', '#mini', '#feed', '#objs', '#clock'].map(s => ({ id: s, r: document.querySelector(s).getBoundingClientRect() })).filter(h => h.r.width > 0);
  const hit = (a, b) => a.left < b.right - 2 && b.left < a.right - 2 && a.top < b.bottom - 2 && b.top < a.bottom - 2; const out = [];
  for (let i = 0; i < els.length; i++) { for (let j = i + 1; j < els.length; j++) if (hit(els[i].r, els[j].r)) out.push(els[i].id + '×' + els[j].id); for (const h of hud) if (hit(els[i].r, h.r)) out.push(els[i].id + '×' + h.id); }
  const off = els.filter(e => e.r.left < 0 || e.r.top < 0 || e.r.right > innerWidth || e.r.bottom > innerHeight).map(e => e.id);
  return { overlaps: out, offscreen: off, buttons: els.map(e => e.id + ':' + Math.round(e.r.width)) };
});
console.log('layout', JSON.stringify(overlap));
await shot('moved');
console.log(errs.length ? 'ERRORS:\n' + errs.slice(0, 10).join('\n') : 'no page errors');
await b.close();
