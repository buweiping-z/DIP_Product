using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) { _auth = auth; }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var result = await _auth.LoginAsync(req.Username, req.Password);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _auth.LogoutAsync(userId, req.RefreshToken);
        return Ok(ApiResponse.Ok(null, "已登出"));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _auth.GetCurrentUserAsync(userId);
        if (user == null) return Ok(ApiResponse.Fail(401, "用户不存在"));
        return Ok(ApiResponse.Ok(new
        {
            user.Id, user.Username, real_name = user.RealName,
            role_code = user.Role?.RoleCode ?? "", line_id = user.LineId, tenant_id = user.TenantId
        }));
    }
}

public class LoginRequest { public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class RefreshRequest { public string RefreshToken { get; set; } = ""; }
public class LogoutRequest { public string RefreshToken { get; set; } = ""; }
