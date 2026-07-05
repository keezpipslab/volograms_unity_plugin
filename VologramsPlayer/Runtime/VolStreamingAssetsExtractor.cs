// VolStreamingAssetsExtractor.cs
// Android/Quest cannot fopen() files living inside StreamingAssets (they're
// compressed inside the APK). Native plugins like vol_geom/vol_av need a real
// filesystem path, so this copies the needed files/folders out to
// Application.persistentDataPath once, using UnityWebRequest (the only way to
// read StreamingAssets content on Android), before any VolPlayer/driver opens
// them.
//
// USAGE: put this on a bootstrap object that runs BEFORE your vologram
// objects try to Open(). Simplest robust approach: a small loading scene that
// runs this, then loads your main scene once IsDone is true. Alternatively,
// mark this with [DefaultExecutionOrder(-1000)] and gate your VolPlayer/
// driver Open() calls on WaitUntil(() => extractor.IsDone), the same pattern
// already used for sharedVideoTexture.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[DefaultExecutionOrder(-1000)]
public class VolStreamingAssetsExtractor : MonoBehaviour
{
    [Tooltip("Relative paths under StreamingAssets to copy. List each FILE individually (e.g. 'vologram_0/header.vols', 'vologram_0/sequence_0.vols', 'atlas.mp4') -- Android has no directory-listing API for StreamingAssets, so whole-folder copying isn't possible; you must enumerate every file you need.")]
    public List<string> relativePathsToExtract = new List<string>();

    public bool IsDone { get; private set; }
    public float Progress { get; private set; }

    void Awake()
    {
        StartCoroutine(ExtractAll());
    }

    private IEnumerator ExtractAll()
    {
        for (int i = 0; i < relativePathsToExtract.Count; i++)
        {
            string relPath = relativePathsToExtract[i];
            string sourcePath = Path.Combine(Application.streamingAssetsPath, relPath);
            string destPath = Path.Combine(Application.persistentDataPath, relPath);

            // Skip if already extracted (e.g. relaunch after first install).
            if (File.Exists(destPath))
            {
                Progress = (i + 1f) / relativePathsToExtract.Count;
                continue;
            }

            yield return CopyFile(sourcePath, destPath);
            Progress = (i + 1f) / relativePathsToExtract.Count;
        }

        IsDone = true;
    }

    private IEnumerator CopyFile(string sourcePath, string destPath)
    {
        string destDir = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: StreamingAssets is compressed inside the APK, so it can only
        // be read via UnityWebRequest, not System.IO.
        using (UnityWebRequest req = UnityWebRequest.Get(sourcePath))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VolStreamingAssetsExtractor] Failed to read '{sourcePath}': {req.error}");
                yield break;
            }

            File.WriteAllBytes(destPath, req.downloadHandler.data);
        }
#else
        // Editor / Windows / Mac / Linux: StreamingAssets is a normal folder on
        // disk, so a direct synchronous copy is simpler and avoids the
        // UnityWebRequest round-trip entirely.
        File.Copy(sourcePath, destPath, overwrite: true);
        yield break;
#endif
    }
}
