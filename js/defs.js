// Fall Weiss web — unit, weapon and class tables (ported from FWCore.cs Defs, with muzzle velocities for real projectiles).
export const SIDE = ['Wehrmacht', 'Polish Army'];
export const SIDE_SHORT = ['GER', 'POL'];

// mv: muzzle velocity m/s (projectile + drop), rof: seconds per shot (bolt cycle for bolt actions), recoil: radians of muzzle climb
export const WEAPONS = {
  kar98k: { name: 'Karabiner 98k', vm: 'rifle', clip: 5, mags: 8, reload: 2.4, rof: 1.15, dmg: 100, spread: .0006, mv: 760, recoil: .045, bolt: true, ads: 40, snd: 'rifle', clipLoad: true },
  wz29:   { name: 'Karabin wz.29', vm: 'rifle', clip: 5, mags: 8, reload: 2.4, rof: 1.15, dmg: 100, spread: .00065, mv: 745, recoil: .045, bolt: true, ads: 40, snd: 'rifle', clipLoad: true },
  mp38:   { name: 'MP 38', vm: 'smg', clip: 32, mags: 5, reload: 2.4, rof: .12, dmg: 45, spread: .006, mv: 380, recoil: .012, auto: true, ads: 46, snd: 'smg' },
  bar28:  { name: 'Rkm wz.28', vm: 'bar', clip: 20, mags: 6, reload: 2.8, rof: .1, dmg: 100, spread: .0028, mv: 853, recoil: .022, auto: true, ads: 45, snd: 'lmg' },
  mg34:   { name: 'MG 34', vm: 'mg', clip: 50, mags: 5, reload: 4.4, rof: .075, dmg: 100, spread: .0035, mv: 755, recoil: .02, auto: true, mg: true, ads: 46, snd: 'mg34' },
  ckm30:  { name: 'Ckm wz.30', vm: 'ckm', clip: 100, mags: 3, reload: 4.6, rof: .095, dmg: 100, spread: .0035, mv: 850, recoil: .018, auto: true, mg: true, ads: 46, snd: 'mg30' },
  p08:    { name: 'Luger P08', vm: 'pistol', clip: 8, mags: 3, reload: 2.1, rof: .28, dmg: 42, spread: .006, mv: 350, recoil: .05, ads: 60, snd: 'pistol' },
  vis:    { name: 'Vis wz.35', vm: 'pistol', clip: 8, mags: 3, reload: 2.0, rof: .3, dmg: 42, spread: .006, mv: 350, recoil: .05, ads: 60, snd: 'pistol' },
  kar98kz:{ name: 'Kar98k ZF 39', vm: 'sniper', clip: 5, mags: 6, reload: 2.7, rof: 1.5, dmg: 110, spread: .0002, mv: 760, recoil: .05, bolt: true, scope: true, ads: 12, snd: 'rifle', clipLoad: true },
  wz29z:  { name: 'Mauser wz.29 (luneta)', vm: 'sniper', clip: 5, mags: 6, reload: 2.7, rof: 1.5, dmg: 110, spread: .0002, mv: 745, recoil: .05, bolt: true, scope: true, ads: 12, snd: 'rifle', clipLoad: true },
  ur35:   { name: 'Karabin ppanc. wz.35 "Ur"', vm: 'rifle', clip: 3, mags: 6, reload: 3.4, rof: 1.8, dmg: 150, spread: .0008, mv: 1275, recoil: .09, bolt: true, ads: 36, snd: 'atr', pen100: 33, penDrop: 6 },
};

// Hand grenades: Stielhandgranate 24 (4.5 s fuse) and the Polish wz.33 (≈3.5 s)
export const GRENADE = [{ name: 'Stielhandgranate 24', fuse: 4.5, kill: 4.5, wound: 11 }, { name: 'Granat wz.33', fuse: 3.6, kill: 4, wound: 12 }];

export const CLASSES = [
  { key: 'rifleman', label: 'Rifleman', type: 'rifle', gren: 2, weps: [['kar98k', 'p08'], ['wz29', 'vis']] },
  { key: 'assault', label: 'Assault', type: 'rifle', gren: 3, weps: [['mp38', 'p08'], ['bar28', 'vis']] },
  { key: 'gunner', label: 'Machine Gunner', type: 'mg', gren: 1, weps: [['mg34', 'p08'], ['ckm30', 'vis']] },
  { key: 'sniper', label: 'Marksman', type: 'sniper', gren: 1, weps: [['kar98kz', 'p08'], ['wz29z', 'vis']] },
  { key: 'atrifle', label: 'AT Rifleman', type: 'rifle', gren: 1, team: 1, weps: [null, ['ur35', 'vis']] },
];

// AI unit stats (FWCore BASE, RO2 ranges). acc is hit chance at ~75 m in the open. speed: m/s at a normal advance with full kit
// (riflemen rush at ×1.45 and charge the last metres at ×1.6; cavalry trot at ×.8 and canter at ×1.2).
export const UNITS = {
  rifle:  { kind: 'inf', hp: 100, speed: 2.2, range: 150, sight: 180, rof: 3.1, acc: .34, dmg: 100, snd: 'rifle' },
  mg:     { kind: 'inf', hp: 100, speed: 1.9, range: 220, sight: 240, rof: .085, burst: 7, pause: 2.4, acc: .2, dmg: 100, snd: 'mg' },
  sniper: { kind: 'inf', hp: 100, speed: 2.1, range: 220, sight: 240, rof: 4.6, acc: .8, dmg: 100, snd: 'rifle' },
  crew:   { kind: 'inf', hp: 100, speed: 2.2, range: 120, sight: 160, rof: 3.3, acc: .3, dmg: 100, snd: 'rifle' },
  cav:    { kind: 'cav', hp: 100, speed: 6.5, range: 80, sight: 140, rof: 3.2, acc: .3, dmg: 100, snd: 'rifle' },
  at:     { kind: 'gun', hp: 60, speed: 0, range: 450, sight: 450, rof: 4.5, acc: .7, dmg: 60, splash: 2.5 },
  tank:   { kind: 'tank', hp: 420, speed: 4.0, range: 400, sight: 380, rof: 3.0, acc: .7, dmg: 70, splash: 4.5 },
};
export const TANK = [
  { name: 'Panzer II Ausf. C', gun: '2 cm KwK 30', hp: 650, armF: 14.5, armS: 14.5, armR: 10, pen100: 23, penDrop: 3, rof: .9, burst: 5, splash: 3.3, dmg: 55, speed: 4.2, snd: 'kwk20' },
  { name: '7TP', gun: '37 mm wz.37', hp: 540, armF: 17, armS: 13, armR: 9, pen100: 45, penDrop: 4, rof: 2.8, burst: 1, splash: 5.2, dmg: 72, speed: 3.7, snd: 'cannon37' },
];
export const ATGUN = [
  { name: '3.7 cm PaK 36', pen100: 38, penDrop: 2, snd: 'cannon37' },
  { name: '37 mm Bofors wz.36', pen100: 45, penDrop: 4, snd: 'cannon37' },
];
export const NAMES = [
  { rifle: 'Schütze', mg: 'MG 34 Team', at: '3.7 cm PaK 36', tank: 'Panzer II', sniper: 'Scharfschütze', cav: 'Kavallerie' },
  { rifle: 'Strzelec', mg: 'Ckm wz.30', at: 'Bofors 37 mm', tank: '7TP', sniper: 'Strzelec wyborowy', cav: 'Ułani' },
];
export const RANKS = [['Schütze', 'Gefreiter', 'Unteroffizier', 'Feldwebel', 'Leutnant'], ['Strzelec', 'Starszy strzelec', 'Kapral', 'Plutonowy', 'Podporucznik']];
export const SURNAMES = [
  ['Becker', 'Hoffmann', 'Wagner', 'Schulz', 'Krüger', 'Lange', 'Vogel', 'Brandt', 'Keller', 'Fischer', 'Wolff', 'Neumann', 'Richter', 'Haas', 'Arnold', 'Dietrich'],
  ['Kowalski', 'Nowak', 'Wiśniewski', 'Zieliński', 'Lewandowski', 'Kamiński', 'Dąbrowski', 'Wójcik', 'Kaczmarek', 'Mazur', 'Krawczyk', 'Piotrowski', 'Grabowski', 'Pawlak', 'Michalski', 'Sikora'],
];

export const G = 9.81;
export function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
export function sm(a, b, x) { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
export function lerp(a, b, t) { return a + (b - a) * t; }
export function angDiff(a, b) { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; }
export function segD(x, z, x1, z1, x2, z2) {
  const dx = x2 - x1, dz = z2 - z1; let l2 = dx * dx + dz * dz; if (l2 === 0) l2 = 1;
  const t = clamp(((x - x1) * dx + (z - z1) * dz) / l2, 0, 1); const px = x1 + dx * t - x, pz = z1 + dz * t - z; return Math.sqrt(px * px + pz * pz);
}
export class Rng { constructor(s) { this.s = (s * 9301 + 49297) % 233280 || 1; } next() { this.s = (this.s * 16807) % 2147483647; return (this.s - 1) / 2147483646; } range(a, b) { return a + (b - a) * this.next(); } pick(a) { return a[Math.floor(this.next() * a.length) % a.length]; } }
export function hex(c) { return '#' + c.toString(16).padStart(6, '0'); }
