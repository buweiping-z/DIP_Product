using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 备料单
/// </summary>
public class PrepOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [Column("production_order_id")]
    public long ProductionOrderId { get; set; }

    [Column("line_id")]
    public long LineId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("kit_check_result")]
    public int KitCheckResult { get; set; }

    [Column("kit_check_time")]
    public DateTime? KitCheckTime { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    public List<PrepDetail> Details { get; set; } = new();
}

/// <summary>
/// 备料明细
/// </summary>
public class PrepDetail : BaseEntity
{
    [Column("prep_order_id")]
    public long PrepOrderId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("reference_designator")]
    public string ReferenceDesignator { get; set; } = string.Empty;

    [Column("required_qty")]
    public decimal RequiredQty { get; set; }

    [Column("actual_qty")]
    public decimal ActualQty { get; set; }

    [Column("loss_qty")]
    public decimal LossQty { get; set; }

    [Column("substitute_flag")]
    public int SubstituteFlag { get; set; }

    [Column("substitute_part_id")]
    public long? SubstitutePartId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [ForeignKey(nameof(PrepOrderId))]
    public PrepOrder? PrepOrder { get; set; }
}

/// <summary>
/// 备料扫描记录
/// </summary>
public class PrepScanRecord : TimestampEntity
{
    [Column("prep_detail_id")]
    public long PrepDetailId { get; set; }

    [Column("source_location_id")]
    public long SourceLocationId { get; set; }

    [Column("source_location_code")]
    public string SourceLocationCode { get; set; } = string.Empty;

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("scanned_barcode")]
    public string ScannedBarcode { get; set; } = string.Empty;

    [Column("operator_id")]
    public long OperatorId { get; set; }
}
