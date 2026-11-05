// Import settings for the recorded sound library in Resources/Audio (clips cut from the Sonniss GDC bundles).
// Short one-shots decompress on load so rapid fire never stalls; long beds (ambience, bomber drone) stream from disk.
using UnityEditor;
using UnityEngine;

namespace FallWeiss.EditorTools
{
    public class FallWeissAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("FallWeiss/Resources/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            bool bed = assetPath.Contains("/amb_") || assetPath.Contains("/drone/");
            var s = imp.defaultSampleSettings;
            // one-shots: ADPCM keeps gunshot transients crisp, decodes for almost nothing, and has no trouble with very short clips
            // (FMOD's Vorbis encoder failed on an 83 ms punch); long beds stay Vorbis and stream
            s.compressionFormat = bed ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = .5f;
            s.loadType = bed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = !bed;
            imp.defaultSampleSettings = s;
            imp.forceToMono = !assetPath.Contains("/amb_");
            imp.loadInBackground = bed;
        }
    }
}
