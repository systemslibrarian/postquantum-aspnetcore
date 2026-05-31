# Migrating from `PostQuantum.Jwt.AspNetCore`

`PostQuantum.AspNetCore` is the renamed, repackaged successor to the
`PostQuantum.Jwt.AspNetCore` companion that ships from the
[`postquantum-jwt`](https://github.com/systemslibrarian/postquantum-jwt)
repository. **Same engine, same shape, cleaner naming, its own release
cadence.** Most consumers can switch in a single PR.

This document is the migration path. If you spot something missing, please
open an issue — that's a gap worth recording.

## Why two packages exist (transitional state)

`PostQuantum.Jwt.AspNetCore` lives inside the engine repository so it could
ship alongside the first release of `PostQuantum.Jwt`. It works, and it will
keep working — but it carries a few rough edges that the engine's release
cadence couldn't easily revisit:

- `Pq*`-prefixed type names that read oddly next to `AddJwtBearer` and
  friends.
- Bundled with the engine's release, so a bug fix in the auth handler is
  gated on cutting an engine release.
- Limited test coverage at the HTTP boundary.

`PostQuantum.AspNetCore` is a clean rewrite in its own repo with its own
release timeline, full integration tests, an event-hook surface, an
async key-ring resolver, and a CycloneDX SBOM packed inside every
`.nupkg`.

Both packages will publish in parallel through the `0.x` series so anyone
on `PostQuantum.Jwt.AspNetCore` has time to migrate without pressure.
Sometime before `1.0`, the older package will be marked **deprecated** on
nuget.org with a pointer to this one.

## Drop-in mapping

The mapping is mechanical — same shape, longer names:

| `PostQuantum.Jwt.AspNetCore`         | `PostQuantum.AspNetCore`                  |
|--------------------------------------|-------------------------------------------|
| `AddPqJwtBearer(...)`                | `AddPostQuantumJwtBearer(...)`            |
| `PqJwtBearerHandler`                 | `PostQuantumJwtBearerHandler`             |
| `PqJwtBearerOptions`                 | `PostQuantumJwtBearerOptions`             |
| `PqJwtBearerDefaults.AuthenticationScheme` (`"PqJwtBearer"`) | `PostQuantumJwtBearerDefaults.AuthenticationScheme` (`"PostQuantumJwtBearer"`) |
| `IPqJwtKeyRing`                      | `IPostQuantumJwtKeyRing`                  |
| `HttpPqJwtKeyRing`                   | `HttpPostQuantumJwtKeyRing`               |
| `PqJwtKeyEntry` / `PqJwtKeyDirectory` | `PostQuantumJwtKeyEntry` / `PostQuantumJwtKeyDirectory` |

Everything from `PostQuantum.Jwt` itself (`PqJwtBuilder`,
`PqJwtValidator`, `PqJwtValidationParameters`, `MLDsa`, `InMemoryReplayCache`,
…) stays the same — the engine has not been forked.

## Step-by-step

### 1. Swap the package reference

```diff
- <PackageReference Include="PostQuantum.Jwt.AspNetCore" Version="0.3.0-preview.1" />
+ <PackageReference Include="PostQuantum.AspNetCore"     Version="0.1.0-preview.1" />
```

`PostQuantum.Jwt` itself stays — both packages depend on it.

### 2. Rename the `using` directive

```diff
- using PostQuantum.Jwt.AspNetCore;
+ using PostQuantum.AspNetCore;
```

### 3. Rename the extension method and types

```diff
  builder.Services
-     .AddAuthentication(PqJwtBearerDefaults.AuthenticationScheme)
-     .AddPqJwtBearer(options =>
+     .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
+     .AddPostQuantumJwtBearer(options =>
      {
          options.ValidationParameters = new PqJwtValidationParameters
          {
              SignatureVerificationKey = verificationKey,
              ValidIssuer   = "https://issuer.example",
              ValidAudience = "https://api.example",
          };
      });
```

### 4. Update any `[Authorize(AuthenticationSchemes = ...)]` attributes

The scheme name changed from `"PqJwtBearer"` to `"PostQuantumJwtBearer"`.
If you have multi-scheme apps that pin specific endpoints, update the
attribute:

```diff
- [Authorize(AuthenticationSchemes = PqJwtBearerDefaults.AuthenticationScheme)]
+ [Authorize(AuthenticationSchemes = PostQuantumJwtBearerDefaults.AuthenticationScheme)]
```

Or if you've used the raw string literal:

```diff
- [Authorize(AuthenticationSchemes = "PqJwtBearer")]
+ [Authorize(AuthenticationSchemes = "PostQuantumJwtBearer")]
```

### 5. (Optional) Rename the HTTP key ring

If you wire up the JWKS-equivalent yourself:

```diff
- builder.Services.AddHttpClient<HttpPqJwtKeyRing>();
- builder.Services.AddSingleton<IPqJwtKeyRing>(sp =>
+ builder.Services.AddHttpClient<HttpPostQuantumJwtKeyRing>();
+ builder.Services.AddSingleton<IPostQuantumJwtKeyRing>(sp =>
  {
-     var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpPqJwtKeyRing));
-     return new HttpPqJwtKeyRing(http, new Uri(builder.Configuration["Auth:KeysEndpoint"]!));
+     var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpPostQuantumJwtKeyRing));
+     return new HttpPostQuantumJwtKeyRing(http, new Uri(builder.Configuration["Auth:KeysEndpoint"]!));
  });
```

The wire format is identical — your existing key-directory endpoint does
not need to change.

### 6. (Optional) Adopt the new event surface

`PostQuantum.AspNetCore` adds `PostQuantumJwtBearerEvents` —
`OnTokenValidated`, `OnAuthenticationFailed`, `OnChallenge` — modelled on
`JwtBearerEvents`. If you previously rolled your own enrichment middleware,
you can collapse it into the handler now:

```csharp
.AddPostQuantumJwtBearer(options =>
{
    options.ValidationParameters = new PqJwtValidationParameters { /* ... */ };
    options.Events.OnTokenValidated = ctx =>
    {
        var identity = (ClaimsIdentity)ctx.Principal.Identity!;
        identity.AddClaim(new Claim("tenant", ResolveTenant(ctx.HttpContext)));
        return Task.CompletedTask;
    };
});
```

This is purely additive — leaving `Events` alone gives you exactly the
same behaviour as before.

## Wire-format compatibility

**Tokens minted by the old companion validate unchanged in the new one,
and vice versa.** Both packages call into the same `PqJwtValidator` from
`PostQuantum.Jwt`; they only differ in the ASP.NET Core adapter layer.

The HTTP key-directory wire format is also identical — same JSON shape,
same single-suite (`ML-DSA-65` only) policy. You can run both packages
against the same key endpoint in parallel during a phased migration.

## Things that changed

| Behaviour | Old | New |
|---|---|---|
| Scheme name | `PqJwtBearer` | `PostQuantumJwtBearer` |
| Default `Authorization` value on challenge | `Bearer realm="..."` | `Bearer realm="...", error="invalid_token"` (configurable; set `IncludeErrorDetailsInChallenge=false` to match old behaviour exactly) |
| Event hooks | none | `Events.OnTokenValidated` / `OnAuthenticationFailed` / `OnChallenge` |
| Async key resolution | sync only | `IPostQuantumJwtKeyRing.ResolveAsync` added (default interface impl falls back to `Resolve`) |
| Integration tests | none | `Microsoft.AspNetCore.Mvc.Testing`-backed suite covering the fail-closed contract end-to-end |
| SBOM in `.nupkg` | no | yes (`/bom.json`) |

Nothing else moved.

## Need to keep both packages installed?

You shouldn't, but if you need to during a migration window (e.g. some
services on the old companion, some on the new one), they coexist
without trouble as long as you register them under **different scheme
names**. The defaults already differ (`PqJwtBearer` vs.
`PostQuantumJwtBearer`), so a side-by-side `AddPqJwtBearer(...)
.AddPostQuantumJwtBearer(...)` registration works out of the box.

Don't try to combine them on the same `Authorization` header by accident —
both handlers will see the same token and either may succeed first,
which is non-deterministic in a way you don't want.

---

*To God be the glory — 1 Corinthians 10:31.*
