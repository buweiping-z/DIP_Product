using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UserController : ControllerBase
{
    private readonly UserService _svc;
    private readonly AppDbContext _db;

    public UserController(UserService svc, AppDbContext db) { _svc = svc; _db = db; }

    private async Task EnsureAdminAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
            throw AppException.Business("无法识别用户身份");

        // 先查 JWT claim
        var jwtRole = User.FindFirstValue("role") ?? "";
        if (jwtRole == "admin") return;

        // JWT 不可信则查数据库
        var user = await _db.Operators.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw AppException.Business($"用户不存在: id={userId}");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId);
        if (role == null)
            throw AppException.Business($"角色不存在: roleId={user.RoleId}, userId={userId}");

        if (role.RoleCode != "admin")
            throw AppException.Business($"仅管理员可操作，当前角色: {role.RoleCode}");

        // 是 admin，无需额外操作
    }

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
    {
        await EnsureAdminAsync();
        return Ok(ApiResponse.Ok(await _svc.CreateAsync(req.Username, req.RealName, req.RoleId, req.Password), "创建成功"));
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest req)
    {
        await EnsureAdminAsync();
        return Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.RealName, req.RoleId, req.Status), "更新成功"));
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id)
    {
        await EnsureAdminAsync();
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPut("{id}/reset-password")]
    [Authorize]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest req)
    {
        await EnsureAdminAsync();
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
