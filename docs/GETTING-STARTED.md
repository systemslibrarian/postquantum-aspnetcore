# Getting started

Zero to working post-quantum-authenticated API in 10 minutes. Follow
this verbatim once — it's deliberately the simplest happy path.

## Prerequisites

- **.NET 10 SDK.** `dotnet --version` should report `10.x`.
- **PQ-capable runtime.** On Windows recent enough or Linux with
  OpenSSL 3.5+, the BCL `MLDsa` / `MLKem` primitives are available.
  If you're on Linux with older OpenSSL, see
  [`KNOWN-GAPS.md`](../KNOWN-GAPS.md#tooling--environment).

Quick sanity check:

```bash
dotnet --list-sdks      # at least one 10.x
```

```csharp
// In an `dotnet script` REPL or a throwaway console app:
Console.WriteLine($"MLDsa supported: {System.Security.Cryptography.MLDsa.IsSupported}");
// → True
```

If that prints `False`, fix the runtime first; the library will fail
closed at startup otherwise.

## Step 1 — Scaffold an API

```bash
dotnet new webapi -n MyPqApi -minimal
cd MyPqApi
```

## Step 2 — Install the package

```bash
dotnet add package PostQuantum.AspNetCore --version 0.8.0-preview.1
```

That transitively pulls in `PostQuantum.Jwt`. No other packages are
required for the basic happy path.

## Step 3 — Wire authentication

Replace `Program.cs` with:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;

var builder = WebApplication.CreateBuilder(args);

// FOR DEV ONLY: generate a fresh key pair on each start.
// In production: load `verificationKey` from configuration (the public
// half published by your issuer), and let your issuer service own the
// `signingKey` (the private half).
var signingKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65, signingKey.ExportMLDsaPublicKey());
builder.Services.AddSingleton(signingKey);

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer   = "https://demo.local",
            ValidAudience = "https://demo.local/api",
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Public — no auth needed.
app.MapGet("/", () => "PostQuantum.AspNetCore getting-started demo.");

// DEV ONLY token-mint endpoint. NEVER ship this as written —
// a real issuer is its own service that holds the signing key offline.
app.MapPost("/dev/token", (MLDsa signer, string user) =>
{
    var token = new PqJwtBuilder()
        .WithIssuer("https://demo.local")
        .WithAudience("https://demo.local/api")
        .WithSubject(user)
        .WithJwtId(Guid.NewGuid().ToString("N"))
        .WithLifetime(TimeSpan.FromMinutes(10))
        .WithClaim("role", "demo")
        .SignWith(signer)
        .Build();
    return Results.Ok(new { token });
});

// Protected.
app.MapGet("/me", [Authorize] (ClaimsPrincipal user) => Results.Ok(new
{
    sub  = user.FindFirstValue("sub"),
    role = user.FindFirstValue("role"),
}));

app.Run();
```

## Step 4 — Run

```bash
dotnet run
```

You should see something like:

```
Now listening on: http://localhost:5xxx
```

## Step 5 — Exercise it

In another shell:

```bash
# 1. Hit the public endpoint — no auth needed.
curl http://localhost:5xxx/

# 2. Hit the protected endpoint without a token — 401.
curl -i http://localhost:5xxx/me

# 3. Mint a token.
TOKEN=$(curl -s -X POST "http://localhost:5xxx/dev/token?user=alice" | jq -r .token)
echo "Token is $(echo $TOKEN | wc -c) chars (expect ~4500)"

# 4. Hit the protected endpoint WITH the token — 200.
curl -H "Authorization: Bearer $TOKEN" http://localhost:5xxx/me
# → {"sub":"alice","role":"demo"}
```

That's it. You've validated a hybrid post-quantum JWT through the
standard ASP.NET Core authentication pipeline.

## What just happened

1. **`AddPostQuantumJwtBearer`** registered a fail-closed authentication
   handler that delegates token validation to `PqJwtValidator` from
   the engine library. The same shape as `AddJwtBearer` — only the
   algorithm changed.
2. The **token** carries an `ML-DSA-65` signature (~3.3 KB after
   base64url encoding). That's why the token is ~4.5 KB — vs ~200
   bytes for a classical HMAC JWT. The size is the price of
   quantum resistance.
3. **Authorization** is unchanged. `[Authorize]`, roles, policies — all
   the standard ASP.NET Core machinery works because the handler emits
   a real `ClaimsPrincipal`.

## What to do next

- **Read [`RECIPES.md`](RECIPES.md)** for copy-paste-able scenarios:
  Redis replay protection, JWKS-equivalent key rotation, OpenTelemetry
  metrics + traces, multi-scheme coexistence, SignalR, multi-tenant,
  Swagger, Docker/Kubernetes.
- **Read [`PRODUCTION-CHECKLIST.md`](PRODUCTION-CHECKLIST.md)** before
  putting anything in front of real users. It enumerates the things
  this happy-path demo *doesn't* cover: key management, replay
  protection at scale, transport security, observability, supply-chain
  verification.
- **Read [`SECURITY.md`](../SECURITY.md) and [`KNOWN-GAPS.md`](../KNOWN-GAPS.md)**
  end-to-end. The underlying cryptography has **not** been independently
  audited — a permanent, documented limitation, not a pending item;
  both documents state honestly what that means.
- **Read the [`FAQ.md`](FAQ.md)** for answers to "should I use this in
  production?", "what about Auth0/IdentityServer?", "how big are
  tokens really?", and other common questions.

---

*To God be the glory — 1 Corinthians 10:31.*
