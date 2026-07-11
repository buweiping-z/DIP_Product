using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 退料单
/// </summary>
public class ReturnOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [Column("prep_order_id")]
    public long? PrepOrderId { get; set; }

    [Column("return_reason")]
    public string ReturnReason { get; set; } = string.Empty;

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("approver_id")]
    public long? ApproverId { get; set; }

    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    public List<ReturnOrderItem> Items { get; set; } = new();
}

/// <summary>
/// 退料单明细
/// </summary>
public class ReturnOrderItem : BaseEntity
{
    [Column("return_order_id")]
    public long ReturnOrderId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [ForeignKey(nameof(ReturnOrderId))]
    public ReturnOrder? ReturnOrder { get; set; }
}
