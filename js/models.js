// Fall Weiss web — loads data/models.bin (the original three.js models, exported via the Unity models.bytes and compacted).
import * as THREE from './three.module.min.js';

let HDR = null, BUF = null;
const geoCache = new Map();

export async function loadModels(url, onProgress) {
  const res = await fetch(url);
  const total = +res.headers.get('content-length') || 0;
  let ab;
  if (res.body && total && onProgress) {
    const reader = res.body.getReader(); const chunks = []; let got = 0;
    for (;;) { const { done, value } = await reader.read(); if (done) break; chunks.push(value); got += value.length; onProgress(got / total); }
    const all = new Uint8Array(got); let o = 0; for (const c of chunks) { all.set(c, o); o += c.length; } ab = all.buffer;
  } else ab = await res.arrayBuffer();
  const dv = new DataView(ab);
  const magic = String.fromCharCode(...new Uint8Array(ab, 0, 4));
  if (magic !== 'FWW1') throw new Error('bad models.bin');
  const hl = dv.getUint32(4, true);
  HDR = JSON.parse(new TextDecoder().decode(new Uint8Array(ab, 8, hl)));
  BUF = ab.slice(8 + hl);
}

function geometryFor(key, m) {
  let g = geoCache.get(key); if (g) return g;
  g = new THREE.BufferGeometry();
  const q = new Int16Array(BUF, m.po, m.v * 3), pos = new Float32Array(m.v * 3);
  for (let i = 0; i < m.v; i++) for (let a = 0; a < 3; a++) pos[i * 3 + a] = m.mn[a] + (q[i * 3 + a] + 32768) / 65535 * m.sc[a];
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.BufferAttribute(new Int8Array(BUF, m.no, m.v * 3), 3, true));
  g.setAttribute('color', new THREE.BufferAttribute(new Uint8Array(BUF, m.co, m.v * 3), 3, true));
  g.setIndex(new THREE.BufferAttribute(m.b ? new Uint32Array(BUF, m.io, m.i) : new Uint16Array(BUF, m.io, m.i), 1));
  g.computeBoundingSphere();
  geoCache.set(key, g); return g;
}

export function hasModel(name) { return !!(HDR && HDR[name]); }

/** Build a model hierarchy. Named pivots (hip, upper, head, arm0, leg0, knee0, turret, weapon, muzzle…) keep their names. */
export function spawnModel(name, material, shadows = true) {
  const nd = HDR[name]; if (!nd) { console.warn('no model', name); return new THREE.Group(); }
  let n = 0;
  const build = (d, path) => {
    let o;
    if (d.m) { o = new THREE.Mesh(geometryFor(name + path, d.m), material); o.castShadow = shadows; o.receiveShadow = shadows; }
    else o = new THREE.Group();
    o.name = d.n || '';
    o.position.set(d.p[0], d.p[1], d.p[2]); o.quaternion.set(d.q[0], d.q[1], d.q[2], d.q[3]);
    if (d.s) o.scale.set(d.s[0], d.s[1], d.s[2]);
    if (d.k) d.k.forEach((k, i) => o.add(build(k, path + '/' + i)));
    n++; return o;
  };
  const root = build(nd, ''); root.name = name; return root;
}

const mergeCache = new Map();
/** Collapse every mesh under each named pivot into one mesh in that pivot's space (the pivot still animates; far fewer draw calls). */
export function mergeUnder(root, modelName, pivots, material, shadows) {
  for (const pv of pivots) {
    const node = findNode(root, pv); if (!node) continue; node.updateWorldMatrix(true, true);
    const key = modelName + '/' + pv; let geo = mergeCache.get(key);
    const meshes = []; node.traverse(o => { if (o.isMesh) meshes.push(o); });
    if (!geo) {
      const inv = new THREE.Matrix4().copy(node.matrixWorld).invert(); const parts = [];
      for (const m of meshes) { const g = m.geometry.clone(); const mx = new THREE.Matrix4().multiplyMatrices(inv, m.matrixWorld); for (const a of ['position', 'normal']) { const src = g.attributes[a]; const arr = new Float32Array(src.count * 3); for (let i = 0; i < src.count; i++) { arr[i * 3] = src.getX(i); arr[i * 3 + 1] = src.getY(i); arr[i * 3 + 2] = src.getZ(i); } g.setAttribute(a, new THREE.BufferAttribute(arr, 3)); }
        const c = g.attributes.color, ca = new Float32Array(c.count * 3); for (let i = 0; i < c.count; i++) { ca[i * 3] = c.getX(i); ca[i * 3 + 1] = c.getY(i); ca[i * 3 + 2] = c.getZ(i); } g.setAttribute('color', new THREE.BufferAttribute(ca, 3));
        g.applyMatrix4(mx); parts.push(g); }
      let vc = 0, ic = 0; for (const g of parts) { vc += g.attributes.position.count; ic += g.index.count; }
      const P = new Float32Array(vc * 3), N = new Float32Array(vc * 3), Cc = new Float32Array(vc * 3), I = vc > 65535 ? new Uint32Array(ic) : new Uint16Array(ic); let vo = 0, io = 0;
      for (const g of parts) { P.set(g.attributes.position.array, vo * 3); N.set(g.attributes.normal.array, vo * 3); Cc.set(g.attributes.color.array, vo * 3); const idx = g.index.array; for (let i = 0; i < idx.length; i++) I[io + i] = idx[i] + vo; vo += g.attributes.position.count; io += idx.length; g.dispose(); }
      geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(P, 3)); geo.setAttribute('normal', new THREE.BufferAttribute(N, 3)); geo.setAttribute('color', new THREE.BufferAttribute(Cc, 3)); geo.setIndex(new THREE.BufferAttribute(I, 1)); geo.computeBoundingSphere(); mergeCache.set(key, geo);
    }
    for (const m of meshes) { const kids = [...m.children]; const par = m.parent; for (const k of kids) { par.add(k); k.applyMatrix4(m.matrix); } par.remove(m); }
    const mm = new THREE.Mesh(geo, material); mm.castShadow = shadows; mm.receiveShadow = shadows; node.add(mm);
  }
}
export function findNode(root, name) { let r = null; root.traverse(o => { if (!r && o.name === name) r = o; }); return r; }
