// Fall Weiss web — painted "photo" textures for 1939 Poland, generated at start-up (no downloads, no licences): dry September
// meadow and stubble, dirt and gravel roads, Warsaw cobbles, lime-washed plaster and brick façades with windows, clay tile, thatch,
// tarred planks, shuttered concrete, bark, foliage and sandbags. Each comes with a normal map derived from its height field.
import * as THREE from './three.module.min.js';

// ---------- seeded value noise (tileable over `period` cells)
let PERM = new Uint8Array(512);
function seed(s) { const p = new Uint8Array(256); for (let i = 0; i < 256; i++) p[i] = i; let r = s * 7919 + 13; for (let i = 255; i > 0; i--) { r = (r * 16807) % 2147483647; const j = r % (i + 1); [p[i], p[j]] = [p[j], p[i]]; } for (let i = 0; i < 512; i++) PERM[i] = p[i & 255]; }
seed(1939);
const fade = t => t * t * (3 - 2 * t);
function vn(x, y, per) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi; const X0 = ((xi % per) + per) % per, Y0 = ((yi % per) + per) % per, X1 = (X0 + 1) % per, Y1 = (Y0 + 1) % per;
  const h = (a, b) => PERM[PERM[a & 255] + (b & 255)] / 255; const u = fade(xf), v = fade(yf);
  const a = h(X0, Y0), b = h(X1, Y0), c = h(X0, Y1), d = h(X1, Y1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
function fbm(x, y, per, oct = 5, lac = 2, g = .5) { let s = 0, a = .5, f = 1, t = 0; for (let i = 0; i < oct; i++) { s += vn(x * f + i * 17.3, y * f + i * 9.1, per * f) * a; t += a; a *= g; f *= lac; } return s / t; }
// cellular (Worley) for stones, tiles of known period
function worley(x, y, per, rnd) { const xi = Math.floor(x), yi = Math.floor(y); let d1 = 9, d2 = 9, id = 0;
  for (let j = -1; j <= 1; j++) for (let i = -1; i <= 1; i++) { const cx = xi + i, cy = yi + j, wx = ((cx % per) + per) % per, wy = ((cy % per) + per) % per; const hh = PERM[PERM[wx & 255] + (wy & 255)];
    const px = cx + (PERM[hh] / 255) * rnd + (1 - rnd) / 2, py = cy + (PERM[(hh + 77) & 255] / 255) * rnd + (1 - rnd) / 2; const d = Math.hypot(x - px, y - py); if (d < d1) { d2 = d1; d1 = d; id = hh; } else if (d < d2) d2 = d; }
  return [d1, d2, id]; }

function canvas(S) { const c = document.createElement('canvas'); c.width = c.height = S; return c; }
function mix(a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]; }
const hexc = h => [(h >> 16) & 255, (h >> 8) & 255, h & 255];

/** fill pixels: fn(u,v) -> [r,g,b,height(0..1), alpha?] with u,v in [0,1) */
function paint(S, fn) {
  const c = canvas(S), g = c.getContext('2d'), im = g.createImageData(S, S), d = im.data, H = new Float32Array(S * S);
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) { const r = fn(x / S, y / S, x, y), i = (y * S + x) * 4; d[i] = r[0]; d[i + 1] = r[1]; d[i + 2] = r[2]; d[i + 3] = r[4] ?? 255; H[y * S + x] = r[3] ?? .5; }
  g.putImageData(im, 0, 0); return { c, g, H, S };
}
function normalFrom(H, S, str) {
  const c = canvas(S), g = c.getContext('2d'), im = g.createImageData(S, S), d = im.data;
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const l = H[y * S + ((x - 1 + S) % S)], r = H[y * S + (x + 1) % S], u = H[((y - 1 + S) % S) * S + x], dn = H[((y + 1) % S) * S + x];
    let nx = (l - r) * str, ny = (u - dn) * str, nz = 1; const L = Math.hypot(nx, ny, nz); const i = (y * S + x) * 4;
    d[i] = (nx / L * .5 + .5) * 255; d[i + 1] = (ny / L * .5 + .5) * 255; d[i + 2] = (nz / L * .5 + .5) * 255; d[i + 3] = 255;
  }
  g.putImageData(im, 0, 0); return c;
}
function tex(cv, srgb, aniso) { const t = new THREE.CanvasTexture(cv); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = aniso; if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.generateMipmaps = true; t.minFilter = THREE.LinearMipmapLinearFilter; return t; }
// re-read height after canvas strokes so normals include them
function heightFromCanvas(g, S, H, w) { const d = g.getImageData(0, 0, S, S).data; for (let i = 0; i < S * S; i++) { const lum = (d[i * 4] * .3 + d[i * 4 + 1] * .59 + d[i * 4 + 2] * .11) / 255; H[i] = H[i] * (1 - w) + lum * w; } }

// ---------- generators (all tile seamlessly)
const GEN = {
  grass(S) { // dry late-summer meadow: olive, straw and dark green tufts
    const P = 8, base = paint(S, (u, v) => { const n = fbm(u * P, v * P, P, 5), m = fbm(u * 3 + 5, v * 3, 3, 3), s = vn(u * 64, v * 64, 64);
      let c = mix(hexc(0x5d6a32), hexc(0x8a8a4a), m); c = mix(c, hexc(0x3f4f22), Math.max(0, n - .55) * 1.6); c = mix(c, hexc(0x9a8d5a), Math.max(0, s - .82) * 3); const k = .78 + n * .4; return [c[0] * k, c[1] * k, c[2] * k, n]; });
    const { g } = base, R = rng(3);
    for (let i = 0; i < S * S / 26; i++) { const x = R() * S, y = R() * S, l = 3 + R() * S / 70, a = -Math.PI / 2 + (R() - .5) * 1.3; const t = R();
      g.strokeStyle = t < .45 ? `rgba(${70 + R() * 40},${90 + R() * 30},${30 + R() * 20},.55)` : t < .8 ? `rgba(${140 + R() * 40},${130 + R() * 30},${70 + R() * 20},.45)` : `rgba(30,40,15,.4)`;
      g.lineWidth = .6 + R() * .9; g.beginPath(); for (const ox of [0, -S, S]) for (const oy of [0, -S, S]) { g.moveTo(x + ox, y + oy); g.lineTo(x + ox + Math.cos(a) * l, y + oy + Math.sin(a) * l); } g.stroke(); }
    heightFromCanvas(g, S, base.H, .6); return base;
  },
  stubble(S) { // harvested grain field: straw rows with green regrowth
    const base = paint(S, (u, v) => { const n = fbm(u * 6, v * 6, 6, 4), row = Math.pow(Math.abs(Math.sin(v * Math.PI * 16)), 3); let c = mix(hexc(0xb59c5c), hexc(0x8a7a44), n); c = mix(c, hexc(0x6a6a34), Math.max(0, fbm(u * 3, v * 3 + 9, 3, 3) - .55) * 2); c = mix(c, hexc(0x5a4a30), row * .35); return [c[0], c[1], c[2], .5 + n * .4 - row * .3]; });
    const { g } = base, R = rng(5); for (let i = 0; i < S * S / 40; i++) { const x = R() * S, y = Math.round(R() * 16) / 16 * S + (R() - .5) * 6; g.fillStyle = `rgba(${200 + R() * 40},${180 + R() * 30},${110 + R() * 30},.6)`; g.fillRect(x, y, 1, 2 + R() * 3); }
    heightFromCanvas(g, S, base.H, .5); return base;
  },
  dirt(S) { return paint(S, (u, v) => { const n = fbm(u * 8, v * 8, 8, 6), w = worley(u * 40, v * 40, 40, .9); const peb = Math.max(0, .18 - w[0]) * 5; let c = mix(hexc(0x5a4632), hexc(0x7d6548), n); c = mix(c, hexc(0x8f8676), peb * (w[2] / 255)); c = mix(c, hexc(0x3c2e22), Math.max(0, .45 - n) * 1.5); return [c[0], c[1], c[2], n * .7 + peb * .5]; }); },
  gravel(S) { return paint(S, (u, v) => { const n = fbm(u * 6, v * 6, 6, 5), w = worley(u * 70, v * 70, 70, 1); const st = Math.max(0, 1 - w[0] * 2.6); const t = w[2] / 255; let c = mix(hexc(0x8a7c66), hexc(0xa89a80), n); c = mix(c, mix(hexc(0x6f675c), hexc(0xb8b0a0), t), st * .8); const wet = fbm(u * 2, v * 2 + 4, 2, 3); c = mix(c, hexc(0x5c4d3a), Math.max(0, wet - .62) * 1.8); return [c[0], c[1], c[2], st * .6 + n * .4]; }); },
  cobble(S) { // granite setts, offset rows (Wolska St., Heveliusplatz)
    return paint(S, (u, v) => { const rows = 10, ry = v * rows, ri = Math.floor(ry), off = (ri % 2) * .5, cols = 8, cx = u * cols + off, ci = Math.floor(cx), fx = cx - ci, fy = ry - ri;
      const e = Math.min(fx, 1 - fx, fy * 1.1, (1 - fy) * 1.1); const stone = sm(.02, .12, e); const id = PERM[(PERM[(((ci % cols) + cols) % cols)] + ri) & 255] / 255; const n = fbm(u * 24, v * 24, 24, 4);
      let c = mix(hexc(0x55524e), hexc(0x8d8a84), id); c = mix(c, hexc(0x6a5a4a), id > .8 ? .4 : 0); const k = .75 + n * .45; c = [c[0] * k, c[1] * k, c[2] * k]; c = mix(hexc(0x2e2a24), c, stone); return [c[0], c[1], c[2], stone * (.7 + n * .3)]; });
  },
  plaster(S, opts = {}) { // lime-wash over brick, with damp, flaking and soot; one window per tile when opts.win
    const tint = hexc(opts.tint ?? 0xe2d8c0);
    const base = paint(S, (u, v) => { const n = fbm(u * 5, v * 5, 5, 6), f = fbm(u * 14 + 3, v * 14, 14, 4); let c = mix(tint, mix(tint, hexc(0x9a8c74), .6), n * .7);
      const flake = f > .7 ? sm(.7, .74, f) : 0; const bx = u * 16, by = v * 32, br = (Math.floor(by) % 2) * .5; const mort = Math.min(Math.abs(((bx + br) % 1) - .5) - .45, Math.abs((by % 1) - .5) - .42) > 0 ? 1 : 0;
      if (flake) c = mix(c, mort ? hexc(0x8a8070) : hexc(0x9a5a40), flake);
      const damp = sm(.75, 1, v) * fbm(u * 8, v * 2, 8, 3); c = mix(c, hexc(0x6a6050), damp * .55); const soot = sm(.4, 0, v) * Math.max(0, fbm(u * 6 + 7, v * 3, 6, 3) - .5); c = mix(c, hexc(0x3a3632), soot);
      return [c[0], c[1], c[2], .6 - flake * .3 + n * .2]; });
    if (opts.win) drawWindow(base.g, S, opts);
    heightFromCanvas(base.g, S, base.H, .3); return base;
  },
  brick(S, opts = {}) { // Warsaw tenement brick, sooty
    const base = paint(S, (u, v) => { const rows = 24, by = v * rows, ri = Math.floor(by), bx = u * 8 + (ri % 2) * .5, ci = Math.floor(bx), fx = bx - ci, fy = by - ri; const mort = Math.min(fx, 1 - fx) * 4 < .12 || Math.min(fy, 1 - fy) < .1;
      const id = PERM[(PERM[((ci % 8) + 8) % 8] + ri) & 255] / 255, n = fbm(u * 10, v * 10, 10, 4); let c = mix(hexc(0x7a3c2a), hexc(0xa0583a), id); c = mix(c, hexc(0x4a2a20), id > .85 ? .6 : 0); const k = .8 + n * .35; c = [c[0] * k, c[1] * k, c[2] * k];
      if (mort) c = hexc(0x9a9284); c = mix(c, hexc(0x2e2a28), Math.max(0, fbm(u * 4, v * 2 + 5, 4, 3) - .55) * 1.4); return [c[0], c[1], c[2], mort ? .2 : .6 + n * .2]; });
    if (opts.win) drawWindow(base.g, S, { ...opts, frame: '#d8d0bc' });
    heightFromCanvas(base.g, S, base.H, .2); return base;
  },
  tiles(S) { // clay roof tiles (karpiówka beaver-tail), mossy
    return paint(S, (u, v) => { const rows = 14, ry = v * rows, ri = Math.floor(ry), fy = ry - ri, cx = u * 10 + (ri % 2) * .5, ci = Math.floor(cx), fx = cx - ci;
      const tail = Math.hypot((fx - .5) * 1.4, Math.max(0, fy - .45) * 1.6) < .55 ? 1 : 0; const id = PERM[(PERM[((ci % 10) + 10) % 10] + ri) & 255] / 255, n = fbm(u * 12, v * 12, 12, 4);
      let c = mix(hexc(0x8a3c28), hexc(0xa85a38), id); c = mix(c, hexc(0x5a4a2c), Math.max(0, fbm(u * 4 + 3, v * 4, 4, 3) - .55) * 1.6); const sh = .55 + fy * .55; c = [c[0] * sh * (.85 + n * .3), c[1] * sh * (.85 + n * .3), c[2] * sh * (.85 + n * .3)]; if (!tail) c = mix(c, hexc(0x2a1a14), .6); return [c[0], c[1], c[2], fy * tail]; });
  },
  thatch(S) { const base = paint(S, (u, v) => { const n = fbm(u * 6, v * 6, 6, 5); let c = mix(hexc(0x6a5a3a), hexc(0x8a7a50), n); c = mix(c, hexc(0x3c3a28), Math.max(0, fbm(u * 3, v * 3 + 2, 3, 3) - .5) * 1.5); const row = (v * 10) % 1; c = mix(c, hexc(0x2a2418), sm(.85, 1, row) * .6); return [c[0], c[1], c[2], n - sm(.85, 1, row) * .4]; });
    const R = rng(9); for (let i = 0; i < S * S / 18; i++) { const x = R() * S, y = R() * S; base.g.strokeStyle = `rgba(${120 + R() * 60},${100 + R() * 50},${60 + R() * 30},.5)`; base.g.lineWidth = .7; base.g.beginPath(); base.g.moveTo(x, y); base.g.lineTo(x + (R() - .5) * 3, y + 6 + R() * 10); base.g.stroke(); }
    heightFromCanvas(base.g, S, base.H, .6); return base; },
  planks(S) { return paint(S, (u, v) => { const bx = u * 6, bi = Math.floor(bx), fx = bx - bi, gap = Math.min(fx, 1 - fx) < .03; const id = PERM[bi & 255] / 255, grain = fbm(u * 6 * 4 + id * 9, v * 1.5, 24, 4) * .6 + vn(u * 160, v * 6, 160) * .4;
    let c = mix(hexc(0x4a3626), hexc(0x6e5238), id * .6 + grain * .4); c = mix(c, hexc(0x8a8274), Math.max(0, fbm(u * 3, v * 3 + 6, 3, 3) - .58) * 1.2); if (gap) c = hexc(0x140e0a); const knot = Math.max(0, .06 - Math.hypot(fx - .5, ((v * 3 + id) % 1) - .5)) * 12; c = mix(c, hexc(0x2a1c12), knot); return [c[0], c[1], c[2], gap ? 0 : .5 + grain * .3]; }); },
  concrete(S) { return paint(S, (u, v) => { const n = fbm(u * 8, v * 8, 8, 6), board = (v * 8) % 1 < .04 ? 1 : 0, pit = vn(u * 120, v * 120, 120) > .86 ? 1 : 0; let c = mix(hexc(0x7e7c74), hexc(0x9c9a90), n); c = mix(c, hexc(0x5a5850), board * .5 + pit * .4); c = mix(c, hexc(0x4a4a40), Math.max(0, fbm(u * 2, v * 4 + 1, 2, 3) - .55) * sm(.3, 1, v)); return [c[0], c[1], c[2], .5 + n * .3 - board * .2 - pit * .3]; }); },
  bark(S) { return paint(S, (u, v) => { const n = fbm(u * 4, v * 20, 4, 5), r = Math.abs(Math.sin((u * 12 + fbm(u * 3, v * 6, 3, 3) * 3) * Math.PI)); let c = mix(hexc(0x3a2e24), hexc(0x6a5a48), n); c = mix(c, hexc(0x1e1610), Math.pow(1 - r, 4)); return [c[0], c[1], c[2], r * .8 + n * .2]; }); },
  pinebark(S) { return paint(S, (u, v) => { const w = worley(u * 6, v * 14, 6, 1), n = fbm(u * 8, v * 8, 8, 4); let c = mix(hexc(0x8a4a2c), hexc(0xb06a40), n); c = mix(c, hexc(0x3a241a), sm(.0, .12, w[1] - w[0]) < 1 ? .9 - sm(0, .12, w[1] - w[0]) : 0); return [c[0], c[1], c[2], sm(0, .15, w[1] - w[0])]; }); },
  leaves(S, opts = {}) { // foliage canopy: clustered leaves with light/shade (used on crown meshes)
    const dark = hexc(opts.dark ?? 0x24361a), lite = hexc(opts.lite ?? 0x6a7e34);
    return paint(S, (u, v) => { const w = worley(u * 18, v * 18, 18, 1), n = fbm(u * 5, v * 5, 5, 4); const leaf = 1 - sm(.15, .5, w[0]); const id = w[2] / 255;
      let c = mix(dark, lite, leaf * (.4 + id * .6) * (.6 + n * .6)); if (opts.autumn && id > .85) c = mix(c, hexc(0xa07a2a), .6); return [c[0], c[1], c[2], leaf * .7 + n * .3]; });
  },
  needles(S) { const base = paint(S, (u, v) => { const n = fbm(u * 6, v * 6, 6, 4); const c = mix(hexc(0x1a2a1a), hexc(0x3a5030), n); return [c[0], c[1], c[2], n]; });
    const R = rng(11); for (let i = 0; i < S * S / 10; i++) { const x = R() * S, y = R() * S, a = R() * Math.PI; base.g.strokeStyle = `rgba(${50 + R() * 40},${80 + R() * 40},${40 + R() * 30},.6)`; base.g.lineWidth = .8; base.g.beginPath(); base.g.moveTo(x, y); base.g.lineTo(x + Math.cos(a) * 5, y + Math.sin(a) * 5); base.g.stroke(); }
    heightFromCanvas(base.g, S, base.H, .7); return base; },
  sandbag(S) { return paint(S, (u, v) => { const rows = 4, ry = v * rows, ri = Math.floor(ry), fy = ry - ri, bx = u * 2 + (ri % 2) * .5, fx = bx - Math.floor(bx); const bulge = Math.sin(fy * Math.PI) * Math.sin(fx * Math.PI); const weave = (Math.sin(u * S * 1.4) * Math.sin(v * S * 1.4)) * .5 + .5, n = fbm(u * 8, v * 8, 8, 4);
    let c = mix(hexc(0x8a7c5e), hexc(0xb8a67e), n * .7 + weave * .15); c = [c[0] * (.72 + bulge * .4), c[1] * (.72 + bulge * .4), c[2] * (.72 + bulge * .4)]; c = mix(c, hexc(0x4a3c2c), Math.max(0, fbm(u * 3, v * 3, 3, 3) - .6)); return [c[0], c[1], c[2], bulge * .8 + weave * .1]; }); },
  cloth(S) { // grey detail for uniforms / painted steel on the original vertex-coloured models: weave + wear + grime
    return paint(S, (u, v) => { const weave = ((Math.sin(u * S * .9) * Math.sin(v * S * .9)) * .5 + .5), n = fbm(u * 8, v * 8, 8, 5), g = fbm(u * 3 + 4, v * 3, 3, 3); const k = 150 + weave * 25 + n * 60 - Math.max(0, g - .55) * 160; return [k, k, k, weave * .4 + n * .6]; });
  },
  water(S) { return paint(S, (u, v) => { const n = fbm(u * 6, v * 6, 6, 5); return [128, 128, 255, n]; }); },
  // ---- foliage cards (RGBA, alpha-tested): a clump of leaves or a spray of needles per card
  leafCard(S, o = {}) {
    const c = canvas(S), g = c.getContext('2d'), R = rng(o.seed || 21), dark = hexc(o.dark ?? 0x2c3d1c), lite = hexc(o.lite ?? 0x7d8c3c), n = Math.round((o.n || 700) * (S / 256) ** 2 / (S / 256));
    // a few twigs first, then the leaves over them
    g.strokeStyle = 'rgba(52,40,28,.9)'; g.lineCap = 'round';
    for (let i = 0; i < 7; i++) { const a = R() * 6.28, L = S * (.25 + R() * .2); g.lineWidth = S / 180 + R() * S / 220; g.beginPath(); g.moveTo(S / 2, S / 2); g.quadraticCurveTo(S / 2 + Math.cos(a + .3) * L * .5, S / 2 + Math.sin(a + .3) * L * .5, S / 2 + Math.cos(a) * L, S / 2 + Math.sin(a) * L); g.stroke(); }
    for (let i = 0; i < n; i++) {
      const a = R() * 6.283, r = Math.pow(R(), .62) * S * .46, x = S / 2 + Math.cos(a) * r, y = S / 2 + Math.sin(a) * r * .92;
      const len = S * (o.len || .055) * (.65 + R() * .7), wid = len * (o.wid || .52), rot = R() * 6.283, edge = r / (S * .46);
      const t = Math.min(1, Math.max(0, R() * .85 + (1 - edge) * .1 + (y < S / 2 ? .12 : -.05))); let col = mix(dark, lite, t);
      if (o.autumn && R() < .08) col = mix(col, hexc(0xb08a2a), .65); if (R() < .05) col = mix(col, hexc(0x9a9a50), .4); // a few yellowing leaves (September)
      g.save(); g.translate(x, y); g.rotate(rot); g.fillStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`;
      g.beginPath(); g.moveTo(-len / 2, 0); g.quadraticCurveTo(0, -wid, len / 2, 0); g.quadraticCurveTo(0, wid, -len / 2, 0); g.fill();
      g.strokeStyle = `rgba(255,255,210,${.08 + R() * .1})`; g.lineWidth = Math.max(.6, S / 400); g.beginPath(); g.moveTo(-len / 2, 0); g.lineTo(len / 2, 0); g.stroke(); g.restore();
    }
    return { c, alpha: true };
  },
  needleCard(S, o = {}) {
    const c = canvas(S), g = c.getContext('2d'), R = rng(o.seed || 33); g.lineCap = 'round';
    for (let k = 0; k < 9; k++) { // sprays: a twig with needles both sides, drooping toward the card's bottom
      const a = -Math.PI / 2 + (k - 4) * .32 + (R() - .5) * .2, L = S * (.3 + R() * .14), x0 = S / 2 + (R() - .5) * S * .1, y0 = S * .58 + (R() - .5) * S * .1;
      g.strokeStyle = 'rgba(70,48,32,.95)'; g.lineWidth = S / 200 + 1; const x1 = x0 + Math.cos(a) * L, y1 = y0 + Math.sin(a) * L * .8 + L * .25; g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke();
      for (let i = 0; i < 70; i++) { const t = .08 + R() * .92, px = x0 + (x1 - x0) * t, py = y0 + (y1 - y0) * t, side = R() < .5 ? -1 : 1, na = a + side * (.9 + R() * .5), nl = S * (.03 + R() * .03) * (1.1 - t * .4);
        const col = mix(hexc(0x1f3220), hexc(0x52693a), R() * .8 + (1 - t) * .15); g.strokeStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`; g.lineWidth = Math.max(1, S / 300);
        g.beginPath(); g.moveTo(px, py); g.lineTo(px + Math.cos(na) * nl, py + Math.sin(na) * nl + nl * .25); g.stroke(); }
    }
    return { c, alpha: true };
  },
};
/** tileable cloud / variation noise (R: broad fbm, G: finer fbm), linear, shared by the sky, cloud shadows and terrain tint */
export function noiseTexture(S = 256) {
  const d = new Uint8Array(S * S * 4);
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) { const u = x / S, v = y / S, i = (y * S + x) * 4; const a = fbm(u * 4, v * 4, 4, 6, 2, .55), b = fbm(u * 12 + 3, v * 12, 12, 4); d[i] = Math.max(0, Math.min(255, (a - .5) * 1.9 * 255 + 128)); d[i + 1] = Math.max(0, Math.min(255, (b - .5) * 1.7 * 255 + 128)); d[i + 2] = 128; d[i + 3] = 255; }
  const t = new THREE.DataTexture(d, S, S, THREE.RGBAFormat); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true; t.needsUpdate = true; return t;
}
function drawWindow(g, S, o) {
  const w = S * .34, h = S * .46, x = (S - w) / 2, y = S * .2; g.fillStyle = o.frame || '#e8e2d0'; g.fillRect(x - S * .03, y - S * .03, w + S * .06, h + S * .08);
  g.fillStyle = '#1c1e20'; g.fillRect(x, y, w, h); const R = rng(Math.floor(S));
  for (let i = 0; i < 4; i++) { const gx = x + (i % 2) * w / 2 + S * .01, gy = y + Math.floor(i / 2) * h / 2 + S * .01, gw = w / 2 - S * .02, gh = h / 2 - S * .02; const gr = g.createLinearGradient(gx, gy, gx + gw, gy + gh); gr.addColorStop(0, '#5a6a78'); gr.addColorStop(.5, '#26303a'); gr.addColorStop(1, '#3a4652'); g.fillStyle = gr; g.fillRect(gx, gy, gw, gh); if (R() < .35) { g.strokeStyle = 'rgba(220,230,240,.5)'; g.beginPath(); g.moveTo(gx + R() * gw, gy); g.lineTo(gx + R() * gw, gy + gh); g.stroke(); } }
  g.fillStyle = 'rgba(0,0,0,.35)'; g.fillRect(x - S * .03, y + h + S * .04, w + S * .06, S * .02);
  if (o.shutters) { g.fillStyle = o.shutters; g.fillRect(x - S * .2, y - S * .02, S * .15, h + S * .04); g.fillRect(x + w + S * .05, y - S * .02, S * .15, h + S * .04); g.fillStyle = 'rgba(0,0,0,.25)'; for (let k = 0; k < 8; k++) { g.fillRect(x - S * .2, y + k * h / 8, S * .15, 2); g.fillRect(x + w + S * .05, y + k * h / 8, S * .15, 2); } }
}
function sm(a, b, x) { const t = Math.max(0, Math.min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
function rng(s) { let x = s * 99991 + 7; return () => { x = (x * 16807) % 2147483647; return (x - 1) / 2147483646; }; }

export const TEX = {};
/** Generate every texture; quality 0..2 sets resolution. Yields between textures so the loading bar can repaint. */
export async function buildTextures(quality, renderer, onStep) {
  const S = quality >= 2 ? 1024 : quality === 1 ? 512 : 256, aniso = Math.min(8, renderer.capabilities.getMaxAnisotropy());
  const list = [
    ['grass', S, 3], ['stubble', S, 2], ['dirt', S, 4], ['gravel', S, 3], ['cobble', S, 6], ['planks', S / 2, 3], ['concrete', S / 2, 3],
    ['tiles', S / 2, 5], ['thatch', S / 2, 4], ['bark', S / 4, 6], ['pinebark', S / 4, 6], ['needles', S / 2, 3], ['sandbag', S / 2, 6], ['cloth', S / 2, 2], ['water', 256, 3],
    ['leaves', S / 2, 4, {}], ['leavesB', S / 2, 4, { gen: 'leaves', dark: 0x2e3a1c, lite: 0x8a9440, autumn: true }],
    ['plasterWin', S / 2, 3, { gen: 'plaster', win: true, shutters: '#3c5a3a' }], ['plasterWinB', S / 2, 3, { gen: 'plaster', win: true, tint: 0xd6c69a, shutters: '#5a3a28' }],
    ['plasterWinC', S / 2, 3, { gen: 'plaster', win: true, tint: 0xc9cfc8 }], ['plaster', S / 2, 3, { gen: 'plaster', tint: 0xd8cdb2 }],
    ['leafCard', S / 2, 0, { gen: 'leafCard', seed: 21 }], ['birchCard', S / 2, 0, { gen: 'leafCard', seed: 23, dark: 0x3c4a22, lite: 0x9aa64a, len: .045, autumn: true }], ['needleCard', S / 2, 0, { gen: 'needleCard' }],
    ['brickWin', S / 2, 3, { gen: 'brick', win: true }], ['brick', S / 2, 3, { gen: 'brick' }], ['stuccoWin', S / 2, 3, { gen: 'plaster', win: true, tint: 0xb8a68c }],
  ];
  for (let i = 0; i < list.length; i++) {
    const [name, s, str, o] = list[i]; const gen = GEN[(o && o.gen) || name];
    const r = gen(Math.max(64, s | 0), o || {});
    TEX[name] = { map: tex(r.c, true, aniso), normal: r.alpha ? null : tex(normalFrom(r.H, r.S, str * (r.S / 256)), false, aniso) };
    if (onStep) onStep((i + 1) / list.length);
    await new Promise(res => setTimeout(res, 0));
  }
  if (!TEX.noise) TEX.noise = noiseTexture(256);
  return TEX;
}
