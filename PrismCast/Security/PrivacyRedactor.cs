using System.Text.RegularExpressions;

namespace PrismCast.Security;

internal static partial class PrivacyRedactor
{
    [GeneratedRegex(@"(?i)([?&](?:x-plex-token|token|access_token|refresh_token|code)=)[^&\s]+")]
    private static partial Regex SecretQueryValue();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailAddress();

    internal static string Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "The operation failed.";
        var safe = SecretQueryValue().Replace(text, "$1[redacted]");
        return EmailAddress().Replace(safe, "[email redacted]");
    }
}
