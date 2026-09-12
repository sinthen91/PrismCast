using Dalamud.Plugin.Services;
using PrismCast.Hosting;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace PrismCast.Playback;

internal sealed class VideoEngine : IDisposable
{
    internal const int DefaultWidth = 1280;
    internal const int DefaultHeight = 720;
    internal const int MaximumWidth = 1920;
    internal const int MaximumHeight = 1080;

    private readonly PrismDx _dx;
    private readonly DependencyManager _deps;
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly WorldScreenRenderer _screen;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);

    private MpvSoftwareRenderer? _mpv;
    private Texture2D? _texture;
    private int _width = DefaultWidth;
    private int _height = DefaultHeight;
    private byte[] _frame = new byte[DefaultWidth * DefaultHeight * 4];
    private int _uploadedVersion = -1;
    private bool _subtitlesEnabled;

    // The opening movie has its own renderer and texture. Keeping it separate from the
    // session renderer means opening PrismCast can never replace or seek hosted media.
    private const int StartupWidth = 540;
    private const int StartupHeight = 960;
    private readonly SemaphoreSlim _startupInitializeGate = new(1, 1);
    private MpvSoftwareRenderer? _startupMpv;
    private Texture2D? _startupTexture;
    private ShaderResourceView? _startupTextureView;
    private byte[] _startupFrame = new byte[StartupWidth * StartupHeight * 4];
    private int _startupUploadedVersion = -1;

    internal WorldScreenRenderer Screen => _screen;
    internal bool Ready => _mpv is not null;
    internal nint StartupTextureHandle => _startupTextureView?.NativePointer ?? nint.Zero;
    internal MpvPlaybackInfo StartupPlaybackInfo => _startupMpv?.ReadInfo() ?? default;

    public VideoEngine(PrismDx dx, DependencyManager deps, IPluginLog log, IFramework framework)
    {
        _dx = dx;
        _deps = deps;
        _log = log;
        _framework = framework;
        _screen = new WorldScreenRenderer(dx, log) { Visible = false };
    }

    internal async Task EnsureInitializedAsync(int volume, CancellationToken ct = default,
        int width = DefaultWidth, int height = DefaultHeight)
    {
        (width, height) = NormalizeResolution(width, height);
        if (_mpv is not null && _width == width && _height == height)
            return;

        await _initializeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mpv is not null && _width == width && _height == height)
                return;

            await _deps.EnsurePlaybackAsync(ct).ConfigureAwait(false);
            _deps.PrepareRuntimePath();

            // Dalamud/game graphics objects must be created from the framework thread.
            // The old alpha initialized these from Task.Run(), which caused the
            // "Not on main thread!" failure when hosting a file.
            await _framework.Run(() => InitializeGraphics(volume, width, height), ct).ConfigureAwait(false);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private void InitializeGraphics(int volume, int width, int height)
    {
        if (_mpv is not null && _width == width && _height == height)
            return;

        ResetSessionRenderer();
        _width = width;
        _height = height;
        _frame = new byte[checked(width * height * 4)];
        _uploadedVersion = -1;

        _texture = new Texture2D(_dx.Device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });

        _screen.SetTarget(_texture);

        var renderer = new MpvSoftwareRenderer(_log);
        try
        {
            renderer.Initialize(width, height, File.Exists(_deps.YtDlpExe) ? _deps.YtDlpExe : null, volume);
            renderer.SetSubtitlesEnabled(_subtitlesEnabled);
            _mpv = renderer;
        }
        catch
        {
            renderer.Dispose();
            _screen.SetTarget(null);
            _texture.Dispose();
            _texture = null;
            throw;
        }
    }

    internal bool Play(string source, double seconds, bool playing) =>
        _mpv?.Play(source, seconds, playing) ?? false;

    internal void Pause(bool paused) => _mpv?.Pause(paused);
    internal void Seek(double seconds) => _mpv?.Seek(seconds);
    internal void Stop() => _mpv?.Stop();
    internal void SetVolume(int volume) => _mpv?.SetVolume(volume);
    internal void SetSpeed(double speed) => _mpv?.SetSpeed(speed);
    internal void SetSubtitlesEnabled(bool enabled)
    {
        _subtitlesEnabled = enabled;
        _mpv?.SetSubtitlesEnabled(enabled);
    }
    internal MpvPlaybackInfo ReadInfo() => _mpv?.ReadInfo() ?? default;

    internal async Task StartStartupVideoAsync(string source, CancellationToken ct = default)
    {
        await _startupInitializeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_startupMpv is null)
            {
                await _deps.EnsurePlaybackAsync(ct).ConfigureAwait(false);
                _deps.PrepareRuntimePath();
                await _framework.Run(InitializeStartupGraphics, ct).ConfigureAwait(false);
            }

            if (_startupMpv?.Play(source, 0, true) != true)
                throw new InvalidOperationException("The PrismCast startup movie could not be started.");
        }
        finally
        {
            _startupInitializeGate.Release();
        }
    }

    private void InitializeStartupGraphics()
    {
        if (_startupMpv is not null)
            return;

        _startupTexture = new Texture2D(_dx.Device, new Texture2DDescription
        {
            Width = StartupWidth,
            Height = StartupHeight,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });
        _startupTextureView = new ShaderResourceView(_dx.Device, _startupTexture);

        var renderer = new MpvSoftwareRenderer(_log);
        try
        {
            renderer.Initialize(StartupWidth, StartupHeight, null, 0);
            _startupMpv = renderer;
        }
        catch
        {
            renderer.Dispose();
            _startupTextureView.Dispose();
            _startupTexture.Dispose();
            _startupTextureView = null;
            _startupTexture = null;
            throw;
        }
    }

    internal void StopStartupVideo()
    {
        _startupMpv?.Stop();
    }

    internal void Tick()
    {
        UploadFrame(_mpv, _texture, _frame, ref _uploadedVersion, _width);
        UploadFrame(_startupMpv, _startupTexture, _startupFrame, ref _startupUploadedVersion, StartupWidth);
    }

    private void UploadFrame(MpvSoftwareRenderer? renderer, Texture2D? texture, byte[] buffer,
        ref int uploadedVersion, int width)
    {
        if (renderer is null || texture is null)
            return;

        var version = renderer.FrameVersion;
        if (version == uploadedVersion || !renderer.TryCopyLatestFrame(buffer))
            return;

        uploadedVersion = version;
        unsafe
        {
            fixed (byte* p = buffer)
                _dx.Device.ImmediateContext.UpdateSubresource(texture, 0, null, (IntPtr)p, width * 4, 0);
        }
    }

    public void Dispose()
    {
        ResetSessionRenderer();
        _startupMpv?.Dispose();
        _screen.Dispose();
        _startupTextureView?.Dispose();
        _startupTexture?.Dispose();
        _startupInitializeGate.Dispose();
        _initializeGate.Dispose();
    }

    private static (int Width, int Height) NormalizeResolution(int width, int height)
    {
        if (width >= MaximumWidth || height >= MaximumHeight)
            return (MaximumWidth, MaximumHeight);
        return (DefaultWidth, DefaultHeight);
    }

    private void ResetSessionRenderer()
    {
        _mpv?.Dispose();
        _mpv = null;
        _screen.SetTarget(null);
        _texture?.Dispose();
        _texture = null;
    }
}
