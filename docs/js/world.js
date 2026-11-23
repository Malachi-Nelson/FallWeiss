// Fall Weiss web — the battlefield. Terrain heights are a straight port of World.cs BaseHeight/TrenchCut (same sin-hash noise,
// hills, rim, river, sea, rail embankments, brooks), evaluated in Unity coordinates; the game itself runs in three.js coordinates
// where z = -z(Unity). Everything is built from data/missions.json.
import * as THREE from './three.module.min.js';
import { spawnModel, hasModel } from './models.js';
import { TEX } from './textures.js';
import { Rng, clamp, sm, lerp, segD } from './defs.js';
import { FOGU, CLOUD_GLSL } from './look.js';

// ---------------- geometry builder ----------------
export class GB {
  constructor() { this.p = []; this.n = []; this.u = []; this.c = []; this.i = []; }
  get vc() { return this.p.length / 3; }
  quad(a, b, c, d, n, uvs, col) { // a,b,c,d counter-clockwise seen from the front
    const o = this.vc; for (const v of [a, b, c, d]) this.p.push(v[0], v[1], v[2]); for (let k = 0; k < 4; k++) { this.n.push(n[0], n[1], n[2]); this.c.push(col[0], col[1], col[2]); }
    for (const t of uvs) this.u.push(t[0], t[1]); this.i.push(o, o + 1, o + 2, o, o + 2, o + 3);
  }
  tri(a, b, c, uvs, col) { const o = this.vc; const e1 = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], e2 = [c[0] - a[0], c[1] - a[1], c[2] - a[2]]; const n = norm([e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]]);
    for (const v of [a, b, c]) { this.p.push(v[0], v[1], v[2]); this.n.push(n[0], n[1], n[2]); this.c.push(col[0], col[1], col[2]); } for (const t of uvs) this.u.push(t[0], t[1]); this.i.push(o, o + 1, o + 2); }
  /** axis-aligned (optionally y-rotated) box; uv in metres / tile size */
  box(cx, y0, cz, w, h, d, col = [1, 1, 1], tu = 4, tv = 4, rot = 0, top = true, bottom = false) {
    const hw = w / 2, hd = d / 2, cs = Math.cos(rot), sn = Math.sin(rot); const P = (x, y, z) => [cx + x * cs + z * sn, y, cz - x * sn + z * cs]; const N = (x, y, z) => [x * cs + z * sn, y, -x * sn + z * cs];
    const y1 = y0 + h;
    this.quad(P(-hw, y0, hd), P(hw, y0, hd), P(hw, y1, hd), P(-hw, y1, hd), N(0, 0, 1), [[0, 0], [w / tu, 0], [w / tu, h / tv], [0, h / tv]], col);
    this.quad(P(hw, y0, -hd), P(-hw, y0, -hd), P(-hw, y1, -hd), P(hw, y1, -hd), N(0, 0, -1), [[0, 0], [w / tu, 0], [w / tu, h / tv], [0, h / tv]], col);
    this.quad(P(hw, y0, hd), P(hw, y0, -hd), P(hw, y1, -hd), P(hw, y1, hd), N(1, 0, 0), [[0, 0], [d / tu, 0], [d / tu, h / tv], [0, h / tv]], col);
    this.quad(P(-hw, y0, -hd), P(-hw, y0, hd), P(-hw, y1, hd), P(-hw, y1, -hd), N(-1, 0, 0), [[0, 0], [d / tu, 0], [d / tu, h / tv], [0, h / tv]], col);
    if (top) this.quad(P(-hw, y1, hd), P(hw, y1, hd), P(hw, y1, -hd), P(-hw, y1, -hd), N(0, 1, 0), [[0, 0], [w / tu, 0], [w / tu, d / tu], [0, d / tu]], col);
    if (bottom) this.quad(P(-hw, y0, -hd), P(hw, y0, -hd), P(hw, y0, hd), P(-hw, y0, hd), N(0, -1, 0), [[0, 0], [w / tu, 0], [w / tu, d / tu], [0, d / tu]], col);
  }
  cyl(cx, y0, cz, r0, r1, h, seg, col, tu = 2, tv = 2) {
    for (let s = 0; s < seg; s++) { const a0 = s / seg * Math.PI * 2, a1 = (s + 1) / seg * Math.PI * 2; const c0 = Math.cos(a0), s0 = Math.sin(a0), c1 = Math.cos(a1), s1 = Math.sin(a1);
      const o = this.vc; this.p.push(cx + c0 * r0, y0, cz + s0 * r0, cx + c1 * r0, y0, cz + s1 * r0, cx + c1 * r1, y0 + h, cz + s1 * r1, cx + c0 * r1, y0 + h, cz + s0 * r1);
      this.n.push(c0, 0, s0, c1, 0, s1, c1, 0, s1, c0, 0, s0); for (let k = 0; k < 4; k++) this.c.push(col[0], col[1], col[2]);
      const u0 = s / seg * 2 * Math.PI * r0 / tu, u1 = (s + 1) / seg * 2 * Math.PI * r0 / tu; this.u.push(u0, 0, u1, 0, u1, h / tv, u0, h / tv); this.i.push(o, o + 2, o + 1, o, o + 3, o + 2); }
  }
  build() { const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(this.p, 3)); g.setAttribute('normal', new THREE.Float32BufferAttribute(this.n, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(this.u, 2)); g.setAttribute('color', new THREE.Float32BufferAttribute(this.c, 3)); g.setIndex(this.i); g.computeBoundingSphere(); return g; }
}
function norm(v) { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; }
const C3 = h => { const c = new THREE.Color(h); return [c.r, c.g, c.b]; };

// ---------------- Unity-space terrain shape (World.cs port) ----------------
function makeShape(MU) {
  const NSEED = MU.seed;
  const Hash2 = (x, z) => { const nn = Math.sin(x * 127.1 + z * 311.7 + NSEED * 74.7) * 43758.5453; return nn - Math.floor(nn); };
  const VNoise = (x, z) => { const xi = Math.floor(x), zi = Math.floor(z), xf = x - xi, zf = z - zi, u = xf * xf * (3 - 2 * xf), w = zf * zf * (3 - 2 * zf); const a = Hash2(xi, zi), b = Hash2(xi + 1, zi), c = Hash2(xi, zi + 1), d = Hash2(xi + 1, zi + 1); return a + (b - a) * u + (c - a) * w + (a - b - c + d) * u * w; };
  const Fbm = (x, z, o = 4) => { let s = 0, a = .5, f = 1; for (let i = 0; i < o; i++) { s += VNoise(x * f, z * f) * a; f *= 2; a *= .5; } return s; };
  const RiverC = x => MU.rz0 + MU.ramp * Math.sin(x * MU.rk);
  const SeaEdge = (x, z) => Math.min(MU.seaHx - Math.abs(x), MU.seaHz - Math.abs(z)) + (Fbm(x * .03 + 4, z * .03, 3) - .5) * 14;
  const trSegs = []; for (const t of MU.trenches || []) for (let i = 0; i + 3 < t.pts.length; i += 2) trSegs.push([t.pts[i], t.pts[i + 1], t.pts[i + 2], t.pts[i + 3]]);
  const TrenchD = (x, z) => { let d = 1e9; for (const s of trSegs) { if (Math.abs(x - s[0]) > 60 && Math.abs(x - s[2]) > 60 && Math.sign(x - s[0]) === Math.sign(x - s[2])) continue; d = Math.min(d, segD(x, z, s[0], s[1], s[2], s[3])); } return d; };
  const TrenchCut = (x, z) => { if (!trSegs.length) return 0; const dm = TrenchD(x, z); if (dm >= 4) return 0; return -1.8 * (1 - sm(.9, 1.9, dm)) + .5 * sm(1.9, 2.6, dm) * (1 - sm(2.6, 3.8, dm)); };
  let brooks = [], carving = false;
  function Base(x, z) {
    const A = MU.relief; let h = (Fbm(x * .007, z * .007, 4) - .47) * 2.3 * A + (VNoise(x * .05, z * .05) - .5) * A * .14;
    for (const hl of MU.hills || []) { const d = Math.hypot(x - hl.x, z - hl.z) / hl.r; if (d < 2.6) h += hl.h * Math.exp(-d * d * 1.3); }
    if (MU.hasSea) { h += 3.2; h = lerp(h, -5, 1 - sm(-3, 9, SeaEdge(x, z))); }
    else { const e = Math.max(Math.abs(x), Math.abs(z)) - MU.half; if (e > -25) { let rim = 1; for (const r of MU.roads) rim = Math.min(rim, .25 + .75 * sm(12, 40, segD(x, z, r.x1, r.z1, r.x2, r.z2))); h += sm(-25, 150, e) * (35 + Fbm(x * .015 + 9, z * .015, 3) * 110) * rim; } }
    if (MU.hasRiver) { h += MU.rlevel + 4.5; const d = Math.abs(z - RiverC(x)); h = lerp(h, MU.rlevel + 1, 1 - sm(MU.rw, MU.rw * 3.4, d)); h = lerp(h, MU.rlevel - 1.8, 1 - sm(MU.rw * .45, MU.rw, d)); }
    if (MU.hasRiver) { const d = Math.abs(z - RiverC(x)); if (d > MU.rw * 3.6) h = Math.max(h, MU.rlevel + .8 + (h - MU.rlevel - .8) * .25 * sm(0, 1, h - MU.rlevel)); }
    if (MU.hasSea && SeaEdge(x, z) > 12) h = Math.max(h, .9 + Math.max(0, h - .9));
    for (const r of MU.rails) { const d = segD(x, z, r.x1, r.z1, r.x2, r.z2); if (d > 6.5) continue; let cross = 1; for (const rd of MU.roads) cross = Math.min(cross, sm(5, 16, segD(x, z, rd.x1, rd.z1, rd.x2, rd.z2))); h += 1.3 * (1 - sm(2.5, 6.5, d)) * (MU.rails.length > 1 ? .4 : 1) * cross; }
    if (carving) for (const b of brooks) {
      let best = 1e9, bi = 0, bt = 0; for (let i = 0; i < b.p.length - 1; i++) { const a = b.p[i], c = b.p[i + 1], abx = c[0] - a[0], abz = c[1] - a[1], L2 = Math.max(1e-4, abx * abx + abz * abz), t = clamp(((x - a[0]) * abx + (z - a[1]) * abz) / L2, 0, 1), d = Math.hypot(x - a[0] - abx * t, z - a[1] - abz * t); if (d < best) { best = d; bi = i; bt = t; } }
      if (best > b.w + 30) continue; const lv = lerp(b.lv[bi], b.lv[bi + 1], bt), hw = b.w / 2; let ford = 1; for (const rd of MU.roads) ford = Math.min(ford, sm(4, 14, segD(x, z, rd.x1, rd.z1, rd.x2, rd.z2)));
      const bank = (3 + Math.max(0, h - lv) * 1.2) * lerp(4, 1, ford); if (best < hw) h = Math.min(h, lv - b.depth * (1 - (best / hw) * (best / hw) * .6)); else if (best < hw + bank) h = Math.min(h, lerp(lv + .05, h, (best - hw) / bank));
    }
    return h;
  }
  for (const s of MU.streams || []) { // brook levels never climb (World.cs PlaceWater)
    const P = []; for (let i = 0; i + 3 < s.pts.length; i += 2) { const ax = s.pts[i], az = s.pts[i + 1], bx = s.pts[i + 2], bz = s.pts[i + 3], L = Math.hypot(bx - ax, bz - az), n = Math.max(1, Math.ceil(L / 4)); for (let k = 0; k < n; k++) P.push([ax + (bx - ax) * k / n, az + (bz - az) * k / n]); }
    P.push([s.pts[s.pts.length - 2], s.pts[s.pts.length - 1]]); const g = P.map(p => Base(p[0], p[1])); const lv = new Array(P.length); let cur = g[0];
    for (let i = 0; i < P.length; i++) { cur = Math.min(cur, g[i] - .3); lv[i] = Math.max(cur, g[i] - 1.8); cur = Math.max(cur, lv[i] - .01); }
    for (let it = 0; it < 3; it++) for (let i = 1; i < lv.length - 1; i++) lv[i] = Math.min(lv[i], (lv[i - 1] + lv[i] + lv[i + 1]) / 3 + .02);
    brooks.push({ p: P, lv, w: s.w > 0 ? s.w : 3, depth: s.depth > 0 ? s.depth : .45 });
  }
  carving = true;
  return { H: (x, z) => Base(x, z) + TrenchCut(x, z), TrenchD, RiverC, SeaEdge, brooks, Fbm };
}

/** Flip a Unity-space mission into game space (z → -z). */
export function flipMission(MU) {
  const M = JSON.parse(JSON.stringify(MU)); const fz = o => { if (o.z !== undefined) o.z = -o.z; if (o.z1 !== undefined) { o.z1 = -o.z1; o.z2 = -o.z2; } if (o.rot) o.rot = -o.rot; };
  for (const k of ['roads', 'lanes', 'rails', 'forests', 'villages', 'orchards', 'hills', 'build', 'landmarks', 'segs', 'props', 'points', 'spawn']) (M[k] || []).forEach(fz);
  for (const t of M.trenches || []) for (let i = 1; i < t.pts.length; i += 2) t.pts[i] = -t.pts[i];
  for (const s of M.streams || []) for (let i = 1; i < s.pts.length; i += 2) s.pts[i] = -s.pts[i];
  M.rz0 = -M.rz0; M.ramp = -M.ramp; M.shipZ = -M.shipZ; if (M.deploy0) M.deploy0 = null; return M;
}

// ---------------- materials ----------------
let detailUniforms = null;
/** Terrain: five photo layers blended per vertex (grass, stubble, dirt, gravel, cobble), two scales each against tiling, plus a
 *  world-space normal map. */
function terrainMaterial(q) {
  const m = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .95, metalness: 0 });
  m.onBeforeCompile = sh => {
    for (const k of ['grass', 'stubble', 'dirt', 'gravel', 'cobble']) sh.uniforms['t_' + k] = { value: TEX[k].map };
    sh.uniforms.n_grass = { value: TEX.grass.normal }; sh.uniforms.n_cobble = { value: TEX.cobble.normal }; sh.uniforms.n_dirt = { value: TEX.dirt.normal };
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nattribute vec4 splat; varying vec4 vSplat; varying vec2 vW;')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvSplat = splat; vW = (modelMatrix * vec4(position,1.0)).xz;');
    sh.fragmentShader = sh.fragmentShader.replace('#include <common>', `#include <common>
uniform sampler2D t_grass, t_stubble, t_dirt, t_gravel, t_cobble, n_grass, n_cobble, n_dirt; varying vec4 vSplat; varying vec2 vW;
vec3 L2(sampler2D t, float s){ vec3 a = texture2D(t, vW / s).rgb; vec3 b = texture2D(t, vW / (s*4.37) + 0.37).rgb; return mix(a, b, 0.4); }`)
      .replace('#include <map_fragment>', `
vec4 w = clamp(vSplat, 0.0, 1.0); float wg = clamp(1.0 - w.x - w.y - w.z - w.w, 0.0, 1.0);
vec3 tc = L2(t_grass, 3.2) * wg + L2(t_stubble, 4.0) * w.w + L2(t_dirt, 3.0) * w.x + L2(t_gravel, 2.6) * w.y + L2(t_cobble, 3.5) * w.z;
diffuseColor.rgb *= tc * 1.9;
#if defined(USE_FOG) && defined(FW_PATCHED)
{ float mv = texture2D(fwNoise, vW / 760.0 + 0.21).g, mv2 = texture2D(fwNoise, vW / 2300.0).r; diffuseColor.rgb *= mix(vec3(1.07, 1.01, 0.84), vec3(0.9, 1.0, 1.05), mv) * (0.9 + mv2 * 0.2); }
#endif`)
      .replace('#include <normal_fragment_maps>', `
{ vec3 nm = (texture2D(n_grass, vW/3.2).xyz*2.0-1.0) * (wg + w.w) + (texture2D(n_cobble, vW/3.5).xyz*2.0-1.0) * w.z + (texture2D(n_dirt, vW/3.0).xyz*2.0-1.0) * (w.x + w.y);
  vec3 nw = normalize((vec4(normal,0.0) * viewMatrix).xyz); nw = normalize(nw + vec3(nm.x, 0.0, -nm.y) * 0.55);
  normal = normalize((viewMatrix * vec4(nw,0.0)).xyz); }
#include <normal_fragment_maps>`);
  };
  return m;
}
export function texMat(name, opts = {}) {
  const t = TEX[name]; const m = new THREE.MeshStandardMaterial({ map: t.map, normalMap: opts.noNormal ? null : t.normal, vertexColors: !!opts.vc, roughness: opts.rough ?? .9, metalness: opts.metal ?? 0, side: opts.side ?? THREE.FrontSide, transparent: !!opts.transparent, alphaTest: opts.alphaTest ?? 0 });
  if (m.normalMap) m.normalScale.set(opts.ns ?? 1, opts.ns ?? 1); return m;
}
const NORMAL_NOFLIP = THREE.ShaderChunk.normal_fragment_begin.replace('normal *= faceDirection;', '');
/** Leaf / needle cards: alpha-cut clumps that sway in the wind, keep their rounded crown normals on both sides, and glow when
 *  the sun is behind them (light through leaves). */
export function foliageMat(texName, q) {
  const m = new THREE.MeshStandardMaterial({ map: TEX[texName].map, alphaTest: .4, side: THREE.DoubleSide, vertexColors: true, roughness: .8, metalness: 0 });
  if (q >= 2) m.alphaToCoverage = true;
  m.onBeforeCompile = sh => {
    sh.uniforms.fwTime = FOGU.fwTime;
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nuniform float fwTime;').replace('#include <begin_vertex>', `#include <begin_vertex>
 { vec3 ip = vec3(0.0);
 #ifdef USE_INSTANCING
   ip = vec3(instanceMatrix[3][0], instanceMatrix[3][1], instanceMatrix[3][2]);
 #endif
   float hgt = max(position.y - 2.0, 0.0), sw = sin(fwTime * 1.1 + ip.x * 0.07 + ip.z * 0.05) * 0.6 + sin(fwTime * 2.3 + ip.x * 0.13 + ip.z * 0.11) * 0.25;
   transformed.x += sw * hgt * 0.012; transformed.z += sw * hgt * 0.008;
   transformed += normal * sin(fwTime * 5.0 + position.x * 2.7 + position.z * 1.9 + ip.x) * 0.03; }`);
    sh.fragmentShader = sh.fragmentShader.replace('#include <normal_fragment_begin>', NORMAL_NOFLIP).replace('#include <fog_fragment>', `#if defined(USE_FOG) && defined(FW_PATCHED)
 { vec3 Vw = normalize(vFogW - cameraPosition); float tr = pow(max(dot(Vw, normalize(fwSunDir)), 0.0), 5.0); gl_FragColor.rgb += diffuseColor.rgb * fwSunCol * tr * 0.6 * fwCS; }
#endif
#include <fog_fragment>`);
  };
  m.customProgramCacheKey = () => 'fwleaf1'; return m;
}
/** Shared material for the original vertex-coloured models, with a triplanar cloth / painted-steel detail texture (texture pass). */
export function modelMaterial(kind = 'cloth') {
  const m = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: kind === 'steel' ? .62 : .88, metalness: kind === 'steel' ? .25 : 0 });
  m.onBeforeCompile = sh => {
    sh.uniforms.t_detail = { value: TEX.cloth.map }; sh.uniforms.d_scale = { value: kind === 'steel' ? 1.3 : 3.5 };
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nvarying vec3 vOP; varying vec3 vON;').replace('#include <begin_vertex>', '#include <begin_vertex>\nvOP = position; vON = normal;');
    sh.fragmentShader = sh.fragmentShader.replace('#include <common>', '#include <common>\nuniform sampler2D t_detail; uniform float d_scale; varying vec3 vOP; varying vec3 vON;')
      .replace('#include <map_fragment>', `vec3 bw = abs(vON); bw /= (bw.x+bw.y+bw.z+1e-4);
float dt = texture2D(t_detail, vOP.yz*d_scale).r*bw.x + texture2D(t_detail, vOP.xz*d_scale).r*bw.y + texture2D(t_detail, vOP.xy*d_scale).r*bw.z;
diffuseColor.rgb *= 0.55 + dt*0.75;`);
  };
  return m;
}

// ---------------- the World ----------------
export class World {
  constructor(MU, quality, scene) {
    this.MU = MU; this.M = flipMission(MU); this.q = quality; this.scene = scene; this.group = new THREE.Group(); scene.add(this.group);
    const M = this.M; this.half = M.half; this.city = M.city;
    this.S = makeShape(MU);
    this.WL = M.hasRiver ? M.rlevel : M.hasSea ? 0 : null;
    this.obst = []; this.trees = []; this.tgrid = new Map(); this.ogrid = new Map(); this.zones = []; this.covers = [];
    this.bridges = (M.bridges || []).map(bx => ({ x: bx, c: -this.S.RiverC(bx), y: M.rlevel + 1.0, len: M.rw * 3.1 }));
    const R = new Rng(M.seed);
    this.planBuildings(R); this.planTrees(R);
    this.bakeHeights();
    for (const b of this.obst) { b.y0 = this.Hraw((b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2) - .3; b.y1 = b.y0 + .3 + b.h; }
    this.buildTerrain(); this.buildWater(); this.buildBuildingMeshes(); this.buildTrees(); this.buildSegs(); this.buildProps(R); this.buildRails(); this.buildTrenchDressing(); this.buildSky();
    for (const b of this.obst) this.gridAdd(this.ogrid, b, 20);
  }
  // ---- coordinate helpers (game ↔ unity)
  HU(x, z) { return this.S.H(x, -z); }
  inRiver(x, z, m = 0) { const M = this.M; if (M.hasRiver) return Math.abs(-z - this.S.RiverC(x)) < M.rw * 3.4 + m; if (M.hasSea) return this.S.SeaEdge(x, -z) < 5 + m; return false; }
  nearRoad(x, z, m) { for (const r of this.M.roads) if (segD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + m) return true; for (const r of this.M.lanes) if (segD(x, z, r.x1, r.z1, r.x2, r.z2) < r.w / 2 + m) return true; return false; }
  nearRail(x, z, m) { for (const r of this.M.rails) if (segD(x, z, r.x1, r.z1, r.x2, r.z2) < m) return true; return false; }
  clearOfKey(x, z, r) { for (const zn of this.zones) if (Math.hypot(x - zn.x, z - zn.z) < zn.r + r * 0 + (zn.b || 0) * 0 + r) return false; return true; }
  inBuilding(x, z, m) { for (const b of this.obst) if (x > b.x0 - m && x < b.x1 + m && z > b.z0 - m && z < b.z1 + m) return true; return false; }
  addZone(x, z, r) { this.zones.push({ x, z, r }); }
  addObst(x0, x1, z0, z1, h, kind) { const o = { x0, x1, z0, z1, h, kind, hp: 1 }; this.obst.push(o); return o; }
  gridAdd(grid, o, cell) { const x0 = Math.floor((o.x0 ?? o.x - 1) / cell), x1 = Math.floor((o.x1 ?? o.x + 1) / cell), z0 = Math.floor((o.z0 ?? o.z - 1) / cell), z1 = Math.floor((o.z1 ?? o.z + 1) / cell);
    for (let i = x0; i <= x1; i++) for (let j = z0; j <= z1; j++) { const k = i * 100003 + j; let l = grid.get(k); if (!l) grid.set(k, l = []); l.push(o); } }
  gridGet(grid, x, z, cell) { return grid.get(Math.floor(x / cell) * 100003 + Math.floor(z / cell)); }

  planBuildings(R) {
    const M = this.M; this.specs = [];
    const KIND = { house: [8, 7, 3.4, 1], barn: [14, 9, 4.6, 1], shed: [5, 4, 2.6, 1], barracks: [26, 8, 4.2, 1], fortbarracks: [40, 12, 7, 0], villa: [11, 9, 6.2, 1], warehouse: [24, 13, 7, 0], guardhouse: [7, 7, 3.8, 1], bunker: [9, 6, 2.6, 0], casemate: [12, 8, 3, 0],
      city: [12, 16, 8, 0], tenement: [14, 22, 9, 0], factory: [40, 24, 10, 0], admin: [40, 14, 12, 0], stack: [5, 5, 38, 0], tank: [10, 10, 7, 0], boxcar: [12, 3.4, 3.6, 0], ruin: [12, 12, 4, 0], gate: [10, 6, 6, 0], garage: [12, 10, 5, 0], engineshed: [30, 14, 8, 0], postoffice: [24, 16, 13, 0], sorting: [20, 14, 8, 0], arsenal: [30, 14, 9, 0] };
    this.KIND = KIND;
    for (const p of M.points) this.addZone(p.x, p.z, 10); for (const s of M.spawn) this.addZone(s.x, s.z, 24);
    const spec = (cx, cz, w, d, h, k, pitched) => { const o = this.addObst(cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2, h + (pitched ? 2.2 : .6), k); o.cx = cx; o.cz = cz; this.specs.push({ cx, cz, w, d, h, k, pitched, o, v: R.next(), v2: R.next() }); this.addZone(cx, cz, Math.max(w, d) / 2 + 1.5); };
    this.lms = [];
    for (const l of M.landmarks) { const o = l.k === 'church' ? this.addObst(l.x - 5, l.x + 5, l.z - 8, l.z + 8, 9, 'church') : this.addObst(l.x - 2.5, l.x + 2.5, l.z - 2.5, l.z + 2.5, 8, l.k); this.lms.push(l); this.addZone(l.x, l.z, l.k === 'church' ? 11 : 6); }
    for (const b of M.build) { const K = KIND[b.k] || KIND.house; const vary = ['house', 'barn', 'shed', 'villa'].includes(b.k), f = vary ? .85 + R.next() * .4 : 1, tall = (b.k === 'house' && R.next() < .3) ? 2.4 : 0;
      spec(b.x, b.z, b.w > 0 ? b.w : K[0] * f, b.d > 0 ? b.d : K[1] * (vary ? .9 + R.next() * .3 : 1), b.h > 0 ? b.h : K[2] + tall, b.k, !!K[3]); }
    const Hh = M.half;
    if (M.city) {
      for (let gx = -Hh + 15; gx < Hh - 10; gx += 44) for (let gz = -Hh + 15; gz < Hh - 10; gz += 40) {
        if (R.next() < .1) continue; const cx = gx + 15, cz = gz + 13; let bad = !this.clearOfKey(cx, cz, 28) || Math.abs(cz) > Hh - 24 || this.inBuilding(cx - 8, cz, 10) || this.inBuilding(cx + 9, cz, 10);
        for (const l of this.lms) if (Math.hypot(l.x - cx, l.z - cz) < 26) bad = true; for (const l of M.lanes) if (segD(cx, cz, l.x1, l.z1, l.x2, l.z2) < l.w / 2 + 26) bad = true; if (bad) continue;
        const ruined = R.next() < .24, w = 12 + R.next() * 3, d = 18 + R.next() * 6;
        spec(cx - 8, cz, w, d, ruined ? 3 + R.next() * 3 : 7 + R.next() * 9, ruined ? 'ruin' : 'city', false); if (R.next() < .85) spec(cx + 9, cz, w - 1, d - 2, 6 + R.next() * 9, 'city', false);
      }
      // street frontage along the lanes (Wolska St.): continuous tenement rows with gaps for side streets
      for (const l of M.lanes) { const dx = l.x2 - l.x1, dz = l.z2 - l.z1, L = Math.hypot(dx, dz), ux = dx / L, uz = dz / L; for (const side of [-1, 1]) for (let s = 10; s < L - 10; s += 17 + R.next() * 6) {
        if (R.next() < .18) { s += 8; continue; } const off = l.w / 2 + 8, cx = l.x1 + ux * s - uz * off * side, cz = l.z1 + uz * s + ux * off * side; if (!this.clearOfKey(cx, cz, 9) || this.inBuilding(cx, cz, 6) || Math.abs(cx) > Hh || Math.abs(cz) > Hh) continue;
        const along = Math.abs(ux) > Math.abs(uz); const w = along ? 15 : 14, d = along ? 14 : 15; spec(cx, cz, w, d, 9 + R.next() * 7, R.next() < .15 ? 'ruin' : 'tenement', false); } }
    } else for (const v of M.villages) { let placed = 0, tries = 0;
      while (placed < v.n && tries++ < 90) { const x = v.x + (R.next() - .5) * 2 * v.r, z = v.z + (R.next() - .5) * 2 * v.r, w = 8 * (.85 + R.next() * .4), d = 7 * (.9 + R.next() * .3);
        if (!this.clearOfKey(x, z, 26 - 12) || this.inBuilding(x, z, 9) || this.nearRoad(x, z, w / 2 + 3) || this.inRiver(x, z, w) || this.S.TrenchD(x, -z) < 5 || this.nearRail(x, z, 9)) continue;
        let nl = false; for (const l of this.lms) if (Math.hypot(l.x - x, l.z - z) < 16) nl = true; if (nl) continue;
        spec(x, z, w, d, 3.4 + (R.next() < .3 ? 2.4 : 0), 'house', true); placed++;
        if (R.next() < .45) { const bx = x + (R.next() < .5 ? -1 : 1) * (w / 2 + 8), bz = z + (R.next() - .5) * 6; if (!this.inBuilding(bx, bz, 5) && !this.nearRoad(bx, bz, 8) && this.clearOfKey(bx, bz, 4)) spec(bx, bz, 11, 7, 4.2, R.next() < .5 ? 'barn' : 'shed', true); }
      } }
  }
  planTrees(R) {
    const M = this.M, Hh = M.half; const AreaK = (Hh * Hh) / (400 * 400);
    const add = (x, z, s, k) => { if (Math.abs(x) > Hh + 120 || Math.abs(z) > Hh + 120 || !this.clearOfKey(x, z, 4) || this.nearRoad(x, z, 4) || this.inBuilding(x, z, 3) || this.inRiver(x, z, 4) || this.S.TrenchD(x, -z) < 3.5 || this.nearRail(x, z, 8)) return; this.trees.push({ x, z, s: s ?? .8 + R.next() * .7, k: k ?? (R.next() < .3 ? 1 : 0) }); };
    for (const fo of M.forests) { const pine = R.next() < .65; for (let i = 0; i < fo.n; i++) { const a = R.next() * 6.28, d = Math.sqrt(R.next()) * fo.r; add(fo.x + Math.cos(a) * d, fo.z + Math.sin(a) * d, undefined, pine ? (R.next() < .85 ? 0 : 1) : (R.next() < .3 ? 0 : R.next() < .8 ? 1 : 2)); } }
    const lone = Math.round((M.city ? 30 : 100) * AreaK); for (let i = 0; i < lone; i++) add((R.next() - .5) * 2 * (Hh + 20), (R.next() - .5) * 2 * (Hh + 20));
    if (!M.city) {
      const cop = Math.round(9 * AreaK); for (let i = 0; i < cop; i++) { const cx = (R.next() - .5) * 2 * (Hh - 30), cz = (R.next() - .5) * 2 * (Hh - 30); if (!this.clearOfKey(cx, cz, 20) || this.nearRoad(cx, cz, 14)) continue; const n = 5 + (R.next() * 7 | 0), cr = 6 + R.next() * 7; for (let k = 0; k < n; k++) { const a = R.next() * 6.28, dd = Math.sqrt(R.next()) * cr; add(cx + Math.cos(a) * dd, cz + Math.sin(a) * dd); } }
      for (const r of M.roads) { let dx = r.x2 - r.x1, dz = r.z2 - r.z1; const L = Math.hypot(dx, dz); if (L < 90 || R.next() < .35) continue; dx /= L; dz /= L; const off = r.w / 2 + 5.5, both = R.next() < .5;
        for (let s = 14 + R.next() * 10; s < L - 14; s += 13 + R.next() * 5) { if (R.next() < .15) continue; const px = r.x1 + dx * s, pz = r.z1 + dz * s; add(px - dz * off, pz + dx * off, 1 + R.next() * .3, 1); if (both) add(px + dz * off, pz - dx * off, 1 + R.next() * .3, 1); } }
      // the forest belt beyond the map edge: the horizon is woods, not bare rim
      for (let i = 0; i < 380 * AreaK; i++) { const a = R.next() * 6.28, d = Hh + 30 + R.next() * 90; add(Math.cos(a) * d * 1.05, Math.sin(a) * d * 1.05, 1 + R.next() * .5, R.next() < .6 ? 0 : 1); }
    }
    for (const o of M.orchards) for (let x = o.x - o.w / 2; x <= o.x + o.w / 2; x += 6) for (let z = o.z - o.d / 2; z <= o.z + o.d / 2; z += 6) add(x + (R.next() - .5) * .8, z + (R.next() - .5) * .8, .5 + R.next() * .12, 3);
    for (const t of this.trees) { t.r = t.k === 0 ? .22 * t.s : .3 * t.s; this.gridAdd(this.tgrid, t, 8); }
  }

  // ---- heights: inner fine grid over the play area, coarse outer grid to the horizon
  bakeHeights() {
    const M = this.M; const step = [6, 5, 3][this.q] || 5; this.step = step;
    this.inE = M.half + 50; this.inN = Math.ceil(2 * this.inE / step) + 1; this.inH = new Float32Array(this.inN * this.inN);
    for (let j = 0; j < this.inN; j++) for (let i = 0; i < this.inN; i++) this.inH[j * this.inN + i] = this.HU(-this.inE + i * step, -this.inE + j * step);
    this.outE = M.half + (M.hasSea ? 450 : 520); this.outStep = this.q >= 2 ? 12 : 18; this.outN = Math.ceil(2 * this.outE / this.outStep) + 1; this.outH = new Float32Array(this.outN * this.outN);
    for (let j = 0; j < this.outN; j++) for (let i = 0; i < this.outN; i++) this.outH[j * this.outN + i] = this.HU(-this.outE + i * this.outStep, -this.outE + j * this.outStep);
  }
  Hraw(x, z) {
    let E = this.inE, s = this.step, N = this.inN, A = this.inH;
    if (Math.abs(x) > E - s || Math.abs(z) > E - s) { E = this.outE; s = this.outStep; N = this.outN; A = this.outH; }
    const fx = clamp((x + E) / s, 0, N - 1.001), fz = clamp((z + E) / s, 0, N - 1.001), i = Math.floor(fx), j = Math.floor(fz), u = fx - i, v = fz - j;
    const a = A[j * N + i], b = A[j * N + i + 1], c = A[(j + 1) * N + i], d = A[(j + 1) * N + i + 1];
    return u + v < 1 ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1 - u) + (b - d) * (1 - v); // matches the mesh triangulation
  }
  /** ground height (bridge decks included) */
  H(x, z) { let h = this.Hraw(x, z); for (const b of this.bridges) if (Math.abs(x - b.x) < 5.6 && Math.abs(z - b.c) < b.len / 2) h = Math.max(h, b.y + .2); return h; }
  waterDepth(x, z) { if (this.WL == null) { for (const b of this.S.brooks) { /* shallow brooks */ } return 0; } return Math.max(0, this.WL - this.H(x, z)); }
  surf(x, z) { if (this.waterDepth(x, z) > .05) return 'water'; if (this.city && this.nearRoad(x, z, 0)) return 'stone'; if (this.nearRoad(x, z, 0)) return 'road'; for (const b of this.bridges) if (Math.abs(x - b.x) < 5.6 && Math.abs(z - b.c) < b.len / 2) return 'wood'; return 'grass'; }
  trenchD(x, z) { return this.S.TrenchD(x, -z); }

  // ---- terrain mesh with splat weights
  buildTerrain() {
    const M = this.M, MU = this.MU; const gnd = new THREE.Color(M.ground); const gl = (gnd.r + gnd.g + gnd.b) / 3 || 1; const tint = [1 + (gnd.r / gl - 1) * .25, 1 + (gnd.g / gl - 1) * .25, 1 + (gnd.b / gl - 1) * .25];
    const mk = (E, s, N, A, outer) => {
      const pos = new Float32Array(N * N * 3), col = new Float32Array(N * N * 3), spl = new Float32Array(N * N * 4), idx = [];
      for (let j = 0; j < N; j++) for (let i = 0; i < N; i++) {
        const x = -E + i * s, z = -E + j * s, k = j * N + i; let y = A[k];
        if (outer && Math.abs(x) < this.inE - s * 1.2 && Math.abs(z) < this.inE - s * 1.2) y -= 4;
        pos[k * 3] = x; pos[k * 3 + 1] = y; pos[k * 3 + 2] = z;
        const w = this.splatAt(x, z, y); spl.set(w.s, k * 4);
        col[k * 3] = tint[0] * w.k; col[k * 3 + 1] = tint[1] * w.k; col[k * 3 + 2] = tint[2] * w.k;
      }
      for (let j = 0; j < N - 1; j++) for (let i = 0; i < N - 1; i++) { const a = j * N + i, b = a + 1, c = a + N, d = c + 1; idx.push(a, c, b, b, c, d); }
      const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setAttribute('color', new THREE.BufferAttribute(col, 3)); g.setAttribute('splat', new THREE.BufferAttribute(spl, 4));
      g.setIndex(N * N > 65000 ? new THREE.Uint32BufferAttribute(idx, 1) : new THREE.Uint16BufferAttribute(idx, 1)); g.computeVertexNormals(); g.computeBoundingSphere();
      const mesh = new THREE.Mesh(g, this.terrainMat || (this.terrainMat = terrainMaterial(this.q))); mesh.receiveShadow = true; this.group.add(mesh); return mesh;
    };
    this.terrain = mk(this.inE, this.step, this.inN, this.inH, false);
    mk(this.outE, this.outStep, this.outN, this.outH, true);
  }
  splatAt(x, z, y) {
    const M = this.M, F = this.S.Fbm; let dirt = 0, grav = 0, cob = 0, stub = 0, k = 1;
    for (const r of M.roads) { const d = segD(x, z, r.x1, r.z1, r.x2, r.z2); if (d < r.w / 2 + 3) { const t = 1 - sm(r.w / 2 - .5, r.w / 2 + 1.5, d); if (M.city) cob = Math.max(cob, t); else grav = Math.max(grav, t); dirt = Math.max(dirt, (1 - sm(r.w / 2, r.w / 2 + 3, d)) * .6); } }
    for (const r of M.lanes) { const d = segD(x, z, r.x1, r.z1, r.x2, r.z2); if (d < r.w / 2 + 3) cob = Math.max(cob, 1 - sm(r.w / 2 - 1, r.w / 2 + 1, d)); }
    for (const r of M.rails) { const d = segD(x, z, r.x1, r.z1, r.x2, r.z2); if (d < 5) grav = Math.max(grav, 1 - sm(2.5, 4.5, d)); }
    const td = this.trenchD(x, z); if (td < 4.5) dirt = Math.max(dirt, 1 - sm(2.5, 4.5, td));
    for (const s of this.specs) { const dx = Math.max(0, Math.abs(x - s.cx) - s.w / 2), dz = Math.max(0, Math.abs(z - s.cz) - s.d / 2), d = Math.hypot(dx, dz); if (d < 10) { if (M.city) cob = Math.max(cob, (1 - sm(2, 9, d)) * .7); else dirt = Math.max(dirt, (1 - sm(2, 9, d)) * .7); } }
    for (const p of M.points) { const d = Math.hypot(x - p.x, z - p.z); if (d < 20) dirt = Math.max(dirt, (1 - sm(6, 20, d)) * .35); }
    let forest = 0; for (const f of M.forests) { const d = Math.hypot(x - f.x, z - f.z); if (d < f.r + 10) forest = Math.max(forest, 1 - sm(f.r - 10, f.r + 10, d)); }
    if (!M.city) {
      // field parcels: harvested stubble strips across the plain (Sept 1939), meadow on slopes and near woods
      const ang = .35, u = x * Math.cos(ang) + z * Math.sin(ang), v = -x * Math.sin(ang) + z * Math.cos(ang); const strip = Math.floor(u / 45) * 7.31 + Math.floor(v / 120) * 3.17; const r = (Math.sin(strip * 12.9898) * 43758.5453) % 1;
      if (Math.abs(r) < .45 && forest < .2 && Math.abs(x) < M.half && Math.abs(z) < M.half) stub = .9 * (1 - forest) * (1 - grav);
      dirt = Math.max(dirt, forest * .55);
    } else { const n = F(x * .05, z * .05, 3); dirt = Math.max(dirt, n * .5); grav = Math.max(grav, (1 - cob) * .55); }
    if (this.WL != null && y < this.WL + 1.2) dirt = Math.max(dirt, 1 - sm(this.WL + .2, this.WL + 1.2, y));
    if (M.hasSea && y < 2.2) { grav = Math.max(grav, 1 - sm(1, 2.2, y)); }
    const n = F(x * .02 + 3, z * .02, 3); k = .78 + n * .38 - forest * .12;
    let ao = 0; for (let di = -1; di <= 1; di++) for (let dj = -1; dj <= 1; dj++) { const tl = this.gridGet(this.tgrid, x + di * 8, z + dj * 8, 8); if (tl) for (const t of tl) { const cr = (t.k === 0 ? 2.2 : t.k === 3 ? 1.6 : 3.2) * t.s, d = Math.hypot(x - t.x, z - t.z); if (d < cr) ao = Math.max(ao, (1 - d / cr) * (t.k === 0 ? .25 : .4)); } }
    for (const s of this.specs) { const dx = Math.max(0, Math.abs(x - s.cx) - s.w / 2), dz = Math.max(0, Math.abs(z - s.cz) - s.d / 2), d = Math.hypot(dx, dz); if (d < 2.5) ao = Math.max(ao, (1 - d / 2.5) * .35); }
    k *= 1 - ao;
    const tot = dirt + grav + cob + stub; if (tot > 1) { dirt /= tot; grav /= tot; cob /= tot; stub /= tot; }
    return { s: [dirt, grav, cob, stub], k };
  }

  buildWater() {
    const M = this.M; if (this.WL == null && !this.S.brooks.length) return;
    const mat = new THREE.MeshStandardMaterial({ color: M.hasSea ? 0x2e4a58 : 0x3a4a3a, roughness: .12, metalness: .1, normalMap: TEX.water.normal, transparent: true, opacity: .88 });
    mat.normalMap.repeat.set(60, 60); this.waterMat = mat;
    if (this.WL != null) { const g = new THREE.PlaneGeometry(this.outE * 2.4, this.outE * 2.4, 1, 1); g.rotateX(-Math.PI / 2); const w = new THREE.Mesh(g, mat); w.position.y = this.WL; this.group.add(w); }
    for (const b of this.S.brooks) { const gb = new GB(); for (let i = 0; i < b.p.length - 1; i++) { const a = b.p[i], c = b.p[i + 1], dx = c[0] - a[0], dz = c[1] - a[1], L = Math.hypot(dx, dz) || 1, nx = -dz / L * b.w * .55, nz = dx / L * b.w * .55; const y0 = b.lv[i] - b.depth * .45, y1 = b.lv[i + 1] - b.depth * .45;
      gb.quad([a[0] - nx, y0, -(a[1] - nz)], [a[0] + nx, y0, -(a[1] + nz)], [c[0] + nx, y1, -(c[1] + nz)], [c[0] - nx, y1, -(c[1] - nz)], [0, 1, 0], [[0, 0], [1, 0], [1, 1], [0, 1]], [1, 1, 1]); }
      const m2 = mat.clone(); m2.side = THREE.DoubleSide; this.group.add(new THREE.Mesh(gb.build(), m2)); }
    for (const b of this.bridges) { const gb = new GB(); gb.box(b.x, b.y - .4, b.c, 11.4, .4, b.len, [1, 1, 1], 3, 3); gb.box(b.x - 5.5, b.y, b.c, .35, 1.0, b.len, [1, 1, 1], 3, 1); gb.box(b.x + 5.5, b.y, b.c, .35, 1.0, b.len, [1, 1, 1], 3, 1);
      for (let s = -b.len / 2 + 3; s < b.len / 2; s += 6) gb.box(b.x, M.rlevel - 3, b.c + s, 9, b.y - M.rlevel + 2.6, 1.2, [.8, .8, .8], 3, 3);
      const m = new THREE.Mesh(gb.build(), texMat('planks', { vc: true })); m.castShadow = m.receiveShadow = true; this.group.add(m); }
  }

  // ---- buildings: plaster / brick façades with windows, tile or thatch roofs; merged per material
  buildBuildingMeshes() {
    const groups = {}; const G = k => groups[k] || (groups[k] = new GB());
    const roofGable = (gb, cx, y, cz, w, d, rh, col, tu) => { // ridge along the longer side
      const along = w >= d, L = along ? w : d, S = along ? d : w, hl = L / 2 + .35, hs = S / 2 + .45; const P = (a, yy, b) => along ? [cx + a, yy, cz + b] : [cx + b, yy, cz + a];
      const slope = Math.hypot(hs, rh);
      gb.quad(P(-hl, y, hs), P(hl, y, hs), P(hl, y + rh, 0), P(-hl, y + rh, 0), norm(along ? [0, hs, rh] : [rh, hs, 0]).map((v, i) => v), [[0, 0], [L / tu, 0], [L / tu, slope / tu], [0, slope / tu]], col);
      gb.quad(P(hl, y, -hs), P(-hl, y, -hs), P(-hl, y + rh, 0), P(hl, y + rh, 0), norm(along ? [0, hs, -rh] : [-rh, hs, 0]), [[0, 0], [L / tu, 0], [L / tu, slope / tu], [0, slope / tu]], col);
      return P;
    };
    const fixQuadNormals = () => { };
    for (const s of this.specs) {
      const y = this.Hraw(s.cx, s.cz) - .4, k = s.k, rural = ['house', 'barn', 'shed', 'villa', 'guardhouse'].includes(k);
      let wall, roof = 'tiles', storey = 3.4, flat = !s.pitched;
      if (k === 'house') wall = ['plasterWin', 'plasterWinB', 'plasterWinC'][Math.floor(s.v * 3)], roof = s.v2 < .45 ? 'thatch' : 'tiles';
      else if (k === 'barn' || k === 'shed') { wall = 'planks'; roof = s.v2 < .6 ? 'thatch' : 'tiles'; storey = 4; }
      else if (k === 'bunker' || k === 'casemate' || k === 'gate') { wall = 'concrete'; flat = true; storey = 3; }
      else if (k === 'city' || k === 'tenement' || k === 'postoffice' || k === 'admin' || k === 'sorting') { wall = s.v < .5 ? 'brickWin' : 'stuccoWin'; flat = true; storey = 3.3; }
      else if (k === 'ruin') { wall = 'brick'; flat = true; }
      else if (k === 'boxcar') { wall = 'planks'; flat = true; storey = 3.6; }
      else if (k === 'stack') { const gb = G('brick'); gb.cyl(s.cx, y, s.cz, 2.6, 1.6, s.h, 12, [1, 1, 1], 3, 3); continue; }
      else if (k === 'tank') { const gb = G('concrete'); gb.cyl(s.cx, y, s.cz, s.w / 2, s.w / 2, s.h, 18, [.9, .9, .9], 4, 4); continue; }
      else if (['factory', 'warehouse', 'engineshed', 'garage', 'arsenal'].includes(k)) { wall = 'brickWin'; flat = true; storey = 4.5; }
      else { wall = 'stuccoWin'; storey = 3.5; }
      const gb = G(wall); const tu = wall.endsWith('Win') ? 4 : 4, h = s.h;
      if (k === 'ruin') { // broken walls: four ragged wall stubs, no roof, rubble inside
        const t = .5; for (let i = 0; i < 4; i++) { const hh = h * (.4 + ((s.v * 7 + i * 3.3) % 1) * .8); if (i === 0) gb.box(s.cx, y, s.cz - s.d / 2 + t / 2, s.w, hh, t, [1, 1, 1], 4, 4); if (i === 1) gb.box(s.cx, y, s.cz + s.d / 2 - t / 2, s.w * .6, hh, t, [1, 1, 1], 4, 4); if (i === 2) gb.box(s.cx - s.w / 2 + t / 2, y, s.cz, t, hh, s.d, [1, 1, 1], 4, 4); if (i === 3) gb.box(s.cx + s.w / 2 - t / 2, y, s.cz + s.d * .2, t, hh * .6, s.d * .6, [1, 1, 1], 4, 4); }
        G('rubble').box(s.cx, y, s.cz, s.w * .7, 1.2, s.d * .7, [.75, .7, .65], 3, 3); s.o.h = h * .6; continue;
      }
      gb.box(s.cx, y, s.cz, s.w, h + .4, s.d, [1, 1, 1], tu, storey, 0, flat);
      if (flat) { // parapet + tar roof
        const r = G('roofFlat'); r.box(s.cx, y + h + .4, s.cz, s.w - .3, .05, s.d - .3, [.5, .5, .5], 4, 4); if (k !== 'bunker' && k !== 'casemate') { const cornice = G('concrete'); cornice.box(s.cx, y + h + .3, s.cz, s.w + .3, .35, s.d + .3, [.85, .82, .76], 4, 4, 0, false); }
        if (k === 'bunker' || k === 'casemate') { const sl = G('dark'); sl.box(s.cx, y + h * .55, s.cz - s.d / 2 - .02, s.w * .6, .3, .05, [0, 0, 0], 4, 4); sl.box(s.cx, y + h * .55, s.cz + s.d / 2 + .02, s.w * .6, .3, .05, [0, 0, 0], 4, 4); }
      } else {
        const rh = Math.min(s.w, s.d) * (roof === 'thatch' ? .62 : .5); const rg = G(roof); const along = s.w >= s.d; const top = y + h + .4;
        roofGable(rg, s.cx, top, s.cz, s.w, s.d, rh, [1, 1, 1], 2.5);
        // gable ends in wall material
        const L = (along ? s.w : s.d) / 2, S2 = (along ? s.d : s.w) / 2; const P = (a, yy, b) => along ? [s.cx + a, yy, s.cz + b] : [s.cx + b, yy, s.cz + a];
        const ge = G(wall === 'planks' ? 'planks' : 'plaster');
        ge.tri(P(L, top, S2), P(L, top, -S2), P(L, top + rh, 0), [[0, 0], [S2 * 2 / 4, 0], [S2 / 4, rh / 4]], [1, 1, 1]); ge.tri(P(-L, top, -S2), P(-L, top, S2), P(-L, top + rh, 0), [[0, 0], [S2 * 2 / 4, 0], [S2 / 4, rh / 4]], [1, 1, 1]);
        if (k === 'house' && s.v2 > .3) G('brick').box(s.cx + (along ? s.w * .2 : 0), top + rh * .5, s.cz + (along ? 0 : s.d * .2), .7, rh * .8, .7, [1, 1, 1], 2, 2); // chimney
        s.o.h = h + rh;
      }
      if (rural || k === 'barracks' || k === 'villa') { const door = G('dark'); const dz = s.cz + s.d / 2 + .03; door.box(s.cx + s.w * .18, y + .4, dz, 1.1, 2.1, .06, [.18, .13, .09], 4, 4); }
    }
    // landmarks from the model library (church, windmill)
    const mm = modelMaterial('cloth');
    for (const l of this.lms) { const y = this.Hraw(l.x, l.z) - .2; if (hasModel(l.k)) { const m = spawnModel(l.k, mm); m.position.set(l.x, y, l.z); m.scale.setScalar(l.k === 'church' ? 1.6 : 1.4); this.group.add(m); }
      else { G('stuccoWin').box(l.x, y, l.z, 5, 8, 5, [1, 1, 1], 4, 3.4); } }
    const mats = { tiles: texMat('tiles', { vc: true, ns: 1.2, side: THREE.DoubleSide }), thatch: texMat('thatch', { vc: true, ns: 1.5, side: THREE.DoubleSide }), planks: texMat('planks', { vc: true, side: THREE.DoubleSide }), concrete: texMat('concrete', { vc: true }), brick: texMat('brick', { vc: true }), brickWin: texMat('brickWin', { vc: true }), stuccoWin: texMat('stuccoWin', { vc: true }),
      plaster: texMat('plaster', { vc: true, side: THREE.DoubleSide }), plasterWin: texMat('plasterWin', { vc: true }), plasterWinB: texMat('plasterWinB', { vc: true }), plasterWinC: texMat('plasterWinC', { vc: true }), rubble: texMat('gravel', { vc: true }),
      roofFlat: new THREE.MeshStandardMaterial({ color: 0x3a3836, roughness: .95 }), dark: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .9 }) };
    for (const k in groups) { const g = groups[k]; if (!g.vc) continue; const m = new THREE.Mesh(g.build(), mats[k] || mats.plaster); m.castShadow = true; m.receiveShadow = true; this.group.add(m); }
  }

  // ---- trees: leafy card crowns (alpha-cut clumps on a dense core), instanced and chunked so off-screen chunks are culled
  buildTrees() {
    const q = this.q, N = (a, b, c) => [a, b, c][q] ?? b;
    const pine = new GB(), broad = new GB(), birch = new GB(), fruit = new GB(), trunkP = new GB(), trunkB = new GB(), trunkW = new GB(), trunkF = new GB();
    const R = new Rng(97), up = [0, 1, 0];
    /** one card: centre p, facing (yaw, tilt), size; lit as part of a round crown (normal from the crown centre) */
    const card = (gb, p, cen, yaw, tilt, size, col, sq = 1) => {
      const t1 = [Math.cos(yaw), 0, Math.sin(yaw)], t2 = [-Math.sin(yaw) * Math.sin(tilt), Math.cos(tilt), Math.cos(yaw) * Math.sin(tilt)];
      const h = size / 2, hv = size / 2 * sq, P = (a, b) => [p[0] + t1[0] * a + t2[0] * b, p[1] + t1[1] * a + t2[1] * b, p[2] + t1[2] * a + t2[2] * b];
      const n = norm([(p[0] - cen[0]) * 1.0 + .0, (p[1] - cen[1]) * 1.3 + .9, (p[2] - cen[2]) * 1.0]); const f = R.next() < .5;
      gb.quad(P(-h, -hv), P(h, -hv), P(h, hv), P(-h, hv), n, f ? [[0, 0], [1, 0], [1, 1], [0, 1]] : [[1, 0], [0, 0], [0, 1], [1, 1]], col);
    };
    /** a crown: cards scattered through an ellipsoid (most near its skin), darker underneath and inside */
    const crown = (gb, cx, cy, cz, rx, ry, rz, n, size, base = [1, 1, 1]) => {
      for (let i = 0; i < n; i++) { let u, v, w, d; do { u = R.next() * 2 - 1; v = R.next() * 2 - 1; w = R.next() * 2 - 1; d = u * u + v * v + w * w; } while (d > 1 || d < .01);
        const L = Math.sqrt(d), k = Math.pow(R.next(), .3) / L, p = [cx + u * k * rx, cy + v * k * ry, cz + w * k * rz];
        const ao = .42 + .58 * clamp((v * k + 1) / 2, 0, 1) * (.55 + .45 * Math.min(1, k * L)); const c = base.map(x => x * ao * (.9 + R.next() * .2));
        card(gb, p, [cx, cy, cz], R.next() * 6.283, (R.next() - .5) * 1.6, size * (.75 + R.next() * .5), c); }
    };
    /** a dense core inside the crown (uv on the leafy centre of the card) so distant trees keep their mass */
    const core = (gb, cx, cy, cz, r, sy, col) => { const ico = new THREE.IcosahedronGeometry(r, 0), p = ico.attributes.position;
      for (let i = 0; i < p.count; i += 3) { const v = []; for (let k = 0; k < 3; k++) v.push([cx + p.getX(i + k), cy + p.getY(i + k) * sy, cz + p.getZ(i + k)]); gb.tri(v[0], v[1], v[2], [[.42, .42], [.58, .45], [.5, .58]], col.map(c => c * (.6 + (v[0][1] - cy + r) / (2 * r) * .3))); } ico.dispose(); };
    const branch = (gb, y0, a, len, rise, r) => { const x1 = Math.cos(a) * len, z1 = Math.sin(a) * len; const steps = 3; for (let s = 0; s < steps; s++) { const t0 = s / steps, t1 = (s + 1) / steps; const p0 = [x1 * t0, y0 + rise * t0, z1 * t0], p1 = [x1 * t1, y0 + rise * t1, z1 * t1], rr0 = r * (1 - t0 * .7), rr1 = r * (1 - t1 * .7);
      const dx = p1[0] - p0[0], dz = p1[2] - p0[2], dl = Math.hypot(dx, dz) || 1, nx = -dz / dl, nz = dx / dl; gb.quad([p0[0] + nx * rr0, p0[1], p0[2] + nz * rr0], [p1[0] + nx * rr1, p1[1], p1[2] + nz * rr1], [p1[0] - nx * rr1, p1[1] + rr1 * 2, p1[2] - nz * rr1], [p0[0] - nx * rr0, p0[1] + rr0 * 2, p0[2] - nz * rr0], [nx, .3, nz], [[0, t0], [1, t0], [1, t1], [0, t1]], [.9, .9, .9]); } };
    // Scots pine: tall bare orange trunk, a flat, broken crown of needle sprays high up
    trunkP.cyl(0, -.5, 0, .22, .14, 14.2, 6, [1, 1, 1], 1.2, 2);
    core(pine, 0, 12.3, 0, 1.55, 1.3, [.55, .62, .5]);
    for (let t = 0; t < N(5, 6, 8); t++) { const y = 9.6 + t * (4.6 / N(5, 6, 8)), rad = 2.3 - t * .17, nb = N(3, 4, 5);
      for (let b = 0; b < nb; b++) { const a = R.next() * 6.283, r0 = rad * (.45 + R.next() * .55), px = Math.cos(a) * r0, pz = Math.sin(a) * r0; branch(trunkP, y - .5, a, r0, .4, .05);
        for (let c = 0; c < N(2, 3, 4); c++) card(pine, [px + (R.next() - .5) * .8, y + (R.next() - .5) * .5, pz + (R.next() - .5) * .8], [0, y - .8, 0], a + (R.next() - .5) * 1.2, 1.15 + (R.next() - .5) * .5, 1.9 + R.next() * .7, [.92, .95, .9].map(x => x * (.75 + t * .05))); } }
    card(pine, [0, 14.6, 0], [0, 13, 0], 0, 1.4, 1.6, [1, 1, 1]); card(pine, [0, 14.5, 0], [0, 13, 0], 1.57, 1.4, 1.5, [1, 1, 1]);
    // broadleaf (oak / lime): stout trunk, four limbs, a lumpy crown of three lobes
    trunkB.cyl(0, -.5, 0, .34, .22, 6.2, 7, [1, 1, 1], 1.2, 2); for (let b = 0; b < 4; b++) branch(trunkB, 4.4 + b * .35, b * 1.6 + .4, 2.1, 1.8, .11);
    core(broad, .2, 7.3, 0, 2.6, .85, [.6, .66, .52]);
    crown(broad, .2, 7.4, 0, 3.4, 2.7, 3.4, N(22, 36, 58), 2.5); crown(broad, -1.5, 6.5, -.9, 2.3, 1.9, 2.3, N(8, 13, 20), 2.2); crown(broad, 1.3, 6.8, 1.1, 2.1, 1.8, 2.1, N(7, 11, 18), 2.1);
    // birch: slim white trunk, light airy crown
    trunkW.cyl(0, -.5, 0, .17, .1, 10, 6, [1.7, 1.7, 1.6], 1, 2); for (let b = 0; b < 3; b++) branch(trunkW, 5.5 + b * .9, b * 2.1, 1.4, 1.6, .06);
    core(birch, 0, 7.8, 0, 1.6, 1.4, [.62, .66, .5]); crown(birch, 0, 7.6, 0, 2.2, 3.0, 2.2, N(16, 26, 42), 1.7);
    // orchard fruit tree: short trunk, small round crown
    trunkF.cyl(0, -.3, 0, .14, .1, 2.3, 6, [.8, .7, .6], 1, 2); core(fruit, 0, 2.9, 0, 1.2, .8, [.6, .65, .5]); crown(fruit, 0, 2.9, 0, 1.8, 1.4, 1.8, N(10, 16, 24), 1.4);
    const leaf = foliageMat('leafCard', q), mats = { pine: foliageMat('needleCard', q), broad: leaf, birch: foliageMat('birchCard', q), fruit: leaf, trunkP: texMat('pinebark', { vc: true, side: THREE.DoubleSide }), trunkB: texMat('bark', { vc: true, side: THREE.DoubleSide }), trunkW: texMat('bark', { vc: true, side: THREE.DoubleSide }), trunkF: texMat('bark', { vc: true, side: THREE.DoubleSide }) };
    const geos = { pine: pine.build(), broad: broad.build(), birch: birch.build(), fruit: fruit.build(), trunkP: trunkP.build(), trunkB: trunkB.build(), trunkW: trunkW.build(), trunkF: trunkF.build() };
    const layers = [['pine', 'trunkP'], ['broad', 'trunkB'], ['birch', 'trunkW'], ['fruit', 'trunkF']];
    const chunk = q >= 2 ? 200 : 260, buckets = new Map();
    for (const t of this.trees) { const key = Math.floor(t.x / chunk) + ',' + Math.floor(t.z / chunk); let b = buckets.get(key); if (!b) buckets.set(key, b = []); b.push(t); }
    const m4 = new THREE.Matrix4(), qq = new THREE.Quaternion(), sc = new THREE.Vector3(), ps = new THREE.Vector3(), upv = new THREE.Vector3(0, 1, 0), col = new THREE.Color();
    this.treeChunks = [];
    for (const [, list] of buckets) for (let k = 0; k < 4; k++) {
      const L = list.filter(t => t.k === k); if (!L.length) continue;
      for (const part of layers[k]) { const im = new THREE.InstancedMesh(geos[part], mats[part], L.length); im.castShadow = q > 0; im.receiveShadow = true;
        L.forEach((t, i) => { const s = t.s; ps.set(t.x, this.Hraw(t.x, t.z), t.z); qq.setFromAxisAngle(upv, (t.x * 7.13 + t.z * 3.1) % 6.28); sc.set(s * (.9 + (t.x * 13 % 1) * .2), s, s * (.9 + (t.z * 11 % 1) * .2)); m4.compose(ps, qq, sc); im.setMatrixAt(i, m4);
          if (!part.startsWith('trunk')) { const h = Math.abs(Math.sin(t.x * 12.9898 + t.z * 78.233) * 43758.5453) % 1; col.setRGB(.88 + h * .24, .9 + h * .18, .82 + (1 - h) * .2); im.setColorAt(i, col); } });
        im.computeBoundingSphere(); im.userData.cx = L.reduce((a, t) => a + t.x, 0) / L.length; im.userData.cz = L.reduce((a, t) => a + t.z, 0) / L.length; this.group.add(im); this.treeChunks.push(im); }
    }
  }

  // ---- fences, hedges, walls, telegraph lines, lamps, piers
  buildSegs() {
    const G = { planks: new GB(), hedge: new GB(), stone: new GB(), pole: new GB(), concrete: new GB() }; const wires = [];
    for (const s of this.M.segs) {
      const dx = s.x2 - s.x1, dz = s.z2 - s.z1, L = Math.hypot(dx, dz); if (L < .5) continue; const ux = dx / L, uz = dz / L, rot = Math.atan2(-uz, ux) * 0 + Math.atan2(dx, dz) - Math.PI / 2;
      const mid = (a) => [s.x1 + dx * a, s.z1 + dz * a];
      if (s.k === 'fence') { for (let d = 0; d <= L; d += 2.5) { const x = s.x1 + ux * d, z = s.z1 + uz * d; G.planks.box(x, this.Hraw(x, z) - .2, z, .12, 1.4, .12, [.8, .75, .7], 1, 1); }
        const n = Math.ceil(L / 6); for (let i = 0; i < n; i++) { const [x, z] = mid((i + .5) / n); const y = this.Hraw(x, z); G.planks.box(x, y + .55, z, L / n, .1, .04, [1, 1, 1], 2, .5, rot); G.planks.box(x, y + 1.0, z, L / n, .1, .04, [1, 1, 1], 2, .5, rot); }
        this.addLowObst(s, .6, 1.1); }
      else if (s.k === 'hedge') { const n = Math.ceil(L / 3); for (let i = 0; i < n; i++) { const [x, z] = mid((i + .5) / n); G.hedge.box(x, this.Hraw(x, z) - .2, z, L / n + .4, 1.6 + Math.sin(i * 2.1) * .3, 1.4, [.9, .95, .85], 2, 2, rot); } this.addLowObst(s, .9, 1.6, true); }
      else if (s.k === 'stonewall') { const n = Math.ceil(L / 4); for (let i = 0; i < n; i++) { const [x, z] = mid((i + .5) / n); G.stone.box(x, this.Hraw(x, z) - .3, z, L / n + .05, 1.25, .6, [.85, .85, .8], 2, 2, rot); } this.addLowObst(s, .5, 1.0); }
      else if (s.k === 'telegraph' || s.k === 'lamps') { const sp = s.k === 'lamps' ? 22 : 40; let prev = null; for (let d = 0; d <= L; d += sp) { const x = s.x1 + ux * d, z = s.z1 + uz * d, y = this.Hraw(x, z); G.pole.cyl(x, y - .3, z, .12, .09, 7, 5, [.7, .6, .5], 1, 2); if (s.k === 'telegraph') G.pole.box(x, y + 6.3, z, 1.4, .1, .1, [.7, .6, .5], 1, 1, rot); const p = [x, y + 6.4, z]; if (prev) wires.push(prev, p); prev = p; } }
      else if (s.k === 'pier') { const n = Math.ceil(L / 4); for (let i = 0; i < n; i++) { const [x, z] = mid((i + .5) / n); G.planks.box(x, 1.3, z, L / n, .3, 4, [1, 1, 1], 3, 3, rot); G.planks.cyl(x, -3, z, .2, .2, 4.4, 5, [.6, .5, .4]); } }
      else { const n = Math.ceil(L / 6); for (let i = 0; i < n; i++) { const [x, z] = mid((i + .5) / n); G.concrete.box(x, this.Hraw(x, z) + .2, z, L / n, .5, .5, [.6, .55, .5], 2, 2, rot); } }
    }
    const mats = { planks: texMat('planks', { vc: true }), hedge: texMat('leaves', { vc: true }), stone: texMat('cobble', { vc: true }), pole: texMat('bark', { vc: true }), concrete: texMat('concrete', { vc: true }) };
    for (const k in G) if (G[k].vc) { const m = new THREE.Mesh(G[k].build(), mats[k]); m.castShadow = m.receiveShadow = true; this.group.add(m); }
    if (wires.length) { const g = new THREE.BufferGeometry().setFromPoints(wires.flatMap(p => [new THREE.Vector3(...p)])); const l = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: 0x222222, transparent: true, opacity: .6 })); this.group.add(l); }
  }
  addLowObst(s, t, h, soft) { const n = Math.max(1, Math.ceil(Math.hypot(s.x2 - s.x1, s.z2 - s.z1) / 4)); for (let i = 0; i < n; i++) { const a = (i + .5) / n, x = s.x1 + (s.x2 - s.x1) * a, z = s.z1 + (s.z2 - s.z1) * a; const o = this.addObst(x - 2.1, x + 2.1, z - 2.1, z + 2.1, h, soft ? 'hedge' : 'low'); o.low = true; o.soft = !!soft; o.seg = [s.x1, s.z1, s.x2, s.z2, t]; this.covers.push({ x, z, h }); } }

  buildProps(R) {
    const mm = modelMaterial('cloth'), steel = modelMaterial('steel'); this.wrecks = []; const G = { planks: new GB(), concrete: new GB(), gravel: new GB(), dark: new GB(), steel: new GB(), sand: new GB() };
    const put = (name, x, z, rot = 0, s = 1, mat = mm) => { if (!hasModel(name)) return null; const m = spawnModel(name, mat); m.position.set(x, this.Hraw(x, z), z); m.rotation.y = rot; m.scale.setScalar(s); this.group.add(m); return m; };
    for (const p of this.M.props) {
      const y = this.Hraw(p.x, p.z), a = p.a || 4, b = p.b || 4, rot = R.next() * 6.28;
      switch (p.k) {
        case 'rocks': for (let i = 0; i < 6; i++) { const r = .6 + R.next() * 1.4, x = p.x + (R.next() - .5) * a, z = p.z + (R.next() - .5) * b; G.gravel.box(x, this.Hraw(x, z) - .4, z, r * 1.6, r, r * 1.3, [.7, .7, .68], 1.5, 1.5, R.next() * 3); } break;
        case 'rubble': for (let i = 0; i < 8; i++) { const x = p.x + (R.next() - .5) * 6, z = p.z + (R.next() - .5) * 6; G.gravel.box(x, this.Hraw(x, z) - .2, z, 1 + R.next() * 2, .4 + R.next() * .8, 1 + R.next() * 2, [.8, .72, .66], 1.5, 1.5, R.next() * 3); } break;
        case 'crates': for (let i = 0; i < 5; i++) { const x = p.x + (R.next() - .5) * 4, z = p.z + (R.next() - .5) * 4; G.planks.box(x, this.Hraw(x, z), z, 1, 1, 1, [.9, .8, .6], 1, 1, R.next() * 3); } this.covers.push({ x: p.x, z: p.z, h: 1 }); break;
        case 'barrels': for (let i = 0; i < 6; i++) { const x = p.x + (R.next() - .5) * 3, z = p.z + (R.next() - .5) * 3; G.steel.cyl(x, this.Hraw(x, z), z, .32, .32, .9, 8, [.35, .32, .25]); } break;
        case 'barricade': { // Wola: overturned carts, tram rails, paving stones and sandbags across the street
          const n = 4; for (let i = 0; i < n; i++) { const x = p.x + (i - n / 2) * 3, z = p.z + (R.next() - .5); if (i % 2) put('cart', x, z, Math.PI / 2 + R.next() * .4); else G.sand.box(x, this.Hraw(x, z), z, 3, 1.2, 1.2, [1, 1, 1], 1.5, .6); }
          this.addObst(p.x - 7, p.x + 7, p.z - 1, p.z + 1, 1.2, 'low').low = true; this.covers.push({ x: p.x, z: p.z, h: 1.2 }); break; }
        case 'train': case 'derailed': case 'deadloco': { const n = p.k === 'train' ? 4 : 1; for (let i = 0; i < n; i++) { const x = p.x + i * 13, z = p.z; G.planks.box(x, y + .9, z, 12, 2.8, 3, [.55, .3, .22], 4, 3, p.k === 'derailed' ? .2 : 0); G.dark.box(x, y, z, 10, .9, 2.4, [.12, .12, .12], 4, 4); this.addObst(x - 6, x + 6, z - 1.6, z + 1.6, 3.8, 'car'); } break; }
        case 'tram': G.steel.box(p.x, y + .5, p.z, 3, 3, 11, [.7, .55, .2], 3, 3, p.a ? p.a * Math.PI / 180 : 0); this.addObst(p.x - 2, p.x + 2, p.z - 5.5, p.z + 5.5, 3.5, 'car'); break;
        case 'shrine': G.planks.box(p.x, y, p.z, .3, 3, .3, [.5, .4, .3], 1, 1); G.planks.box(p.x, y + 2.1, p.z, 1.3, .2, .2, [.5, .4, .3], 1, 1); break;
        case 'watertower': G.concrete.cyl(p.x, y, p.z, 2.2, 2.2, 9, 12, [.85, .82, .78]); G.planks.cyl(p.x, y + 9, p.z, 3.4, 3.4, 4, 14, [.6, .5, .4]); this.addObst(p.x - 2.4, p.x + 2.4, p.z - 2.4, p.z + 2.4, 13, 'tower'); break;
        case 'signal': G.steel.cyl(p.x, y, p.z, .1, .1, 6, 5, [.25, .25, .25]); G.steel.box(p.x, y + 5.6, p.z, .2, .2, 1.4, [.7, .2, .2], 1, 1); break;
        case 'fountain': G.concrete.cyl(p.x, y, p.z, 3, 3, .7, 16, [.85, .82, .78]); break;
        case 'boat': G.planks.box(p.x, .2, p.z, 3, 1, 9, [.5, .4, .3], 3, 1); break;
        case 'buffer': G.steel.box(p.x, y, p.z, 3, 1.2, .8, [.5, .25, .2], 1, 1); break;
        case 'bushes': case 'flowers': break; // handled by the hedge material below
        default: break;
      }
      if (p.k === 'bushes') for (let i = 0; i < 5; i++) { const x = p.x + (R.next() - .5) * a, z = p.z + (R.next() - .5) * b; (G.hedge || (G.hedge = new GB())).box(x, this.Hraw(x, z) - .2, z, 1.6 + R.next() * 1.5, 1 + R.next() * .8, 1.6 + R.next(), [.9, .95, .8], 2, 2, R.next() * 3); }
    }
    // village dressing: haystacks, carts, wells near farms; sandbag nests and wire at objectives
    const M = this.M;
    for (const s of this.specs) if ((s.k === 'barn' || s.k === 'house') && R.next() < .35) { const x = s.cx + (R.next() - .5) * (s.w + 10), z = s.cz + s.d / 2 + 4 + R.next() * 6; if (!this.inBuilding(x, z, 1.5) && !this.nearRoad(x, z, 2)) put(R.next() < .6 ? 'haystack' : 'cart', x, z, R.next() * 6.28); }
    const def = 1 - M.attacker;
    M.points.forEach((p, i) => { if (M.range) return; const n = 2 + (i % 2); for (let k = 0; k < n; k++) { const a = (k / n) * 6.28 + i, r = (p.r || 30) * .55, x = p.x + Math.cos(a) * r, z = p.z + Math.sin(a) * r; if (this.inBuilding(x, z, 2) || this.inRiver(x, z, 1)) continue; const m = put('sandbags', x, z, a + Math.PI / 2); if (m) { this.addObst(x - 1.4, x + 1.4, z - 1.4, z + 1.4, 1.1, 'low').low = true; this.covers.push({ x, z, h: 1.1, sand: true }); } }
      if (M.fortified && i < M.points.length) for (let k = 0; k < 3; k++) { const a = Math.atan2(-p.z, -p.x) + (k - 1) * .5, r = (p.r || 30) + 12, x = p.x + Math.cos(a) * r, z = p.z + Math.sin(a) * r; if (!this.inBuilding(x, z, 2) && !this.nearRoad(x, z, 1) && !this.inRiver(x, z, 1)) put('wire', x, z, a + Math.PI / 2); } });
    // wrecks and burning ruins (mission.wrecks / fires)
    for (let i = 0; i < (M.wrecks || 0); i++) { const r = M.roads[i % Math.max(1, M.roads.length)] || M.lanes[0]; if (!r) break; const t = .2 + R.next() * .6, x = r.x1 + (r.x2 - r.x1) * t + (R.next() - .5) * 10, z = r.z1 + (r.z2 - r.z1) * t; if (this.inBuilding(x, z, 3)) continue; put('wreck', x, z, R.next() * 6.28, 1, steel); this.addObst(x - 2, x + 2, z - 2.5, z + 2.5, 2, 'wreck'); this.wrecks.push({ x, z }); }
    this.fires = []; for (let i = 0; i < (M.fires || 0); i++) { const s = this.specs[Math.floor(R.next() * this.specs.length)]; if (s) this.fires.push({ x: s.cx, z: s.cz, y: this.Hraw(s.cx, s.cz) + s.h * .6, big: s.w > 10 }); }
    if (M.hasShip) { // SMS Schleswig-Holstein: 127 m pre-dreadnought moored in the harbour channel, grey hull, two twin 28 cm turrets
      const sx = M.shipX, sz = M.shipZ, g = G.steel, gy = -2.5, rot = Math.atan2(-sx, -sz) + Math.PI / 2; const hull = [.42, .44, .46], sup = [.5, .52, .54];
      const P = (a, b) => [sx + Math.cos(rot) * a + Math.sin(rot) * b, sz - Math.sin(rot) * a + Math.cos(rot) * b];
      for (let i = -6; i <= 6; i++) { const w = 22 * Math.sqrt(1 - Math.pow(Math.abs(i) / 6.6, 2)); const [x, z] = P(i * 10, 0); g.box(x, gy, z, 10.2, 9.5, w, hull, 4, 4, rot); }
      const [ax, az] = P(-18, 0), [bx, bz] = P(10, 0); g.box(ax, gy + 9.5, az, 16, 7, 12, sup, 4, 4, rot); g.box(bx, gy + 9.5, bz, 12, 5, 10, sup, 4, 4, rot);
      for (const o of [-8, 4]) { const [x, z] = P(o, 0); g.cyl(x, gy + 13, z, 1.6, 1.6, 9, 10, [.3, .3, .3]); }
      for (const o of [-46, 46]) { const [x, z] = P(o, 0); g.cyl(x, gy + 9.5, z, 4.2, 4.2, 3, 12, sup); const [mx, mz] = P(o + (o < 0 ? -9 : 9), 0); g.box(mx, gy + 10.8, mz, 10, .7, 1.4, [.35, .36, .38], 4, 4, rot); }
      const [m1x, m1z] = P(-25, 0); g.cyl(m1x, gy + 9.5, m1z, .5, .4, 26, 6, [.4, .4, .4]);
      this.ship = { x: sx, z: sz };
    }
    const mats = { planks: texMat('planks', { vc: true }), concrete: texMat('concrete', { vc: true }), gravel: texMat('gravel', { vc: true }), dark: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .8, metalness: .2 }), steel: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .55, metalness: .3, map: TEX.cloth.map }), sand: texMat('sandbag', { vc: true }), hedge: texMat('leaves', { vc: true }) };
    for (const k in G) if (G[k].vc) { const m = new THREE.Mesh(G[k].build(), mats[k]); m.castShadow = m.receiveShadow = true; this.group.add(m); }
  }
  buildRails() {
    const gb = new GB(), steel = new GB();
    for (const r of this.M.rails) { const dx = r.x2 - r.x1, dz = r.z2 - r.z1, L = Math.hypot(dx, dz), ux = dx / L, uz = dz / L, rot = Math.atan2(dx, dz);
      for (let d = 0; d < L; d += .9) { const x = r.x1 + ux * d, z = r.z1 + uz * d; gb.box(x, this.Hraw(x, z) - .05, z, 2.5, .18, .24, [.55, .45, .38], 2, 1, rot); }
      for (const off of [-.72, .72]) for (let d = 0; d < L; d += 6) { const x = r.x1 + ux * (d + 3) - uz * off, z = r.z1 + uz * (d + 3) + ux * off; steel.box(x, this.Hraw(x, z) + .12, z, .08, .14, 6.05, [.45, .4, .36], 1, 1, rot); } }
    if (gb.vc) { const m = new THREE.Mesh(gb.build(), texMat('planks', { vc: true })); m.receiveShadow = true; this.group.add(m); const s = new THREE.Mesh(steel.build(), new THREE.MeshStandardMaterial({ vertexColors: true, metalness: .6, roughness: .4 })); this.group.add(s); }
  }
  buildTrenchDressing() { // parapet sandbags and revetment planks along trench lines
    const sand = new GB(), plank = new GB();
    for (const t of this.M.trenches || []) for (let i = 0; i + 3 < t.pts.length; i += 2) { const ax = t.pts[i], az = t.pts[i + 1], bx = t.pts[i + 2], bz = t.pts[i + 3], L = Math.hypot(bx - ax, bz - az), ux = (bx - ax) / L, uz = (bz - az) / L, rot = Math.atan2(bx - ax, bz - az);
      for (let d = 0; d < L; d += 2.2) { const x = ax + ux * d, z = az + uz * d; for (const side of [1]) { const px = x - uz * 2.3 * side, pz = z + ux * 2.3 * side; sand.box(px, this.Hraw(px, pz) - .25, pz, 1.0, .6, 2.15, [1, 1, 1], 1.4, .6, rot); }
        } }
    if (sand.vc) { const m = new THREE.Mesh(sand.build(), texMat('sandbag', { vc: true })); m.castShadow = m.receiveShadow = true; this.group.add(m); const p = new THREE.Mesh(plank.build(), texMat('planks', { vc: true })); p.receiveShadow = true; this.group.add(p); }
  }

  // ---- sky dome: horizon glow, Mie halo and sun disc, and the cloud deck whose shadows drift across the ground
  buildSky() {
    const g = new THREE.SphereGeometry(3000, 48, 24);
    this.skyU = { top: { value: new THREE.Color() }, hor: { value: new THREE.Color() }, sunCol: { value: new THREE.Color() }, sunDir: { value: new THREE.Vector3(0, 1, 0) }, cover: { value: .4 }, time: { value: 0 },
      fwNoise: FOGU.fwNoise, fwTime: FOGU.fwTime, fwWind: FOGU.fwWind, fwCloud: FOGU.fwCloud };
    const m = new THREE.ShaderMaterial({ uniforms: this.skyU, side: THREE.BackSide, depthWrite: false, fog: false,
      vertexShader: 'varying vec3 vD; void main(){ vD = normalize(position); vec4 p = modelViewMatrix*vec4(position,1.0); gl_Position = projectionMatrix*p; gl_Position.z = gl_Position.w*0.99999; }',
      fragmentShader: `uniform vec3 top, hor, sunCol, sunDir; uniform float cover, time; uniform sampler2D fwNoise; uniform float fwTime; uniform vec2 fwWind; uniform vec4 fwCloud; varying vec3 vD;
${CLOUD_GLSL}
void main(){ vec3 d = normalize(vD); float y = max(d.y, 0.0); vec3 sd = normalize(sunDir); float mu = dot(d, sd), mp = max(mu, 0.0);
 vec3 c = mix(hor, top, pow(y, 0.5));
 c += hor * 0.16 * exp(-y * 10.0);                                       // the pale band along the horizon
 c += sunCol * (pow(mp, 10.0) * 0.30 + pow(mp, 3.0) * 0.09 + pow(mp, 900.0) * 2.5);   // Mie glow round the sun
 c += sunCol * smoothstep(0.99935, 0.99968, mu) * 7.0;                   // the sun's disc
 if (d.y > 0.0) { float t = (fwCloud.z - cameraPosition.y) / max(d.y, 0.025); vec2 q = cameraPosition.xz + d.xz * t;
  float den = fwCloudD(q), toward = fwCloudD(q + sd.xz * 180.0);       // thicker toward the sun: this side is in the cloud's own shade
  float lit = clamp(1.05 - (toward - den) * 1.7 - den * 0.3, 0.22, 1.15);
  vec3 shade = hor * 0.55 + top * 0.12, light = sunCol * 0.8 + hor * 0.5;
  vec3 cc = mix(shade, light, lit); cc += sunCol * pow(mp, 7.0) * (1.0 - den) * 1.4;   // silver lining
  c = mix(c, cc, den * smoothstep(0.0, 0.14, d.y) * 0.97); }
 c = mix(c, hor * 0.85, smoothstep(0.0, -0.2, d.y));
 gl_FragColor = vec4(c, 1.0);
 #include <tonemapping_fragment>
 #include <colorspace_fragment>
}` });
    this.sky = new THREE.Mesh(g, m); this.sky.frustumCulled = false; this.sky.renderOrder = -1; this.scene.add(this.sky);
  }

  /** hide tree chunks past `dist` (fog hides them anyway) */
  cullTrees(cx, cz, dist) { for (const im of this.treeChunks) { const d = Math.hypot(im.userData.cx - cx, im.userData.cz - cz); im.visible = d < dist + 90; } }
  // ---- queries
  /** first blocking hit between two points: terrain, buildings, low walls (if below their top), tree trunks. Returns t∈[0,1] or null */
  rayBlock(ax, ay, az, bx, by, bz, opts = {}) {
    const dx = bx - ax, dy = by - ay, dz = bz - az, L = Math.hypot(dx, dy, dz); if (L < 1e-3) return null;
    const step = Math.min(2, Math.max(.5, L / 120)); let best = null, mat = null;
    // terrain marching
    for (let s = step; s < L; s += step) { const t = s / L, x = ax + dx * t, y = ay + dy * t, z = az + dz * t; if (y < this.Hraw(x, z) - .05) { best = t; mat = 'dirt'; break; } }
    // obstacles along the segment (grid walk)
    const seen = new Set(), cell = 20, steps = Math.ceil(Math.hypot(dx, dz) / 10) + 1;
    for (let i = 0; i <= steps; i++) { const t = i / steps, l = this.gridGet(this.ogrid, ax + dx * t, az + dz * t, cell); if (!l) continue;
      for (const o of l) { if (seen.has(o)) continue; seen.add(o); if (opts.noSoft && o.soft) continue; const hit = rayAABB(ax, ay, az, dx, dy, dz, o.x0, o.y0, o.z0, o.x1, o.y1, o.z1); if (hit != null && (best == null || hit < best)) { best = hit; mat = o.kind === 'bunker' || o.kind === 'casemate' || o.kind === 'gate' ? 'stone' : o.kind === 'barn' || o.kind === 'shed' || o.kind === 'hedge' ? 'wood' : o.kind === 'car' || o.kind === 'wreck' ? 'metal' : 'stone'; } } }
    if (!opts.noTrees) for (let i = 0; i <= steps; i++) { const t = i / steps, l = this.gridGet(this.tgrid, ax + dx * t, az + dz * t, 8); if (!l) continue;
      for (const tr of l) { if (seen.has(tr)) continue; seen.add(tr); const hit = rayCyl(ax, az, dx, dz, tr.x, tr.z, tr.r); if (hit != null && (best == null || hit < best)) { const y = ay + dy * hit; const gy = this.Hraw(tr.x, tr.z); if (y < gy + (tr.k === 3 ? 1.6 : 6)) { best = hit; mat = 'wood'; } } } }
    return best == null ? null : { t: best, mat, x: ax + dx * best, y: ay + dy * best, z: az + dz * best };
  }
  /** line of sight for spotting: also blocked by dense foliage (≥3 crowns within 2.5 m of the line) */
  los(ax, ay, az, bx, by, bz) {
    if (this.rayBlock(ax, ay, az, bx, by, bz, { noTrees: true })) return false;
    const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz), steps = Math.ceil(L / 8); let foliage = 0; const seen = new Set();
    for (let i = 1; i < steps; i++) { const t = i / steps, x = ax + dx * t, z = az + dz * t, l = this.gridGet(this.tgrid, x, z, 8); if (!l) continue; for (const tr of l) if (!seen.has(tr) && segD(tr.x, tr.z, ax, az, bx, bz) < 2.5 + tr.s) { seen.add(tr); if (Math.min(Math.hypot(tr.x - ax, tr.z - az), Math.hypot(tr.x - bx, tr.z - bz)) > 6 && ++foliage >= 3) return false; } }
    return true;
  }
  /** push a circle out of buildings / trunks; returns corrected [x,z] */
  collide(x, z, r, y) {
    const l = this.gridGet(this.ogrid, x, z, 20);
    if (l) for (const o of l) { if (o.low && y != null && y > o.y1 - .5) continue; if (o.low && o.seg) { // thin wall segment
        const [x1, z1, x2, z2, t] = o.seg; const d = segD(x, z, x1, z1, x2, z2); if (d < t / 2 + r) { const dx = x2 - x1, dz = z2 - z1, L2 = dx * dx + dz * dz || 1, tt = clamp(((x - x1) * dx + (z - z1) * dz) / L2, 0, 1), px = x1 + dx * tt, pz = z1 + dz * tt, nx = x - px, nz = z - pz, nl = Math.hypot(nx, nz) || 1; x = px + nx / nl * (t / 2 + r); z = pz + nz / nl * (t / 2 + r); } continue; }
      const cx = clamp(x, o.x0, o.x1), cz = clamp(z, o.z0, o.z1), dx = x - cx, dz = z - cz, d = Math.hypot(dx, dz);
      if (d < r) { if (d > 1e-4) { x = cx + dx / d * r; z = cz + dz / d * r; } else { const e = [x - o.x0, o.x1 - x, z - o.z0, o.z1 - z], m = Math.min(...e); if (m === e[0]) x = o.x0 - r; else if (m === e[1]) x = o.x1 + r; else if (m === e[2]) z = o.z0 - r; else z = o.z1 + r; } } }
    const tl = this.gridGet(this.tgrid, x, z, 8); if (tl) for (const t of tl) { const dx = x - t.x, dz = z - t.z, d = Math.hypot(dx, dz), rr = t.r + r; if (d < rr && d > 1e-4) { x = t.x + dx / d * rr; z = t.z + dz / d * rr; } }
    const E = this.M.half + 30; x = clamp(x, -E, E); z = clamp(z, -E, E);
    return [x, z];
  }
  blocked(x, z, r = .4) { const l = this.gridGet(this.ogrid, x, z, 20); if (l) for (const o of l) if (!o.low && x > o.x0 - r && x < o.x1 + r && z > o.z0 - r && z < o.z1 + r) return true; return false; }
  /** how well a spot shields a man lying / crouching there: trench, wall or sandbags nearby (0..1) */
  coverAt(x, z) { let c = this.trenchD(x, z) < 1.6 ? .75 : 0; for (const k of this.covers) { const d = Math.hypot(k.x - x, k.z - z); if (d < 2.5) c = Math.max(c, .6); } if (this.inBuilding(x, z, 1.5)) c = Math.max(c, .5); return c; }
  dispose() { this.scene.remove(this.group); this.scene.remove(this.sky); this.group.traverse(o => { if (o.geometry) o.geometry.dispose(); }); }
}
function rayAABB(ox, oy, oz, dx, dy, dz, x0, y0, z0, x1, y1, z1) {
  let tmin = 0, tmax = 1; for (const [o, d, a, b] of [[ox, dx, x0, x1], [oy, dy, y0, y1], [oz, dz, z0, z1]]) { if (Math.abs(d) < 1e-9) { if (o < a || o > b) return null; } else { let t1 = (a - o) / d, t2 = (b - o) / d; if (t1 > t2) [t1, t2] = [t2, t1]; tmin = Math.max(tmin, t1); tmax = Math.min(tmax, t2); if (tmin > tmax) return null; } }
  return tmin;
}
function rayCyl(ox, oz, dx, dz, cx, cz, r) { const fx = ox - cx, fz = oz - cz, a = dx * dx + dz * dz, b = 2 * (fx * dx + fz * dz), c = fx * fx + fz * fz - r * r, disc = b * b - 4 * a * c; if (disc < 0 || a < 1e-9) return null; const t = (-b - Math.sqrt(disc)) / (2 * a); return t >= 0 && t <= 1 ? t : null; }
