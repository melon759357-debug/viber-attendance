using System.Text.RegularExpressions;

namespace ViberAttendance.Infrastructure;

public static partial class DiagnosticText
{
    public static string Sanitize(string? value)
    {
        var text = value ?? string.Empty;
        text = PrivateKeyFieldPattern().Replace(text, "\"private_key\":\"<redacted-key>\"");
        text = PemPattern().Replace(text, "<redacted-key>");
        text = UrlPattern().Replace(text, "<redacted-url>");
        text = EmailPattern().Replace(text, "<redacted-email>");
        text = PhonePattern().Replace(text, "<redacted-phone>");
        return text.Length <= 4_000 ? text : text[..4_000] + "…<truncated>";
    }

    [GeneratedRegex(@"""private_key""\s*:\s*""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyFieldPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PemPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\w)\+?\d[\d\s().-]{7,}\d(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
