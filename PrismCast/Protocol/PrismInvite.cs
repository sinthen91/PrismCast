using System.Text;

namespace PrismCast.Protocol;

internal static class PrismInvite
{
    internal static string Encode(string baseUrl, string token)
    {
        var plain = $"{baseUrl.TrimEnd('/')}|{token}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static bool TryDecode(string code, out string baseUrl, out string token)
    {
        baseUrl = "";
        token = "";
        try
        {
            var normalized = code.Trim().Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => ""
            };

            var plain = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var split = plain.LastIndexOf('|');
            if (split <= 0 || split >= plain.Length - 1)
                return false;

            baseUrl = plain[..split].TrimEnd('/');
            token = plain[(split + 1)..];

            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                   && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                   && token.Length >= 32;
        }
        catch
        {
            return false;
        }
    }
}
