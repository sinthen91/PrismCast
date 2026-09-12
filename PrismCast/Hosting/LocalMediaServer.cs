using Dalamud.Plugin.Services;
using PrismCast.Protocol;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PrismCast.Hosting;

internal sealed class LocalMediaServer : IDisposable
{
    private readonly IPluginLog _log;
    private readonly HttpClient _proxyHttp = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true
    };

    internal int Port { get; private set; }
    internal string Token { get; private set; } = "";
    internal string? MediaPath { get; set; }
    internal string? MediaProxyUrl { get; set; }
    internal string? LiveMediaDirectory { get; set; }
    internal Func<PrismSessionState?>? StateProvider { get; set; }

    public LocalMediaServer(IPluginLog log)
    {
        _log = log;
    }

    internal void Start()
    {
        Stop();
        ViewerPresenceRegistry.BeginSession();

        Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.Debug(ex, "PrismCast local server accept failed");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            string headerText;
            try
            {
                headerText = await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return;

            var request = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (request.Length < 2)
                return;

            var method = request[0].ToUpperInvariant();
            if (method is not ("GET" or "HEAD"))
            {
                await WriteTextAsync(stream, 405, "Method Not Allowed", "text/plain", "Method Not Allowed", true, ct)
                    .ConfigureAwait(false);
                return;
            }

            var rawTarget = request[1];
            if (!Uri.TryCreate("http://127.0.0.1" + rawTarget, UriKind.Absolute, out var uri))
                return;

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("token", out var token) || !CryptographicEquals(token, Token))
            {
                await WriteTextAsync(stream, 403, "Forbidden", "text/plain", "Forbidden", method == "HEAD", ct)
                    .ConfigureAwait(false);
                return;
            }

            if (uri.AbsolutePath.Equals("/presence", StringComparison.OrdinalIgnoreCase))
            {
                if (query.TryGetValue("viewerId", out var viewerId))
                {
                    if (query.TryGetValue("action", out var action) &&
                        action.Equals("leave", StringComparison.OrdinalIgnoreCase))
                        ViewerPresenceRegistry.Remove(viewerId);
                    else if (query.TryGetValue("name", out var name))
                        ViewerPresenceRegistry.Register(viewerId, name);
                }

                await WriteTextAsync(stream, 200, "OK", "application/json", "{\"ok\":true}", method == "HEAD", ct)
                    .ConfigureAwait(false);
                return;
            }

            if (uri.AbsolutePath.Equals("/state", StringComparison.OrdinalIgnoreCase))
            {
                var state = StateProvider?.Invoke();
                if (state is null)
                {
                    await WriteTextAsync(stream, 404, "Not Found", "application/json", "{}", method == "HEAD", ct)
                        .ConfigureAwait(false);
                    return;
                }

                var json = JsonSerializer.Serialize(state, SessionJsonOptions);
                await WriteTextAsync(stream, 200, "OK", "application/json", json, method == "HEAD", ct)
                    .ConfigureAwait(false);
                return;
            }

            if (uri.AbsolutePath.Equals("/media", StringComparison.OrdinalIgnoreCase))
            {
                await ServeMediaAsync(stream, lines, method, ct).ConfigureAwait(false);
                return;
            }

            if (uri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
            {
                await ServeLiveMediaAsync(stream, uri.AbsolutePath[6..], method, ct).ConfigureAwait(false);
                return;
            }

            await WriteTextAsync(stream, 404, "Not Found", "text/plain", "Not Found", method == "HEAD", ct)
                .ConfigureAwait(false);
        }
    }

    private async Task ServeLiveMediaAsync(NetworkStream stream, string requestedName, string method, CancellationToken ct)
    {
        var directory = LiveMediaDirectory;
        var name = Path.GetFileName(requestedName);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name) ||
            !string.Equals(name, requestedName, StringComparison.Ordinal) ||
            !(name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
              name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)))
        {
            await WriteTextAsync(stream, 404, "Not Found", "text/plain", "Live media unavailable", method == "HEAD", ct).ConfigureAwait(false);
            return;
        }

        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            await WriteTextAsync(stream, 404, "Not Found", "text/plain", "Live segment unavailable", method == "HEAD", ct).ConfigureAwait(false);
            return;
        }

        byte[] payload;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                payload = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(30, ct).ConfigureAwait(false);
            }
        }

        var contentType = name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.apple.mpegurl" : "video/mp2t";
        if (name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            var lines = Encoding.UTF8.GetString(payload).Split('\n');
            payload = Encoding.UTF8.GetBytes(string.Join("\n", lines.Select(line =>
                string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')
                    ? line : line.Trim() + "?token=" + Uri.EscapeDataString(Token))));
        }

        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (method != "HEAD")
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    private async Task ServeMediaAsync(NetworkStream stream, string[] headers, string method, CancellationToken ct)
    {
        var path = MediaPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            await ServeLocalFileAsync(stream, headers, method, path, ct).ConfigureAwait(false);
            return;
        }

        var proxyUrl = MediaProxyUrl;
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            await ServeProxyAsync(stream, headers, method, proxyUrl, ct).ConfigureAwait(false);
            return;
        }

        await WriteTextAsync(stream, 404, "Not Found", "text/plain", "Media unavailable", method == "HEAD", ct)
            .ConfigureAwait(false);
    }

    private static async Task ServeLocalFileAsync(NetworkStream stream, string[] headers, string method, string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var length = file.Length;
        long start = 0;
        long end = length - 1;
        var partial = false;

        var rangeLine = headers.FirstOrDefault(h => h.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
        if (rangeLine is not null)
        {
            var value = rangeLine[(rangeLine.IndexOf(':') + 1)..].Trim();
            if (value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value[6..].Split('-', 2);
                if (parts.Length > 0 && long.TryParse(parts[0], out var parsedStart))
                    start = Math.Clamp(parsedStart, 0, Math.Max(0, length - 1));
                if (parts.Length > 1 && long.TryParse(parts[1], out var parsedEnd))
                    end = Math.Clamp(parsedEnd, start, Math.Max(start, length - 1));
                partial = true;
            }
        }

        var count = Math.Max(0, end - start + 1);
        var status = partial ? "206 Partial Content" : "200 OK";
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status}\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("Accept-Ranges: bytes\r\n");
        sb.Append($"Content-Length: {count}\r\n");
        if (partial)
            sb.Append($"Content-Range: bytes {start}-{end}/{length}\r\n");
        sb.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        if (method == "HEAD" || count == 0)
            return;

        file.Position = start;
        var buffer = new byte[1024 * 1024];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct)
                .ConfigureAwait(false);
            if (read <= 0)
                break;
            await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private async Task ServeProxyAsync(NetworkStream stream, string[] headers, string method, string proxyUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, proxyUrl);
        var rangeLine = headers.FirstOrDefault(h => h.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
        if (rangeLine is not null)
        {
            var value = rangeLine[(rangeLine.IndexOf(':') + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
                request.Headers.TryAddWithoutValidation("Range", value);
        }

        using var response = await _proxyHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "OK" : response.ReasonPhrase;
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {(int)response.StatusCode} {reason}\r\n");
        if (response.Content.Headers.ContentType is { } contentType)
            sb.Append($"Content-Type: {contentType}\r\n");
        else
            sb.Append("Content-Type: application/octet-stream\r\n");
        if (response.Content.Headers.ContentLength is { } contentLength)
            sb.Append($"Content-Length: {contentLength}\r\n");
        if (response.Content.Headers.ContentRange is { } contentRange)
            sb.Append($"Content-Range: {contentRange}\r\n");
        if (response.Headers.AcceptRanges.Count > 0)
            sb.Append($"Accept-Ranges: {string.Join(", ", response.Headers.AcceptRanges)}\r\n");
        else
            sb.Append("Accept-Ranges: bytes\r\n");
        sb.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        if (method == "HEAD")
            return;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await source.CopyToAsync(stream, 1024 * 1024, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(4096);
        var one = new byte[1];
        while (bytes.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            bytes.Add(one[0]);
            var n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
                break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            var key = Uri.UnescapeDataString(split[0]);
            var value = split.Length > 1 ? Uri.UnescapeDataString(split[1]) : "";
            result[key] = value;
        }
        return result;
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static async Task WriteTextAsync(NetworkStream stream, int code, string reason, string contentType, string body, bool headOnly, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {code} {reason}\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (!headOnly)
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    internal void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;
        Port = 0;
        Token = "";
        MediaPath = null;
        MediaProxyUrl = null;
        LiveMediaDirectory = null;
        StateProvider = null;
        ViewerPresenceRegistry.EndSession();
    }

    public void Dispose()
    {
        Stop();
        _proxyHttp.Dispose();
    }
}
