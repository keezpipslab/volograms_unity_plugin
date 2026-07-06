// VolAtlasVideoDriver.cs
// Owns the ONE native video decode for a packed 2x2 texture-atlas mp4.
// Mirrors VolPlayer's private ReadVideoFrame() logic, but exposes the
// resulting Texture2D publicly so multiple VolPlayer "followers" (with
// useSharedVideoTexture = true) can all read from it.
//
// Audio comes from a plain AudioSource playing a SEPARATELY EXTRACTED audio
// file (see setup note below) -- NOT from a Unity VideoPlayer. A VideoPlayer,
// even in APIOnly render mode, still fully decodes the video internally to
// stay in sync -- meaning it would decode this same video a second time,
// competing with the native decode above for the same hardware decoder.
// Confirmed on Quest: this caused a large slowdown, resolved by removing it.
//
// SETUP: extract the atlas mp4's audio track once, offline:
//   ffmpeg -i atlas.mp4 -vn -acodec pcm_s16le atlas_audio.wav
// then import that file into Unity as a normal AudioClip asset and assign it
// to `audioClip` below.
//
// This does NOT touch geometry at all -- geometry stays per-object in
// each follower's own VolPlayer, driven independently.

using System;
using UnityEngine;
using Volograms;

[RequireComponent(typeof(AudioSource))]
public class VolAtlasVideoDriver : MonoBehaviour
{
    [Header("Atlas video path")]
    public VolEnums.PathType atlasVideoPathType;
    public string atlasVideoFile;

    [Header("Playback")]
    public bool isLooping = true;
    public bool audioOn = true;
    [Tooltip("The atlas video's audio track, extracted separately (see file header) and imported as a normal Unity AudioClip asset.")]
    public AudioClip audioClip;

    [Header("Streaming Assets Extraction (optional)")]
    [Tooltip("If assigned, opening waits until this extractor has finished copying files to persistentDataPath. Leave null if you're not using extraction (e.g. reading directly from an already-real folder).")]
    public VolStreamingAssetsExtractor extractor;

    public Texture2D AtlasTexture { get; private set; }
    public bool IsReady { get; private set; }
    public double FrameRate => _framesPerSecond;

    private VolPluginInterface.VolNativeContext _ctx;
    private AudioSource _audioSource;
    private int _currentFrameIndex = -1;
    private long _numFrames;
    private double _framesPerSecond;
    private double _secondsPerFrame;
    private double _accumulatedSeconds;
    private IntPtr _colorPtr;
    private bool _isOpen;

    void Awake()
    {
        _ctx = new VolPluginInterface.VolNativeContext();
        StartCoroutine(OpenWhenReady());
    }

    private System.Collections.IEnumerator OpenWhenReady()
    {
        if (extractor != null)
        {
            yield return new WaitUntil(() => extractor.IsDone);
        }

        string fullPath = atlasVideoPathType.ResolvePath(atlasVideoFile);
        bool opened = _ctx.OpenVideo(fullPath);
        if (!opened)
        {
            Debug.LogError("[VolAtlasVideoDriver] Failed to open atlas video: " + fullPath);
            yield break;
        }

        int width = _ctx.GetVideoWidth();
        int height = _ctx.GetVideoHeight();
        _numFrames = _ctx.GetNumFrames();
        _framesPerSecond = _ctx.GetFrameRate();
        if (_framesPerSecond <= 0.0) _framesPerSecond = 30.0;
        _secondsPerFrame = 1.0 / _framesPerSecond;

        AtlasTexture = new Texture2D(width, height, TextureFormat.RGB24, false, false);

        _isOpen = true;
        IsReady = true; // texture + dimensions exist now; pixel data fills in on first Update

        if (audioOn && audioClip != null)
        {
            if (!TryGetComponent(out _audioSource))
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.clip = audioClip;
            _audioSource.loop = isLooping;
            _audioSource.playOnAwake = false;
            _audioSource.Play();
        }
    }

    void Update()
    {
        if (!_isOpen) return;

        _accumulatedSeconds += Time.deltaTime;
        int desiredFrameIndex = (int)(_accumulatedSeconds / _secondsPerFrame);

        if (_numFrames > 0 && desiredFrameIndex >= _numFrames)
        {
            if (isLooping)
            {
                RestartVideo();
            }
            return;
        }

        if (desiredFrameIndex == _currentFrameIndex) return;

        // Cap how many frames we'll decode in a single Update to catch up. If
        // decoding is slower than realtime, blindly chasing desiredFrameIndex
        // compounds the problem (falls further behind -> more catch-up frames
        // -> slower still). Better to drop frames than spiral.
        const int maxFramesPerUpdate = 4;
        if (desiredFrameIndex - _currentFrameIndex > maxFramesPerUpdate)
        {
            desiredFrameIndex = _currentFrameIndex + maxFramesPerUpdate;
        }

        ReadVideoFrame(_currentFrameIndex, desiredFrameIndex);
        _currentFrameIndex = desiredFrameIndex;
    }

    // Mirrors VolPlayer.Restart()'s pattern: the native decoder is forward-only,
    // so looping means fully closing and reopening the decode session, not just
    // resetting a frame counter.
    private void RestartVideo()
    {
        _ctx.CloseVideo();

        string fullPath = atlasVideoPathType.ResolvePath(atlasVideoFile);
        bool reopened = _ctx.OpenVideo(fullPath);
        if (!reopened)
        {
            Debug.LogError("[VolAtlasVideoDriver] Failed to reopen atlas video on loop: " + fullPath);
            _isOpen = false;
            return;
        }

        _currentFrameIndex = -1;
        _accumulatedSeconds = 0.0;
    }

    // Same skip-ahead pattern VolPlayer.ReadVideoFrame uses internally.
    private void ReadVideoFrame(int currentFrameIndex, int desiredFrameIndex)
    {
        if (currentFrameIndex >= desiredFrameIndex) return;

        for (int i = currentFrameIndex; i < desiredFrameIndex - 1; i++)
        {
            _colorPtr = _ctx.ReadNextVideoFrame(false);
        }
        _colorPtr = _ctx.ReadNextVideoFrame(true);

        if (AtlasTexture != null && _colorPtr != IntPtr.Zero)
        {
            AtlasTexture.LoadRawTextureData(_colorPtr, (int)_ctx.GetFrameSize());
            AtlasTexture.Apply();
        }
    }

    void OnDestroy()
    {
        try
        {
            if (_isOpen) _ctx.CloseVideo();
        }
        finally
        {
            _ctx?.Dispose();
            _ctx = null;
        }
    }
}

