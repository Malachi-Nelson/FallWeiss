// Fall Weiss — collider-free ballistic motion for ejected casings / empty stripper clips (so they never block shot
// line-casts): gravity, a tumbling spin, a damped bounce on the terrain height, then rest and despawn.
using UnityEngine;

namespace FallWeiss
{
    public class CasingMotion : MonoBehaviour
    {
        public static int live;
        public Vector3 vel, spin; public float life = 4f; float t; bool rest;

        void OnEnable() { live++; }
        void OnDestroy() { live = Mathf.Max(0, live - 1); }

        void Update()
        {
            float dt = Time.deltaTime; t += dt; if (t > life) { Destroy(gameObject); return; }
            if (rest) return;
            vel += Physics.gravity * dt; var p = transform.position + vel * dt;
            transform.rotation = Quaternion.Euler(spin * dt) * transform.rotation;
            float g = World.H(p.x, p.z) + .01f;
            if (p.y < g)
            {
                p.y = g; vel = new Vector3(vel.x * .35f, -vel.y * .3f, vel.z * .35f); spin *= .4f;
                if (vel.sqrMagnitude < .15f) { rest = true; transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 90); }
            }
            transform.position = p;
        }
    }
}
