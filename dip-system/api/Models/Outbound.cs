using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 出库订单
/// </summary>
public class OutboundOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("location_code")]
    public string LocationCode { get; set; } = string.Empty;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>1=待出库 2=已出库 3=已取消</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}
