using Dalamud.Plugin.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace PrismCast.Hosting;

internal sealed record DesktopCaptureTarget(string Id, string Label, int X, int Y, int Width, int Height)
{
    internal bool IsDesktop => Id.Equals("desktop", StringComparison.OrdinalIgnoreCase);
}

internal sealed record ScreenShareEncodingProfile(
    int Id,
    string Name,
    int Width,
    int Height,
    int FramesPerSecond,
    int VideoBitrateKbps,
    int MaximumBitrateKbps,
    int BufferSizeKbps)
{
    internal string Summary => $"{Width}x{Height} • {FramesPerSecond} FPS • {VideoBitrateKbps / 1000.0:0.#} Mbps";
}

internal sealed class DesktopCaptureStreamer : IDisposable
{
    internal static readonly ScreenShareEncodingProfile[] QualityProfiles =
    [
        new(0, "Performance", 1280, 720, 30, 4500, 4500, 9000),
        new(1, "High Quality", 1920, 1080, 30, 8000, 8000, 16000),
        new(2, "High Motion", 1920, 1080, 60, 12000, 15000, 24000),
    ];

    private readonly DependencyManager _deps;
    private readonly IPluginLog _log;
    private readonly string _outputDirectory;
    private Process? _ffmpeg;
    private NamedPipeServerStream? _audioPipe;
    private WasapiLoopbackSource? _audioCapture;
    private BlockingCollection<byte[]>? _audioQueue;
    private Task? _audioWriter;
    private CancellationTokenSource? _cts;

    internal string OutputDirectory => _outputDirectory;
    internal bool Running => _ffmpeg is { HasExited: false };

    internal DesktopCaptureStreamer(string configDirectory, DependencyManager deps, IPluginLog log)
    {
        _deps = deps;
        _log = log;
        _outputDirectory = Path.Combine(configDirectory, "screen-share");
    }

    internal static IReadOnlyList<DesktopCaptureTarget> GetTargets()
    {
        var result = new List<DesktopCaptureTarget>
        {
            new("desktop", "Entire desktop", 0, 0, 0, 0)
        };
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) <= 0)
                return true;
            var builder = new StringBuilder(GetWindowTextLength(handle) + 1);
            _ = GetWindowText(handle, builder, builder.Capacity);
            var title = builder.ToString().Trim();
            if (title.Length > 0 && !title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) &&
                !IsIconic(handle) && TryGetCaptureBounds(handle, out var bounds))
                result.Add(new(handle.ToInt64().ToString("X"), title,
                    bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            return true;
        }, IntPtr.Zero);
        return result.DistinctBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Take(100).ToArray();
    }

    internal static IReadOnlyList<WasapiEndpoint> GetAudioEndpoints() => WasapiLoopbackSource.GetRenderEndpoints();

    internal static ScreenShareEncodingProfile GetQualityProfile(int id) =>
        QualityProfiles.FirstOrDefault(x => x.Id == id) ?? QualityProfiles[0];

    internal async Task StartAsync(DesktopCaptureTarget target, WasapiEndpoint audioEndpoint,
        ScreenShareEncodingProfile profile, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        await _deps.EnsureCaptureAsync(ct).ConfigureAwait(false);
        Directory.CreateDirectory(_outputDirectory);
        foreach (var file in Directory.EnumerateFiles(_outputDirectory))
            try { File.Delete(file); } catch { }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pipeName = "prismcast-audio-" + Guid.NewGuid().ToString("N");
        _audioPipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _audioCapture = new WasapiLoopbackSource(audioEndpoint.Id);
        var format = _audioCapture.Format;
        var sampleFormat = format.FfmpegSampleFormat;

        var start = new ProcessStartInfo(_deps.FfmpegExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-f", "gdigrab", "-framerate",
            profile.FramesPerSecond.ToString(), "-draw_mouse", "1"
        };
        if (!target.IsDesktop)
        {
            var region = ResolveCurrentBounds(target);
            arguments.AddRange(["-offset_x", region.X.ToString(), "-offset_y", region.Y.ToString(),
                "-video_size", $"{region.Width}x{region.Height}"]);
        }
        arguments.AddRange([
            "-i", "desktop",
            "-thread_queue_size", "1024", "-f", sampleFormat, "-ar", format.SampleRate.ToString(),
            "-ac", format.Channels.ToString(), "-i", $@"\\.\pipe\{pipeName}",
            "-vf", $"scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2", "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-pix_fmt", "yuv420p",
            "-g", Math.Max(1, profile.FramesPerSecond / 2).ToString(), "-keyint_min", Math.Max(1, profile.FramesPerSecond / 2).ToString(), "-sc_threshold", "0",
            "-b:v", $"{profile.VideoBitrateKbps}k", "-maxrate", $"{profile.MaximumBitrateKbps}k", "-bufsize", $"{profile.BufferSizeKbps}k",
            "-c:a", "aac", "-b:a", "160k", "-ar", "48000",
            "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "4",
            "-hls_flags", "delete_segments+append_list+omit_endlist+independent_segments+program_date_time",
            "-hls_segment_filename", Path.Combine(_outputDirectory, "segment-%06d.ts"),
            Path.Combine(_outputDirectory, "index.m3u8")
        ]);
        foreach (var value in arguments) start.ArgumentList.Add(value);

        _ffmpeg = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg could not be started.");
        _ = Task.Run(async () =>
        {
            try
            {
                _ = await _ffmpeg.StandardError.ReadToEndAsync().ConfigureAwait(false);
                if (_ffmpeg.HasExited && _ffmpeg.ExitCode != 0)
                    _log.Debug("PrismCast screen-share encoder exited with code {ExitCode}", _ffmpeg.ExitCode);
            }
            catch { }
        });

        try
        {
            await _audioPipe.WaitForConnectionAsync(_cts.Token).WaitAsync(TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
            _audioQueue = new BlockingCollection<byte[]>(64);
            _audioWriter = Task.Run(() => WriteAudioAsync(_cts.Token));
            _audioCapture.Start(bytes => _audioQueue.TryAdd(bytes));

            var playlist = Path.Combine(_outputDirectory, "index.m3u8");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((!File.Exists(playlist) || new FileInfo(playlist).Length == 0) && DateTime.UtcNow < deadline)
            {
                if (_ffmpeg.HasExited)
                    throw new InvalidOperationException("Screen capture stopped before producing video. The selected window may no longer exist.");
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            if (!File.Exists(playlist))
                throw new TimeoutException("Screen capture did not become ready in time.");
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task WriteAudioAsync(CancellationToken ct)
    {
        try
        {
            if (_audioQueue is null || _audioPipe is null)
                return;
            foreach (var bytes in _audioQueue.GetConsumingEnumerable(ct))
                await _audioPipe.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    internal async Task StopAsync()
    {
        var capture = _audioCapture;
        _audioCapture = null;
        if (capture is not null)
        {
            try { capture.Stop(); } catch { }
            capture.Dispose();
        }
        _audioQueue?.CompleteAdding();
        _cts?.Cancel();
        if (_audioWriter is not null)
            try { await _audioWriter.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        _audioWriter = null;
        _audioQueue?.Dispose();
        _audioQueue = null;
        _audioPipe?.Dispose();
        _audioPipe = null;

        var process = _ffmpeg;
        _ffmpeg = null;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            process.Dispose();
        }
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static (int X, int Y, int Width, int Height) ResolveCurrentBounds(DesktopCaptureTarget target)
    {
        var bounds = new Rect(target.X, target.Y, target.X + target.Width, target.Y + target.Height);
        if (long.TryParse(target.Id, System.Globalization.NumberStyles.HexNumber, null, out var raw) &&
            IsWindow(new IntPtr(raw)) && TryGetCaptureBounds(new IntPtr(raw), out var current))
            bounds = current;

        var virtualLeft = GetSystemMetrics(76);
        var virtualTop = GetSystemMetrics(77);
        var virtualRight = virtualLeft + GetSystemMetrics(78);
        var virtualBottom = virtualTop + GetSystemMetrics(79);
        var left = Math.Clamp(bounds.Left, virtualLeft, virtualRight - 2);
        var top = Math.Clamp(bounds.Top, virtualTop, virtualBottom - 2);
        var right = Math.Clamp(bounds.Right, left + 2, virtualRight);
        var bottom = Math.Clamp(bounds.Bottom, top + 2, virtualBottom);
        var width = Math.Max(2, (right - left) & ~1);
        var height = Math.Max(2, (bottom - top) & ~1);
        return (left, top, width, height);
    }

    private static bool TryGetCaptureBounds(IntPtr handle, out Rect bounds)
    {
        if (DwmGetWindowAttribute(handle, 9, out bounds, Marshal.SizeOf<Rect>()) != 0 &&
            !GetWindowRect(handle, out bounds))
            return false;
        return bounds.Right - bounds.Left >= 64 && bounds.Bottom - bounds.Top >= 64;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom;
        internal Rect(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; } }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
}
