using System.Text.RegularExpressions;

namespace QuebecsCave.Services.Audit;

/// <summary>
/// Cheap, regex-based redaction over arbitrary JSON / form / query strings.
/// Replaces the value of any field whose name matches the allowlist with
/// a sentinel. Tolerates malformed JSON and works on bodies we don't fully
/// understand. Not a security boundary — just a "don't write secrets to
/// the audit table" backstop.
/// </summary>
public static class BodySanitizer
{
    private const int MaxBodyLength = 4 * 1024;
    private const string Redacted = "***REDACTED***";

    private static readonly string[] SensitiveFields = new[]
    {
        "clientSecret",
        "client_secret",
        "accessToken",
        "access_token",
        "refreshToken",
        "refresh_token",
        "apiKey",
        "api_key",
        "password",
        "authorization",
        "auth",
        "x-api-key",
    };

    private static readonly Regex[] JsonPatterns = SensitiveFields
        .Select(f => new Regex(
            $"\"({Regex.Escape(f)})\"\\s*:\\s*\"[^\"]*\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    // Same shape, but with backslash-escaped quotes — catches the case where
    // a JSON object is itself stringified inside another JSON value.
    private static readonly Regex[] EscapedJsonPatterns = SensitiveFields
        .Select(f => new Regex(
            $"\\\\\"({Regex.Escape(f)})\\\\\"\\s*:\\s*\\\\\"[^\"\\\\]*\\\\\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    private static readonly Regex[] QueryPatterns = SensitiveFields
        .Select(f => new Regex(
            $"({Regex.Escape(f)})=([^&\\s]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    public static string? Sanitize(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;

        var truncated = body.Length > MaxBodyLength
            ? body.Substring(0, MaxBodyLength) + "…[truncated]"
            : body;

        var s = truncated;
        foreach (var rx in JsonPatterns)
        {
            s = rx.Replace(s, m => $"\"{m.Groups[1].Value}\":\"{Redacted}\"");
        }
        foreach (var rx in EscapedJsonPatterns)
        {
            s = rx.Replace(s, m => $"\\\"{m.Groups[1].Value}\\\":\\\"{Redacted}\\\"");
        }
        foreach (var rx in QueryPatterns)
        {
            s = rx.Replace(s, m => $"{m.Groups[1].Value}={Redacted}");
        }
        return s;
    }
}
