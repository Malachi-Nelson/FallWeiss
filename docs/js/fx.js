// Fall Weiss web — particles (smoke, dust, fire, sparks, blood), tracers, craters, a pooled flash light and rain.
// Two point-sprite systems (alpha and additive) keep the whole battlefield at two draw calls for particles.
import * as THREE from './three.module.min.js';

const TINT = { value: new THREE.Color(1, 1, 1) };
function puffTexture() {
  const S = 128, c = document.createElement('canvas'); c.width = c.height = S; const g = c.getContext('2d');
  const gr = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2); gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(.5, 'rgba(255,255,255,.55)'); gr.addColorStop(1, 'rgba(255,255,255,0)'); g.fillStyle = gr; g.fillRect(0, 0, S, S);
  // break the disc up so puffs read as smoke, not balls
  const im = g.getImageData(0, 0, S, S), d = im.data; for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) { const i = (y * S + x) * 4; const n = (Math.sin(x * .21) * Math.cos(y * .17) + Math.sin((x + y) * .09) + Math.random() * .6) * .18 + .82; d[i + 3] *= Math.max(0, Math.min(1, n)); }
  g.putImageData(im, 0, 0); const t = new THREE.CanvasTexture(c); return t;
}

class PointSys {
  constructor(scene, cap, additive, tex) {
    this.cap = cap; this.n = 0; this.p = new Float32Array(cap * 3); this.v = new Float32Array(cap * 3); this.life = new Float32Array(cap); this.max = new Float32Array(cap); this.s0 = new Float32Array(cap); this.s1 = new Float32Array(cap);
    this.col = new Float32Array(cap * 4); this.a0 = new Float32Array(cap); this.drag = new Float32Array(cap); this.grav = new Float32Array(cap); this.size = new Float32Array(cap); this.rgba = new Float32Array(cap * 4);
    const g = new THREE.BufferGeometry(); this.pa = new THREE.BufferAttribute(this.p, 3); this.sa = new THREE.BufferAttribute(this.size, 1); this.ca = new THREE.BufferAttribute(this.rgba, 4);
    for (const a of [this.pa, this.sa, this.ca]) a.setUsage(THREE.DynamicDrawUsage); g.setAttribute('position', this.pa); g.setAttribute('size', this.sa); g.setAttribute('rgba', this.ca); g.setDrawRange(0, 0);
    this.uniforms = { tex: { value: tex }, scale: { value: 600 }, fogColor: { value: new THREE.Color() }, fogNear: { value: 100 }, fogFar: { value: 900 }, tint: TINT };
    const m = new THREE.ShaderMaterial({ uniforms: this.uniforms, transparent: true, depthWrite: false, blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      vertexShader: `attribute float size; attribute vec4 rgba; varying vec4 vC; varying float vF; uniform float scale; void main(){ vC = rgba; vec4 mv = modelViewMatrix * vec4(position,1.0); gl_Position = projectionMatrix * mv; gl_PointSize = min(512.0, size * scale / -mv.z); vF = -mv.z; }`,
      fragmentShader: `uniform sampler2D tex; uniform vec3 fogColor, tint; uniform float fogNear, fogFar; varying vec4 vC; varying float vF; void main(){ vec4 t = texture2D(tex, gl_PointCoord); float f = smoothstep(fogNear, fogFar, vF); vec4 c = vec4(vC.rgb, vC.a * t.a); ${additive ? 'c.rgb *= c.a * (1.0 - f);' : 'c.rgb = mix(c.rgb * tint * (0.85 + 0.3 * (1.0 - t.a)), fogColor, f*0.85);'} gl_FragColor = c; }` });
    this.pts = new THREE.Points(g, m); this.pts.frustumCulled = false; this.pts.renderOrder = additive ? 3 : 2; scene.add(this.pts); this.g = g;
  }
  emit(x, y, z, vx, vy, vz, life, s0, s1, r, g, b, a, drag = 1, grav = 0) {
    if (this.n >= this.cap) return; const i = this.n++; this.p.set([x, y, z], i * 3); this.v.set([vx, vy, vz], i * 3); this.life[i] = 0; this.max[i] = life; this.s0[i] = s0; this.s1[i] = s1; this.col.set([r, g, b, a], i * 4); this.drag[i] = drag; this.grav[i] = grav;
  }
  update(dt) {
    for (let i = 0; i < this.n; i++) {
      this.life[i] += dt; if (this.life[i] >= this.max[i]) { const j = --this.n; if (i !== j) { for (const [arr, k] of [[this.p, 3], [this.v, 3], [this.col, 4]]) for (let q = 0; q < k; q++) arr[i * k + q] = arr[j * k + q]; this.life[i] = this.life[j]; this.max[i] = this.max[j]; this.s0[i] = this.s0[j]; this.s1[i] = this.s1[j]; this.drag[i] = this.drag[j]; this.grav[i] = this.grav[j]; } i--; continue; }
      const t = this.life[i] / this.max[i], d = Math.pow(this.drag[i], dt * 60);
      this.v[i * 3] *= d; this.v[i * 3 + 1] = this.v[i * 3 + 1] * d - this.grav[i] * dt; this.v[i * 3 + 2] *= d;
      this.p[i * 3] += this.v[i * 3] * dt; this.p[i * 3 + 1] += this.v[i * 3 + 1] * dt; this.p[i * 3 + 2] += this.v[i * 3 + 2] * dt;
      this.size[i] = this.s0[i] + (this.s1[i] - this.s0[i]) * Math.sqrt(t);
      const fade = t < .1 ? t / .1 : 1 - (t - .1) / .9; this.rgba[i * 4] = this.col[i * 4]; this.rgba[i * 4 + 1] = this.col[i * 4 + 1]; this.rgba[i * 4 + 2] = this.col[i * 4 + 2]; this.rgba[i * 4 + 3] = this.col[i * 4 + 3] * fade;
    }
    this.g.setDrawRange(0, this.n); this.pa.needsUpdate = this.sa.needsUpdate = this.ca.needsUpdate = true;
  }
  clear() { this.n = 0; this.g.setDrawRange(0, 0); }
}

export class Fx {
  constructor(scene, quality) {
    this.scene = scene; this.q = quality; const tex = puffTexture();
    this.smoke = new PointSys(scene, [700, 1400, 2400][quality], false, tex); this.glow = new PointSys(scene, [300, 600, 1000][quality], true, tex);
    this.tr = []; const tg = new THREE.BufferGeometry(); this.trPos = new Float32Array(600 * 6); this.trCol = new Float32Array(600 * 6); tg.setAttribute('position', new THREE.BufferAttribute(this.trPos, 3).setUsage(THREE.DynamicDrawUsage)); tg.setAttribute('color', new THREE.BufferAttribute(this.trCol, 3).setUsage(THREE.DynamicDrawUsage));
    this.trLines = new THREE.LineSegments(tg, new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false })); this.trLines.frustumCulled = false; scene.add(this.trLines); this.tg = tg;
    this.light = new THREE.PointLight(0xffb060, 0, 30, 1.6); scene.add(this.light); this.lightT = 0;
    this.craterMat = new THREE.MeshBasicMaterial({ color: 0x1a140e, transparent: true, opacity: .75, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -4 }); this.craters = []; this.craterGeo = new THREE.CircleGeometry(1, 12); this.craterGeo.rotateX(-Math.PI / 2);
    this.emitters = []; this.rain = null; this.windX = .6; this.windZ = .2;
  }
  setFog(c, n, f) { for (const s of [this.smoke, this.glow]) { s.uniforms.fogColor.value.copy(c); s.uniforms.fogNear.value = n; s.uniforms.fogFar.value = f; } }
  /** smoke and dust pick up the colour of the light: warm at dawn, grey under rain */
  setTint(c) { TINT.value.copy(c); }
  setScale(h) { this.smoke.uniforms.scale.value = this.glow.uniforms.scale.value = h * .9; }
  flash(x, y, z, power = 1) { if (power >= this.light.intensity / 40) { this.light.position.set(x, y, z); this.light.intensity = 40 * power; this.light.distance = 12 + power * 18; this.lightT = .05 + power * .05; } }
  muzzle(x, y, z, dx, dy, dz, big = 1) {
    for (let i = 0; i < 2 + big * 2; i++) { const s = Math.random() * .6 * big; this.glow.emit(x + dx * s, y + dy * s, z + dz * s, dx * 4, dy * 4, dz * 4, .05 + Math.random() * .04, .35 * big, .7 * big, 1, .7, .35, .9); }
    this.smoke.emit(x + dx * .5, y + dy * .5, z + dz * .5, dx * 2 + this.windX * .5, .4, dz * 2 + this.windZ * .5, 1.2 + big, .3 * big, 1.8 * big, .75, .73, .7, .22 * big, .96);
    this.flash(x, y, z, .35 * big);
  }
  tracer(x, y, z, dx, dy, dz, speed, len, color = [1, .55, .25], life = 1.2) { if (this.tr.length < 600) this.tr.push({ x, y, z, dx, dy, dz, speed, len, c: color, t: 0, life }); }
  impact(x, y, z, mat, nx = 0, ny = 1, nz = 0) {
    const col = mat === 'dirt' ? [.42, .34, .24] : mat === 'stone' ? [.62, .6, .55] : mat === 'wood' ? [.45, .35, .25] : mat === 'water' ? [.8, .85, .9] : mat === 'flesh' ? [.4, .06, .04] : [.5, .48, .45];
    const n = mat === 'flesh' ? 3 : 5; for (let i = 0; i < n; i++) this.smoke.emit(x, y, z, nx * 2 + (Math.random() - .5) * 2, ny * 2 + Math.random() * 2.5, nz * 2 + (Math.random() - .5) * 2, .5 + Math.random() * .6, .15, mat === 'water' ? .9 : .7, col[0], col[1], col[2], mat === 'flesh' ? .8 : .55, .93, mat === 'water' ? 6 : 2);
    if (mat === 'metal' || mat === 'stone') for (let i = 0; i < 3; i++) this.glow.emit(x, y, z, (Math.random() - .5) * 8, Math.random() * 6, (Math.random() - .5) * 8, .15, .06, .03, 1, .8, .4, 1, .95, 9);
  }
  explosion(x, y, z, size = 1, ground = true) {
    for (let i = 0; i < 6 * size; i++) this.glow.emit(x, y + .5, z, (Math.random() - .5) * 10 * size, Math.random() * 8 * size, (Math.random() - .5) * 10 * size, .25 + Math.random() * .25, 1.2 * size, 3.5 * size, 1, .55, .2, 1, .9);
    for (let i = 0; i < 14 * size; i++) this.smoke.emit(x + (Math.random() - .5) * 2 * size, y + Math.random() * size, z + (Math.random() - .5) * 2 * size, (Math.random() - .5) * 6 * size + this.windX, 2 + Math.random() * 7 * size, (Math.random() - .5) * 6 * size + this.windZ, 3 + Math.random() * 4 * size, 1.5 * size, 7 * size, .3, .27, .24, .85, .97, -.3);
    if (ground) { for (let i = 0; i < 18 * size; i++) this.smoke.emit(x, y + .3, z, (Math.random() - .5) * 9 * size, 6 + Math.random() * 14 * size, (Math.random() - .5) * 9 * size, 1 + Math.random(), .3, .9, .3, .23, .16, .95, .99, 15);
      if (this.craters.length > 40) { const c = this.craters.shift(); this.scene.remove(c); } const c = new THREE.Mesh(this.craterGeo, this.craterMat); c.position.set(x, y + .06, z); c.scale.setScalar(1.3 * size + Math.random() * .4); this.scene.add(c); this.craters.push(c); }
    this.flash(x, y + 1.5, z, 1.5 * size);
  }
  dust(x, y, z, amt = 1) { this.smoke.emit(x + (Math.random() - .5) * 2, y + .4, z + (Math.random() - .5) * 2, this.windX + (Math.random() - .5), .6 + Math.random(), this.windZ + (Math.random() - .5), 2.5 + Math.random() * 2, .8, 4 * amt, .62, .56, .44, .35 * amt, .97); }
  /** continuous emitter (burning building, wreck): returns handle */
  fire(x, y, z, big) { const e = { x, y, z, big, t: 0 }; this.emitters.push(e); return e; }
  setRain(on, intensity, camera) {
    if (on && !this.rain) { const N = [800, 1500, 2500][this.q]; const g = new THREE.BufferGeometry(); const p = new Float32Array(N * 6); g.setAttribute('position', new THREE.BufferAttribute(p, 3)); this.rain = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: 0xaab4c0, transparent: true, opacity: .35, depthWrite: false })); this.rain.frustumCulled = false; this.scene.add(this.rain); this.rainN = N;
      for (let i = 0; i < N; i++) { const x = (Math.random() - .5) * 40, y = Math.random() * 20, z = (Math.random() - .5) * 40; p.set([x, y, z, x - .1, y + .9, z - .05], i * 6); } }
    if (this.rain) { this.rain.visible = on && intensity > .02; this.rain.material.opacity = .35 * intensity; this.rainI = intensity; }
  }
  update(dt, cam) {
    this.smoke.update(dt); this.glow.update(dt);
    if (this.lightT > 0) { this.lightT -= dt; if (this.lightT <= 0) this.light.intensity = 0; else this.light.intensity *= .8; }
    // tracers
    let k = 0; for (let i = 0; i < this.tr.length; i++) { const t = this.tr[i]; t.t += dt; if (t.t > t.life) { this.tr.splice(i--, 1); continue; } const d = t.speed * t.t; const hx = t.x + t.dx * d, hy = t.y + t.dy * d - 4.9 * t.t * t.t * .3, hz = t.z + t.dz * d; const L = Math.min(t.len, d);
      this.trPos.set([hx, hy, hz, hx - t.dx * L, hy - t.dy * L, hz - t.dz * L], k * 6); const f = 1 - t.t / t.life; this.trCol.set([t.c[0] * f * 2, t.c[1] * f * 2, t.c[2] * f * 2, t.c[0] * f * .2, t.c[1] * f * .2, t.c[2] * f * .2], k * 6); k++; }
    this.tg.setDrawRange(0, k * 2); this.tg.attributes.position.needsUpdate = this.tg.attributes.color.needsUpdate = true;
    // fires: flame flicker + tall smoke columns that drift with the wind (they read for kilometres)
    for (const e of this.emitters) { e.t -= dt; if (e.t > 0) continue; e.t = .09 + Math.random() * .08; const s = e.big ? 1.5 : 1;
      this.glow.emit(e.x + (Math.random() - .5) * 3 * s, e.y, e.z + (Math.random() - .5) * 3 * s, 0, 2 + Math.random() * 2, 0, .5 + Math.random() * .4, 1.2 * s, 2.4 * s, 1, .5, .15, .8);
      this.smoke.emit(e.x + (Math.random() - .5) * 2 * s, e.y + 2, e.z + (Math.random() - .5) * 2 * s, this.windX * 2.5, 3 + Math.random() * 2, this.windZ * 2.5, 9 + Math.random() * 6, 2.5 * s, 14 * s, .16, .15, .14, .6, .995, -.05); }
    if (this.rain && this.rain.visible) { const p = this.rain.geometry.attributes.position.array; const fall = 18 * dt; for (let i = 0; i < this.rainN; i++) { let o = i * 6; p[o + 1] -= fall; p[o + 4] -= fall; p[o] -= fall * .08; p[o + 3] -= fall * .08;
        if (p[o + 1] < cam.position.y - 6 || Math.abs(p[o] - cam.position.x) > 22 || Math.abs(p[o + 2] - cam.position.z) > 22) { const x = cam.position.x + (Math.random() - .5) * 40, y = cam.position.y + 6 + Math.random() * 12, z = cam.position.z + (Math.random() - .5) * 40; p[o] = x; p[o + 1] = y; p[o + 2] = z; p[o + 3] = x - .08; p[o + 4] = y + .9; p[o + 5] = z - .04; } }
      this.rain.geometry.attributes.position.needsUpdate = true; }
  }
  clear() { this.smoke.clear(); this.glow.clear(); this.tr.length = 0; this.emitters.length = 0; for (const c of this.craters) this.scene.remove(c); this.craters.length = 0; if (this.rain) { this.scene.remove(this.rain); this.rain = null; } }
}
