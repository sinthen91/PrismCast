using PrismCast.Protocol;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrismCast.Hosting;

internal sealed record PrismRoomMember
{
    public string ViewerId { get; init; } = "";
    public string FirstName { get; init; } = "";
    public bool IsHost { get; init; }
}

internal sealed record PrismRoomInfo
{
    public string RoomId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsPrivate { get; init; }
    public string HostId { get; init; } = "";
    public string InviteCode { get; init; } = "";
    public int MemberCount { get; init; }
    public bool Live { get; init; }
    public string SessionInviteCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string HostName { get; init; } = "";
    public PrismRoomMember[] Members { get; init; } = [];
}

internal sealed record PrismNearbySession
{
    public string CharacterKey { get; init; } = "";
    public string HostName { get; init; } = "";
    public string Title { get; init; } = "";
    public string SessionKind { get; init; } = "temporary";
    public string InviteCode { get; init; } = "";
    public string RoomId { get; init; } = "";
    public string RoomName { get; init; } = "";
}

internal sealed class RelayClient : IDisposable
{
    // PrismCast's code/group directory is application infrastructure, not an end-user setting.
    // Production builds use this endpoint automatically. Developers can override it with the
    // PRISMCAST_DIRECTORY_URL environment variable when testing a local/preview Worker.
    internal const string DefaultDirectoryBaseUrl = "https://prismcast-directory-main-production.up.railway.app";
    internal static string DirectoryBaseUrl =>
        (Environment.GetEnvironmentVariable("PRISMCAST_DIRECTORY_URL") ?? DefaultDirectoryBaseUrl).TrimEnd('/');

    private readonly HttpClient _http = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string EnsureSecret(Configuration config)
    {
        if (!string.IsNullOrWhiteSpace(config.RelaySecret))
            return config.RelaySecret;

        config.RelaySecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return config.RelaySecret;
    }

    internal static string HostIdFromSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes[..10]).ToLowerInvariant();
    }

    internal static string CharacterKey(string characterName)
    {
        var normalized = characterName.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes[..12]).ToLowerInvariant();
    }

    internal async Task AnnounceAsync(string relayBaseUrl, string secret, string inviteCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl))
            return;

        var json = JsonSerializer.Serialize(new { secret, inviteCode });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relayBaseUrl.TrimEnd('/') + "/announce", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<string?> ResolveAsync(string relayBaseUrl, string hostId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl))
            return null;

        using var response = await _http.GetAsync(
            relayBaseUrl.TrimEnd('/') + "/resolve/" + Uri.EscapeDataString(hostId), ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("inviteCode", out var value)
            ? value.GetString()
            : null;
    }

    internal async Task AnnounceTemporaryAsync(string relayBaseUrl, string secret, string code, string inviteCode,
        string title, string hostName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(code))
            return;

        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/temporary/announce",
            new { secret, code, inviteCode, title, hostName }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<string?> ResolveTemporaryAsync(string relayBaseUrl, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(code))
            return null;

        using var response = await _http.GetAsync(
            relayBaseUrl.TrimEnd('/') + "/temporary/resolve/" + Uri.EscapeDataString(code.Trim().ToUpperInvariant()), ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("inviteCode", out var invite) ? invite.GetString() : null;
    }

    internal async Task CloseTemporaryAsync(string relayBaseUrl, string secret, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(code))
            return;
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/temporary/close",
            new { secret, code = code.Trim().ToUpperInvariant() }, JsonOptions, ct).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    internal async Task<PrismRoomInfo> CreateRoomAsync(string relayBaseUrl, string secret, string name, bool isPrivate,
        string viewerId, string firstName, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/create",
            new { secret, name, isPrivate, viewerId, firstName }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<PrismRoomInfo>(response, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The relay returned an invalid room response.");
    }

    internal async Task<PrismRoomInfo> JoinRoomAsync(string relayBaseUrl, string codeOrRoomId,
        string viewerId, string firstName, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/join",
            new { code = codeOrRoomId.Trim(), viewerId, firstName }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<PrismRoomInfo>(response, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The relay returned an invalid room response.");
    }

    internal async Task<PrismRoomInfo?> ResolveRoomAsync(string relayBaseUrl, string roomId, string viewerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(roomId))
            return null;

        var url = relayBaseUrl.TrimEnd('/') + "/room/resolve/" + Uri.EscapeDataString(roomId)
                  + "?viewerId=" + Uri.EscapeDataString(viewerId);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await ReadJsonAsync<PrismRoomInfo>(response, ct).ConfigureAwait(false);
    }

    internal async Task LeaveRoomAsync(string relayBaseUrl, string roomId, string viewerId, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/leave",
            new { roomId, viewerId }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<bool> DeleteRoomAsync(string relayBaseUrl, string secret, string roomId, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/delete",
            new { secret, roomId }, JsonOptions, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    internal async Task KickRoomMemberAsync(string relayBaseUrl, string secret, string roomId, string viewerId,
        CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/kick",
            new { secret, roomId, viewerId }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task AnnounceRoomSessionAsync(string relayBaseUrl, string secret, string roomId, string inviteCode,
        string title, string hostName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(roomId))
            return;

        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/announce",
            new { secret, roomId, inviteCode, title, hostName }, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task CloseRoomSessionAsync(string relayBaseUrl, string secret, string roomId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(roomId))
            return;
        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/room/close-session",
            new { secret, roomId }, JsonOptions, ct).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    internal async Task AnnounceNearbyAsync(string relayBaseUrl, string secret, string characterKey,
        string sessionKind, string inviteCode, string title, string hostName, string roomId, string roomName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(characterKey))
            return;

        using var response = await _http.PostAsJsonAsync(relayBaseUrl.TrimEnd('/') + "/nearby/announce",
            new { secret, characterKey, sessionKind, inviteCode, title, hostName, roomId, roomName }, JsonOptions, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<PrismNearbySession?> ResolveNearbyAsync(string relayBaseUrl, string characterKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(characterKey))
            return null;

        using var response = await _http.GetAsync(
            relayBaseUrl.TrimEnd('/') + "/nearby/resolve/" + Uri.EscapeDataString(characterKey), ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await ReadJsonAsync<PrismNearbySession>(response, ct).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public void Dispose() => _http.Dispose();
}
