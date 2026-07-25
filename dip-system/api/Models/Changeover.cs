using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>途中切替批次（会话）—— 管理一次切替的完整生命周期</summary>
[Table("changeover_batches")]
public class ChangeoverBatch : BaseEntity
{
    [Column("batch_no")]
    public string BatchNo { get; set; } = string.Empty;

    [Column("product_name")]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>BOM 料号清单 JSON：[{part_no, part_name, required_qty}]</summary>
    [Column("bom_json")]
    public string BomJson { get; set; } = "[]";

    /// <summary>已确认计数 JSON：{part_no: count}</summary>
    [Column("scanned_json")]
    public string ScannedJson { get; set; } = "{}";

    /// <summary>1=进行中, 2=已完成</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("operator_id")]
    public long OperatorId { get; set; }
}

/// <summary>途中切替扫描明细记录</summary>
[Table("inline_changeovers")]
public class InlineChangeover : BaseEntity
{
    [Column("batch_no")]
    public string BatchNo { get; set; } = string.Empty;

    [Column("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("scanned_at")]
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}
