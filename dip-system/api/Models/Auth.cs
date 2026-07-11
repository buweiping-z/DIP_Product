using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 角色表
/// </summary>
public class Role : BaseEntity
{
    [Column("role_code")]
    public string RoleCode { get; set; } = string.Empty;

    [Column("role_name")]
    public string RoleName { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;
}

/// <summary>
/// 操作员/用户表
/// </summary>
public class Operator : BaseEntity
{
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("real_name")]
    public string RealName { get; set; } = string.Empty;

    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("role_id")]
    public long RoleId { get; set; }

    [Column("line_id")]
    public long? LineId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    // 导航属性
    [ForeignKey(nameof(RoleId))]
    public Role? Role { get; set; }
}

/// <summary>
/// Refresh Token 表
/// </summary>
public class RefreshToken
{
    [Column("id")]
    public long Id { get; set; }

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Column("replaced_by_token")]
    public string? ReplacedByToken { get; set; }

    [Column("is_revoked")]
    public bool IsRevoked { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
