// Two-player online check: page A hosts a battle, page B joins it through a stand-in for the MQTT library that links the
// pages with a BroadcastChannel (the public relays are not reachable from the test machine). Checks the lobby listing,
// joining, the mirrored battlefield, shots / hits / damage both ways, grenades, deaths and the host leaving.
// Needs docs/ served on 127.0.0.1:8765 (see smoke.mjs).  Usage: node webtest/online.mjs [mission=1]
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
const args = Object.fromEntries(process.argv.slice(2).map(a => a.split('=')));
const BASE = args.base || 'http://127.0.0.1:8765/', MI = +(args.mission ?? 1);
export const FAKE_MQTT = `window.__fakeMsgs = 0; window.mqtt = { connect(url, opts) {
  const ch = new BroadcastChannel('fakemqtt:' + url), h = {}, subs = []; let open = true;
  const emit = (e, ...a) => (h[e] || []).forEach(f => f(...a)), match = (s, t) => s.endsWith('#') ? t.startsWith(s.slice(0, -1)) : s === t;
  const deliver = (topic, payload) => { if (!open || !subs.some(s => match(s, topic))) return; emit('message', topic, { toString: () => payload }); };
  const c = { url, on(e, f) { (h[e] = h[e] || []).push(f); return c; },
    subscribe(t, o, cb) { (Array.isArray(t) ? t : [t]).forEach(x => subs.push(x)); setTimeout(() => cb && cb(null), 0); },
    publish(topic, payload) { if (!open) return; window.__fakeMsgs++; window.__fakeBytes = (window.__fakeBytes || 0) + String(payload).length; window.__fakeMax = Math.max(window.__fakeMax || 0, String(payload).length); ch.postMessage({ topic, payload: String(payload) }); setTimeout(() => deliver(topic, String(payload)), 2); },
    end() { open = false; try { ch.close(); } catch (e) {} } };
  ch.onmessage = e => deliver(e.data.topic, e.data.payload);
  setTimeout(() => emit('connect'), 30); return c; } };`;
const b = await chromium.launch({ executablePath: '/usr/bin/google-chrome', args: ['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows', '--autoplay-policy=no-user-gesture-required'] });
const ctx = await b.newContext({ viewport: { width: 400, height: 225 } });
await ctx.addInitScript(`localStorage.setItem('fw_settings', JSON.stringify({ quality: 0, size: 0.35, length: 'short' })); window.MQ_SERVERS = ['wss://relay-a.test/mqtt', 'wss://relay-b.test/mqtt'];`);
const errs = [];
async function open(tag) {
  const p = await ctx.newPage();
  p.on('console', m => { const t = m.text(); if (m.type() === 'error') errs.push(tag + ' ' + t.slice(0, 300)); });
  p.on('pageerror', e => errs.push(tag + ' PAGEERROR: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 4).join(' | ')));
  await p.route('**/mqtt.min.js', r => r.fulfill({ body: FAKE_MQTT, contentType: 'application/javascript' }));
  await p.goto(BASE); await p.waitForFunction(() => window.FW && FW.missions, null, { timeout: 60000 });
  return p;
}
const sleep = ms => new Promise(r => setTimeout(r, ms)), log = (k, v) => console.log(k.padEnd(22), JSON.stringify(v));
let fails = 0; const check = (name, ok, v) => { if (!ok) fails++; console.log((ok ? 'PASS ' : 'FAIL ') + name.padEnd(48), v === undefined ? '' : JSON.stringify(v)); };
const A = await open('A'), B = await open('B');
await sleep(800);
log('relay status', await Promise.all([A, B].map(p => p.evaluate(() => FW.online.status()))));
// A hosts Mokra as the Wehrmacht
await A.evaluate(mi => { FW.online.setName('Alice'); FW.mi = mi; FW.side = 0; FW.online.hostBattle(); FW.start(); }, MI);
await A.waitForFunction(() => FW.state === 'deploy', null, { timeout: 120000 }); await A.click('#go'); await A.waitForFunction(() => FW.state === 'play');
// B sees it in the lobby and joins as the Polish Army
await B.waitForFunction(() => FW.online.openGames().length > 0, null, { timeout: 20000 });
const listed = await B.evaluate(() => { FW.side = 1; FW.renderOnline(); return { games: FW.online.openGames().map(g => ({ mi: g.mi, host: g.host, n: g.n })), html: document.querySelector('#on-games').textContent.slice(0, 120) }; });
check('B sees the hosted battle in the lobby', listed.games.length === 1 && listed.games[0].host === 'Alice', listed);
await B.evaluate(() => { FW.online.setName('Bob'); document.querySelector('#on-name').value = 'Bob'; document.querySelector('#on-games .join').click(); });
await B.waitForFunction(() => FW.state === 'deploy', null, { timeout: 120000 });
await sleep(1500);
const mirror = await Promise.all([A.evaluate(() => FW.units.length), B.evaluate(() => ({ units: FW.units.length, net: FW.units.filter(u => u.net).length, mi: FW.mi, tickets: FW.tickets, ao: FW.activeObj }))]);
check('B mirrors the host battlefield (same mission, units)', mirror[1].mi === MI && Math.abs(mirror[1].units - (mirror[0] + 1)) <= 2 && mirror[1].net === mirror[1].units, mirror);
await B.click('#go'); await B.waitForFunction(() => FW.state === 'play');
await sleep(2500);
const seen = await A.evaluate(() => { const rp = [...FW.remotes.values()][0]; return rp ? { name: rp.name, team: rp.team, alive: rp.alive, x: rp.x | 0, z: rp.z | 0, inUnits: FW.units.includes(rp) } : null; });
const bpos = await B.evaluate(() => ({ x: FW.player.x | 0, z: FW.player.z | 0, team: FW.player.team }));
check('A has a stand-in soldier for Bob where Bob is', seen && seen.name === 'Bob' && seen.team === 1 && seen.alive && Math.hypot(seen.x - bpos.x, seen.z - bpos.z) < 4, { seen, bpos });
const hostOnB = await B.evaluate(() => { const u = FW.netById.get(65535); return u ? { name: u.name, human: u.human, team: u.team, x: u.x | 0 } : null; });
const apos = await A.evaluate(() => ({ x: FW.player.x | 0, z: FW.player.z | 0 }));
check('B draws Alice (the host) as a named soldier', hostOnB && hostOnB.name === 'Alice' && hostOnB.team === 0 && Math.abs(hostOnB.x - apos.x) < 4, { hostOnB, apos });
// B shoots: aim straight at the nearest enemy AI soldier on its screen and fire one round
const shot = await B.evaluate(() => {
  const p = FW.player; let best = null, bd = 1e9; for (const u of FW.units) if (u.alive && u.team !== p.team && u.kind === 'inf' && !u.human) { const d = Math.hypot(u.x - p.x, u.z - p.z); if (d < bd) { bd = d; best = u; } }
  if (!best) return null; // stand next to it so nothing is in the way, and fire
  p.x = best.x + 3; p.z = best.z; p.y = FW.world.H(p.x, p.z); FW.camera.position.set(p.x, p.y + p.eyeH(), p.z);
  const tx = best.x, ty = best.y + (best.stance === 2 ? .25 : best.stance === 1 ? .8 : 1.3), tz = best.z, dx = tx - p.x, dy = ty - (p.y + p.eyeH()), dz = tz - p.z, L = Math.hypot(dx, dy, dz);
  FW.fireRound({ x: p.x, y: p.y + p.eyeH(), z: p.z, vx: dx / L * 760, vy: dy / L * 760, vz: dz / L * 760, w: { mv: 760, dmg: 400 }, from: p, team: p.team, t: 0 }); FW.playerShot('rifle', p.x, p.y + p.eyeH(), p.z, dx / L, dy / L, dz / L, 0);
  FW.updateRounds(.05); FW.updateRounds(.05);
  return { id: best.id, dist: bd | 0, acts: FW.online.acts.map(a => a[1]) };
});
await sleep(2500);
const victim = shot && await A.evaluate(id => { const u = FW.unitByNetId(id); return u ? { alive: u.alive, hp: u.hp | 0 } : 'gone'; }, shot.id);
check('B\'s hit on an AI soldier is applied by the host', shot && shot.acts.includes('h') && victim && victim.alive === false, { shot, victim });
const onB = shot && await B.evaluate(id => { const u = FW.netById.get(id); return { alive: u ? u.alive : 'gone', kills: FW.stats.kills, feed: document.querySelector('#feed').textContent.slice(0, 80) }; }, shot.id);
check('B sees the kill and gets the credit', onB && onB.alive === false && onB.kills === 1, onB);
// the host's AI hurts Bob: an MG burst from the nearest German AI straight at Bob
const hp0 = await B.evaluate(() => FW.player.hp);
await A.evaluate(() => { const rp = [...FW.remotes.values()][0]; FW.hurtHuman(rp, 30, FW.units.find(u => u.alive && u.team === 0 && u.kind === 'inf'), 'chest'); });
await sleep(2000);
const hp1 = await B.evaluate(() => ({ hp: FW.player.hp | 0, bleed: FW.player.bleed, supp: +FW.player.supp.toFixed(2) }));
check('Damage from the host reaches Bob', hp1.hp < hp0 && hp1.bleed > 0, { hp0, hp1 });
// a blast near Bob on the host hurts him and he sees the explosion
const ex = await A.evaluate(() => { const rp = [...FW.remotes.values()][0]; FW.explode(rp.x + 6, FW.world.H(rp.x + 6, rp.z), rp.z, 1, null, 4); return 1; });
await sleep(2000);
const hp2 = await B.evaluate(() => ({ hp: FW.player.hp | 0, alive: FW.player.alive, craters: FW.fx.craters.length, state: FW.state }));
check('A shell burst near Bob: he sees it and takes damage', hp2.craters >= 1 && (hp2.hp < hp1.hp || !hp2.alive), hp2);
// Alice shoots Bob (versus): the host's round hits the stand-in, Bob's page applies it
if (hp2.alive) {
  await A.evaluate(() => { const p = FW.player, rp = [...FW.remotes.values()][0]; p.x = rp.x + 4; p.z = rp.z; p.y = FW.world.H(p.x, p.z); FW.camera.position.set(p.x, p.y + p.eyeH(), p.z); FW.rebuildGrid();
    const ty = rp.y + 1.3, dx = rp.x - p.x, dy = ty - (p.y + p.eyeH()), dz = rp.z - p.z, L = Math.hypot(dx, dy, dz);
    FW.fireRound({ x: p.x, y: p.y + p.eyeH(), z: p.z, vx: dx / L * 760, vy: dy / L * 760, vz: dz / L * 760, w: { mv: 760, dmg: 400 }, from: p, team: p.team, t: 0 }); FW.updateRounds(.05); FW.updateRounds(.05); });
  await sleep(2500);
}
const dead = await B.evaluate(() => ({ alive: FW.player.alive, state: FW.state, msg: FW.deathMsg, deaths: FW.online.deaths }));
check('Alice shoots Bob (versus): Bob dies on his own page', dead.alive === false && dead.deaths >= 1 && /Alice/.test(dead.msg), dead);
await sleep(2000);
const aSees = await A.evaluate(() => { const rp = [...FW.remotes.values()][0]; return { alive: rp?.alive, kills: FW.stats.kills, feed: document.querySelector('#feed').textContent.slice(0, 160) }; });
check('The host sees Bob fall', aSees.alive === false, aSees);
// grenade from B after redeploying
await B.evaluate(() => { FW.time += 2; FW.runTimers(); FW.redeployT = 0; }); // software rendering is slow: skip the 6 s redeploy wait
await B.waitForFunction(() => document.querySelector('#deploy').style.display === 'flex' && !document.querySelector('#go').disabled, null, { timeout: 30000 });
await B.click('#go'); await B.waitForFunction(() => FW.state === 'play');
await sleep(2000);
const life = await A.evaluate(() => { const rp = [...FW.remotes.values()][0]; return { life: rp?.life, alive: rp?.alive, units: FW.units.filter(u => u.isRemote).length }; });
check('Bob redeploys and the host makes him a new soldier', life.alive === true && life.life === 2, life);
const n0 = await A.evaluate(() => FW.grenades.length);
await B.evaluate(() => { const p = FW.player; FW.throwGrenadeFrom(p, p.x, p.y + 1.6, p.z, 8, 4, 0, 3); });
await sleep(1500);
const nade = await A.evaluate(() => FW.grenades.map(n => ({ visual: !!n.visual, from: n.from?.name })));
check('Bob\'s grenade is live on the host', nade.some(n => !n.visual && n.from === 'Bob'), { n0, nade });
// traffic
const traffic = await Promise.all([A, B].map(p => p.evaluate(() => ({ msgs: window.__fakeMsgs, kb: Math.round((window.__fakeBytes || 0) / 1024), maxMsg: window.__fakeMax, relayOut: Math.round(FW.online.relay.bytesOut / 1024) }))));
log('traffic A,B', traffic);
// the host quits: Bob is told and goes back to the menu
await A.evaluate(() => FW.leaveBattle());
await B.waitForFunction(() => FW.state === 'menu', null, { timeout: 20000 }).catch(() => { });
const after = await B.evaluate(() => ({ state: FW.state, notice: FW.online.notice, mode: FW.online.mode }));
check('Bob is told the host ended the battle', after.state === 'menu' && /host/i.test(after.notice), after);
console.log(errs.length ? 'ERRORS:\n' + errs.slice(0, 15).join('\n') : 'no page errors');
await b.close();
process.exit(fails || errs.length ? 1 : 0);
