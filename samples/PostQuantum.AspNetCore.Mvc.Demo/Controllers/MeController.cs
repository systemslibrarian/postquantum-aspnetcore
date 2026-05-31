using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PostQuantum.AspNetCore.Mvc.Demo.Controllers;

/// <summary>
/// The classic "who am I" endpoint. Any authenticated user can hit it.
/// Demonstrates that the standard [Authorize] attribute Just Works
/// with the PQ scheme.
/// </summary>
[ApiController]
[Route("me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        sub = User.FindFirst("sub")?.Value,
        role = User.FindFirst("role")?.Value,
        tenant = User.FindFirst("tenant")?.Value,
        iss = User.FindFirst("iss")?.Value,
        aud = User.FindFirst("aud")?.Value,
    });
}
