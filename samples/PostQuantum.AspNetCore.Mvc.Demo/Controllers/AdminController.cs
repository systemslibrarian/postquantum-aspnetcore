using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PostQuantum.AspNetCore.Mvc.Demo.Controllers;

/// <summary>
/// Role-based authorization: only tokens with role=admin reach these
/// endpoints. Anyone else gets a 403.
/// </summary>
[ApiController]
[Route("admin")]
[Authorize(Roles = "admin")]
public sealed class AdminController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true });
}
