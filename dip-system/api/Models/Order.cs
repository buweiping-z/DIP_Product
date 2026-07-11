using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 生产订单
/// </summary>
public class ProductionOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [Column("line_id")]
    public long LineId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [Column("plan_qty")]
    public decimal PlanQty { get; set; }

    [Column("plan_start_date")]
    public DateTime? PlanStartDate { get; set; }

    [Column("plan_end_date")]
    public DateTime? PlanEndDate { get; set; }

    [Column("actual_start_date")]
    public DateTime? ActualStartDate { get; set; }

    [Column("actual_end_date")]
    public DateTime? ActualEndDate { get; set; }

    [Column("priority")]
    public int Priority { get; set; } = 2;

    [Column("customer_order_no")]
    public string? CustomerOrderNo { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    public List<BomItem> BomItems { get; set; } = new();
}

/// <summary>
/// BOM 明细
/// </summary>
public class BomItem : BaseEntity
{
    [Column("order_id")]
    public long OrderId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("reference_designator")]
    public string ReferenceDesignator { get; set; } = string.Empty;

    [Column("required_qty")]
    public decimal RequiredQty { get; set; }

    [Column("loss_rate")]
    public decimal LossRate { get; set; }

    [Column("substitute_part_id")]
    public long? SubstitutePartId { get; set; }

    [Column("part_type")]
    public int? PartType { get; set; }

    [Column("seq_no")]
    public int SeqNo { get; set; }

    [Column("is_critical")]
    public int IsCritical { get; set; }

    [ForeignKey(nameof(OrderId))]
    public ProductionOrder? Order { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }
}
