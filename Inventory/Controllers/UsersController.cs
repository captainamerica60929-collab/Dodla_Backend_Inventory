using Inventory.Application.Users.DTOs;
using Inventory.Application.Users.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "ApiAdmin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var users = await _userService.GetAllAsync(ct);
        return Ok(users);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id, CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(id, ct);
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var user = await _userService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(
        int id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var user = await _userService.UpdateAsync(id, dto, ct);
        return Ok(user);
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        await _userService.ResetPasswordAsync(id, dto, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken ct)
    {
        await _userService.UnlockAsync(id, ct);
        return NoContent();
    }
}
