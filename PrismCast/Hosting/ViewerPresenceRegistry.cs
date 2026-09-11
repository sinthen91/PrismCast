namespace PrismCast.Hosting;

internal sealed record PrismViewerPresence(string ViewerId, string FirstName, DateTimeOffset JoinedAt);

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
            if (Viewers.TryGetValue(viewerId, out var existing))
                Viewers[viewerId] = existing with { FirstName = firstName };
            else
                Viewers[viewerId] = new PrismViewerPresence(viewerId, firstName, DateTimeOffset.UtcNow);
        }
    }

    internal static IReadOnlyList<string> GetViewerNames()
    {
        lock (Gate)
            return Viewers.Values.OrderBy(x => x.JoinedAt).Select(x => x.FirstName).ToArray();
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
