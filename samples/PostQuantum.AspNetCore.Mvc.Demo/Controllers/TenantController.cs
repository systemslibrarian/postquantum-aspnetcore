using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PostQuantum.AspNetCore.Mvc.Demo.Controllers;

/// <summary>
/// Policy-based authorization: only tokens with tenant=acme reach these
/// endpoints. The policy is configured in Program.cs.
/// </summary>
[ApiController]
[Route("acme")]
[Authorize(Policy = "AcmeTenant")]
public sealed class TenantController : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Ok(new
    {
        message = "Welcome to the Acme dashboard.",
        user = User.FindFirst("sub")?.Value,
    });
}
