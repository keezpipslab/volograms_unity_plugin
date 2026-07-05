// VolAtlasVideoDriver.cs
// Owns the ONE native video decode for a packed 2x2 texture-atlas mp4.
// Mirrors VolPlayer's private ReadVideoFrame() logic, but exposes the
// resulting Texture2D publicly so multiple VolPlayer "followers" (with
// useSharedVideoTexture = true) can all read from it.
//
// Also owns a single audio-only Unity VideoPlayer (renderMode = APIOnly,
// audioOutputMode = Direct) that plays the SAME atlas mp4's audio track --
// this is the one place audio comes from for the whole group, mirroring
// how VolPlayer normally handles audio for a single vologram.
//
// This does NOT touch geometry at all -- geometry stays per-object in
// each follower's own VolPlayer, driven independently.

using System;
using UnityEngine;
using UnityEngine.Video;
using Volograms;

public class VolAtlasVideoDriver : MonoBehaviour
{
    [Header("Atlas video path")]
    public VolEnums.PathType atlasVideoPathType;
    public string atlasVideoFile;

    [Header("Playback")]
    public bool isLooping = true;
    public bool audioOn = true;

    public Texture2D AtlasTexture { get; private set; }
    public bool IsReady { get; private set; }
    public double FrameRate => _framesPerSecond;

    private VolPluginInterface.VolNativeContext _ctx;
    private VideoPlayer _audioPlayer;
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

        string fullPath = atlasVideoPathType.ResolvePath(atlasVideoFile);
        bool opened = _ctx.OpenVideo(fullPath);
        if (!opened)
        {
            Debug.LogError("[VolAtlasVideoDriver] Failed to open atlas video: " + fullPath);
            return;
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

        if (audioOn)
        {
            StartCoroutine(SetUpAudio(fullPath));
        }
    }

    private System.Collections.IEnumerator SetUpAudio(string fullPath)
    {
        if (!TryGetComponent(out _audioPlayer))
        {
            _audioPlayer = gameObject.AddComponent<VideoPlayer>();
        }

        _audioPlayer.Stop();
        _audioPlayer.source = VideoSource.Url;
        _audioPlayer.url = fullPath;
        _audioPlayer.renderMode = VideoRenderMode.APIOnly; // we never read its video frames
        _audioPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        _audioPlayer.EnableAudioTrack(0, true);
        _audioPlayer.SetDirectAudioVolume(0, 1f);
        _audioPlayer.SetDirectAudioMute(0, false);
        _audioPlayer.controlledAudioTrackCount = 1;
        _audioPlayer.isLooping = isLooping;
        _audioPlayer.errorReceived += (source, message) =>
            Debug.LogError("[VolAtlasVideoDriver] Audio VideoPlayer error: " + message);

        _audioPlayer.Prepare();
        yield return new WaitUntil(() => _audioPlayer.isPrepared);
        _audioPlayer.Play();
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
                _accumulatedSeconds = 0.0;
                _currentFrameIndex = -1;
                desiredFrameIndex = 0;
                // Note: the audio VideoPlayer above loops on its own timeline via
                // isLooping; since both started together off the same fullPath and
                // fps, they stay close, but for long sessions consider periodically
                // resyncing _accumulatedSeconds to _audioPlayer.time if drift shows up.
            }
            else
            {
                return; // hold last frame
            }
        }

        if (desiredFrameIndex == _currentFrameIndex) return;

        ReadVideoFrame(_currentFrameIndex, desiredFrameIndex);
        _currentFrameIndex = desiredFrameIndex;
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

