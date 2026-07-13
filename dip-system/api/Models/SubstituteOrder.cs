using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 替代料移库订单
/// </summary>
public class SubstituteOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>1=待确认, 2=已完成, 3=已取消</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    public List<SubstituteDetail> Details { get; set; } = new();
}

/// <summary>
/// 替代料移库明细
/// </summary>
public class SubstituteDetail : BaseEntity
{
    [Column("order_id")]
    public long OrderId { get; set; }

    [Column("original_part_id")]
    public long OriginalPartId { get; set; }

    [Column("original_part_no")]
    public string OriginalPartNo { get; set; } = string.Empty;

    [Column("substitute_part_id")]
    public long SubstitutePartId { get; set; }

    [Column("substitute_part_no")]
    public string SubstitutePartNo { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long SourceLocationId { get; set; }

    [Column("source_location_code")]
    public string SourceLocationCode { get; set; } = string.Empty;

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [Column("target_location_code")]
    public string TargetLocationCode { get; set; } = string.Empty;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>1=待确认, 2=已确认</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [ForeignKey(nameof(OrderId))]
    public SubstituteOrder? Order { get; set; }
}
