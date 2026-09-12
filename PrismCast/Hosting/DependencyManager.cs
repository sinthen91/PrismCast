using Dalamud.Plugin.Services;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PrismCast.Hosting;

internal sealed class DependencyManager : IDisposable
{
    private readonly string _root;
    private readonly IPluginLog _log;
    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _ensureGate = new(1, 1);

    internal string MpvDll => Path.Combine(_root, "libmpv-2.dll");
    internal string YtDlpExe => Path.Combine(_root, "yt-dlp.exe");
    internal string DenoExe => Path.Combine(_root, "deno.exe");
    internal string CloudflaredExe => Path.Combine(_root, "cloudflared.exe");
    internal string FfmpegExe => Path.Combine(_root, "ffmpeg.exe");

    internal bool ReadyForPlayback =>
        File.Exists(MpvDll) && File.Exists(YtDlpExe) && File.Exists(DenoExe);

    internal bool ReadyForHosting => File.Exists(CloudflaredExe);
    internal bool ReadyForCapture => File.Exists(FfmpegExe);

    internal string Status { get; private set; } = "Not initialized";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    public DependencyManager(string configDirectory, IPluginLog log)
    {
        _root = Path.Combine(configDirectory, "runtime");
        _log = log;
        Directory.CreateDirectory(_root);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PrismCast", "0.2"));
    }

    internal async Task EnsurePlaybackAsync(CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReadyForPlayback)
            {
                PrepareRuntimePath();
                Status = "Playback ready";
                return;
            }

            Status = "Downloading playback runtime";
            Directory.CreateDirectory(_root);

            if (!File.Exists(MpvDll))
                await DownloadMpvAsync(cancellationToken).ConfigureAwait(false);

            if (!File.Exists(YtDlpExe))
                await DownloadDirectAsync(
                    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                    YtDlpExe, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(DenoExe))
                await DownloadDenoAsync(cancellationToken).ConfigureAwait(false);

            PrepareRuntimePath();
            Status = "Playback ready";
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    internal async Task EnsureTunnelAsync(CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReadyForHosting)
            {
                Status = ReadyForPlayback ? "Ready" : "Hosting runtime ready";
                return;
            }

            Status = "Downloading hosting runtime";
            Directory.CreateDirectory(_root);

            await DownloadDirectAsync(
                "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe",
                CloudflaredExe, cancellationToken).ConfigureAwait(false);

            Status = ReadyForPlayback ? "Ready" : "Hosting runtime ready";
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    internal async Task EnsureCaptureAsync(CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReadyForCapture)
            {
                Status = "Screen capture ready";
                return;
            }

            Status = "Downloading screen-capture runtime";
            Directory.CreateDirectory(_root);
            var zipPath = Path.Combine(_root, "ffmpeg.zip");
            try
            {
                await DownloadDirectAsync(
                    "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
                    zipPath, cancellationToken).ConfigureAwait(false);
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    throw new InvalidOperationException("The FFmpeg package did not contain ffmpeg.exe.");
                entry.ExtractToFile(FfmpegExe, true);
            }
            finally
            {
                try { File.Delete(zipPath); } catch { }
            }
            Status = "Screen capture ready";
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    internal void PrepareRuntimePath()
    {
        _ = SetDllDirectory(_root);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Contains(_root, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", _root + ";" + path);
    }

    private async Task DownloadMpvAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync(
            "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest", ct).ConfigureAwait(false);

        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => new
            {
                Name = a.GetProperty("name").GetString() ?? "",
                Url = a.GetProperty("browser_download_url").GetString() ?? ""
            })
            .FirstOrDefault(a =>
                a.Name.StartsWith("mpv-dev-lgpl-x86_64-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));

        if (asset is null || asset.Url.Length == 0)
            throw new InvalidOperationException("Could not locate the current libmpv Windows package.");

        var archivePath = Path.Combine(_root, "mpv.7z");
        try
        {
            await DownloadDirectAsync(asset.Url, archivePath, ct).ConfigureAwait(false);

            await using var stream = File.OpenRead(archivePath);
            using var archive = ArchiveFactory.OpenArchive(stream);

            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory &&
                string.Equals(Path.GetFileName(e.Key), "libmpv-2.dll", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                throw new InvalidOperationException("The downloaded mpv package did not contain libmpv-2.dll.");

            entry.WriteToFile(MpvDll, new ExtractionOptions { Overwrite = true });
        }
        finally
        {
            try { File.Delete(archivePath); } catch { }
        }
    }

    private async Task DownloadDenoAsync(CancellationToken ct)
    {
        const string api = "https://api.github.com/repos/denoland/deno/releases/latest";
        using var doc = await GetJsonAsync(api, ct).ConfigureAwait(false);

        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => new
            {
                Name = a.GetProperty("name").GetString() ?? "",
                Url = a.GetProperty("browser_download_url").GetString() ?? ""
            })
            .FirstOrDefault(a => a.Name.Equals(
                "deno-x86_64-pc-windows-msvc.zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null || asset.Url.Length == 0)
            throw new InvalidOperationException("Could not locate the current Deno Windows package.");

        var zipPath = Path.Combine(_root, "deno.zip");
        try
        {
            await DownloadDirectAsync(asset.Url, zipPath, ct).ConfigureAwait(false);

            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("deno.exe", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException("The downloaded Deno package did not contain deno.exe.");

            entry.ExtractToFile(DenoExe, true);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task DownloadDirectAsync(string url, string destination, CancellationToken ct)
    {
        _log.Information("PrismCast downloading {Url}", url);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var temporary = destination + ".download";
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);

            File.Move(temporary, destination, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _ensureGate.Dispose();
    }
}
