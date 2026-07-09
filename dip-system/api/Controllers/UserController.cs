using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UserController : ControllerBase
{
    private readonly UserService _svc;

    public UserController(UserService svc) { _svc = svc; }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword,
        [FromQuery] int? role_id,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(keyword, role_id, page, page_size)));

    [HttpGet("roles")]
    [Authorize]
    public async Task<IActionResult> GetRoles()
        => Ok(ApiResponse.Ok(await _svc.GetRolesAsync()));

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(req.Username, req.RealName, req.RoleId, req.Password), "创建成功"));

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest req)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.RealName, req.RoleId, req.Status, req.Password), "更新成功"));

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPut("{id}/reset-password")]
    [Authorize]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest req)
    {
        await _svc.ResetPasswordAsync(id, req.NewPassword);
        return Ok(ApiResponse.Ok(null, "密码已重置"));
    }

    [HttpPut("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.ChangePasswordAsync(userId, req.OldPassword, req.NewPassword);
        return Ok(ApiResponse.Ok(null, "密码已修改"));
    }
}

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string RealName { get; set; } = "";
    public long RoleId { get; set; }
    public string Password { get; set; } = "";
}

public class UpdateUserRequest
{
    public string? RealName { get; set; }
    public long? RoleId { get; set; }
    public int? Status { get; set; }
    public string? Password { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
