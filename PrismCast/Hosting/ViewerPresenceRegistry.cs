namespace PrismCast.Hosting;

internal sealed record PrismViewerPresence(
    string ViewerId,
    string FirstName,
    DateTimeOffset JoinedAt,
    DateTimeOffset LastSeenAt);

internal static class ViewerPresenceRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PrismViewerPresence> Viewers = new(StringComparer.Ordinal);

    internal static void BeginSession()
    {
        lock (Gate)
            Viewers.Clear();
    }

    internal static void Register(string viewerId, string firstName)
    {
        viewerId = viewerId.Trim();
        firstName = SanitizeFirstName(firstName);
        if (viewerId.Length is < 8 or > 128 || firstName.Length == 0)
            return;

        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (Viewers.TryGetValue(viewerId, out var existing))
                Viewers[viewerId] = existing with { FirstName = firstName, LastSeenAt = now };
            else
                Viewers[viewerId] = new PrismViewerPresence(viewerId, firstName, now, now);
        }
    }

    internal static void Remove(string viewerId)
    {
        viewerId = viewerId.Trim();
        if (viewerId.Length == 0)
            return;

        lock (Gate)
            Viewers.Remove(viewerId);
    }

    internal static IReadOnlyList<string> GetViewerNames()
    {
        lock (Gate)
        {
            // Viewers refresh every ten seconds. Expiring stale entries also handles a
            // game crash or lost connection where an explicit leave cannot be sent.
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
            foreach (var viewerId in Viewers
                         .Where(x => x.Value.LastSeenAt < cutoff)
                         .Select(x => x.Key)
                         .ToArray())
                Viewers.Remove(viewerId);

            return Viewers.Values.OrderBy(x => x.JoinedAt).Select(x => x.FirstName).ToArray();
        }
    }

    internal static void EndSession()
    {
        lock (Gate)
            Viewers.Clear();
    }

    private static string SanitizeFirstName(string value)
    {
        var first = value.Trim()
            .Split(new[] { ' ', '@', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        var safe = new string(first.Where(c => char.IsLetterOrDigit(c) || c is '-' or '\'').ToArray());
        return safe.Length > 24 ? safe[..24] : safe;
    }
}
