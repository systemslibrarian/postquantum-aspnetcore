# Diagnostics

Why is my token failing validation? Walk this guide top-to-bottom; each
step rules out a class of cause.

## 1. What does `dotnet test` say?

If the test suite passes on your machine and your app still rejects
tokens, the issue is in your wiring — not the library. Verify
`dotnet test` is green against this repository's own suite first.

## 2. Is the runtime ML-DSA-capable?

```csharp
Console.WriteLine($"MLDsa.IsSupported = {System.Security.Cryptography.MLDsa.IsSupported}");
```

If `False`, you're on a runtime without the native primitive (typical on
Linux with OpenSSL < 3.5). The library fails closed with a clear error
on its first attempt to use ML-DSA; check the inner exception of
whatever startup error you're seeing.

## 3. Is the right scheme being used?

```csharp
app.MapGet("/whoami", (HttpContext ctx) => new
{
    schemes = ctx.RequestServices
        .GetRequiredService<IAuthenticationSchemeProvider>()
        .GetAllSchemesAsync().Result.Select(s => s.Name),
    default_scheme = ctx.RequestServices
        .GetRequiredService<IAuthenticationSchemeProvider>()
        .GetDefaultAuthenticateSchemeAsync().Result?.Name,
});
```

You should see `"PostQuantumJwtBearer"` (or your custom name) in
`schemes`, and the default authenticate scheme should match. If you see
the engine companion's `"PqJwtBearer"` alongside, you have *both*
packages installed — pick one (see
[MIGRATION.md](MIGRATION.md)).

## 4. Turn on Debug-level logging for this category

```json
{
  "Logging": {
    "LogLevel": {
      "PostQuantum.AspNetCore": "Debug"
    }
  }
}
```

Event IDs:

| ID | Level    | Message |
|----|----------|---------|
| 1  | Debug    | `Post-quantum JWT validation failed.` (includes the exception) |
| 2  | Warning  | Key ring fetched an empty document |
| 3  | Warning  | Key ring entry skipped due to malformed key material |
| 4  | Warning  | Key ring fetch failed |

The exception attached to event 1 is a `PqJwtValidationException` from
the engine — its message names the exact validation rule that failed
(signature, exp, nbf, iss, aud, alg, replay, …).

## 5. Common rejection causes

### "exp" required but missing or in the past

The validator enforces `RequireExpiration` and `ValidateLifetime` by
default. A token without `exp`, or with `exp` in the past minus
`ClockSkew` (60 s), is rejected.

### Wrong issuer / wrong audience

`ValidIssuer` and `ValidAudience` are exact-match. If your issuer
publishes tokens with a trailing slash and your validator pins without,
they will not match.

### Wrong key (or wrong kid)

If you're using `SignatureKeyResolver` / a key ring, log the resolved
`kid` and compare to the token's header:

```csharp
options.Events.OnAuthenticationFailed = ctx =>
{
    var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
    // Extract and base64-decode the first segment; the "kid" header
    // claim is in the resulting JSON.
    // …
    return Task.CompletedTask;
};
```

### Replay cache rejected a previously-seen `jti`

A token can only be presented once when a `ReplayCache` is configured.
If you legitimately need to replay a request (e.g., HTTP retry on
network failure), accept that 401 and have the caller fetch a fresh
token.

### `WWW-Authenticate` says `error="invalid_token"`

That parameter is added when a token was supplied but failed validation,
not when the header was simply missing. If you see it on a request you
didn't send a token with, check `OnMessageReceived` — it may be
substituting a token from somewhere (e.g., a stale cookie).

### Token isn't reaching the handler at all

If your `OnTokenValidated` and `OnAuthenticationFailed` callbacks never
fire, the handler isn't seeing the token:

- The `Authorization` header may be stripped by a proxy or middleware.
- The `Bearer` prefix matches case-insensitively (RFC 6750), but
  there must still be exactly one space between scheme and token.
- An `OnMessageReceived` handler may be returning `null`/empty `Token`
  while assuming the header path will still run — check the header
  fallback is reached.

## 6. Isolate against the engine

If you've ruled out wiring and the engine still rejects the token,
reproduce against `PqJwtValidator` directly in a console app:

```csharp
var validator = new PqJwtValidator(new PqJwtValidationParameters
{
    SignatureVerificationKey = verificationKey,
    ValidIssuer = "...",
    ValidAudience = "...",
});
try { validator.Validate(token); Console.WriteLine("OK"); }
catch (PqJwtValidationException ex) { Console.WriteLine(ex); }
```

If the validator rejects the token here too, the issue is in the token
or the keys, not in ASP.NET Core wiring. File against
[`postquantum-jwt`](https://github.com/systemslibrarian/postquantum-jwt).

If the validator accepts it but the ASP.NET Core handler rejects it,
file against [this repo](https://github.com/systemslibrarian/postquantum-aspnetcore)
with the reproduction.

---

*To God be the glory — 1 Corinthians 10:31.*
