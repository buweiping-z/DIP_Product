using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DIP.Api.Services;

/// <summary>
/// JWT Token 生成与验证服务
/// </summary>
public class JwtTokenService
{
    private readonly string _secret;
    private readonly int _expiresMinutes;
    private readonly int _refreshExpireDays;

    public JwtTokenService(IConfiguration config)
    {
        _secret = config["Jwt:Secret"]!;
        _expiresMinutes = int.Parse(config["Jwt:ExpiresMinutes"] ?? "30");
        _refreshExpireDays = int.Parse(config["Jwt:RefreshExpireDays"] ?? "7");
    }

    public string CreateAccessToken(long userId, string username, string roleCode, long tenantId, long? lineId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new("role", roleCode),
            new("tenant_id", tenantId.ToString()),
        };
        if (lineId.HasValue)
            claims.Add(new("line_id", lineId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
