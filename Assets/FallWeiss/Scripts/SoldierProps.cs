// Fall Weiss — small weapon props built from the POLYGON pack's bullet prefabs: loose rounds, 5-round stripper clips
// and ejected casings / empty clips (short-lived rigidbodies).
using UnityEngine;

namespace FallWeiss
{
    public static class SoldierProps
    {
        /// Instance of a bullet prefab re-oriented so its long axis is +Z and scaled to `length` metres.
        public static GameObject Make(GameObject prefab, float length, string name)
        {
            var wrap = new GameObject(name);
            if (!prefab) { var p = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.Destroy(p.GetComponent<Collider>()); p.transform.SetParent(wrap.transform, false); p.transform.localScale = new Vector3(.012f, length * .5f, .012f); p.transform.localRotation = Quaternion.Euler(90, 0, 0); return wrap; }
            var g = Object.Instantiate(prefab); g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); g.transform.localScale = Vector3.one;
            foreach (var c in g.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var b = new Bounds(); bool first = true;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            var s = b.size; float lng = Mathf.Max(s.x, Mathf.Max(s.y, s.z)); if (lng < 1e-5f) lng = 1;
            Quaternion toZ = s.z >= s.x && s.z >= s.y ? Quaternion.identity : s.y >= s.x ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(0, -90, 0);
            g.transform.SetParent(wrap.transform, false); float k = length / lng;
            g.transform.localScale = Vector3.one * k; g.transform.localRotation = toZ; g.transform.localPosition = -(toZ * (b.center * k));
            return wrap;
        }

        /// Stripper clip: 5 rounds side by side along local +Y (bottom round at y = 0), pointing +Z, steel strip at the base.
        public static GameObject Clip(SoldierLib lib)
        {
            var clip = new GameObject("StripperClip");
            for (int i = 0; i < 5; i++) { var r = Make(lib ? lib.roundPrefab : null, .08f, "r" + i); r.transform.SetParent(clip.transform, false); r.transform.localPosition = new Vector3(0, i * .011f, 0); }
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(strip.GetComponent<Collider>()); strip.name = "strip";
            strip.transform.SetParent(clip.transform, false); strip.transform.localPosition = new Vector3(0, .022f, -.036f); strip.transform.localScale = new Vector3(.014f, .058f, .006f);
            strip.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(.18f, .18f, .17f), .5f);
            return clip;
        }

        const int MaxLive = 60;
        /// Ejected casing (no collider: it must never block shot line-casts); cleans itself up.
        public static void Eject(GameObject prefab, float length, Vector3 pos, Quaternion rot, Vector3 vel, float life = 4f)
        {
            if (CasingMotion.live >= MaxLive) return;
            var g = Make(prefab, length, "Casing"); g.transform.SetPositionAndRotation(pos, rot); EjectObject(g, vel, life);
        }
        public static void EjectObject(GameObject g, Vector3 vel, float life = 5f)
        {
            g.transform.SetParent(null, true); var m = g.AddComponent<CasingMotion>(); m.vel = vel; m.spin = Random.insideUnitSphere * 900; m.life = life;
        }
    }
}
