// Fall Weiss — faithful port of game.js decorate() / spiceList() / groundPatches(): every map-specific prop type
// (lamps, pipes, trains, trams, boats, stone walls, shrines, bushes, flowers, rubble, barricades, fountains, telegraph
// lines, dead rails, buffers, signals, water towers, dead locomotives, derailed wagons, piers…).
// The decor list is exported raw in three.js space, so all maths below is written in three.js coordinates (z3) exactly
// like the original and converted on emit: unity z = -z3, euler (rx, ry, rz) -> (-rx, -ry, rz).
using System.Collections.Generic;
using UnityEngine;

namespace FallWeiss
{
    public static partial class World
    {
        static MeshBatch dmb;
        static Quaternion Q3(float rx, float ry, float rz) =>
            Quaternion.AngleAxis(-rx * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-ry * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward);
        static Vector3 V3(float x, float y, float z3) => new Vector3(x, y, -z3);
        // SolParts equivalents (three.js space)
        static void Bx(float w, float h, float d, float x, float y, float z, int hex, float rx = 0, float ry = 0, float rz = 0) => dmb.PBox(V3(x, y, z), new Vector3(w, h, d), Q3(rx, ry, rz), FU.Hex(hex));
        static void Cy(float rt, float rb, float h, float x, float y, float z, int hex, float rx = 0, float ry = 0, float rz = 0) => dmb.PCyl(V3(x, y, z), rt, rb, h, Q3(rx, ry, rz), FU.Hex(hex));
        static void Cyz(float r, float len, float x, float y, float z, int hex) => Cy(r, r, len, x, y, z, hex, Mathf.PI / 2);
        static void Sp(float sx, float sy, float sz, float x, float y, float z, int hex) => dmb.PSph(V3(x, y, z), new Vector3(sx, sy, sz), Quaternion.identity, FU.Hex(hex));
        static void Ico(int hex, float x, float y, float z, float sx, float sy, float sz, float rx, float ry, float rz) => dmb.PIco(V3(x, y, z), new Vector3(sx, sy, sz), Q3(rx, ry, rz), FU.Hex(hex));
        static void LineCyl(float r, float x1, float y1, float z1, float x2, float y2, float z2, int hex)
        {
            Vector3 a = V3(x1, y1, z1), b = V3(x2, y2, z2), d = b - a; float len = d.magnitude; if (len < .01f) return;
            dmb.PCyl((a + b) / 2, r, r, len, Quaternion.FromToRotation(Vector3.up, d / len), FU.Hex(hex), 6);
        }
        static float Gy(float x, float z3) => H(x, -z3);
        static bool Okp(float x, float z3, float m) => !InBuilding(x, -z3, m) && !InRiver(x, -z3, 0) && !InWater(x, -z3);
        static bool NearRoad3(float x, float z3, float m) => NearRoad(x, -z3, m);
        static Obst Solid3(float x0, float x1, float z0, float z1, float h, float hp, bool low)
        {   // collider box given in three space
            var b = AddObst(x0, x1, -z1, -z0, h, low, hp); b.y0 = H((b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2); b.y1 = b.y0 + h; return b;
        }
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Rnd(float a, float b) => FU.Rand(a, b);

        static void DecorPort(MeshBatch mb, Rng R)
        {
            dmb = mb; var list = new List<FDecor>(M.decor ?? new FDecor[0]); list.AddRange(SpiceList(R));
            int[] wallC = { 0x8a857a, 0x7d786d, 0x938e82 };
            float wl = WL ?? 0;
            foreach (var dd in list)
            {
                string t = dd.k; var d = dd.v; float V(int i) => i < d.Length ? d[i] : 0;
                switch (t)
                {
                    case "fence":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), L = FU.Hyp(x2 - x1, z2 - z1), ang = Mathf.Atan2(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / 3));
                            for (int i = 0; i <= n; i++) { float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n); if (Okp(x, z, 1.5f)) Bx(.12f, 1.15f, .12f, x, Gy(x, z) + .55f, z, 0x5a4630); }
                            for (int i = 0; i < n; i++)
                            {
                                float xa = Lerp(x1, x2, i / (float)n), za = Lerp(z1, z2, i / (float)n), xb = Lerp(x1, x2, (i + 1) / (float)n), zb = Lerp(z1, z2, (i + 1) / (float)n), mx = (xa + xb) / 2, mz = (za + zb) / 2; if (!Okp(mx, mz, 1.5f)) continue;
                                foreach (var hh in new[] { .4f, .85f }) Bx(.05f, .07f, FU.Hyp(xb - xa, zb - za), mx, (Gy(xa, za) + Gy(xb, zb)) / 2 + hh, mz, 0x7a6448, 0, ang, 0);
                            }
                            break;
                        }
                    case "hedge":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), L = FU.Hyp(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / 2.2f)); int[] cs = { 0x3d5a2c, 0x486332, 0x35502a };
                            for (int i = 0; i <= n; i++) { float x = Lerp(x1, x2, i / (float)n) + Rnd(-.3f, .3f), z = Lerp(z1, z2, i / (float)n) + Rnd(-.3f, .3f); if (!Okp(x, z, 1) || NearRoad3(x, z, 1.5f)) continue; float r = Rnd(.8f, 1.15f); Sp(r, r * .75f, r, x, Gy(x, z) + .55f, z, cs[i % 3]); }
                            break;
                        }
                    case "rocks":
                        {
                            float cx = V(0), cz = V(1), rad = V(2); int n = (int)V(3); int[] cs = { 0x77746a, 0x6a685f, 0x85827a };
                            for (int i = 0; i < n; i++) { float a = R.Next() * 6.28f, dd2 = Mathf.Sqrt(R.Next()) * rad, x = cx + Mathf.Cos(a) * dd2, z = cz + Mathf.Sin(a) * dd2; if (!Okp(x, z, 2) || NearRoad3(x, z, 2)) continue; float s = Rnd(.7f, 2.1f); Ico(cs[i % 3], x, Gy(x, z) + s * .25f, z, s * Rnd(.9f, 1.4f), s * .7f, s * Rnd(.9f, 1.4f), Rnd(0, 3), Rnd(0, 3), Rnd(0, 3)); }
                            break;
                        }
                    case "crates":
                        {
                            float cx = V(0), cz = V(1); int n = (int)V(2); int[] cs = { 0x7a6038, 0x6b5530, 0x5e4a2c };
                            for (int i = 0; i < n; i++) { float x = cx + Rnd(-4, 4), z = cz + Rnd(-4, 4); if (!Okp(x, z, 1.5f)) continue; float y = Gy(x, z), s = Rnd(1, 1.5f); Bx(s, s, s, x, y + s / 2, z, cs[i % 3], 0, Rnd(0, 3)); if (R.Next() < .4f) Bx(s * .8f, s * .8f, s * .8f, x, y + s * 1.4f, z, 0x7a6038, 0, Rnd(0, 3)); }
                            break;
                        }
                    case "barrels":
                        {
                            float cx = V(0), cz = V(1); int n = (int)V(2); int[] cs = { 0x3d5a3a, 0x7a2a22, 0x2a2a2a, 0x5a5a60 };
                            for (int i = 0; i < n; i++) { float x = cx + Rnd(-3, 3), z = cz + Rnd(-3, 3); if (!Okp(x, z, 1.5f)) continue; Cy(.36f, .36f, .95f, x, Gy(x, z) + .48f, z, cs[i % 4]); Cy(.37f, .37f, .06f, x, Gy(x, z) + .7f, z, 0x1a1a1a); }
                            break;
                        }
                    case "lamps":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), step = Mathf.Max(1, V(4)), L = FU.Hyp(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / step));
                            for (int i = 0; i <= n; i++)
                            {
                                float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n); if (!Okp(x, z, 2)) continue; float y = Gy(x, z), o = x1 < 0 ? 1 : -1;
                                Cy(.09f, .12f, 6.4f, x, y + 3.2f, z, 0x2a2c2e); Bx(1.4f, .1f, .1f, x + o * .7f, y + 6.3f, z, 0x2a2c2e); Bx(.5f, .22f, .5f, x + o * 1.3f, y + 6.15f, z, 0xd8c88a);
                            }
                            break;
                        }
                    case "pipes":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), hgt = V(4), L = FU.Hyp(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / 8));
                            float yA = Gy(x1, z1) + hgt, yB = Gy(x2, z2) + hgt; LineCyl(.55f, x1, yA, z1, x2, yB, z2, 0x6a6e70); LineCyl(.3f, x1, yA + .9f, z1, x2, yB + .9f, z2, 0x7a3a2a);
                            for (int i = 0; i <= n; i++) { float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n), y = Gy(x, z); Bx(.35f, hgt, .35f, x, y + hgt / 2, z, 0x4a4a4c); }
                            break;
                        }
                    case "train":
                        {
                            float cx = V(0), cz = V(1), y = Gy(cx, cz) + .9f;
                            for (int i = 0; i < 4; i++)
                            {
                                float x = cx + (i - 1.5f) * 12.4f; Bx(11, 2.8f, 3.1f, x, y + 1.8f, cz, i == 0 ? 0x2a2a2a : 0x3c4a3a); Bx(11.4f, .3f, 3.3f, x, y + 3.35f, cz, 0x2a2e2a);
                                for (int w = 0; w < 5; w++) Bx(.5f, 1.4f, 3.3f, x - 4.4f + w * 2.2f, y + .35f, cz, 0x1a1a1a);
                                if (i > 0) { Bx(3, 1.6f, 3, x + 1.5f, y + 4.2f, cz, 0x34402f); Cyz(.14f, 3.2f, x + 3.6f, y + 4.4f, cz, 0x1a1a1a); }
                            }
                            Cy(1.35f, 1.35f, 8, cx - 18.6f, y + 2.6f, cz, 0x2a2a2a, 0, 0, Mathf.PI / 2); Bx(3.2f, 3.4f, 3.1f, cx - 13.6f, y + 2.6f, cz, 0x2a2a2a); Cy(.5f, .5f, 3, cx - 22, y + 4.7f, cz, 0x1a1a1a);
                            Solid3(cx - 25, cx + 25, cz - 1.7f, cz + 1.7f, 4.6f, 1000, false);
                            break;
                        }
                    case "tram":
                        {
                            float cx = V(0), cz = V(1), y = Gy(cx, cz); bool along = V(2) != 0; float hw = along ? 5.6f : 1.7f, hd = along ? 1.7f : 5.6f;
                            if (!Okp(cx, cz, 3)) break;
                            Bx(along ? 11 : 3.1f, 3, along ? 3.1f : 11, cx, y + 2, cz, 0x8a7a4a); Bx(along ? 11.2f : 3.2f, .3f, along ? 3.2f : 11.2f, cx, y + 3.65f, cz, 0x4a4438);
                            Bx(along ? 10 : 3.2f, .8f, along ? 3.2f : 10, cx, y + 2.4f, cz, 0x2a343d); Bx(along ? 11 : 3.2f, .3f, along ? 3.2f : 11, cx, y + .45f, cz, 0x2a2a2a);
                            Solid3(cx - hw, cx + hw, cz - hd, cz + hd, 4, 500, false);
                            break;
                        }
                    case "boat":
                        {
                            float cx = V(0), cz = V(1), rot = V(2), y = (WL.HasValue ? wl : Gy(cx, cz)) + .15f;
                            Bx(1.5f, .6f, 5, cx, y + .2f, cz, 0x6a4a2c, 0, rot); Bx(1.2f, .12f, 4.4f, cx, y + .55f, cz, 0x8a6a42, 0, rot); Bx(.2f, 1.6f, .2f, cx + Mathf.Sin(rot) * .4f, y + 1.2f, cz + Mathf.Cos(rot) * .4f, 0x4a3a2a);
                            break;
                        }
                    case "stonewall":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), hh = d.Length > 4 && V(4) > 0 ? V(4) : 1.3f, L = FU.Hyp(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / 2));
                            float sl = L / n, ang = Mathf.Atan2(x2 - x1, z2 - z1), sA = Mathf.Abs(Mathf.Sin(ang)), cA = Mathf.Abs(Mathf.Cos(ang));
                            for (int i = 0; i < n; i++)
                            {
                                float mx = Lerp(x1, x2, (i + .5f) / n), mz = Lerp(z1, z2, (i + .5f) / n); if (!Okp(mx, mz, 1.3f) || NearRoad3(mx, mz, -1)) continue; float y = Gy(mx, mz);
                                Bx(.75f, hh + .3f, sl + .1f, mx, y + hh / 2 - .05f, mz, wallC[(int)(R.Next() * 3) % 3], 0, ang); Bx(.98f, .16f, sl + .1f, mx, y + hh + .1f, mz, 0x6f6a60, 0, ang);
                                Solid3(mx - sA * sl / 2 - .45f, mx + sA * sl / 2 + .45f, mz - cA * sl / 2 - .45f, mz + cA * sl / 2 + .45f, hh, 0, hh < 2);
                            }
                            break;
                        }
                    case "shrine":
                        {
                            float x = V(0), z = V(1); if (!Okp(x, z, 2)) break; float y = Gy(x, z);
                            Bx(1, .55f, 1, x, y + .27f, z, 0x8a847a); Bx(.28f, 2.7f, .28f, x, y + 1.7f, z, 0x5a3e28); Bx(1.1f, .24f, .24f, x, y + 2.35f, z, 0x5a3e28); Bx(.95f, .14f, .6f, x, y + 3.1f, z, 0x4a3828, .25f);
                            break;
                        }
                    case "bushes":
                        {
                            float cx = V(0), cz = V(1), rad = V(2); int n = (int)V(3); int[] cs = { 0x3b5a2a, 0x4a6a32, 0x35502a, 0x56703a };
                            for (int i = 0; i < n; i++)
                            {
                                float a = R.Next() * 6.28f, dd2 = Mathf.Sqrt(R.Next()) * rad, x = cx + Mathf.Cos(a) * dd2, z = cz + Mathf.Sin(a) * dd2; if (!Okp(x, z, 1.5f) || NearRoad3(x, z, 1.5f) || !ClearOfKey(x, -z, 12)) continue;
                                for (int j = 0; j < 3; j++) { float r = Rnd(.5f, 1), ox = Rnd(-.7f, .7f), oz = Rnd(-.7f, .7f); Sp(r, r * .8f, r, x + ox, Gy(x, z) + r * .55f, z + oz, cs[(i + j) % 4]); }
                            }
                            break;
                        }
                    case "flowers":
                        {
                            float cx = V(0), cz = V(1), rad = V(2); int n = (int)V(3); int[] cs = { 0xf2e8c8, 0xe8c840, 0xc83a3a, 0x8a5ac8, 0xf0a0c0 };
                            for (int i = 0; i < n; i++)
                            {
                                float a = R.Next() * 6.28f, dd2 = Mathf.Sqrt(R.Next()) * rad, x = cx + Mathf.Cos(a) * dd2, z = cz + Mathf.Sin(a) * dd2; if (!Okp(x, z, 1) || NearRoad3(x, z, 1) || TrenchD(x, -z) < 3) continue;
                                float y = Gy(x, z); Bx(.03f, .4f, .03f, x, y + .2f, z, 0x4f7a32); Bx(.15f, .1f, .15f, x, y + .43f, z, cs[i % 5]);
                            }
                            break;
                        }
                    case "rubble":
                        {
                            float cx = V(0), cz = V(1), rad = V(2); int n = (int)V(3); int[] cs = { 0x77716a, 0x8a8278, 0x5a544c, 0x9a8f84 };
                            for (int i = 0; i < n; i++)
                            {
                                float a = R.Next() * 6.28f, dd2 = Mathf.Sqrt(R.Next()) * rad, x = cx + Mathf.Cos(a) * dd2, z = cz + Mathf.Sin(a) * dd2; if (!Okp(x, z, 1.2f) || NearRoad3(x, z, -2)) continue;
                                float s = Rnd(.4f, 1.5f); Bx(s, s * Rnd(.4f, .9f), s * Rnd(.8f, 1.2f), x, Gy(x, z) + s * .3f, z, cs[i % 4], Rnd(-.4f, .4f), Rnd(0, 3), Rnd(-.4f, .4f));
                            }
                            break;
                        }
                    case "barricade":
                        {
                            float bx = V(0), bz = V(1), len = V(2), rot = V(3); if (!Okp(bx, bz, 1.2f)) break; float y = Gy(bx, bz), c = Mathf.Cos(rot), s = Mathf.Sin(rot);
                            Bx(len, 1.1f, .5f, bx, y + .55f, bz, 0x6a5a40, 0, rot); Bx(len * .94f, .2f, .7f, bx, y + 1.15f, bz, 0x7a6848, 0, rot);
                            foreach (var e in new[] { -1f, 1f }) Bx(1.8f, .9f, 1.3f, bx + c * e * len * .55f, y + .45f, bz - s * e * len * .55f, 0x8a7a55, 0, rot);
                            Cy(.38f, .38f, .95f, bx + c * len * .2f, y + 1.7f, bz - s * len * .2f, 0x7a2a22); Bx(len * .8f, .12f, .12f, bx, y + .9f, bz + .38f, 0x4a3a28, 0, rot, .35f);
                            float ac = Mathf.Abs(c), asn = Mathf.Abs(s);
                            Solid3(bx - ac * (len / 2 + 1) - asn * .8f, bx + ac * (len / 2 + 1) + asn * .8f, bz - asn * (len / 2 + 1) - ac * .8f, bz + asn * (len / 2 + 1) + ac * .8f, 1.2f, 0, true);
                            break;
                        }
                    case "fountain":
                        {
                            float x = V(0), z = V(1); if (!Okp(x, z, 3)) break; float y = Gy(x, z);
                            Cy(3.5f, 3.7f, .9f, x, y + .45f, z, 0x8a857a); Cy(3.1f, 3.1f, .12f, x, y + .86f, z, 0x3d6f8f); Cy(.6f, .8f, 2.2f, x, y + 1.6f, z, 0x9a958a); Cy(1.5f, 1.5f, .25f, x, y + 2.6f, z, 0x9a958a); Cy(.25f, .3f, .8f, x, y + 2.95f, z, 0x9a958a);
                            Solid3(x - 3.7f, x + 3.7f, z - 3.7f, z + 3.7f, 1, 0, true);
                            break;
                        }
                    case "telegraph":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), step = Mathf.Max(1, V(4)), L = FU.Hyp(x2 - x1, z2 - z1); int n = Mathf.Max(1, Mathf.RoundToInt(L / step)); Vector3? prev = null;
                            for (int i = 0; i <= n; i++)
                            {
                                float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n); if (!Okp(x, z, 2) || NearRoad3(x, z, -1)) { prev = null; continue; }
                                float y = Gy(x, z); Cy(.1f, .14f, 7, x, y + 3.5f, z, 0x4a3a28); Bx(1.9f, .12f, .12f, x, y + 6.6f, z, 0x4a3a28);
                                if (prev.HasValue) foreach (var o in new[] { -.8f, .8f }) LineCyl(.018f, prev.Value.x + o, prev.Value.y, prev.Value.z, x + o, y + 6.5f, z, 0x1a1a1a);
                                prev = new Vector3(x, y + 6.45f, z);
                            }
                            break;
                        }
                    case "deadrail":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), L = FU.Hyp(x2 - x1, z2 - z1); if (L < .5f) break; int n = Mathf.Max(2, Mathf.RoundToInt(L / 1.6f));
                            float ang = Mathf.Atan2(x2 - x1, z2 - z1), ux = (x2 - x1) / L, uz = (z2 - z1) / L, px = uz, pz = -ux;
                            int nb = Mathf.Max(1, Mathf.CeilToInt(L / 4)); // ballast ribbon
                            for (int i = 0; i < nb; i++) { float t0 = (i + .5f) / nb, x = Lerp(x1, x2, t0), z = Lerp(z1, z2, t0); Bx(3.8f, .08f, L / nb + .05f, x, Gy(x, z) + .1f, z, 0x544d42, 0, ang); }
                            int[] tc = { 0x4a3a2a, 0x3c2f22, 0x55432e }, rc = { 0x6a3e2a, 0x7a4a30, 0x5a3a2c }, gc = { 0x6a7a3a, 0x8a8040, 0x5a6a30 };
                            for (int i = 0; i <= n; i++) { float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n); if (R.Next() < .08f) continue; Bx(2.5f, .13f, .28f, x, Gy(x, z) + .1f, z, tc[i % 3], 0, ang + Rnd(-.06f, .06f)); }
                            foreach (var o in new[] { -.72f, .72f })
                                for (int i = 0; i < n; i++)
                                {
                                    if (R.Next() < .1f) continue; float xa = Lerp(x1, x2, i / (float)n), za = Lerp(z1, z2, i / (float)n), xb = Lerp(x1, x2, (i + 1) / (float)n), zb = Lerp(z1, z2, (i + 1) / (float)n), mx = (xa + xb) / 2 + px * o, mz = (za + zb) / 2 + pz * o;
                                    Bx(.1f, .16f, FU.Hyp(xb - xa, zb - za) + .04f, mx, Gy(mx, mz) + .22f, mz, rc[i % 3], 0, ang);
                                }
                            for (int i = 0; i < n * 1.5f; i++) { float k = R.Next() * n, x = Lerp(x1, x2, k / n) + Rnd(-2.4f, 2.4f), z = Lerp(z1, z2, k / n) + Rnd(-2.4f, 2.4f), h = Rnd(.3f, .85f); Bx(.08f, h, .08f, x, Gy(x, z) + h / 2, z, gc[i % 3], Rnd(-.3f, .3f), Rnd(0, 3), Rnd(-.3f, .3f)); }
                            break;
                        }
                    case "buffer":
                        {
                            float x = V(0), z = V(1), rot = V(2), y = Gy(x, z), c = Mathf.Cos(rot), s = Mathf.Sin(rot);
                            Sp(2.2f, .6f, 3.2f, x - c * 1.2f, y + .2f, z + s * 1.2f, 0x4a4a32); foreach (var o in new[] { -.9f, .9f }) Bx(.28f, 1.2f, .28f, x + s * o, y + .7f, z + c * o, 0x3a2e26); Bx(.35f, .4f, 2.4f, x, y + 1.2f, z, 0x6a3a2a, 0, rot);
                            break;
                        }
                    case "signal":
                        {
                            float x = V(0), z = V(1), y = Gy(x, z); Cy(.09f, .12f, 5.6f, x, y + 2.8f, z, 0x3a3a38); Bx(.1f, .9f, .7f, x + .35f, y + 5.1f, z, 0x7a3a2a, 0, 0, .4f); Bx(.45f, .7f, .4f, x, y + 4.4f, z, 0x2a2a2a);
                            break;
                        }
                    case "watertower":
                        {
                            float x = V(0), z = V(1), y = Gy(x, z);
                            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) Bx(.35f, 7, .35f, x + sx * 1.7f, y + 3.5f, z + sz * 1.7f, 0x5a3a2c, sz * .06f, 0, -sx * .06f);
                            Cy(2.7f, 2.7f, 3.4f, x, y + 8.4f, z, 0x6a4a3a); Cy(.1f, 2.9f, 1.2f, x, y + 10.7f, z, 0x4a3a30); Bx(.3f, 4.2f, .3f, x + 3, y + 7.5f, z, 0x4a4a4c); Bx(3, .3f, .3f, x + 1.6f, y + 9.6f, z, 0x4a4a4c);
                            Solid3(x - 2, x + 2, z - 2, z + 2, 9, 0, false);
                            break;
                        }
                    case "deadloco":
                        {
                            float x = V(0), z = V(1), rot = V(2), y = Gy(x, z) + .5f, c = Mathf.Cos(rot), s = Mathf.Sin(rot);
                            Vector2 W(float u, float v) => new Vector2(x + u * c + v * s, z - u * s + v * c);
                            var q = W(-1.2f, 0); Cy(1.15f, 1.15f, 6.5f, q.x, y + 2, q.y, 0x2a2420, 0, rot, Mathf.PI / 2);
                            q = W(3.1f, 0); Bx(2.6f, 3, 2.8f, q.x, y + 2.5f, q.y, 0x3a2e26, 0, rot); Bx(3, .2f, 3.2f, q.x, y + 4.1f, q.y, 0x22201e, 0, rot);
                            q = W(-3.9f, 0); Cy(.45f, .65f, 1.7f, q.x, y + 3.6f, q.y, 0x1c1a18); q = W(.2f, 0); Cy(.5f, .5f, .8f, q.x, y + 3.4f, q.y, 0x5a3a2c);
                            foreach (var u in new[] { -3f, -1.2f, .8f, 3.2f }) foreach (var v in new[] { -1.2f, 1.2f }) { q = W(u, v); Bx(1.4f, 1.4f, .25f, q.x, y + .5f, q.y, 0x5a3a2c, 0, rot); }
                            float hx = Mathf.Abs(s) > .7f ? 1.7f : 5.2f, hz = Mathf.Abs(s) > .7f ? 5.2f : 1.7f; Solid3(x - hx, x + hx, z - hz, z + hz, 4.2f, 0, false);
                            break;
                        }
                    case "derailed":
                        {
                            float x = V(0), z = V(1), rot = V(2), tilt = V(3), y = Gy(x, z);
                            Bx(12, 3.4f, 3.2f, x, y + 1.7f, z, 0x5a3626, 0, rot, tilt); Bx(12.2f, .3f, 3.4f, x, y + 3.5f, z, 0x2a2a2a, 0, rot, tilt); Bx(.3f, 2.4f, 1.6f, x, y + 1.5f, z, 0x2a1c14, 0, rot, tilt);
                            for (int i = 0; i < 2; i++) Bx(1.2f, 1.2f, .28f, x + Rnd(-5, 5), y + .55f, z + Rnd(-3, 3), 0x4a3a2c, Rnd(-.3f, .3f), Rnd(0, 3), Rnd(0, 1));
                            bool al = Mathf.Abs(Mathf.Sin(rot)) > .7f; Solid3(x - (al ? 2.4f : 6.2f), x + (al ? 2.4f : 6.2f), z - (al ? 6.2f : 2.4f), z + (al ? 6.2f : 2.4f), 3.6f, 0, false);
                            break;
                        }
                    case "pier":
                        {
                            float x1 = V(0), z1 = V(1), x2 = V(2), z2 = V(3), L = FU.Hyp(x2 - x1, z2 - z1), ang = Mathf.Atan2(x2 - x1, z2 - z1), y = wl + 1.1f; int n = Mathf.RoundToInt(L / 1.2f);
                            for (int i = 0; i < n; i++)
                            {
                                float x = Lerp(x1, x2, i / (float)n), z = Lerp(z1, z2, i / (float)n); Bx(3.2f, .14f, 1.1f, x, y, z, i % 2 == 1 ? 0x6a5236 : 0x5e4830, 0, ang);
                                if (i % 5 == 0) { Cy(.16f, .16f, 3, x + Mathf.Cos(ang) * 1.6f, y - 1.4f, z - Mathf.Sin(ang) * 1.6f, 0x3a2e22); Cy(.16f, .16f, 3, x - Mathf.Cos(ang) * 1.6f, y - 1.4f, z + Mathf.Sin(ang) * 1.6f, 0x3a2e22); }
                            }
                            break;
                        }
                }
                if (mb.Verts > 55000) Flush(mb, "Decor");
            }
        }

        /// Port of spiceList(): extra seeded variety (city rubble + street barricades, or bushes, flowers, shrines,
        /// garden walls and telegraph lines in the countryside). Returned in three.js space like M.decor.
        static List<FDecor> SpiceList(Rng R)
        {
            var L = new List<FDecor>(); float Hh = M.half; float rr(float a, float b) => a + R.Next() * (b - a);
            void Add(string k, params float[] v) => L.Add(new FDecor { k = k, v = v });
            bool ok(float x, float z) => ClearOfKey(x, -z, 20) && !InBuilding(x, -z, 4) && !NearRoad(x, -z, 3) && !InRiver(x, -z, 4) && !InWater(x, -z) && TrenchD(x, -z) > 4;
            var roads = new List<float[]>(); foreach (var r in M.roads) roads.Add(new[] { r.x1, -r.z1, r.x2, -r.z2, r.w });
            List<Vector3> roadPts(int n, float off)
            {
                var rs = roads.FindAll(r => FU.Hyp(r[2] - r[0], r[3] - r[1]) > 60); var o = new List<Vector3>();
                for (int i = 0; i < n && rs.Count > 0; i++)
                {
                    var r = rs[Mathf.Min(rs.Count - 1, (int)(R.Next() * rs.Count))]; float t = rr(.12f, .88f), dx = r[2] - r[0], dz = r[3] - r[1], l = FU.Hyp(dx, dz), side = R.Next() < .5f ? -1 : 1;
                    o.Add(new Vector3(r[0] + dx * t - dz / l * side * (r[4] / 2 + off), r[1] + dz * t + dx / l * side * (r[4] / 2 + off), Mathf.Atan2(dx, dz)));
                }
                return o;
            }
            if (M.city)
            {
                for (int i = 0; i < 16; i++) { float x = rr(-Hh + 20, Hh - 20), z = rr(-Hh + 20, Hh - 20); if (ok(x, z)) Add("rubble", x, z, rr(2, 4), Mathf.Round(rr(7, 13))); }
                foreach (var p in roadPts(5, 3.5f)) if (ClearOfKey(p.x, -p.y, 24)) Add("barricade", p.x, p.y, 7, p.z + 1.2f);
                foreach (var ln in M.lanes)
                {
                    float l0 = ln.x1, l1 = -ln.z1, l2 = ln.x2, l3 = -ln.z2, dx = l2 - l0, dz = l3 - l1, len = Mathf.Max(.01f, FU.Hyp(dx, dz));
                    for (int k = 0; k < 3; k++)
                    {
                        float t = rr(.2f, .8f), sg = k % 2 == 1 ? 1 : -1, x = l0 + dx * t + dz / len * (ln.w / 2 + 2) * sg, z = l1 + dz * t - dx / len * (ln.w / 2 + 2) * sg;
                        if (ClearOfKey(x, -z, 22) && Mathf.Abs(x) < Hh - 10 && Mathf.Abs(z) < Hh - 10) Add("rubble", x, z, 2.5f, 8);
                    }
                }
            }
            else
            {
                for (int i = 0; i < 26; i++) { float x = rr(-Hh, Hh), z = rr(-Hh, Hh); if (ok(x, z)) Add("bushes", x, z, rr(4, 9), Mathf.Round(rr(3, 7))); }
                for (int i = 0; i < 14; i++) { float x = rr(-Hh, Hh), z = rr(-Hh, Hh); if (ok(x, z) && Slope(x, -z) < .25f) Add("flowers", x, z, rr(5, 11), Mathf.Round(rr(25, 55))); }
                foreach (var p in roadPts(3, 3.5f)) Add("shrine", p.x, p.y);
                var houses = new List<FBuild>(); foreach (var b in M.build) if (b.k == "house" || b.k == "barn" || b.k == "villa") houses.Add(b);
                for (int i = houses.Count - 1; i > 0; i--) { int j = (int)(R.Next() * (i + 1)) % (i + 1); var tmp = houses[i]; houses[i] = houses[j]; houses[j] = tmp; }
                for (int i = 0; i < Mathf.Min(5, houses.Count); i++) { var b = houses[i]; float bz = -b.z; Add("stonewall", b.x - 6, bz + 10, b.x + 6, bz + 10, 1.3f); }
                foreach (var r in roads)
                    if (FU.Hyp(r[2] - r[0], r[3] - r[1]) > 120 && R.Next() < .6f)
                    {
                        float dx = r[2] - r[0], dz = r[3] - r[1], l = FU.Hyp(dx, dz), o = r[4] / 2 + 4;
                        Add("telegraph", r[0] - dz / l * o + dx * .05f, r[1] + dx / l * o + dz * .05f, r[2] - dz / l * o - dx * .05f, r[3] + dx / l * o - dz * .05f, 34);
                    }
            }
            return L;
        }

        /// Port of groundPatches(): soft-coloured dirt / grass / rubble discs breaking up the ground.
        static void GroundPatches(MeshBatch mb, Rng R)
        {
            int N = M.city ? 46 : 90, n = 0; float Hh = M.half;
            int[] pal = M.city ? new[] { 0x4b4741, 0x7d776c, 0x39362f, 0x8c8578, 0x5f574a } : new[] { 0x765f3a, 0x4d3f2a, 0x8d8050, 0x5c7a38, 0x6a6a52, 0x9a8a5e };
            for (int i = 0; i < N * 4 && n < N; i++)
            {
                float x = (R.Next() - .5f) * 2 * Hh, z = (R.Next() - .5f) * 2 * Hh; if (InRiver(x, z, 1) || InWater(x, z) || Slope(x, z) > .3f || InBuilding(x, z, 0)) continue;
                float r = 2 + R.Next() * 6, rot = R.Next() * 6.28f, sx = r * (.7f + R.Next() * .8f), sz = r * (.5f + R.Next() * .6f); var c = FU.Hex(pal[(int)(R.Next() * pal.Length) % pal.Length]);
                mb.PDisc(x, z, sx, sz, rot, c, H); n++;
            }
        }
    }
}
