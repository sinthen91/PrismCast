using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;

namespace PrismCast.Hosting;

internal sealed record PlexLibrary(string Key, string Title, string Type);
internal sealed record PlexItem(
    string RatingKey,
    string Title,
    string Year,
    string Type,
    int Index,
    string Thumb,
    long DurationMs,
    string Summary);
internal sealed record PlexLoginPin(long Id, string Code, string AuthUrl);
internal sealed record PlexServer(string Name, string BaseUrl, string Token, bool Local, bool Relay);
internal sealed record PlexMediaSource(string Title, string Url);

internal sealed class PlexClient : IDisposable
{
    private const string Product = "PrismCast";
    private const string Version = "0.2";
    private readonly HttpClient _http = new();

    internal string BaseUrl { get; set; } = "http://127.0.0.1:32400";
    internal string Token { get; set; } = "";
    internal string ClientIdentifier { get; set; } = "";

    private string Url(string path)
    {
        var separator = path.Contains('?') ? "&" : "?";
        return BaseUrl.TrimEnd('/') + path + separator + "X-Plex-Token=" + Uri.EscapeDataString(Token);
    }

    private void AddPlexHeaders(HttpRequestMessage request, string? token = null)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Product", Product);
        request.Headers.TryAddWithoutValidation("X-Plex-Version", Version);
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", ClientIdentifier);
        request.Headers.TryAddWithoutValidation("X-Plex-Device", "PC");
        request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Windows");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
    }

    internal async Task<PlexLoginPin> BeginSignInAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ClientIdentifier))
            throw new InvalidOperationException("PrismCast is missing its Plex client identifier.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://plex.tv/api/v2/pins?strong=true")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["strong"] = "true" })
        };
        AddPlexHeaders(request);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var id = doc.RootElement.GetProperty("id").GetInt64();
        var code = doc.RootElement.GetProperty("code").GetString();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Plex did not return an authentication code.");

        var authUrl =
            "https://app.plex.tv/auth#?clientID=" + Uri.EscapeDataString(ClientIdentifier) +
            "&code=" + Uri.EscapeDataString(code) +
            "&context%5Bdevice%5D%5Bproduct%5D=" + Uri.EscapeDataString(Product);
        return new PlexLoginPin(id, code, authUrl);
    }

    internal async Task<string> WaitForSignInAsync(long pinId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://plex.tv/api/v2/pins/{pinId}");
            AddPlexHeaders(request);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("authToken", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
            {
                var token = tokenElement.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Plex sign-in timed out. Click Connect to Plex and complete the browser sign-in again.");
    }

    internal async Task<List<PlexServer>> GetServersAsync(string accountToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1&includeIPv6=1");
        AddPlexHeaders(request, accountToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var servers = new List<PlexServer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return servers;

        foreach (var device in doc.RootElement.EnumerateArray())
        {
            var provides = device.TryGetProperty("provides", out var providesElement) ? providesElement.GetString() ?? "" : "";
            if (!provides.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("server", StringComparer.OrdinalIgnoreCase))
                continue;

            var name = device.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "Plex Media Server" : "Plex Media Server";
            var serverToken = device.TryGetProperty("accessToken", out var tokenElement) ? tokenElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(serverToken) ||
                !device.TryGetProperty("connections", out var connections) || connections.ValueKind != JsonValueKind.Array)
                continue;

            var candidates = new List<PlexServer>();
            foreach (var connection in connections.EnumerateArray())
            {
                var uri = connection.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() ?? "" : "";
                if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                    continue;
                var local = connection.TryGetProperty("local", out var localElement) && localElement.GetBoolean();
                var relay = connection.TryGetProperty("relay", out var relayElement) && relayElement.GetBoolean();
                candidates.Add(new PlexServer(name, uri.TrimEnd('/'), serverToken, local, relay));
            }

            foreach (var candidate in candidates.OrderByDescending(x => x.Local).ThenBy(x => x.Relay)
                         .ThenByDescending(x => x.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                if (await TestServerAsync(candidate.BaseUrl, candidate.Token, ct).ConfigureAwait(false))
                {
                    servers.Add(candidate);
                    break;
                }
            }
        }
        return servers;
    }

    internal async Task<bool> TestServerAsync(string baseUrl, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            return false;
        try
        {
            var url = baseUrl.TrimEnd('/') + "/identity?X-Plex-Token=" + Uri.EscapeDataString(token);
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    internal async Task<List<PlexLibrary>> GetLibrariesAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        var xml = await _http.GetStringAsync(Url("/library/sections"), ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        return doc.Descendants("Directory")
            .Select(e => new PlexLibrary((string?)e.Attribute("key") ?? "", (string?)e.Attribute("title") ?? "", (string?)e.Attribute("type") ?? ""))
            .Where(x => x.Key.Length > 0).ToList();
    }

    internal async Task<List<PlexItem>> GetLibraryItemsAsync(PlexLibrary library, CancellationToken ct = default)
    {
        EnsureConfigured();
        var typeQuery = library.Type.Equals("movie", StringComparison.OrdinalIgnoreCase) ? "type=1&" :
            library.Type.Equals("show", StringComparison.OrdinalIgnoreCase) ? "type=2&" : string.Empty;
        var xml = await _http.GetStringAsync(
            Url($"/library/sections/{Uri.EscapeDataString(library.Key)}/all?{typeQuery}sort=titleSort"), ct).ConfigureAwait(false);
        return ParseItems(XDocument.Parse(xml));
    }

    internal async Task<List<PlexItem>> GetChildrenAsync(string ratingKey, CancellationToken ct = default)
    {
        EnsureConfigured();
        var xml = await _http.GetStringAsync(Url($"/library/metadata/{Uri.EscapeDataString(ratingKey)}/children"), ct)
            .ConfigureAwait(false);
        return ParseItems(XDocument.Parse(xml));
    }

    private static List<PlexItem> ParseItems(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return [];

        return root.Elements()
            .Where(e => e.Name.LocalName is "Video" or "Directory")
            .Select(e =>
            {
                _ = int.TryParse((string?)e.Attribute("index") ?? "0", out var index);
                var type = (string?)e.Attribute("type");
                if (string.IsNullOrWhiteSpace(type))
                    type = e.Name.LocalName == "Video" ? "video" : "directory";
                var thumb = (string?)e.Attribute("thumb") ??
                            (string?)e.Attribute("parentThumb") ??
                            (string?)e.Attribute("grandparentThumb") ?? "";
                _ = long.TryParse((string?)e.Attribute("duration") ?? "0", out var durationMs);
                return new PlexItem(
                    (string?)e.Attribute("ratingKey") ?? "",
                    (string?)e.Attribute("title") ?? "",
                    (string?)e.Attribute("year") ?? "",
                    type,
                    index,
                    thumb,
                    durationMs,
                    (string?)e.Attribute("summary") ?? "");
            })
            .Where(x => x.RatingKey.Length > 0 && x.Title.Length > 0)
            .ToList();
    }

    internal string? GetArtworkUrl(PlexItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Thumb) || string.IsNullOrWhiteSpace(Token))
            return null;

        var thumb = item.Thumb;
        if (Uri.TryCreate(thumb, UriKind.Absolute, out var absolute))
        {
            var separator = absolute.Query.Length > 0 ? "&" : "?";
            return absolute + separator + "X-Plex-Token=" + Uri.EscapeDataString(Token);
        }

        var sep = thumb.Contains('?') ? "&" : "?";
        return BaseUrl.TrimEnd('/') + (thumb.StartsWith('/') ? thumb : "/" + thumb) + sep +
               "X-Plex-Token=" + Uri.EscapeDataString(Token);
    }

    internal async Task<PlexMediaSource> ResolveMediaAsync(string ratingKey, string title, CancellationToken ct = default)
    {
        EnsureConfigured();
        var xml = await _http.GetStringAsync(Url($"/library/metadata/{Uri.EscapeDataString(ratingKey)}"), ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        var partKey = doc.Descendants("Part").Select(e => (string?)e.Attribute("key"))
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (string.IsNullOrWhiteSpace(partKey))
            throw new InvalidOperationException("Plex did not return a playable media part for this item.");
        var separator = partKey.Contains('?') ? "&" : "?";
        var url = BaseUrl.TrimEnd('/') + partKey + separator + "X-Plex-Token=" + Uri.EscapeDataString(Token);
        return new PlexMediaSource(title, url);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Token))
            throw new InvalidOperationException("Connect PrismCast to Plex first.");
    }

    public void Dispose() => _http.Dispose();
}
