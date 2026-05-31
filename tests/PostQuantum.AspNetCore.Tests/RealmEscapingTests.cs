using System.Net;
using System.Net.Http.Headers;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the RFC 7235 §2.2 quoted-string contract on the
/// WWW-Authenticate header. A realm containing " or \ must produce a
/// well-formed, parseable header.
/// </summary>
public sealed class RealmEscapingTests
{
    [PqcFact]
    public async Task RealmWithQuote_ProducesParseableHeader()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options => options.Realm = "Quoted \"Realm\" Of \\Trouble",
        };
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotNull(resp.Headers.WwwAuthenticate);
        var bearer = resp.Headers.WwwAuthenticate.FirstOrDefault(h =>
            string.Equals(h.Scheme, "Bearer", StringComparison.Ordinal));
        Assert.NotNull(bearer);

        // The raw parameter value should contain backslash-escaped quotes.
        Assert.Contains("\\\"Realm\\\"", bearer!.Parameter, StringComparison.Ordinal);
        Assert.Contains("\\\\Trouble", bearer.Parameter, StringComparison.Ordinal);

        // And it must still parse as a quoted-string when round-tripped
        // through AuthenticationHeaderValue.TryParse.
        Assert.True(AuthenticationHeaderValue.TryParse(
            $"{bearer.Scheme} {bearer.Parameter}", out _));
    }

    [PqcFact]
    public async Task RealmWithoutSpecialChars_IsNotMangled()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options => options.Realm = "plain-realm",
        };
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        var bearer = resp.Headers.WwwAuthenticate.FirstOrDefault(h =>
            string.Equals(h.Scheme, "Bearer", StringComparison.Ordinal));
        Assert.NotNull(bearer);
        Assert.Contains("realm=\"plain-realm\"", bearer!.Parameter, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", bearer.Parameter, StringComparison.Ordinal);
    }
}
