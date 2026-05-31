namespace PostQuantum.AspNetCore.Internal;

/// <summary>
/// Internal helpers for HTTP header construction. Extracted so properties
/// can exercise them without spinning a full request pipeline.
/// </summary>
internal static class HeaderEncoding
{
    /// <summary>
    /// Escapes a string for inclusion inside an RFC 7235 quoted-string.
    /// The only characters that require escaping inside a quoted-string
    /// are <c>\</c> and <c>"</c>; both are prefixed with a single
    /// backslash. The output is wrapped at the caller's discretion.
    /// </summary>
    public static string EscapeForQuotedString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!value.AsSpan().ContainsAny('\\', '"'))
        {
            return value;
        }

        var sb = new System.Text.StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (ch is '\\' or '"')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract a bearer token from an <c>Authorization</c>
    /// header value. Matches the scheme prefix case-insensitively per
    /// RFC 6750. Whitespace-only tokens return <see langword="false"/>.
    /// </summary>
    public static bool TryGetBearerToken(string? authorization, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(authorization))
        {
            return false;
        }

        const string Prefix = PostQuantumJwtBearerDefaults.BearerPrefix;
        if (!authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = authorization[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        token = candidate;
        return true;
    }
}
