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
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = bed ? .5f : .75f;
            s.loadType = bed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = !bed;
            imp.defaultSampleSettings = s;
            imp.forceToMono = !assetPath.Contains("/amb_");
            imp.loadInBackground = bed;
        }
    }
}
