// Fall Weiss — master-bus peak limiter on the AudioListener.
// Gunshots are left with their full transient (no per-clip limiting), so when your rifle, a nearby MG burst and a shell land
// on the same instant the mix can exceed full scale. This catches only those peaks: instant attack, ~120 ms release, so a
// single shot passes untouched and stacked sounds are held at -1 dBFS instead of clipping into harsh distortion.
using UnityEngine;

namespace FallWeiss
{
    [RequireComponent(typeof(AudioListener))]
    public class MasterLimiter : MonoBehaviour
    {
        const float Ceiling = .89f;   // -1 dBFS
        float env, release;
        void Awake() { release = Mathf.Exp(-1f / (.12f * AudioSettings.outputSampleRate)); }
        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                float pk = 0; for (int c = 0; c < channels; c++) { float a = Mathf.Abs(data[i + c]); if (a > pk) pk = a; }
                env = pk > env ? pk : env * release + pk * (1 - release);
                if (env > Ceiling) { float g = Ceiling / env; for (int c = 0; c < channels; c++) data[i + c] *= g; }
            }
        }
    }
}
