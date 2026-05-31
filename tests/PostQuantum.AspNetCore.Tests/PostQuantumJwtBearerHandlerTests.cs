using System.Net;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// End-to-end fail-closed contract tests. Each case drives a real
/// <c>TestServer</c> running the full ASP.NET Core authentication pipeline
/// and asserts on the HTTP response — the contract that matters to a
/// consumer of this library.
/// </summary>
public sealed class PostQuantumJwtBearerHandlerTests
{
    [PqcFact]
    public async Task ValidToken_ReturnsTwoHundred_AndExposesClaims()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"sub\":\"test-user\"", body, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"tester\"", body, StringComparison.Ordinal);
    }

    [PqcFact]
    public async Task MissingAuthorizationHeader_ReturnsFourOhOneChallenge()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotNull(resp.Headers.WwwAuthenticate);
        Assert.Contains(resp.Headers.WwwAuthenticate, h =>
            string.Equals(h.Scheme, "Bearer", StringComparison.Ordinal));
    }

    [PqcFact]
    public async Task NonBearerScheme_FallsThroughAsNoResult_AndStillChallenges()
    {
        // Non-Bearer scheme is "no result" for the handler — the
        // authorization layer then issues the standard 401 challenge.
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.TryAddWithoutValidation("Authorization", "Basic Zm9vOmJhcg==");
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task TamperedToken_ReturnsFourOhOne()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        // Flip a character mid-payload so the signature no longer verifies.
        var tampered = TamperMiddleSegment(token);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", tampered);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task WrongAudience_ReturnsFourOhOne()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var token = factory.MintToken(audience: "https://wrong.example");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task WrongIssuer_ReturnsFourOhOne()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var token = factory.MintToken(issuer: "https://attacker.example");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task ExpiredToken_ReturnsFourOhOne()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        // exp set well into the past, comfortably outside the validator's
        // default clock skew (60s).
        var token = factory.MintToken(expiration: DateTimeOffset.UtcNow.AddMinutes(-5));

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task PublicEndpoint_IsReachableWithoutCredentials()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/open");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [PqcFact]
    public async Task ChallengeIncludesErrorDetailsByDefault()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        Assert.NotNull(resp.Headers.WwwAuthenticate);
        var bearer = resp.Headers.WwwAuthenticate.FirstOrDefault(h =>
            string.Equals(h.Scheme, "Bearer", StringComparison.Ordinal));
        Assert.NotNull(bearer);
        Assert.Contains("error=\"invalid_token\"", bearer!.Parameter, StringComparison.Ordinal);
    }

    [PqcFact]
    public async Task ChallengeOmitsErrorDetails_WhenOptedOut()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options => options.IncludeErrorDetailsInChallenge = false,
        };
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        var bearer = resp.Headers.WwwAuthenticate.FirstOrDefault(h =>
            string.Equals(h.Scheme, "Bearer", StringComparison.Ordinal));
        Assert.NotNull(bearer);
        Assert.DoesNotContain("error=", bearer!.Parameter, StringComparison.Ordinal);
    }

    [PqcFact]
    public async Task OnTokenValidated_RunsAndCanEnrichPrincipal()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnTokenValidated = ctx =>
                {
                    var identity = (System.Security.Claims.ClaimsIdentity)ctx.Principal.Identity!;
                    identity.AddClaim(new System.Security.Claims.Claim("extra", "from-event"));
                    return Task.CompletedTask;
                };
            },
        };
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"extra\":\"from-event\"", body, StringComparison.Ordinal);
    }

    [PqcFact]
    public async Task OnAuthenticationFailed_RunsOnTamperedToken()
    {
        var fired = false;
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnAuthenticationFailed = ctx =>
                {
                    fired = true;
                    return Task.CompletedTask;
                };
            },
        };
        using var client = factory.CreateClient();
        var tampered = TamperMiddleSegment(factory.MintToken());

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", tampered);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.True(fired, "OnAuthenticationFailed should fire when validation throws.");
    }

    [PqcFact]
    public async Task OnChallenge_CanSuppressDefaultHeader()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnChallenge = ctx =>
                {
                    ctx.Handled = true;
                    ctx.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return Task.CompletedTask;
                };
            },
        };
        using var client = factory.CreateClient();

        using var resp = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(resp.Headers.WwwAuthenticate);
    }

    private static string TamperMiddleSegment(string token)
    {
        // JWT segments are dot-separated base64url. Flip a byte in the
        // payload segment to break the signature without breaking the
        // outer structure.
        var parts = token.Split('.');
        Assert.True(parts.Length >= 3, "expected a compact JWT");
        var payload = parts[1].ToCharArray();
        // Toggle the first character between 'A' and 'B' (both valid base64url).
        payload[0] = payload[0] == 'A' ? 'B' : 'A';
        parts[1] = new string(payload);
        return string.Join('.', parts);
    }
}
