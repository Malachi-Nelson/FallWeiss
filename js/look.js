// Fall Weiss web — the look: aerial perspective (height fog + valley mist + sun in-scatter), sky-lit image-based lighting,
// and a war-film grade pass (ACES, split toning, sun glare, vignette, grain).
import * as THREE from './three.module.min.js';

// ---------- 1. aerial perspective, patched into every lit material (shared uniforms → one update per frame)
export const FOGU = { fogSunDir: { value: new THREE.Vector3(0, 1, 0) }, fogSunCol: { value: new THREE.Color(1, .9, .7) }, fogP: { value: new THREE.Vector4(.0016, .045, 0, 1) } };
// fogP: x density, y height falloff, z mist base height, w mist strength
export function patchFog(mat) {
  if (!mat || mat.userData.fwFog || !mat.fog) return; mat.userData.fwFog = true;
  const prev = mat.onBeforeCompile, prevKey = mat.customProgramCacheKey ? mat.customProgramCacheKey.call(mat) : '';
  mat.onBeforeCompile = (sh, r) => {
    if (prev) prev(sh, r);
    Object.assign(sh.uniforms, FOGU);
    sh.vertexShader = sh.vertexShader.replace('#include <fog_pars_vertex>', '#include <fog_pars_vertex>\nvarying vec3 vFogW;')
      .replace('#include <fog_vertex>', `#include <fog_vertex>
#ifdef USE_INSTANCING
 vFogW = (modelMatrix * instanceMatrix * vec4(transformed, 1.0)).xyz;
#else
 vFogW = (modelMatrix * vec4(transformed, 1.0)).xyz;
#endif`);
    sh.fragmentShader = sh.fragmentShader.replace('#include <fog_pars_fragment>', '#include <fog_pars_fragment>\nvarying vec3 vFogW; uniform vec3 fogSunDir; uniform vec3 fogSunCol; uniform vec4 fogP;')
      .replace('#include <fog_fragment>', `#ifdef USE_FOG
 { vec3 V = vFogW - cameraPosition; float dist = max(length(V), 0.001); vec3 dir = V / dist;
   float k = fogP.y, h0 = max(cameraPosition.y - fogP.z, 0.0), h1 = max(vFogW.y - fogP.z, 0.0);
   float integ = abs(h1 - h0) > 0.05 ? (exp(-k * h0) - exp(-k * h1)) / (k * (h1 - h0)) : exp(-k * h0);
   float haze = 1.0 - exp(-fogP.x * dist * (0.35 + integ * fogP.w));
   float lin = smoothstep(fogNear, fogFar, vFogDepth);
   float f = clamp(max(haze, lin), 0.0, 1.0);
   float s = max(dot(dir, normalize(fogSunDir)), 0.0);
   vec3 fc = fogColor * (0.92 + 0.08 * dir.y) + fogSunCol * (pow(s, 6.0) * 0.55 + pow(s, 40.0) * 0.6);
   gl_FragColor.rgb = mix(gl_FragColor.rgb, fc, f); }
#endif`);
  };
  mat.customProgramCacheKey = () => prevKey + '|fwfog1'; mat.needsUpdate = true;
}
export function patchScene(scene) { scene.traverse(o => { const m = o.material; if (Array.isArray(m)) m.forEach(patchFog); else if (m && (m.isMeshStandardMaterial || m.isMeshLambertMaterial || m.isMeshBasicMaterial)) patchFog(m); }); }

// ---------- 2. image-based light from the live sky (re-baked when the time of day moves on)
export class SkyLight {
  constructor(renderer) { this.r = renderer; this.pm = new THREE.PMREMGenerator(renderer); this.rt = null; this.scene = new THREE.Scene(); }
  bake(skyMesh, groundColor) {
    const s = this.scene; s.clear(); const sky = skyMesh.clone(); sky.position.set(0, 0, 0); s.add(sky);
    // a ground hemisphere so the env isn't lit from below by sky: warm earth bounce
    const g = new THREE.Mesh(new THREE.SphereGeometry(500, 16, 8, 0, Math.PI * 2, Math.PI / 2, Math.PI / 2), new THREE.MeshBasicMaterial({ color: groundColor, side: THREE.BackSide })); s.add(g);
    const old = this.rt; this.rt = this.pm.fromScene(s, .04, .1, 1000); if (old) old.dispose(); g.geometry.dispose(); return this.rt.texture;
  }
}

// ---------- 3. grade pass
export class Post {
  constructor(renderer, q) {
    this.r = renderer; this.q = q; const size = renderer.getDrawingBufferSize(new THREE.Vector2());
    this.rt = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType, samples: q >= 2 ? 4 : 0, depthBuffer: true });
    this.u = { tDiffuse: { value: this.rt.texture }, exposure: { value: 1.1 }, sunPos: { value: new THREE.Vector3(.5, .5, 0) }, sunCol: { value: new THREE.Color(1, .85, .6) }, time: { value: 0 }, sat: { value: .86 }, aspect: { value: 1 }, supp: { value: 0 }, wet: { value: 0 } };
    this.mat = new THREE.ShaderMaterial({ uniforms: this.u, depthTest: false, depthWrite: false,
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }',
      fragmentShader: `precision highp float; uniform sampler2D tDiffuse; uniform float exposure, time, sat, aspect, supp, wet; uniform vec3 sunPos, sunCol; varying vec2 vUv;
vec3 aces(vec3 x){ const mat3 m1 = mat3(0.59719,0.07600,0.02840, 0.35458,0.90834,0.13383, 0.04823,0.01566,0.83777); const mat3 m2 = mat3(1.60475,-0.10208,-0.00327, -0.53108,1.10813,-0.07276, -0.07367,-0.00605,1.07602);
 vec3 v = m1 * x; vec3 a = v * (v + 0.0245786) - 0.000090537; vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081; return clamp(m2 * (a / b), 0.0, 1.0); }
float h(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233)) + time) * 43758.5453); }
void main(){
 vec3 c = texture2D(tDiffuse, vUv).rgb * exposure;
 // sun glare: soft bloom-like halo + streak when the sun is on screen
 vec2 d = vUv - sunPos.xy; d.x *= aspect; float L = length(d);
 float vis = sunPos.z; c += sunCol * vis * (exp(-L * 7.0) * 0.35 + exp(-L * 24.0) * 0.6 + exp(-abs(d.y) * 160.0) * exp(-abs(d.x) * 2.5) * 0.08);
 c = aces(c);
 // war-film grade: muted colour, warm highlights, cool-green shadows, gentle S-curve
 float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
 float s = sat * (1.0 - supp * 0.65) * (1.0 - wet * 0.15); c = mix(vec3(lum), c, s);
 c += mix(vec3(-0.012, 0.0, 0.016), vec3(0.03, 0.012, -0.022), smoothstep(0.15, 0.75, lum));
 c = c * c * (3.0 - 2.0 * c) * 0.32 + c * 0.68;
 // vignette + fine grain
 vec2 q = vUv - 0.5; q.x *= aspect; c *= 1.0 - dot(q, q) * (0.55 + supp * 0.8);
 c += (h(vUv * 917.0) - 0.5) * 0.014;
 gl_FragColor = vec4(pow(max(c, 0.0), vec3(1.0 / 2.2)), 1.0);
}` });
    this.quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.mat); this.quad.frustumCulled = false; this.scene = new THREE.Scene(); this.scene.add(this.quad); this.cam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  }
  resize() { const s = this.r.getDrawingBufferSize(new THREE.Vector2()); this.rt.setSize(s.x, s.y); this.u.aspect.value = s.x / s.y; }
  begin() { this.r.setRenderTarget(this.rt); this.r.clear(); }
  end() { this.r.setRenderTarget(null); this.r.render(this.scene, this.cam); }
  dispose() { this.rt.dispose(); }
}

// ---------- 4. grass & stubble tufts around the camera (instanced, wind-swayed, placed from the terrain's splat map)
function bladeTexture() {
  const S = 128, c = document.createElement('canvas'); c.width = c.height = S; const g = c.getContext('2d');
  for (let i = 0; i < 110; i++) { const x = 4 + Math.random() * (S - 8), h = S * (.3 + Math.random() * .65), lean = (Math.random() - .5) * 30, w = .8 + Math.random() * 1.6;
    const gr = g.createLinearGradient(0, S, 0, S - h); const k = Math.random(); gr.addColorStop(0, `rgb(${95 + k * 30},${105 + k * 30},${50 + k * 10})`); gr.addColorStop(1, `rgb(${185 + k * 50},${180 + k * 45},${110 + k * 30})`);
    g.fillStyle = gr; g.beginPath(); g.moveTo(x - w, S); g.quadraticCurveTo(x + lean * .3, S - h * .6, x + lean, S - h); g.quadraticCurveTo(x + lean * .3 + w * .4, S - h * .6, x + w, S); g.fill(); }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.generateMipmaps = true; return t;
}
export class Grass {
  constructor(scene, world, q) {
    this.w = world; this.R = [0, 30, 44][q]; this.cell = [0, .85, .68][q]; this.on = q > 0 && !world.M.city; if (!this.on) return;
    const geo = new THREE.BufferGeometry(), P = [], U = [], N = [], I = [];
    for (let k = 0; k < 3; k++) { const a = k * Math.PI / 3, cx = Math.cos(a) * .32, cz = Math.sin(a) * .32, o = P.length / 3;
      P.push(-cx, 0, -cz, cx, 0, cz, cx, .42, cz, -cx, .42, -cz); U.push(0, 0, 1, 0, 1, 1, 0, 1); for (let j = 0; j < 4; j++) N.push(0, 1, 0); I.push(o, o + 1, o + 2, o, o + 2, o + 3); }
    geo.setAttribute('position', new THREE.Float32BufferAttribute(P, 3)); geo.setAttribute('uv', new THREE.Float32BufferAttribute(U, 2)); geo.setAttribute('normal', new THREE.Float32BufferAttribute(N, 3)); geo.setIndex(I);
    this.time = { value: 0 };
    const mat = new THREE.MeshStandardMaterial({ map: bladeTexture(), alphaTest: .45, side: THREE.DoubleSide, roughness: .95, emissive: 0x1a1c0c });
    mat.onBeforeCompile = sh => { sh.uniforms.gtime = this.time; sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nuniform float gtime;')
      .replace('#include <begin_vertex>', `#include <begin_vertex>
 vec3 ip = vec3(instanceMatrix[3][0], 0.0, instanceMatrix[3][2]); float wv = sin(gtime * 1.7 + ip.x * 0.35 + ip.z * 0.21) + 0.5 * sin(gtime * 3.1 + ip.x * 0.9);
 transformed.x += wv * uv.y * 0.09; transformed.z += wv * uv.y * 0.05;`); };
    mat.customProgramCacheKey = () => 'fwgrass1';
    this.max = Math.round(Math.PI * this.R * this.R / (this.cell * this.cell) * .85);
    this.mesh = new THREE.InstancedMesh(geo, mat, this.max); this.mesh.frustumCulled = false; this.mesh.receiveShadow = true; this.mesh.count = 0; scene.add(this.mesh); this.mat = mat;
    this.cx = 1e9; this.cz = 1e9; const spl = world.terrain.geometry.attributes.splat.array; this.spl = spl;
  }
  splat(x, z) { const w = this.w, s = w.step, N = w.inN, fx = (x + w.inE) / s, fz = (z + w.inE) / s; const i = Math.round(fx), j = Math.round(fz); if (i < 0 || j < 0 || i >= N || j >= N) return null; const k = (j * N + i) * 4; return [this.spl[k], this.spl[k + 1], this.spl[k + 2], this.spl[k + 3]]; }
  update(cam, dt) {
    if (!this.on) return; this.time.value += dt; const x = cam.position.x, z = cam.position.z; if (Math.hypot(x - this.cx, z - this.cz) < 5) return; this.cx = x; this.cz = z;
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), sc = new THREE.Vector3(), p = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0), col = new THREE.Color(); const c = this.cell, R = this.R; let n = 0;
    const i0 = Math.floor((x - R) / c), i1 = Math.floor((x + R) / c), j0 = Math.floor((z - R) / c), j1 = Math.floor((z + R) / c);
    for (let i = i0; i <= i1 && n < this.max; i++) for (let j = j0; j <= j1 && n < this.max; j++) {
      const h1 = Math.sin(i * 127.1 + j * 311.7) * 43758.5453 % 1, h2 = Math.sin(i * 269.5 + j * 183.3) * 43758.5453 % 1; const px = (i + .5 + h1 * .45) * c, pz = (j + .5 + h2 * .45) * c; const d = Math.hypot(px - x, pz - z); if (d > R) continue;
      const s = this.splat(px, pz); if (!s) continue; const bare = s[0] + s[1] + s[2]; if (bare > .45 || Math.abs(h1 * 7.7 % 1) < bare * 1.6) continue; const stub = s[3];
      if (this.w.WL != null && this.w.Hraw(px, pz) < this.w.WL + .2) continue; if (this.w.trenchD(px, pz) < 2.2) continue;
      const f = 1 - Math.max(0, (d - R * .7) / (R * .3)); const k = (.55 + Math.abs(h2) * .7) * f * (stub > .4 ? .6 : 1);
      p.set(px, this.w.Hraw(px, pz) - .03, pz); q.setFromAxisAngle(up, h1 * 6.28); sc.set(k * (1 + stub * .3), k * (stub > .4 ? .7 : 1 + Math.abs(h1) * .5), k); m.compose(p, q, sc); this.mesh.setMatrixAt(n, m);
      if (stub > .4) col.setRGB(1.35, 1.15, .72); else col.setRGB(1.0 + Math.abs(h2) * .35, 1.08 + Math.abs(h1) * .2, .78); this.mesh.setColorAt(n, col); n++;
    }
    this.mesh.count = n; this.mesh.instanceMatrix.needsUpdate = true; if (this.mesh.instanceColor) this.mesh.instanceColor.needsUpdate = true;
  }
}
