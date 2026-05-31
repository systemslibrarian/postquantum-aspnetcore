using Microsoft.AspNetCore.Mvc;
using PostQuantum.Jwt;
using System.Security.Cryptography;

namespace PostQuantum.AspNetCore.Mvc.Demo.Controllers;

/// <summary>
/// DEV-ONLY token-mint endpoint. NEVER ship this as written — a real
/// issuer is its own service holding the signing key in HSM / KMS /
/// secrets manager. This is here so the sample is one-process and
/// drivable in a browser.
/// </summary>
[ApiController]
[Route("dev")]
public sealed class DevTokenController(MLDsa signingKey) : ControllerBase
{
    [HttpPost("token")]
    public IActionResult Mint(
        [FromQuery] string user = "alice",
        [FromQuery] string role = "user",
        [FromQuery] string tenant = "acme")
    {
        var token = new PqJwtBuilder()
            .WithIssuer("https://mvc-demo.local")
            .WithAudience("https://mvc-demo.local/api")
            .WithSubject(user)
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(15))
            .WithClaim("role", role)
            .WithClaim("tenant", tenant)
            .SignWith(signingKey)
            .Build();
        return Ok(new { token });
    }
}
