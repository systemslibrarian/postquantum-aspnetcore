using System.Net.Http.Headers;
using System.Text;
using PostQuantum.AspNetCore.Internal;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Property-flavoured tests for the small pure helpers in
/// <see cref="HeaderEncoding"/>. We don't pull in FsCheck — the surface
/// is small enough that a deterministic-seeded random fuzz loop in
/// pure xUnit-v3 covers it without dragging in a second testing
/// framework. The seed is fixed so a failure reproduces locally.
/// </summary>
public sealed class HeaderEncodingProperties
{
    private const int Iterations = 1000;
    private const int Seed = unchecked((int)0xCAFEBABE);
    private static readonly string[] BearerPrefixCases =
        ["Bearer ", "bearer ", "BEARER ", "BeArEr "];

    /// <summary>
    /// For any string without CR/LF/NUL (those terminate header fields and
    /// belong to upstream validation), escaping it and wrapping in quotes
    /// produces a value that <see cref="AuthenticationHeaderValue.TryParse"/>
    /// accepts. This is the load-bearing claim that the realm parameter
    /// of <c>WWW-Authenticate</c> stays well-formed regardless of what
    /// the operator configures.
    /// </summary>
    [Fact]
    public void EscapedRealm_AlwaysProducesParseableHeader()
    {
        var rng = new Random(Seed);
        for (var i = 0; i < Iterations; i++)
        {
            var realm = NextRandomString(rng);
            if (realm.AsSpan().ContainsAny('\r', '\n', '\0'))
            {
                continue;
            }

            var escaped = HeaderEncoding.EscapeForQuotedString(realm);
            var header = $"Bearer realm=\"{escaped}\"";
            Assert.True(
                AuthenticationHeaderValue.TryParse(header, out _),
                $"Iteration {i}: realm {Encode(realm)} produced unparseable header {Encode(header)}.");
        }
    }

    /// <summary>
    /// Escaping is a no-op (reference-equal output) on strings without
    /// <c>\</c> or <c>"</c> — preserves the cheap path.
    /// </summary>
    [Fact]
    public void EscapeForQuotedString_NoopOnSafeStrings()
    {
        var rng = new Random(Seed ^ 1);
        for (var i = 0; i < Iterations; i++)
        {
            var input = NextRandomString(rng);
            if (input.AsSpan().ContainsAny('\\', '"'))
            {
                continue;
            }

            Assert.Same(input, HeaderEncoding.EscapeForQuotedString(input));
        }
    }

    /// <summary>
    /// Backslashes and quotes in input each produce exactly one
    /// backslash + the original character in output. Counting verifies.
    /// </summary>
    [Fact]
    public void EscapeForQuotedString_CountsBalance()
    {
        var rng = new Random(Seed ^ 2);
        for (var i = 0; i < Iterations; i++)
        {
            var input = NextRandomString(rng);
            var output = HeaderEncoding.EscapeForQuotedString(input);

            var inputQuotes = input.Count(c => c == '"');
            var inputBackslashes = input.Count(c => c == '\\');
            var outputQuotes = output.Count(c => c == '"');
            var outputBackslashes = output.Count(c => c == '\\');

            Assert.Equal(inputQuotes, outputQuotes);
            Assert.Equal(inputQuotes + 2 * inputBackslashes, outputBackslashes);
        }
    }

    /// <summary>
    /// Any case variation of "Bearer " followed by a non-whitespace
    /// token must be accepted (RFC 6750 §2.1).
    /// </summary>
    [Fact]
    public void CaseInsensitiveBearer_AnyCaseAccepted()
    {
        var rng = new Random(Seed ^ 3);
        for (var i = 0; i < Iterations; i++)
        {
            var token = NextRandomString(rng);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            foreach (var prefix in BearerPrefixCases)
            {
                var header = prefix + token;
                Assert.True(
                    HeaderEncoding.TryGetBearerToken(header, out var extracted),
                    $"Iteration {i}: prefix {Encode(prefix)} + token {Encode(token)} unexpectedly rejected.");
                Assert.Equal(token, extracted);
            }
        }
    }

    /// <summary>
    /// Anything that doesn't start with "Bearer " (any case) returns
    /// <see langword="false"/>.
    /// </summary>
    [Fact]
    public void NonBearerScheme_Rejected()
    {
        var rng = new Random(Seed ^ 4);
        for (var i = 0; i < Iterations; i++)
        {
            var s = NextRandomString(rng);
            if (s.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.False(
                HeaderEncoding.TryGetBearerToken(s, out _),
                $"Iteration {i}: input {Encode(s)} was unexpectedly accepted as a bearer header.");
        }
    }

    [Fact]
    public void NullOrEmpty_AlwaysReturnsFalse()
    {
        Assert.False(HeaderEncoding.TryGetBearerToken(null, out _));
        Assert.False(HeaderEncoding.TryGetBearerToken(string.Empty, out _));
        Assert.False(HeaderEncoding.TryGetBearerToken("Bearer ", out _));
        Assert.False(HeaderEncoding.TryGetBearerToken("Bearer   ", out _));
    }

    // Generate a varied mix of ASCII, special chars, control chars,
    // and random Unicode to cover the realm/bearer parser corner cases.
    private static string NextRandomString(Random rng)
    {
        var length = rng.Next(0, 32);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            var pick = rng.Next(0, 100);
            sb.Append(pick switch
            {
                < 5 => '"',
                < 10 => '\\',
                < 15 => (char)rng.Next(0, 32),         // control chars
                < 60 => (char)rng.Next(' ', 127),       // printable ASCII
                _ => (char)rng.Next(0x80, 0x800),       // 2-byte UTF
            });
        }

        return sb.ToString();
    }

    private static string Encode(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
