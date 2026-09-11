using Dalamud.Plugin.Services;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PrismCast.Hosting;

internal sealed partial class CloudflareQuickTunnel : IDisposable
{
    private readonly DependencyManager _deps;
    private readonly IPluginLog _log;
    private Process? _process;

    internal string? PublicUrl { get; private set; }

    public CloudflareQuickTunnel(DependencyManager deps, IPluginLog log)
    {
        _deps = deps;
        _log = log;
    }

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelRegex();

    internal async Task<string> StartAsync(int port, CancellationToken ct = default)
    {
        Stop();
        await _deps.EnsureTunnelAsync(ct).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = new ProcessStartInfo
        {
            FileName = _deps.CloudflaredExe,
            Arguments = $"tunnel --url http://127.0.0.1:{port} --no-autoupdate",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };

        void Parse(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var match = TunnelRegex().Match(line);
            if (match.Success)
                tcs.TrySetResult(match.Value);
        }

        _process.OutputDataReceived += (_, e) => Parse(e.Data);
        _process.ErrorDataReceived += (_, e) => Parse(e.Data);
        _process.Exited += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetException(new InvalidOperationException("cloudflared exited before creating a tunnel."));
        };

        if (!_process.Start())
            throw new InvalidOperationException("Could not start cloudflared.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            PublicUrl = await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            _log.Information("PrismCast tunnel ready: {Url}", PublicUrl);
            return PublicUrl;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    internal void Stop()
    {
        PublicUrl = null;
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        _process.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
