// Deployment phase check: only placed troops fight, the computer's side is fully deployed, defenders you place hold that
// ground, Auto-deploy fills in the rest, and the planner remembers your placements next time.
// Needs docs/ served on 127.0.0.1:8765.  Usage: node webtest/deploy.mjs
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
const BASE = 'http://127.0.0.1:8765/';
const FAKE = `window.mqtt = { connect() { const h = {}; const c = { on(e, f) { (h[e] = h[e] || []).push(f); return c; }, subscribe(t, o, cb) { setTimeout(() => cb && cb(null), 0); }, publish() {}, end() {} }; return c; } };`;
const b = await chromium.launch({ executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const ctx = await b.newContext({ viewport: { width: 960, height: 540 } });
await ctx.addInitScript(`localStorage.setItem('fw_settings', JSON.stringify({ quality: 0, size: 0.5, length: 'short', plan: true }));`);
let fails = 0; const check = (name, ok, v) => { if (!ok) fails++; console.log((ok ? 'PASS ' : 'FAIL ') + name.padEnd(56), v === undefined ? '' : JSON.stringify(v)); };
const p = await ctx.newPage(); const errs = [];
p.on('pageerror', e => errs.push('PAGEERROR: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' | ')));
p.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
await p.route('**/mqtt.min.js', r => r.fulfill({ body: FAKE, contentType: 'application/javascript' }));
// ---- defend Mokra as the Polish Army: place two units only
await p.goto(BASE + '?m=1&side=1'); await p.waitForFunction(() => window.FW && FW.missions && document.querySelector('#start'));
await p.evaluate(() => document.querySelector('#start').click()); await p.waitForFunction(() => FW.state === 'plan', null, { timeout: 180000 });
const plan = await p.evaluate(() => {
  const P = FW.planner, mine = P.mine, B = FW.objectives[1], pts = [];
  const rifle = mine.find(m => m.type === 'rifle'), mg = mine.find(m => m.type === 'mg');
  for (const [m, dx] of [[rifle, 26], [mg, -22]]) { P.sel = m; let ok = false; for (let i = 0; i < 40 && !ok; i++) { const x = B.x + dx + (Math.random() - .5) * 10, z = B.z + 18 + (Math.random() - .5) * 10; P.down(x, z, 0); ok = m.placed; } pts.push(m.placed ? [Math.round(m.x), Math.round(m.z)] : null); }
  const outside = (() => { const m = mine.find(q => !q.placed); P.sel = m; const s = FW.M.spawn[FW.M.attacker]; P.down(s.x, s.z, 0); return m.placed; })();
  return { groups: mine.length, ai: P.groups.filter(g => g.team !== FW.side).length, aiPlaced: P.groups.filter(g => g.team !== FW.side && g.placed).length, placed: mine.filter(m => m.placed).map(m => ({ type: m.type, n: m.n })), pts, outside, msg: document.querySelector('#pmsg').textContent };
});
check('planner lists the Polish allotment; computer side placed', plan.groups > 5 && plan.ai > 3 && plan.ai === plan.aiPlaced, plan);
check('placing in the attackers\' area is refused', plan.outside === false && /outside/.test(plan.msg), plan.msg);
await p.evaluate(() => document.querySelector('#pstart').click()); await p.waitForFunction(() => FW.state === 'deploy', null, { timeout: 60000 });
const spawned = await p.evaluate(() => { const pol = FW.units.filter(u => u.team === FW.side), ger = FW.units.filter(u => u.team !== FW.side);
  return { pol: pol.length, polTypes: [...new Set(pol.map(u => u.type))], ger: ger.length, left: document.querySelector('#deploy').textContent.match(/(\d+) units? left out/)?.[1] | 0,
    holds: FW.squads[FW.side].filter(s => s.hold).map(s => ({ order: s.order?.kind, d: Math.round(Math.hypot(s.order.x - s.hold[0], s.order.z - s.hold[1])), home: s.home })) }; });
const want = plan.placed.reduce((a, g) => a + g.n, 0);
check('only the placed Polish troops are on the field', spawned.pol === want, { want, ...spawned });
check('the German side is fully deployed', spawned.ger > 20, spawned.ger);
check('the deploy card says how many were left out', spawned.left === plan.groups - plan.placed.length, spawned.left);
check('placed defenders are ordered to hold their ground', spawned.holds.length === 2 && spawned.holds.every(h => h.order === 'defend' && h.d < 2 && h.home === 1), spawned.holds);
await p.evaluate(() => document.querySelector('#go').click()); await p.waitForFunction(() => FW.state === 'play');
const stay = await p.evaluate(() => { FW.god = true; FW.sim(40, 1 / 20); const out = []; for (const s of FW.squads[FW.side]) if (s.hold) for (const m of s.members) if (m.alive) out.push(Math.round(Math.hypot(m.x - s.hold[0], m.z - s.hold[1]))); return { dists: out, activeObj: FW.activeObj }; });
check('after 40 s they are still at the ground they were put on', stay.dists.length > 0 && Math.max(...stay.dists) < 22, stay);
// ---- attack Mokra as the Wehrmacht: Auto-deploy, and the planner remembers placements
await p.evaluate(() => FW.leaveBattle()); await p.waitForFunction(() => FW.state === 'menu');
await p.evaluate(() => { FW.side = 0; FW.menu(); document.querySelector('#start').click(); }); await p.waitForFunction(() => FW.state === 'plan', null, { timeout: 180000 });
const att = await p.evaluate(() => { const P = FW.planner; const t = P.mine.find(m => m.type === 'tank'); P.sel = t; const s = FW.M.spawn[0]; for (let i = 0; i < 30 && !t.placed; i++) P.down(s.x + (Math.random() - .5) * 60, s.z + (Math.random() - .5) * 60, 0);
  document.querySelector('#pauto').click(); return { all: P.mine.every(m => m.placed), tankKept: t.hold, tx: Math.round(t.x), tz: Math.round(t.z) }; });
check('Auto-deploy places the rest, keeps the tank where it was put', att.all && att.tankKept, att);
await p.evaluate(() => document.querySelector('#pstart').click()); await p.waitForFunction(() => FW.state === 'deploy', null, { timeout: 60000 });
const ger = await p.evaluate(() => ({ tanks: FW.units.filter(u => u.team === 0 && u.kind === 'tank').length, inf: FW.units.filter(u => u.team === 0 && u.kind === 'inf').length }));
check('attackers deploy with their tanks', ger.tanks >= 3 && ger.inf > 10, ger);
await p.evaluate(() => FW.leaveBattle()); await p.waitForFunction(() => FW.state === 'menu');
await p.evaluate(() => document.querySelector('#start').click()); await p.waitForFunction(() => FW.state === 'plan', null, { timeout: 180000 });
const memo = await p.evaluate(([tx, tz]) => { const t = FW.planner.mine.find(m => m.type === 'tank'); return { placed: FW.planner.mine.filter(m => m.placed).length, of: FW.planner.mine.length, same: Math.round(t.x) === tx && Math.round(t.z) === tz }; }, [att.tx, att.tz]);
check('the planner remembers the last placements for this battle', memo.placed === memo.of && memo.same, memo);
console.log(errs.length ? 'ERRORS:\n' + errs.slice(0, 10).join('\n') : 'no page errors');
await b.close(); process.exit(fails || errs.length ? 1 : 0);
