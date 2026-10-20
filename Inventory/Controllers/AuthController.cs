using Inventory.Application.Auth.DTOs;
using Inventory.Application.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Login with branch, username and password.
    /// Returns a JWT valid for 8 hours. Rate-limited to 10 requests/minute/IP.
    /// Always returns a generic error message on failure.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var response = await _authService.LoginAsync(request, ct);
        return Ok(response);
    }

    /// <summary>
    /// Returns a list of active branches (id, code, name only).
    /// Anonymous — used by the login screen branch selector.
    /// </summary>
    [HttpGet("branches")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<BranchLookupDto>>> GetActiveBranches(CancellationToken ct)
    {
        var branches = await _authService.GetActiveBranchesAsync(ct);
        return Ok(branches);
    }

    /// <summary>Returns information about the currently authenticated user.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        var me = await _authService.GetMeAsync(ct);
        return Ok(me);
    }

    /// <summary>Change the current user's password.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await _authService.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}
