# Recipes

Copy-paste-able answers to the questions consumers actually ask. Each
recipe is self-contained — if you grab one and only one, it should
work.

> **For your very first time:** read
> [GETTING-STARTED.md](GETTING-STARTED.md) first. It walks the full
> end-to-end happy path before you dive into a specific recipe.

## Index

- [1. Mint a token from your issuer](#1-mint-a-token-from-your-issuer)
- [2. Validate tokens with a static public key](#2-validate-tokens-with-a-static-public-key)
- [3. Validate tokens via JWKS-equivalent key rotation](#3-validate-tokens-via-jwks-equivalent-key-rotation)
- [4. Warm the key cache at startup](#4-warm-the-key-cache-at-startup)
- [5. Distributed replay protection with Redis](#5-distributed-replay-protection-with-redis)
- [6. Role-based and policy-based authorization](#6-role-based-and-policy-based-authorization)
- [7. Coexist with the standard JwtBearer scheme during migration](#7-coexist-with-the-standard-jwtbearer-scheme-during-migration)
- [8. OpenTelemetry: metrics and distributed tracing](#8-opentelemetry-metrics-and-distributed-tracing)
- [9. SignalR with `?access_token=`](#9-signalr-with-access_token)
- [10. Multi-tenant validation](#10-multi-tenant-validation)
- [11. Health checks for the key-ring endpoint](#11-health-checks-for-the-key-ring-endpoint)
- [12. Swagger / OpenAPI integration](#12-swagger--openapi-integration)
- [13. Docker / Kubernetes deployment notes](#13-docker--kubernetes-deployment-notes)

---

## 1. Mint a token from your issuer

The library is the *receiving* side. Token minting lives in
`PostQuantum.Jwt` itself:

```csharp
using System.Security.Cryptography;
using PostQuantum.Jwt;

// In production: load the signing key from a secret store (Azure Key
// Vault, AWS KMS, HashiCorp Vault). In dev: generate on startup.
using var signingKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);

string token = new PqJwtBuilder()
    .WithIssuer("https://issuer.example")
    .WithAudience("https://api.example")
    .WithSubject("user-42")
    .WithJwtId(Guid.NewGuid().ToString("N"))    // for replay protection
    .WithLifetime(TimeSpan.FromMinutes(15))
    .WithClaim("role", "admin")
    .WithClaim("tenant", "acme")
    .WithKeyId("signing-key-2026-q2")            // for kid-based rotation
    .SignWith(signingKey)
    .Build();
```

Publish the verification half via your JWKS-equivalent endpoint (see
recipe 3) so resource servers can validate without sharing secrets.

---

## 2. Validate tokens with a static public key

The simplest pattern. Right for single-issuer deployments where the
verification key is configured once at deploy time.

```csharp
using System.Security.Cryptography;
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;

var builder = WebApplication.CreateBuilder(args);

using var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65,
    Convert.FromBase64String(builder.Configuration["Auth:VerificationKey"]!));

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidAudience = builder.Configuration["Auth:Audience"],
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", (HttpContext ctx) => Results.Ok(new
{
    sub = ctx.User.FindFirst("sub")?.Value,
})).RequireAuthorization();

app.Run();
```

---

## 3. Validate tokens via JWKS-equivalent key rotation

For multi-service deployments where the issuer rotates signing keys
without coordinating with every verifier:

```csharp
builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidAudience = builder.Configuration["Auth:Audience"],
            // No key here — the ring supplies it per-request from the
            // token's `kid` header.
        };
    });

builder.Services.AddPostQuantumJwtKeyRing(
    new Uri(builder.Configuration["Auth:KeysEndpoint"]!),
    refreshInterval: TimeSpan.FromMinutes(10));
```

Expected key-directory JSON at that endpoint:

```json
{
  "keys": [
    {
      "kid": "signing-key-2026-q2",
      "alg": "ML-DSA-65",
      "key": "<base64 of the raw ML-DSA-65 public-key bytes>"
    },
    {
      "kid": "signing-key-2026-q3",
      "alg": "ML-DSA-65",
      "key": "<base64 of the raw ML-DSA-65 public-key bytes>"
    }
  ]
}
```

The ring caches per-`kid`, refreshes on the interval AND on unknown-kid
misses (throttled to once every 10 s to prevent fan-out attacks).
Entries with any `alg` other than `ML-DSA-65` are silently dropped on
ingest — single-suite policy enforced at the wire.

---

## 4. Warm the key cache at startup

Without warmup, the very first authenticated request pays a network
round trip while every other request waits. The hosted-service helper
fixes that:

```csharp
builder.Services.AddPostQuantumJwtKeyRing(
    new Uri(builder.Configuration["Auth:KeysEndpoint"]!));

builder.Services.AddPostQuantumJwtKeyRingWarmup(options =>
{
    // Default: fail-fast. The host won't start if the key endpoint is
    // unreachable. Strict, matches the engine's fail-closed ethos.
    options.FailFastOnStartup = true;

    // Optional periodic refresh — picks up removed keys without
    // waiting for an unknown-kid miss. Tune to your rotation cadence.
    options.RefreshInterval = TimeSpan.FromMinutes(15);
});
```

For a best-effort warmup (logs and lets the host start), set
`FailFastOnStartup = false`.

---

## 5. Distributed replay protection with Redis

Single-use `jti` enforcement across every instance in your fleet.
Install the companion package:

```bash
dotnet add package PostQuantum.AspNetCore.RedisReplayCache
```

Wire it up:

```csharp
using PostQuantum.AspNetCore.RedisReplayCache;

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer   = "https://issuer.example",
            ValidAudience = "https://api.example",
            // ReplayCache wired by the DI helper below.
        };
    });

builder.Services.AddPostQuantumJwtRedisReplayCache(
    connectionString: builder.Configuration["Redis:ConnectionString"]!,
    keyPrefix: "pqjwt:jti:");
```

Under the hood: `SET key 1 NX PX {remaining-token-lifetime}`. First use
wins, replays return false → validator throws
`PqJwtValidationException` → handler returns `401`. The TTL means the
cache cleans itself up after token expiration.

**Don't use the bundled `InMemoryReplayCache` in production with more
than one instance.** It doesn't coordinate across processes; a token
replayed on a different node won't be caught.

---

## 6. Role-based and policy-based authorization

Roles work out of the box. By default the handler maps the `role`
claim to `ClaimTypes.Role`, so `[Authorize(Roles = "admin")]` matches:

```csharp
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    [HttpGet("/admin/health")]
    public IActionResult Health() => Ok(new { ok = true });
}
```

For richer authorization (multiple claims, expressions, custom
requirements), use policies:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AcmeAdmin", policy =>
        policy
            .RequireAuthenticatedUser()
            .RequireClaim("tenant", "acme")
            .RequireRole("admin"));

    options.AddPolicy("EmailVerified", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.FindFirst("email_verified")?.Value == "true"));
});
```

```csharp
app.MapGet("/admin", () => "ok").RequireAuthorization("AcmeAdmin");
```

If you need claims that aren't on the token (e.g. enrich from a
database after validation), use `OnTokenValidated`:

```csharp
options.Events.OnTokenValidated = async ctx =>
{
    var sub = ctx.Principal.FindFirstValue("sub");
    if (sub is null) return;

    var identity = (ClaimsIdentity)ctx.Principal.Identity!;
    var profile = await profileStore.GetAsync(sub, ctx.HttpContext.RequestAborted);
    foreach (var role in profile.Roles)
    {
        identity.AddClaim(new Claim("role", role));
    }
};
```

---

## 7. Coexist with the standard JwtBearer scheme during migration

You have a working app on `AddJwtBearer` and want to introduce
post-quantum auth gradually. Don't replace — add a second scheme:

```csharp
builder.Services
    .AddAuthentication()
    // Legacy classical scheme — unchanged.
    .AddJwtBearer("ClassicalJwt", options =>
    {
        options.Authority = "https://auth.example/";
        options.Audience  = "https://api.example/";
    })
    // New post-quantum scheme — additive.
    .AddPostQuantumJwtBearer("PostQuantumJwt", options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = pqVerificationKey,
            ValidIssuer   = "https://issuer.example",
            ValidAudience = "https://api.example",
        };
    });
```

Route specific endpoints to specific schemes:

```csharp
[Authorize(AuthenticationSchemes = "PostQuantumJwt")]
public class PostQuantumOnlyController : ControllerBase { /* ... */ }

[Authorize(AuthenticationSchemes = "ClassicalJwt,PostQuantumJwt")]
public class EitherWorksController : ControllerBase { /* ... */ }
```

Or set a policy that accepts either:

```csharp
builder.Services.AddAuthorization(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("ClassicalJwt", "PostQuantumJwt")
        .RequireAuthenticatedUser()
        .Build();
    options.DefaultPolicy = policy;
});
```

> **Don't** register both schemes on the *same* default scheme name —
> the first registration wins and the second is silently ignored.

---

## 8. OpenTelemetry: metrics and distributed tracing

The library emits Metrics + ActivitySource under the
`"PostQuantum.AspNetCore"` instrumentation name. Wire OpenTelemetry to
both:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("PostQuantum.AspNetCore")     // <- this library
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddPrometheusExporter())               // or your exporter
    .WithTracing(tracing => tracing
        .AddSource("PostQuantum.AspNetCore")    // <- this library
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());                    // or your exporter
```

You'll see these signals:

| Signal name                              | Type      | Tags                                  |
|------------------------------------------|-----------|---------------------------------------|
| `postquantum.jwt.auth.success`           | Counter   | `scheme`                              |
| `postquantum.jwt.auth.failure`           | Counter   | `scheme`, `reason` (exception type)   |
| `postquantum.jwt.auth.latency`           | Histogram | `scheme`, `result` (success/failure)  |
| `postquantum.jwt.keyring.resolve`        | Counter   | `result` (hit/miss/refresh-hit/refresh-miss) |
| `postquantum.jwt.keyring.fetch.latency`  | Histogram | `result` (success/failure)            |
| `PostQuantumJwtBearer.Validate` (span)   | Activity  | `scheme`, `result`, `failure.reason`  |

A Grafana dashboard for those signals is a great way to spot
key-rotation problems and auth-failure spikes in production.

---

## 9. SignalR with `?access_token=`

WebSocket clients can't send custom headers from a browser, so the
SignalR convention is `?access_token=…` on the connection URL. Use
`OnMessageReceived` to lift it:

```csharp
builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters { /* ... */ };

        options.Events.OnMessageReceived = ctx =>
        {
            var accessToken = ctx.HttpContext.Request.Query["access_token"].ToString();
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/"))
            {
                ctx.Token = accessToken;
            }

            return Task.CompletedTask;
        };
    });

builder.Services.AddSignalR();

// ...

app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
```

Browser client:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', { accessTokenFactory: () => myToken })
    .build();
```

A complete end-to-end sample lives at
[`samples/PostQuantum.AspNetCore.SignalR.Demo`](../samples/PostQuantum.AspNetCore.SignalR.Demo).

---

## 10. Multi-tenant validation

Different tenants → different issuers/audiences. The handler is
single-scheme by default; for multi-tenant either use one scheme per
tenant (clean) or one scheme with a tenant-aware key resolver
(scalable):

### Pattern A: one scheme per tenant

```csharp
foreach (var tenant in tenants)
{
    builder.Services
        .AddAuthentication()
        .AddPostQuantumJwtBearer($"PQ-{tenant.Id}", options =>
        {
            options.ValidationParameters = new PqJwtValidationParameters
            {
                SignatureVerificationKey = tenant.VerificationKey,
                ValidIssuer   = tenant.Issuer,
                ValidAudience = tenant.Audience,
            };
        });
}
```

Then a policy that accepts any:

```csharp
options.DefaultPolicy = new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes(tenants.Select(t => $"PQ-{t.Id}").ToArray())
    .RequireAuthenticatedUser()
    .Build();
```

### Pattern B: one scheme, tenant-aware resolver

Use a custom `IPostQuantumJwtKeyRing` that inspects the token's
`iss` claim (after Base64-decoding the payload) and returns the
matching tenant's key. Suitable when you have hundreds of tenants and
spinning up a scheme per tenant is unwieldy.

> Whichever pattern you pick, **never** trust the `iss` claim to
> determine which key to verify with unless you've ALSO pinned
> `ValidIssuer` so the validator re-verifies the claim against the
> expected value. Otherwise an attacker forges an `iss` of a tenant
> they don't control and uses that tenant's verification key.

---

## 11. Health checks for the key-ring endpoint

Surface the key-directory reachability as a health-check probe so
Kubernetes / load balancers can react:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

builder.Services.AddPostQuantumJwtKeyRing(keysEndpoint);
builder.Services.AddHealthChecks()
    .AddCheck<PostQuantumKeyRingHealthCheck>("postquantum-keyring");

// ...

app.MapHealthChecks("/health");
```

```csharp
public sealed class PostQuantumKeyRingHealthCheck(IPostQuantumJwtKeyRing ring)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await ring.PreloadAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Key ring fetch failed.", ex);
        }
    }
}
```

The default `HttpPostQuantumJwtKeyRing.PreloadAsync` propagates fetch
failures (unlike the hot-path `Resolve` which swallows them), so the
health check sees real network errors.

---

## 12. Swagger / OpenAPI integration

For Swashbuckle:

```csharp
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("PostQuantumBearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.Http,
        Scheme      = "Bearer",
        BearerFormat = "ML-DSA-65 JWT",
        Description = "Post-quantum JWT (ML-DSA-65). Paste the token without the 'Bearer ' prefix.",
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id   = "PostQuantumBearer",
            },
        }] = []
    });
});
```

For `Microsoft.AspNetCore.OpenApi` (.NET 9+):

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, _) =>
    {
        doc.Components ??= new();
        doc.Components.SecuritySchemes["PostQuantumBearer"] = new()
        {
            Type   = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "ML-DSA-65 JWT",
        };
        return Task.CompletedTask;
    });
});
```

---

## 13. Docker / Kubernetes deployment notes

**Base image.** The engine needs OpenSSL 3.5+ for the native ML-KEM /
ML-DSA primitives. Microsoft's official .NET 10 images ship with a
compatible OpenSSL on a recent Debian/Ubuntu base. **Stick with the
official image.** Distroless or scratch-based images may not include
the OpenSSL providers required.

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyService.dll"]
```

**AOT build.** If you `dotnet publish -p:PublishAot=true`, the AOT
binary embeds the runtime; the deployment image can be slimmer
(`mcr.microsoft.com/dotnet/runtime-deps`). The library is
AOT-compatible (`IsAotCompatible=true`) and we verify the publish
end-to-end on Linux + Windows + macOS in CI on every push.

**Secrets.** Verification keys belong in your secret manager
(Azure Key Vault, AWS Secrets Manager, HashiCorp Vault). Pull them
into `IConfiguration` via your secret provider — never into the
container image.

**Kubernetes probes.**

```yaml
readinessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10
livenessProbe:
  httpGet:
    path: /health
    port: 8080
  periodSeconds: 30
```

Pair with the health check from recipe 11 so a flaky key endpoint
yanks the pod out of rotation instead of returning 500s to users.

**Resource limits.** ML-DSA-65 verification is CPU-bound (low ms per
request) and modest on memory. Plan ~5 MB additional working set
per replica from the library itself; the dominant cost is per-request
allocations from the engine (see [PERFORMANCE.md](PERFORMANCE.md)).

---

If a recipe you need isn't here, [open an issue](https://github.com/systemslibrarian/postquantum-aspnetcore/issues/new/choose)
— the cookbook grows from real use.

---

*To God be the glory — 1 Corinthians 10:31.*
