// Fall Weiss — POLYGON soldier model on an infantry unit, with a procedural upper body:
//  * legs: the pack's Idle / Walk clips (Animator) + leg IK for crouching
//  * upper body: chest / head turn and pitch toward the aim point; the gun (the pack's own rigged weapon, with its
//    moving bolt / magazine / feed cover / rocket parts) is placed in ready, aimed, sprint and reload poses and both
//    hands are put on it with two-bone IK, using grip points baked from the pack's aim poses (SoldierLib.rigs)
//  * actions: spring recoil, K98 bolt cycling with casing ejection, stripper-clip and single-round K98 loading,
//    magazine changes (MP40 / StG 44 / Luger), MG 42 feed-cover belt change, Panzerschreck rocket loading
//  * first person: the same body and gun, positioned on the camera (head collapsed), replacing the old viewmodel
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FallWeiss
{
    public enum ReloadKind { None, Mag, Stripper, Single }

    [DefaultExecutionOrder(1000)]
    public class SoldierVisual : MonoBehaviour
    {
        // ---------- inputs, written every frame by Unit / Game ----------
        [System.NonSerialized] public float speed, crouch, ads, reloadT;
        [System.NonSerialized] public bool sprint, raise, hasAim, dead, singles, hideFP, moving;
        [System.NonSerialized] public Vector3 aimPoint;
        [System.NonSerialized] public ReloadKind reload;
        public bool firstPerson; Transform cam;

        public Animator anim; public SoldierModel model; public string weapon; public WeaponRig rig; public Renderer[] rends;
        Renderer body; SkinnedMeshRenderer gunMesh;
        Transform pelvis, chest, neck, head, hipL, hipR; readonly Transform[] armL = new Transform[3], armR = new Transform[3], legL = new Transform[3], legR = new Transform[3];
        Transform gun; readonly Dictionary<string, Transform> handles = new Dictionary<string, Transform>(); readonly Dictionary<string, Vector3> handleScale = new Dictionary<string, Vector3>();
        Transform[] parts; int iBolt = -1, iMag = -1, iCover = -1, iRocket = -1, iToggle = -1;
        string cls;   // bolt, smg, mg, pistol, launcher
        static readonly int pSpeed = Animator.StringToHash("Speed");

        float wAds, wSprint, wReload, wCrouch, yawS, pitchS;
        float rp, rpV, rr, rrV, rYaw, stab, stabV;           // recoil / bayonet springs
        float cycleT = -1, boltLift, boltSlide, openT = -1, closeT = -1; bool boltHeld, rocketLoaded = true, wasReload;
        Quaternion lastCam = Quaternion.identity; Vector2 sway, swayV; float bobT;
        GameObject clipProp, roundProp; bool clipGone, roundGone;
        ReloadKind lastKind; int lastRound;

        // ================= creation / weapon =================
        public static SoldierVisual Create(Transform parent, SoldierModel m, string wep)
        {
            var go = Instantiate(m.prefab, parent, false); go.name = "Soldier_" + m.name;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var sv = go.AddComponent<SoldierVisual>(); sv.model = m; sv.Init(); sv.SetWeapon(wep); return sv;
        }

        Transform B(string n) { foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

        void Init()
        {
            anim = GetComponentInChildren<Animator>(); anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            pelvis = B("Bone"); chest = B("Bone.001"); neck = B("Bone.024"); head = B("Bone.025"); hipL = B("Bone.014"); hipR = B("Bone.015");
            armL[0] = B("Bone.004"); armL[1] = B("Bone.006"); armL[2] = B("Bone.010");
            armR[0] = B("Bone.005"); armR[1] = B("Bone.007"); armR[2] = B("Bone.011");
            legL[0] = B("Bone.016"); legL[1] = B("Bone.018"); legL[2] = B("Bone.020");
            legR[0] = B("Bone.017"); legR[1] = B("Bone.019"); legR[2] = B("Bone.021");
            // the weapon handle bones leave the hand: they hang off a free gun pivot that we place every frame
            gun = new GameObject("GunPivot").transform; gun.SetParent(transform, false);
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Weapon_handle") && !t.name.EndsWith("_end")) { handleScale[t.name] = t.lossyScale; handles[t.name] = t; }
            foreach (var kv in handles) { kv.Value.SetParent(gun, false); kv.Value.localScale = handleScale[kv.Key]; }
            anim.Rebind();
            if (armL[2]) lHandRest = armL[2].localRotation;   // bind pose of the off hand (relaxed, in line with the forearm)
            rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) if (r.name == "Jacket") body = r;
            if (model != null && model.bareSkin) foreach (var r in rends) if (r is SkinnedMeshRenderer && r.sharedMaterial && r.sharedMaterial.name.ToLower().Contains("skin")) r.sharedMaterial = model.bareSkin;
            // rest pose of every bone we drive: the pack's clips don't key all of them (constant curves are stripped), so
            // without a reset each frame the procedural offsets would accumulate
            var list = new List<Transform> { pelvis, chest, neck, head, hipL, hipR }; list.AddRange(armL); list.AddRange(armR); list.AddRange(legL); list.AddRange(legR);
            list.Add(B("Bone.002")); list.Add(B("Bone.003"));
            fingL = B("Bone.012"); fingR = B("Bone.013"); thumbL = B("Bone.008"); thumbR = B("Bone.009"); list.Add(fingL); list.Add(fingR); list.Add(thumbL); list.Add(thumbR);
            driven = list.FindAll(t => t).ToArray(); restPos = new Vector3[driven.Length]; restRot = new Quaternion[driven.Length];
            for (int i = 0; i < driven.Length; i++) { restPos[i] = driven[i].localPosition; restRot[i] = driven[i].localRotation; }
        }
        Transform[] driven; Vector3[] restPos; Quaternion[] restRot; Transform fingL, fingR, thumbL, thumbR;
        static readonly Vector3 curlAxis = Vector3.left; const float curlF = 75, curlT = 30;   // finger / thumb curl about the bones' -X (closes toward the palm)

        /// RO2-style hit zones on the visible skeleton (bone +Y runs along each bone; the rig is scaled x29, so each
        /// collider sits on an unscaled child and is sized in metres).
        /// Hit zones on the visible skeleton, sized from the model's own mesh: every skinned vertex is credited to the
        /// nearest zone bone up its skin hierarchy (hands to the forearm, feet to the shin, neck to the chest), and each
        /// zone box spans the 4th-96th percentile of its vertices plus 1.5 cm. Any soldier model gets hitboxes that match
        /// its silhouette; a bone with too little geometry (or an unreadable mesh) keeps the default size from the table.
        public void AddHitZones(Unit u)
        {
            var fit = FitZones();
            Zone(u, head, HitZone.Head, new Vector3(0, .11f, .02f), Vector3.zero, .13f, fit);
            Zone(u, chest, HitZone.Chest, new Vector3(0, .22f, .01f), new Vector3(.40f, .30f, .26f), 0, fit);
            Zone(u, pelvis, HitZone.Stomach, new Vector3(0, .15f, 0), new Vector3(.36f, .30f, .24f), 0, fit);
            Zone(u, armL[0], HitZone.Arm, new Vector3(0, .15f, 0), new Vector3(.11f, .30f, .11f), 0, fit); Zone(u, armR[0], HitZone.Arm, new Vector3(0, .15f, 0), new Vector3(.11f, .30f, .11f), 0, fit);
            Zone(u, armL[1], HitZone.Arm, new Vector3(0, .2f, 0), new Vector3(.09f, .40f, .09f), 0, fit); Zone(u, armR[1], HitZone.Arm, new Vector3(0, .2f, 0), new Vector3(.09f, .40f, .09f), 0, fit);
            Zone(u, legL[0], HitZone.Leg, new Vector3(0, .16f, 0), new Vector3(.15f, .34f, .15f), 0, fit); Zone(u, legR[0], HitZone.Leg, new Vector3(0, .16f, 0), new Vector3(.15f, .34f, .15f), 0, fit);
            Zone(u, legL[1], HitZone.Leg, new Vector3(0, .21f, 0), new Vector3(.12f, .42f, .12f), 0, fit); Zone(u, legR[1], HitZone.Leg, new Vector3(0, .21f, 0), new Vector3(.12f, .42f, .12f), 0, fit);
        }
        static void Zone(Unit u, Transform bone, HitZone z, Vector3 c, Vector3 size, float r, Dictionary<Transform, Bounds> fit)
        {
            if (!bone) return; var ls = bone.lossyScale.x; if (ls < 1e-5f) ls = 1; bool fitted = false;
            if (fit != null && fit.TryGetValue(bone, out var b))
            {   // metres in the bone's frame; a head becomes the sphere around its box, limbs keep a sane minimum thickness
                c = b.center; fitted = true;
                if (r > 0) r = Mathf.Clamp(Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * .92f, .09f, .16f);
                else size = new Vector3(Mathf.Max(.07f, b.size.x), Mathf.Max(.07f, b.size.y), Mathf.Max(.07f, b.size.z));
            }
            var g = new GameObject("Hit_" + z); g.transform.SetParent(bone, false); g.transform.localPosition = c / ls; g.transform.localScale = Vector3.one / ls;
            if (r > 0) { var sc = g.AddComponent<SphereCollider>(); sc.radius = r; sc.isTrigger = true; }
            else { var bc = g.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = true; }
            var h = g.AddComponent<UnitHit>(); h.u = u; h.zone = z; h.head = z == HitZone.Head; h.fitted = fitted;
        }
        static readonly Dictionary<string, Dictionary<string, Bounds>> fitCache = new Dictionary<string, Dictionary<string, Bounds>>();
        Dictionary<Transform, Bounds> FitZones()
        {
            var zb = new List<Transform> { head, chest, pelvis, armL[0], armL[1], armR[0], armR[1], legL[0], legL[1], legR[0], legR[1] }; zb.RemoveAll(t => !t);
            string key = (model != null ? model.name : name) + "|" + zb.Count; var res = new Dictionary<Transform, Bounds>();
            if (!fitCache.TryGetValue(key, out var byName))
            {
                byName = new Dictionary<string, Bounds>(); var pts = new Dictionary<string, List<Vector3>>(); var set = new HashSet<Transform>(zb);
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh; if (!mesh || smr == gunMesh || !mesh.isReadable) continue; var bones = smr.bones; if (bones == null || bones.Length == 0) continue;
                    var bp = mesh.bindposes; var bw = mesh.boneWeights; var vs = mesh.vertices; if (bw.Length != vs.Length) continue;
                    var map = new int[bones.Length];
                    for (int i = 0; i < bones.Length; i++) { var t = bones[i]; while (t && !set.Contains(t)) t = t.parent; map[i] = t ? System.Array.IndexOf(bones, t) : -1; }
                    for (int v = 0; v < vs.Length; v++)
                    {
                        int bi = bw[v].boneIndex0; if (bi < 0 || bi >= map.Length) continue; int zi = map[bi]; if (zi < 0 || zi >= bp.Length) continue;
                        var zt = bones[zi]; float ls = zt.lossyScale.x; if (ls < 1e-5f) ls = 1;
                        if (!pts.TryGetValue(zt.name, out var l)) pts[zt.name] = l = new List<Vector3>(); l.Add(bp[zi].MultiplyPoint3x4(vs[v]) * ls);
                    }
                }
                foreach (var kv in pts)
                {
                    if (kv.Value.Count < 24) continue; var l = kv.Value; float Pc(System.Func<Vector3, float> f, float q) { var a = l.Select(f).OrderBy(x => x).ToList(); return a[Mathf.Clamp((int)(q * (a.Count - 1)), 0, a.Count - 1)]; }
                    var mn = new Vector3(Pc(p => p.x, .04f), Pc(p => p.y, .04f), Pc(p => p.z, .04f)); var mx = new Vector3(Pc(p => p.x, .96f), Pc(p => p.y, .96f), Pc(p => p.z, .96f));
                    var bb = new Bounds(); bb.SetMinMax(mn - Vector3.one * .015f, mx + Vector3.one * .015f); byName[kv.Key] = bb;
                }
                fitCache[key] = byName;
            }
            foreach (var t in zb) if (byName.TryGetValue(t.name, out var b)) res[t] = b;
            return res;
        }

        void Update()
        {   // runs before the Animator: restore the rest pose, the Animator then writes whatever its clips key
            if (driven == null) return;
            for (int i = 0; i < driven.Length; i++) driven[i].SetLocalPositionAndRotation(restPos[i], restRot[i]);
        }

        public void SetWeapon(string wep)
        {
            var lib = SoldierLib.I; var nr = lib ? lib.Rig(wep) : null; if (nr == null) { wep = "K98"; nr = lib ? lib.Rig("K98") : null; }
            if (wep == weapon) return; weapon = wep; rig = nr;
            SoldierLib.MESH.TryGetValue(wep, out var keep); gunMesh = null;
            foreach (var kv in SoldierLib.MESH)
            {
                var t = anim.transform.Find(kv.Value); if (!t) continue; t.gameObject.SetActive(kv.Value == keep);
                if (kv.Value == keep) { gunMesh = t.GetComponent<SkinnedMeshRenderer>(); if (gunMesh) gunMesh.updateWhenOffscreen = true; }
            }
            anim.runtimeAnimatorController = lib.Controller(model, wep);
            cls = wep == "K98" ? "bolt" : wep == "MG42" ? "mg" : wep == "Luger" ? "pistol" : wep == "Panzerschreck" ? "launcher" : "smg";
            parts = new Transform[rig.parts.Length]; iBolt = iMag = iCover = iRocket = iToggle = -1;
            for (int i = 0; i < parts.Length; i++) { handles.TryGetValue(rig.parts[i], out parts[i]); }
            if (wep == "K98") iBolt = rig.Part("Weapon_handle");
            if (wep == "MP40" || wep == "STG44") iMag = rig.Part("Weapon_handle.001");
            if (wep == "MG42") iCover = rig.Part("Weapon_handle.001");
            if (wep == "Panzerschreck") iRocket = rig.Part("Weapon_handle.003");
            if (wep == "Luger")
            {   // magazine = lowest non-main part, toggle = highest
                float lo = 9, hi = -9;
                for (int i = 0; i < parts.Length; i++) { if (rig.parts[i] == rig.main) continue; float y = rig.partBounds[i].center.y; if (y < lo) { lo = y; iMag = i; } if (y > hi) { hi = y; iToggle = i; } }
            }
            for (int i = 0; i < parts.Length; i++) PlacePart(i, Vector3.zero, Quaternion.identity, Vector3.zero);
            boltLift = boltSlide = 0; cycleT = -1; rocketLoaded = true; DropProps();
        }

        // ---- first person: arms + gun go to their own overlay camera (fixed viewmodel FOV, never clip into walls) ----
        public const int FPLayer = 31;
        static Camera fpCam; static int fpUsers;
        /// The end of the barrel of the gun this soldier is holding, in world space.
        public Vector3? MuzzleWorld => rig != null && gun ? gun.TransformPoint(rig.muzzle) : (Vector3?)null;
        /// Bore direction (butt to muzzle) of the held gun.
        public Vector3? BoreDir => rig != null && gun ? (gun.TransformPoint(rig.muzzle) - gun.TransformPoint(rig.stock)).normalized : (Vector3?)null;
        /// The muzzle as the player sees it: first-person arms and gun are drawn by their own camera with its own field of
        /// view, so the barrel end is carried over through the screen into the world camera's view at the same depth.
        public Vector3? MuzzleOnScreen(Camera world)
        {
            var m = MuzzleWorld; if (m == null) return null;
            if (firstPerson && fpCam && world) { var sp = fpCam.WorldToScreenPoint(m.Value); if (sp.z > .05f) return world.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Clamp(sp.z, .25f, 3f))); }
            return m;
        }
        static void FpCamera(Camera main, bool on)
        {
            if (!main) return;
            if (on && (!fpCam || fpCam.transform.parent != main.transform))
            {
                if (fpCam) Destroy(fpCam.gameObject);
                var go = new GameObject("FP Viewmodel Camera"); go.transform.SetParent(main.transform, false);
                fpCam = go.AddComponent<Camera>(); fpCam.cullingMask = 1 << FPLayer; fpCam.nearClipPlane = .01f; fpCam.farClipPlane = 8; fpCam.fieldOfView = 58;
                var d = fpCam.GetUniversalAdditionalCameraData(); d.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay; d.renderShadows = true; d.renderPostProcessing = true;   // the last camera in the stack runs post (grading, tonemapping)
            }
            if (!fpCam) return;
            var md = main.GetUniversalAdditionalCameraData();
            if (on) { if (!md.cameraStack.Contains(fpCam)) md.cameraStack.Add(fpCam); main.cullingMask &= ~(1 << FPLayer); }
            else md.cameraStack.Remove(fpCam);
            fpCam.enabled = on;
        }

        public void SetFirstPerson(bool on, Transform camera)
        {
            bool was = firstPerson; firstPerson = on; cam = camera; if (cam) lastCam = cam.rotation;
            var mainCam = camera ? camera.GetComponent<Camera>() : Camera.main;
            if (on != was || on) FpCamera(mainCam, on);
            int layer = on ? FPLayer : 0;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in rends) if (r) r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;   // no self-shadow on the hands
            float hs = on ? .8f : 1;   // the pack's hands are chunky; at viewmodel distance they read oversized
            if (armL[2]) armL[2].localScale = Vector3.one * hs; if (armR[2]) armR[2].localScale = Vector3.one * hs;
            var legs = on ? Vector3.one * .001f : Vector3.one; if (hipL) hipL.localScale = legs; if (hipR) hipR.localScale = legs;
            if (!on) { transform.localPosition = Vector3.zero; transform.localRotation = Quaternion.identity; if (neck) neck.localScale = Vector3.one; hideFP = false; }
        }
        void OnDisable() { if (firstPerson) FpCamera(cam ? cam.GetComponent<Camera>() : Camera.main, false); }

        // ================= events =================
        public void Fire()
        {
            float kp, kr; switch (cls) { case "bolt": kp = .9f; kr = 260; break; case "mg": kp = .55f; kr = 90; break; case "pistol": kp = .45f; kr = 320; break; case "launcher": kp = 1.4f; kr = 120; break; default: kp = .35f; kr = 70; break; }
            if (firstPerson) { kp *= .7f; kr *= .7f; }
            rpV += kp; rrV += kr; rYaw = Random.Range(-1f, 1f) * kr * .015f;
            if (cls == "bolt") cycleT = -.12f;                 // bolt worked a beat after the shot
            else if (cls == "launcher") rocketLoaded = false;
            else Case(cls == "mg" ? SoldierLib.I.caseMG : SoldierLib.I.casePistol, cls == "pistol" ? .022f : cls == "mg" ? .057f : .025f);
        }
        public void Stab() { stabV += 3.5f; }

        void Case(GameObject prefab, float len)
        {
            if (rig == null || (!firstPerson && !Visible())) return;
            var p = gun.TransformPoint(rig.port + Vector3.right * .02f);
            SoldierProps.Eject(prefab, len, p, gun.rotation * Quaternion.Euler(0, 90, 0), gun.right * Random.Range(1.6f, 2.4f) + gun.up * Random.Range(1f, 1.8f) - gun.forward * .3f);
        }

        // ================= frame =================
        bool Visible() => firstPerson || (body && body.enabled);   // fog-of-war hidden units skip the rig
        static bool animWarned; Quaternion lHandRest = Quaternion.identity;
        /// First-person viewmodel tuning (FOV of the viewmodel camera, hand scale, how far the body sits behind the eye when aiming).
        public static float vmFovHip = 58, vmFovAds = 66, vmHandHip = .8f, vmHandAds = .64f, vmBackAds = .22f;
        static float S(float t, float a, float b) { float x = Mathf.Clamp01((t - a) / (b - a)); return x * x * (3 - 2 * x); }
        static float Damp(float cur, float target, float rate, float dt) => Mathf.Lerp(cur, target, 1 - Mathf.Exp(-rate * dt));

        void LateUpdate()
        {
            if (rig == null || !armR[0] || !chest) return;   // (state is lost if scripts reload mid-play)
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (anim.isActiveAndEnabled && anim.runtimeAnimatorController) anim.SetFloat(pSpeed, dead ? 0 : speed, .1f, dt);
            else if (!animWarned)
            {   // report the first idle Animator once instead of a warning every frame for every soldier
                animWarned = true;
                Debug.LogWarning($"[FallWeiss] Soldier Animator not running on {name}: controller={(anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "null")} enabled={anim.enabled} activeInHierarchy={anim.gameObject.activeInHierarchy} weapon={weapon}");
            }
            bool reloading = reload != ReloadKind.None && !dead;
            float boltDip = cls == "bolt" && cycleT > 0 ? Mathf.Sin(Mathf.Clamp01(cycleT) * Mathf.PI) : 0;   // RO2: the rifle comes off the cheek while the bolt is worked, so the hand stays below the eye
            wAds = firstPerson ? ads * (1 - .6f * boltDip) : Damp(wAds, raise && hasAim && !sprint && !reloading && !dead ? 1 : 0, 9, dt);
            wSprint = Damp(wSprint, sprint && !reloading && !dead && wAds < .5f ? 1 : 0, 7, dt);
            wReload = Damp(wReload, reloading ? 1 : 0, 9, dt);
            wCrouch = Damp(wCrouch, crouch, 8, dt);
            Springs(dt);
            if (firstPerson && neck) neck.localScale = Vector3.one * .001f;
            if (firstPerson && fpCam) fpCam.fieldOfView = Mathf.Lerp(vmFovHip, vmFovAds, ads);   // viewmodel FOV stays put while the world zooms (RO2: separate first-person mesh FOV)
            if (firstPerson && armL[2] && armR[2]) { float hs = Mathf.Lerp(vmHandHip, vmHandAds, wAds); armL[2].localScale = armR[2].localScale = Vector3.one * hs; }   // the pack's hands are oversized at sight distance
            if (firstPerson && hideFP) { SetRends(false); return; } else if (firstPerson) SetRends(true);
            if (!Visible() && !firstPerson) { Actions(dt, false); return; }   // keep timers running, skip the IK

            var up = Vector3.up;
            if (firstPerson && cam)
            {   // body hangs off the camera: eye ~ head bone + (0, .1, .1)
                var yaw = Quaternion.Euler(0, cam.eulerAngles.y, 0);
                transform.SetPositionAndRotation(cam.position - yaw * new Vector3(0, Mathf.Lerp(Mathf.Lerp(1.56f, cls == "pistol" ? 1.53f : 1.43f, wAds), 1.66f, wReload), Mathf.Lerp(.1f, vmBackAds, wAds)), yaw);   // cheek weld: the shoulders rise to the stock as you aim
            }
            var bodyRot = transform.rotation; var fwd = bodyRot * Vector3.forward; var right = bodyRot * Vector3.right;
            if (!firstPerson) CrouchLegs(fwd, up);

            // ---- aim direction (yaw / pitch relative to the body, clamped; the rest is the unit turning) ----
            Vector3 dir = firstPerson && cam ? cam.forward : hasAim ? (aimPoint - head.position) : fwd;
            if (dir.sqrMagnitude < 1e-6f) dir = fwd;
            var ld = Quaternion.Inverse(bodyRot) * dir.normalized;
            float yawT = Mathf.Clamp(Mathf.Atan2(ld.x, ld.z) * Mathf.Rad2Deg, -75, 75), pitchT = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(ld.y, -1, 1)) * Mathf.Rad2Deg, -70, 70);
            if (!hasAim && !firstPerson) { yawT = 0; pitchT = -4; }
            if (firstPerson) { yawS = yawT; pitchS = pitchT; } else { yawS = Damp(yawS, yawT, 10, dt); pitchS = Damp(pitchS, pitchT, 10, dt); }
            var aimYaw = bodyRot * Quaternion.Euler(0, yawS, 0); var aimRot = aimYaw * Quaternion.Euler(-pitchS, 0, 0);

            // ---- spine / head ----
            float lean = wCrouch * 3 + wSprint * 7 + wReload * 3;
            chest.rotation = Quaternion.AngleAxis(yawS * .6f, up) * Quaternion.AngleAxis(-pitchS * .45f + lean, right) * chest.rotation;
            if (!firstPerson) head.rotation = Quaternion.AngleAxis(yawS * .3f, up) * Quaternion.AngleAxis(-pitchS * .4f - lean * .6f + wReload * 9, aimYaw * Vector3.right) * head.rotation;

            // ---- gun pose ----
            Pose(aimRot, aimYaw, dt, out var gp, out var gr);
            gun.SetPositionAndRotation(gp, gr);
            if (!firstPerson && wReload > .01f)
            {   // eyes on the action while reloading
                var to = gun.TransformPoint(rig.port) - head.position;
                head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(head.forward, to), .6f * wReload) * head.rotation;
            }

            // ---- parts + hands ----
            Actions(dt, true);
            var rT = gun.TransformPoint(rig.rh); var rR = gun.rotation * rig.rhRot; var lT = gun.TransformPoint(rig.lh); var lR = gun.rotation * rig.lhRot;
            Hands(ref lT, ref lR, ref rT, ref rR, bodyRot);
            var sR = armR[0].position; var sL = armL[0].position;
            var poleR = sR + aimYaw * Vector3.Lerp(new Vector3(.2f, -.85f, -.1f), new Vector3(.5f, -.2f, -.1f), wAds);
            var poleL = sL + aimYaw * new Vector3(-.12f, -.6f, .3f);
            if (cls == "launcher") { poleR = sR + aimYaw * new Vector3(.3f, -.6f, 0); poleL = sL + aimYaw * new Vector3(-.2f, -.6f, .2f); }
            TwoBone(armR[0], armR[1], armR[2], rT, poleR, rR);
            float oneHand = cls == "pistol" && !dead ? 1 - wReload : 0;   // pistols are fired one-handed; the off hand only comes up to change the magazine
            if (oneHand > 0) { var restL = sL + bodyRot * new Vector3(-.07f, -.5f, .07f); lT = Vector3.Lerp(lT, restL, oneHand); poleL = Vector3.Lerp(poleL, sL + bodyRot * new Vector3(-.1f, -.3f, -.4f), oneHand); }
            TwoBone(armL[0], armL[1], armL[2], lT, poleL, lR);
            if (oneHand > 0) armL[2].localRotation = Quaternion.Slerp(armL[2].localRotation, lHandRest, oneHand);
            Grip();
            if (clipProp && clipProp.activeSelf) PlaceClip(); if (roundProp && roundProp.activeSelf) PlaceRound();
        }

        /// Close the hands round the gun: the pack's hands are open paddles, so curl the finger and thumb bones.
        void Grip()
        {
            if (fingL) fingL.localRotation = Quaternion.AngleAxis(curlF, curlAxis) * fingL.localRotation;
            if (fingR) fingR.localRotation = Quaternion.AngleAxis(curlF, curlAxis) * fingR.localRotation;
            if (thumbL) thumbL.localRotation = Quaternion.AngleAxis(curlT, curlAxis) * thumbL.localRotation;
            if (thumbR) thumbR.localRotation = Quaternion.AngleAxis(curlT, curlAxis) * thumbR.localRotation;
        }
        void SetRends(bool on) { foreach (var r in rends) if (r && r.gameObject.activeInHierarchy && r.enabled != on) r.enabled = on; }

        // ---- poses: aimed (from the pack's cheek-weld), low ready, sprint (port arms), reload; then recoil / sway ----
        void Pose(Quaternion aimRot, Quaternion aimYaw, float dt, out Vector3 P, out Quaternion R)
        {
            Vector3 piv = cls == "pistol" ? rig.rh : rig.stock;   // pivot the gun turns about when lowered
            if (firstPerson && cam)
            {
                var c = cam.rotation; var cp = cam.position;
                // weapon sway: lag behind fast camera turns (spring)
                var dq = Quaternion.Inverse(lastCam) * c; lastCam = c; var de = dq.eulerAngles; float dx = Mathf.DeltaAngle(0, de.x), dy = Mathf.DeltaAngle(0, de.y);
                var tgt = new Vector2(Mathf.Clamp(-dy * 1.4f, -6, 6), Mathf.Clamp(-dx * 1.4f, -6, 6)) * (1 - ads * .75f);
                swayV += (tgt - sway) * 90 * dt - swayV * 12 * dt; sway += swayV * dt;
                bobT += dt * (sprint ? 13 : 9) * (moving ? 1 : 0);
                float bob = moving ? 1 : 0;
                var sightDir = (rig.sightFront - rig.sight).normalized; var level = Quaternion.FromToRotation(sightDir, Vector3.forward);
                // ADS: rear sight on the eye line, stock butt ~10 cm behind the eye (the cheek weld decides the distance)
                float eyeRelief = cls == "pistol" ? .42f : cls == "launcher" ? .28f : Mathf.Max(.18f, rig.sight.z - rig.stock.z - .10f);
                var rA = c * level; var pA = cp + c * new Vector3(0, -.006f, eyeRelief) - rA * rig.sight;
                var rH = c * Quaternion.Euler(-1, -4, 7) * level; var pH = cp + c * new Vector3(.15f, -.22f, .2f) - rH * rig.rh;   // hip: grip low on the right, in view
                if (cls == "pistol") pH = cp + c * new Vector3(.08f, -.15f, .36f) - rH * rig.sight;
                if (cls == "launcher") { rH = rA; pH = cp + c * new Vector3(.03f, -.04f, eyeRelief) - rH * rig.sight; }
                var rS = c * Quaternion.Euler(28, -48, -28); var pS = cp + c * new Vector3(.1f, -.3f, .22f) - rS * rig.rh;
                if (cls == "pistol") { rS = c * Quaternion.Euler(55, -10, 0); pS = cp + c * new Vector3(.16f, -.38f, .18f) - rS * rig.rh; }
                if (cls == "launcher") { rS = c * Quaternion.Euler(-25, 0, 0); pS = pH; }
                ReloadPoseFP(c, cp, out var pR, out var rR);
                P = Vector3.Lerp(pH, pA, wAds); R = Quaternion.Slerp(rH, rA, wAds);
                P = Vector3.Lerp(P, pS, wSprint); R = Quaternion.Slerp(R, rS, wSprint);
                P = Vector3.Lerp(P, pR, wReload); R = Quaternion.Slerp(R, rR, wReload);
                R = R * Quaternion.Euler(sway.y, sway.x, sway.x * .6f);
                P += c * new Vector3(Mathf.Sin(bobT * .5f) * .006f, -Mathf.Abs(Mathf.Cos(bobT * .5f)) * .007f, 0) * bob * (1 - ads * .8f);
            }
            else
            {
                // aimed: reproduce the pack's shouldered aim relative to the (already turned) head
                var rA = aimRot; var pA = head.position - rA * rig.head; var pivotA = pA + rA * piv;
                Quaternion rL; Vector3 pL;   // low ready: muzzle dropped about the shoulder pocket
                switch (cls)
                {
                    case "pistol": rL = aimRot * Quaternion.Euler(40, -8, 0); pL = pivotA + aimYaw * new Vector3(-.06f, -.12f, -.12f) - rL * piv; break;
                    case "mg": rL = aimRot * Quaternion.Euler(22, -12, 6); pL = pivotA + aimYaw * new Vector3(0, -.18f, .02f) - rL * piv; break;
                    case "launcher": rL = aimRot * Quaternion.Euler(12, 0, 0); pL = pivotA - rL * piv; break;
                    default: rL = aimRot * Quaternion.Euler(32, -16, 10); pL = pivotA + aimYaw * new Vector3(.01f, -.04f, 0) - rL * piv; break;
                }
                // sprint: port arms across the chest (rifles), pistol down at the side, launcher on the shoulder
                var cp0 = chest.position; Quaternion rS; Vector3 gripS;
                switch (cls)
                {
                    case "pistol": rS = aimYaw * Quaternion.Euler(75, 0, 0); gripS = cp0 + aimYaw * new Vector3(.24f, -.18f, .12f); break;
                    case "launcher": rS = rL * Quaternion.Euler(-14, 0, 0); gripS = pL + rL * rig.rh; break;
                    case "mg": rS = aimYaw * Quaternion.LookRotation(new Vector3(-.55f, .3f, .78f), new Vector3(.25f, .65f, .3f)); gripS = cp0 + aimYaw * new Vector3(.18f, -.05f, .26f); break;
                    case "smg": rS = aimYaw * Quaternion.LookRotation(new Vector3(-.72f, .22f, .66f), new Vector3(.25f, .6f, .75f)); gripS = cp0 + aimYaw * new Vector3(.17f, -.06f, .25f); break;
                    default: rS = aimYaw * Quaternion.LookRotation(new Vector3(-.6f, .62f, .5f), new Vector3(.3f, .35f, .88f)); gripS = cp0 + aimYaw * new Vector3(.16f, .02f, .26f); break;
                }
                var pS = gripS - rS * rig.rh;
                ReloadPoseTP(cp0, aimYaw, out var pR, out var rR);
                P = Vector3.Lerp(pL, pA, wAds); R = Quaternion.Slerp(rL, rA, wAds);
                P = Vector3.Lerp(P, pS, wSprint); R = Quaternion.Slerp(R, rS, wSprint);
                P = Vector3.Lerp(P, pR, wReload); R = Quaternion.Slerp(R, rR, wReload);
            }
            // recoil: kick back along the bore and rotate the muzzle up about the grip; bayonet thrust forward
            var gw = P + R * rig.rh; R = R * Quaternion.Euler(-rr, rYaw * rr * .02f, rr * .05f); P = gw - R * rig.rh;
            P += R * Vector3.forward * (stab * .35f - rp * .06f);
        }

        void ReloadPoseTP(Vector3 cp0, Quaternion aimYaw, out Vector3 P, out Quaternion R)
        {   // gun brought in to the chest, canted so the action faces the eyes; MG stays low, launcher on the shoulder
            Vector3 grip; switch (cls)
            {
                case "smg": R = aimYaw * Quaternion.Euler(-8, -18, -38); grip = cp0 + aimYaw * new Vector3(.17f, -.02f, .28f); break;
                case "mg": R = aimYaw * Quaternion.Euler(4, -10, 0); grip = cp0 + aimYaw * new Vector3(.18f, -.12f, .3f); break;
                case "pistol": R = aimYaw * Quaternion.Euler(-25, -10, -35); grip = cp0 + aimYaw * new Vector3(.1f, .06f, .3f); break;
                case "launcher": R = aimYaw * Quaternion.Euler(-4, 0, 0); P = head.position - R * rig.head + aimYaw * new Vector3(0, -.04f, 0); return;
                default: R = aimYaw * Quaternion.Euler(-30, -16, 22); grip = cp0 + aimYaw * new Vector3(.17f, -.06f, .2f); break;   // butt at the hip, muzzle up, action toward the eyes
            }
            P = grip - R * rig.rh;
        }
        void ReloadPoseFP(Quaternion c, Vector3 cp, out Vector3 P, out Quaternion R)
        {
            switch (cls)
            {
                case "smg": R = c * Quaternion.Euler(-4, -16, -42); P = cp + c * new Vector3(.05f, -.14f, .38f) - R * rig.port; return;   // mag well rolled toward the left hand, in view
                case "mg": R = c * Quaternion.Euler(8, -14, 6); P = cp + c * new Vector3(.04f, -.15f, .46f) - R * rig.port; return;
                case "pistol": R = c * Quaternion.Euler(-30, -14, -36); P = cp + c * new Vector3(.05f, -.13f, .3f) - R * rig.rh; return;
                case "launcher": R = c * Quaternion.Euler(-6, 0, 0); P = cp + c * new Vector3(.04f, -.07f, .2f) - R * rig.sight; return;
                default: R = c * Quaternion.Euler(-10, -14, 26); P = cp + c * new Vector3(.06f, -.17f, .42f) - R * rig.port; return;   // K98: open action under the eyes   // K98: port toward the eyes
            }
        }

        void Springs(float dt)
        {
            const float k = 260, c = 24;
            rpV += (-k * rp - c * rpV) * dt; rp += rpV * dt;
            rrV += (-k * rr - c * rrV) * dt; rr += rrV * dt;
            stabV += (-160 * stab - 18 * stabV) * dt; stab += stabV * dt;
        }

        // ================= actions: bolt, reloads (moving parts + hand targets) =================
        Vector3 lHand, rHand; Quaternion lHandR, rHandR; float lW, rW;

        void Actions(float dt, bool pose)
        {
            lW = rW = 0;
            // K98 bolt: opened for loading, cycled after each shot
            if (cls == "bolt")
            {
                bool loadingSingles = reload == ReloadKind.Single || singles;
                if (loadingSingles && !boltHeld) { boltHeld = true; openT = 0; closeT = -1; }
                if (!loadingSingles && boltHeld && reload != ReloadKind.Stripper) { boltHeld = false; closeT = 0; openT = -1; }
                float lift = 0, slide = 0, hand = 0;
                if (reload == ReloadKind.Stripper)
                {
                    float t = reloadT; lift = S(t, .06f, .12f) * (1 - S(t, .86f, .92f)); slide = S(t, .11f, .19f) * (1 - S(t, .78f, .86f));
                    hand = S(t, .02f, .07f) * (1 - S(t, .2f, .27f)) + S(t, .72f, .77f) * (1 - S(t, .93f, .98f));
                }
                else if (openT >= 0) { openT += dt / .35f; float t = Mathf.Min(1, openT); lift = S(t, .2f, .5f); slide = S(t, .45f, .85f); hand = S(t, 0, .2f) * (1 - S(t, .85f, 1)); if (openT > 1.2f) openT = -1; }
                else if (boltHeld) { lift = 1; slide = 1; }
                else if (closeT >= 0) { closeT += dt / .4f; float t = Mathf.Min(1, closeT); lift = 1 - S(t, .55f, .8f); slide = 1 - S(t, .2f, .55f); hand = S(t, 0, .2f) * (1 - S(t, .85f, 1)); if (closeT > 1.1f) closeT = -1; }
                if (cycleT > -1 && reload == ReloadKind.None && !boltHeld)
                {   // bolt throw: up, back (eject), forward, down
                    cycleT += dt / .72f; float t = cycleT;
                    if (t >= 0) { lift = Mathf.Max(lift, S(t, .1f, .25f) * (1 - S(t, .68f, .82f))); slide = Mathf.Max(slide, S(t, .25f, .42f) * (1 - S(t, .5f, .66f))); hand = Mathf.Max(hand, S(t, 0, .12f) * (1 - S(t, .84f, .98f))); }
                    if (t >= .42f && t - dt / .72f < .42f) Case(SoldierLib.I.caseK98, .057f);
                    if (t > 1) cycleT = -1;
                }
                boltLift = lift; boltSlide = slide;
                if (iBolt >= 0 && pose)
                {
                    var bb = rig.partBounds[iBolt]; var axisP = new Vector3(bb.center.x, bb.center.y, bb.center.z);
                    var spin = Quaternion.AngleAxis(-80 * lift, Vector3.forward);   // handle (right side) swings up
                    PlacePart(iBolt, axisP, spin, -Vector3.forward * .095f * slide);
                    var knob = new Vector3(bb.max.x + .045f, bb.center.y - .015f, bb.min.z + .03f);
                    var knobW = gun.TransformPoint(axisP + spin * (knob - axisP) - Vector3.forward * .095f * slide);
                    rHand = Wrist(knobW, gun.rotation * Quaternion.Euler(0, 0, -70 - 60 * lift) * Quaternion.Euler(-90, 0, 0)); rHandR = gun.rotation * Quaternion.Euler(0, 0, -70) * Quaternion.Euler(-90, 0, 0); rW = hand;
                }
                if (pose) BoltLoading();
            }
            if (!pose) return;
            if (reload == ReloadKind.Mag && (cls == "smg" || cls == "pistol")) MagChange();
            else if (iMag >= 0) PlacePart(iMag, rig.partBounds[iMag].center, Quaternion.identity, Vector3.zero, 1);
            if (cls == "mg") MgReload();
            if (cls == "launcher") RocketReload();
            if (cls != "bolt" || reload == ReloadKind.None) { if (clipProp) clipProp.SetActive(false); if (roundProp) roundProp.SetActive(false); }
            wasReload = reload != ReloadKind.None;
        }

        Vector3 Pouch(bool left) { var b = pelvis ? pelvis : transform; return b.position + transform.rotation * new Vector3(left ? -.14f : .14f, .14f, .17f); }
        /// Loading from above: the hand comes over the action from the left, fingers across the receiver, thumb on the rounds.
        Quaternion ClipGrip => Quaternion.LookRotation(gun.forward, (gun.right - gun.up * .55f).normalized);
        Quaternion PalmDown => Quaternion.LookRotation(transform.forward, Vector3.down) * Quaternion.Euler(0, 0, 0);
        static Vector3 Wrist(Vector3 palm, Quaternion handRot) => palm - handRot * Vector3.up * .075f;

        void SetL(Vector3 palm, Quaternion r, float w) { lHand = Wrist(palm, r); lHandR = r; lW = w; }

        /// Moves a weapon part rigidly: rotate by `rot` about `pivot` (gun space), then offset (gun space). scale 0 hides it.
        void PlacePart(int i, Vector3 pivot, Quaternion rot, Vector3 offset, float scale = 1)
        {
            var t = parts[i]; if (!t) return;
            var lp = pivot + rot * (rig.partPos[i] - pivot) + offset; t.localPosition = lp; t.localRotation = rot * rig.partRot[i];
            t.localScale = handleScale.TryGetValue(rig.parts[i], out var s) ? s * scale : Vector3.one * scale;
        }
        /// Places a part so its mesh centre sits at world point `w` with world rotation `wr` (relative to the gun's own).
        void PlacePartWorld(int i, Vector3 w, Quaternion wr, float scale = 1)
        {
            var c = rig.partBounds[i].center; var lr = Quaternion.Inverse(gun.rotation) * wr; var lw = gun.InverseTransformPoint(w);
            PlacePart(i, c, lr, lw - c, scale);
        }

        // ---- K98: stripper clip (empty magazine) or single rounds (topping up / scoped rifle) ----
        void BoltLoading()
        {
            var port = gun.TransformPoint(rig.port + new Vector3(0, -.004f, .012f));
            if (reload == ReloadKind.Stripper)
            {
                float t = reloadT;
                if (!clipProp) { clipProp = SoldierProps.Clip(SoldierLib.I); clipProp.transform.SetParent(transform, false); }
                if (t < .05f) clipGone = false;
                bool show = t > .3f && t < .84f && !clipGone; clipProp.SetActive(show);
                // hand: support -> belt pouch -> clip into the guide -> thumb presses rounds down -> bolt strips the clip -> back
                var pouch = Pouch(true); var guide = port + gun.up * .085f;   // thumb on top of the clip
                Vector3 palm; Quaternion r = t < .3f ? PalmDown : ClipGrip;
                if (t < .3f) { float k = S(t, .1f, .28f); palm = Vector3.Lerp(gun.TransformPoint(rig.lh), pouch, k); }
                else if (t < .48f) { float k = S(t, .3f, .48f); palm = Vector3.Lerp(pouch, guide, k); }
                else if (t < .72f) { float k = S(t, .5f, .7f); palm = guide - gun.up * .055f * k; }
                else palm = Vector3.Lerp(guide - gun.up * .055f, gun.TransformPoint(rig.lh), S(t, .74f, .92f));
                float w = S(t, .1f, .16f) * (1 - S(t, .86f, .97f)); SetL(palm, Quaternion.Slerp(gun.rotation * rig.lhRot, r * Quaternion.Euler(0, 0, -20), S(t, .12f, .3f) * (1 - S(t, .76f, .9f))), w);
                clipPush = S(t, .5f, .7f);
                if (t >= .84f && !clipGone) { clipGone = true; var g = SoldierProps.Clip(SoldierLib.I); foreach (Transform ch in g.transform) if (ch.name != "strip") ch.gameObject.SetActive(false); g.transform.SetPositionAndRotation(guide, gun.rotation); SoldierProps.EjectObject(g, gun.up * 1.6f + gun.right * .8f, 5); }
            }
            else if (reload == ReloadKind.Single)
            {
                float t = reloadT;
                if (!roundProp) { roundProp = SoldierProps.Make(SoldierLib.I.roundPrefab, .08f, "Round"); roundProp.transform.SetParent(transform, false); }
                roundProp.SetActive(t > .32f && t < .74f);
                var pouch = Pouch(true); var p0 = port + gun.up * .015f;
                Vector3 palm;
                if (t < .32f) palm = Vector3.Lerp(gun.TransformPoint(rig.lh), pouch, S(t, .02f, .3f));
                else if (t < .6f) palm = Vector3.Lerp(pouch, p0 + gun.up * .03f, S(t, .32f, .58f));
                else if (t < .75f) palm = p0 + gun.up * .03f * (1 - S(t, .6f, .72f));
                else palm = Vector3.Lerp(p0, gun.TransformPoint(rig.lh), S(t, .76f, 1));
                SetL(palm, Quaternion.Slerp(gun.rotation * rig.lhRot, t < .32f ? PalmDown : ClipGrip, S(t, .05f, .3f) * (1 - S(t, .76f, .95f))), S(t, 0, .06f) * (1 - S(t, .92f, 1)));
                roundPush = S(t, .6f, .72f);
            }
        }
        float clipPush, roundPush;
        void PlaceClip()
        {   // clip sits in the guide (above the open action), rounds pointing down-range; thumb pressure strips them down
            var guide = gun.TransformPoint(rig.port + new Vector3(0, -.004f, .012f));
            var hand = armL[2].position + armL[2].rotation * Vector3.up * .07f;
            float inGuide = reloadT >= .46f ? 1 : S(reloadT, .4f, .46f);
            var pos = Vector3.Lerp(hand, guide, inGuide); clipProp.transform.SetPositionAndRotation(pos, gun.rotation);
            for (int i = 0; i < 5; i++)
            {
                var r = clipProp.transform.GetChild(i); float y = i * .011f - clipPush * .058f;
                r.localPosition = new Vector3(0, y, 0); r.gameObject.SetActive(y > -.012f);   // rounds vanish into the magazine
            }
        }
        void PlaceRound()
        {
            var hand = armL[2].position + armL[2].rotation * Vector3.up * .07f; var port = gun.TransformPoint(rig.port + new Vector3(0, -.004f, .012f));
            roundProp.transform.SetPositionAndRotation(Vector3.Lerp(hand, port - gun.up * .03f * roundPush, S(reloadT, .5f, .6f)), gun.rotation);
        }

        // ---- MP40 / StG 44 / Luger: magazine out, to the pouch, fresh one in, charge ----
        void MagChange()
        {
            float t = reloadT; if (iMag < 0) return;
            var mb = rig.partBounds[iMag]; var inW = gun.TransformPoint(mb.center); var outW = gun.TransformPoint(mb.center - Vector3.up * (mb.size.y + .04f));
            var pouch = Pouch(true);
            Vector3 mp; float sc = 1; Quaternion mr = gun.rotation;
            if (t < .18f) mp = inW;
            else if (t < .28f) mp = Vector3.Lerp(inW, outW, S(t, .18f, .28f));
            else if (t < .44f) { mp = Vector3.Lerp(outW, pouch, S(t, .28f, .44f)); mr = Quaternion.Slerp(gun.rotation, PalmDown, S(t, .28f, .44f)); }
            else if (t < .5f) { mp = pouch; sc = 0; }
            else if (t < .62f) { mp = Vector3.Lerp(pouch, outW, S(t, .5f, .62f)); mr = Quaternion.Slerp(PalmDown, gun.rotation, S(t, .5f, .62f)); }
            else if (t < .7f) mp = Vector3.Lerp(outW, inW, S(t, .62f, .7f));
            else mp = inW;
            PlacePartWorld(iMag, mp, mr, sc);
            // left hand rides the magazine (grip on its lower half), then returns
            var grab = mp - mr * Vector3.up * mb.size.y * .25f;
            float w = S(t, .06f, .16f) * (1 - S(t, .72f, .84f));
            SetL(grab, Quaternion.Slerp(gun.rotation * rig.lhRot, mr * Quaternion.Euler(0, 0, 90), .6f), w);
            if (cls == "pistol" && iToggle >= 0)
            {   // toggle snap: left hand pinches the knobs and pulls up
                var tb = rig.partBounds[iToggle]; float pull = S(t, .8f, .86f) * (1 - S(t, .88f, .93f));
                PlacePart(iToggle, new Vector3(tb.center.x, tb.min.y, tb.max.z), Quaternion.AngleAxis(-35 * pull, Vector3.right), Vector3.zero);
                float hw = S(t, .76f, .8f) * (1 - S(t, .93f, .98f)); if (hw > 0) SetL(gun.TransformPoint(tb.center + Vector3.up * .02f), PalmDown, hw);
            }
            else
            {   // cocking handle: right hand leaves the grip, racks it back
                float hw = S(t, .76f, .8f) * (1 - S(t, .93f, .98f)); var ch = new Vector3(rig.body.center.x - .04f, rig.sight.y - .015f, rig.rh.z + .2f - .1f * S(t, .82f, .88f));
                if (hw > 0) { rHand = Wrist(gun.TransformPoint(ch), gun.rotation * Quaternion.Euler(0, 0, 80) * Quaternion.Euler(-90, 0, 0)); rHandR = gun.rotation * Quaternion.Euler(0, 0, 80) * Quaternion.Euler(-90, 0, 0); rW = hw; }
            }
        }

        // ---- MG 42: feed cover up, new belt laid in, cover down, charge ----
        void MgReload()
        {
            float open = 0; if (reload == ReloadKind.Mag)
            {
                float t = reloadT; open = S(t, .1f, .2f) * (1 - S(t, .62f, .7f));
                var cb = rig.partBounds[iCover >= 0 ? iCover : 0]; var cover = gun.TransformPoint(cb.center + Vector3.up * .02f);
                var tray = gun.TransformPoint(new Vector3(cb.center.x - .09f, cb.center.y - .03f, cb.center.z)); var pouch = Pouch(true);
                Vector3 palm; Quaternion r = PalmDown;
                if (t < .22f) palm = Vector3.Lerp(gun.TransformPoint(rig.lh), cover, S(t, .03f, .1f));
                else if (t < .4f) palm = Vector3.Lerp(cover, pouch, S(t, .22f, .38f));
                else if (t < .56f) palm = Vector3.Lerp(pouch, tray, S(t, .4f, .54f));
                else if (t < .7f) palm = Vector3.Lerp(tray, cover, S(t, .56f, .62f));
                else palm = Vector3.Lerp(cover, gun.TransformPoint(rig.lh), S(t, .7f, .8f));
                SetL(palm, r, S(t, 0, .05f) * (1 - S(t, .78f, .84f)));
                float hw = S(t, .8f, .84f) * (1 - S(t, .94f, .99f)); var ch = new Vector3(rig.body.center.x + .05f, rig.sight.y - .03f, rig.rh.z + .18f - .14f * S(t, .86f, .9f) * (1 - S(t, .9f, .93f)));
                if (hw > 0) { var q = gun.rotation * Quaternion.Euler(0, 0, -80) * Quaternion.Euler(-90, 0, 0); rHand = Wrist(gun.TransformPoint(ch), q); rHandR = q; rW = hw; }
            }
            if (iCover >= 0) { var cb = rig.partBounds[iCover]; PlacePart(iCover, new Vector3(cb.center.x, cb.max.y, cb.min.z), Quaternion.AngleAxis(-75 * open, Vector3.right), Vector3.zero); }
        }

        // ---- Panzerschreck: rocket fetched from the back, slid into the rear of the tube ----
        void RocketReload()
        {
            if (iRocket < 0) return;
            var rb = rig.partBounds[iRocket];
            if (reload == ReloadKind.Mag)
            {
                float t = reloadT; var back = chest.position + transform.rotation * new Vector3(-.12f, .42f, -.28f);
                var behind = gun.TransformPoint(rb.center - Vector3.forward * (rb.size.z + .35f)); var home = gun.TransformPoint(rb.center);
                Vector3 rp; float sc = 1;
                if (t < .35f) { rp = back; sc = 0; }
                else if (t < .62f) rp = Vector3.Lerp(back, behind, S(t, .35f, .6f));
                else rp = Vector3.Lerp(behind, home, S(t, .62f, .82f));
                PlacePartWorld(iRocket, rp, gun.rotation, sc);
                var palm = t < .35f ? Vector3.Lerp(gun.TransformPoint(rig.lh), back, S(t, .05f, .33f)) : t < .84f ? rp - gun.forward * rb.size.z * .35f : Vector3.Lerp(home, gun.TransformPoint(rig.lh), S(t, .84f, .95f));
                SetL(palm, PalmDown, S(t, 0, .06f) * (1 - S(t, .9f, 1)));
                if (t > .8f) rocketLoaded = true;
            }
            else PlacePart(iRocket, rb.center, Quaternion.identity, Vector3.zero, rocketLoaded ? 1 : 0);
        }

        void Hands(ref Vector3 lT, ref Quaternion lR, ref Vector3 rT, ref Quaternion rR, Quaternion bodyRot)
        {
            if (lW > 0) { lT = Vector3.Lerp(lT, lHand, lW); lR = Quaternion.Slerp(lR, lHandR, lW); }
            if (rW > 0) { rT = Vector3.Lerp(rT, rHand, rW); rR = Quaternion.Slerp(rR, rHandR, rW); }
        }

        // ================= IK =================
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, Quaternion? end)
        {
            if (!a || !b || !c) return;
            Vector3 ap = a.position, bp = b.position, cp = c.position; float la = (bp - ap).magnitude, lb = (cp - bp).magnitude;
            var at = target - ap; float d = Mathf.Clamp(at.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f); var dir = at.normalized;
            var pv = pole - ap; var bend = pv - dir * Vector3.Dot(pv, dir); if (bend.sqrMagnitude < 1e-8f) bend = Vector3.down; bend.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1, 1), sinA = Mathf.Sqrt(1 - cosA * cosA);
            var elbow = ap + dir * (la * cosA) + bend * (la * sinA);
            a.rotation = Quaternion.FromToRotation(bp - ap, elbow - ap) * a.rotation;
            bp = b.position; cp = c.position;
            b.rotation = Quaternion.FromToRotation(cp - bp, ap + dir * d - bp) * b.rotation;
            if (end.HasValue) c.rotation = end.Value;
        }

        void CrouchLegs(Vector3 fwd, Vector3 up)
        {
            float drop = wCrouch * .24f; if (drop < .002f || !pelvis || !legL[0] || !legR[0]) return;
            Vector3 fl = legL[2].position, fr = legR[2].position; Quaternion ql = legL[2].rotation, qr = legR[2].rotation;
            pelvis.position -= up * drop; if (hipL) hipL.position -= up * drop; if (hipR) hipR.position -= up * drop;
            TwoBone(legL[0], legL[1], legL[2], fl, legL[1].position + fwd + up * .2f, ql);
            TwoBone(legR[0], legR[1], legR[2], fr, legR[1].position + fwd + up * .2f, qr);
        }

        void DropProps() { if (clipProp) Destroy(clipProp); if (roundProp) Destroy(roundProp); clipProp = roundProp = null; }
        void OnDestroy() => DropProps();
    }
}
