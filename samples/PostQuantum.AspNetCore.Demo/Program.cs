using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;

// ---------------------------------------------------------------------------
// PostQuantum.AspNetCore demo
//
// A single-process minimal-API server that mints post-quantum JWTs and
// validates them on protected endpoints. The signing key pair is generated
// fresh on every start — never do this in production; persist or distribute
// the verification key out of band.
//
// Try it:
//   dotnet run --project samples/PostQuantum.AspNetCore.Demo
//   # in another shell
//   TOKEN=$(curl -s http://localhost:5000/dev/token | jq -r .token)
//   curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/me
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Ephemeral signing keys for the demo. The private key stays in-process and
// signs new tokens; the verification key is what the auth handler trusts.
var signingKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65, signingKey.ExportMLDsaPublicKey());

builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton(verificationKey);

const string Issuer = "https://demo.postquantum.local";
const string Audience = "https://api.demo.postquantum.local";

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            // Single-process replay defense. Swap to a Redis-backed
            // IPqJwtReplayCache for a horizontally scaled deployment.
            ReplayCache = new InMemoryReplayCache(),
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Public landing page — no auth required.
app.MapGet("/", () => Results.Ok(new
{
    name = "PostQuantum.AspNetCore demo",
    endpoints = Landing.Endpoints,
}));

// Dev-only issuer. In a real app the token comes from your identity provider;
// the verification side stays the same.
app.MapPost("/dev/token", (MLDsa signer) =>
{
    var token = new PqJwtBuilder()
        .WithIssuer(Issuer)
        .WithAudience(Audience)
        .WithSubject("demo-user")
        .WithJwtId(Guid.NewGuid().ToString("N"))
        .WithLifetime(TimeSpan.FromMinutes(15))
        .WithClaim("role", "demo")
        .SignWith(signer)
        .Build();

    return Results.Ok(new { token });
});

// Protected endpoint. Authorize() returns 401 to unauthenticated callers,
// and the [Authorize(Roles = "demo")] attribute equivalent works against
// the "role" claim flattened from the validated token.
app.MapGet("/me", [Authorize] (ClaimsPrincipal user) => Results.Ok(new
{
    sub = user.FindFirstValue("sub"),
    role = user.FindFirstValue("role"),
    issuer = user.FindFirstValue("iss"),
    audience = user.FindFirstValue("aud"),
}));

app.Run();

internal static class Landing
{
    public static readonly string[] Endpoints =
    [
        "GET  /            — this page",
        "POST /dev/token   — mint a demo token (DO NOT ship this endpoint)",
        "GET  /me          — protected; echoes the validated subject + role",
    ];
}
