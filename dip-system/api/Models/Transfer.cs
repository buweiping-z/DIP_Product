using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 调拨单
/// </summary>
public class TransferOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [Column("status")]
    public int Status { get; set; } = 1;

    public List<TransferOrderItem> Items { get; set; } = new();
}

/// <summary>
/// 调拨单明细
/// </summary>
public class TransferOrderItem : BaseEntity
{
    [Column("transfer_order_id")]
    public long TransferOrderId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long SourceLocationId { get; set; }

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [ForeignKey(nameof(TransferOrderId))]
    public TransferOrder? TransferOrder { get; set; }
}
