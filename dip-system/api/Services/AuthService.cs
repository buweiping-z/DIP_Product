using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

/// <summary>
/// 认证服务：登录、刷新、登出
/// </summary>
public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _jwt = new JwtTokenService(config);
    }

    public async Task<object> LoginAsync(string username, string password)
    {
        var user = await _db.Operators.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.Status == 1 && !u.IsDeleted);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw AppException.Unauthorized("用户名或密码错误");

        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId);
        var roleCode = role?.RoleCode ?? "";

        var accessToken = _jwt.CreateAccessToken(user.Id, user.Username, roleCode, user.TenantId, user.LineId);
        var refreshStr = _jwt.CreateRefreshToken();

        var rt = new RefreshToken
        {
            OperatorId = user.Id,
            Token = refreshStr,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(rt);
        await _db.SaveChangesAsync();

        return new
        {
            access_token = accessToken,
            refresh_token = refreshStr,
            expires_in = 1800,
            user = new { id = user.Id, username = user.Username, real_name = user.RealName, role_code = roleCode, line_id = user.LineId }
        };
    }

    public async Task<object> RefreshAsync(string refreshTokenStr)
    {
        var record = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshTokenStr && !r.IsRevoked);
        if (record == null || record.ExpiresAt < DateTime.UtcNow)
            throw AppException.Unauthorized("刷新令牌无效或已过期");

        var user = await _db.Operators.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == record.OperatorId && u.Status == 1);
        if (user == null)
            throw AppException.Unauthorized("用户不存在或已禁用");

        record.IsRevoked = true;
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId);
        var roleCode = role?.RoleCode ?? "";

        var accessToken = _jwt.CreateAccessToken(user.Id, user.Username, roleCode, user.TenantId, user.LineId);
        var newRefresh = _jwt.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            OperatorId = user.Id,
            Token = newRefresh,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return new
        {
            access_token = accessToken,
            refresh_token = newRefresh,
            expires_in = 1800,
            user = new { id = user.Id, username = user.Username, real_name = user.RealName, role_code = roleCode, line_id = user.LineId }
        };
    }

    public async Task LogoutAsync(long operatorId, string refreshTokenStr)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.OperatorId == operatorId && r.Token == refreshTokenStr);
        if (token != null)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<Operator?> GetCurrentUserAsync(long userId)
    {
        return await _db.Operators.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Status == 1);
    }
}
