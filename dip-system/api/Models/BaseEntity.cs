using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 实体基类 — 所有继承 BaseEntity 的表自动拥有这些字段
/// </summary>
public abstract class BaseEntity
{
    [Column("id")]
    public long Id { get; set; }

    [Column("tenant_id")]
    public long TenantId { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("updated_by")]
    public long? UpdatedBy { get; set; }
}

/// <summary>
/// 时间戳 Mixin — 仅 created_at / updated_at，不含软删除
/// </summary>
public abstract class TimestampEntity
{
    [Column("id")]
    public long Id { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
