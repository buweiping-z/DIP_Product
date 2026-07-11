using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 扫描记录
/// </summary>
public class ScanRecord : TimestampEntity
{
    [Column("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Column("operation_type")]
    public string OperationType { get; set; } = string.Empty;

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("part_no")]
    public string? PartNo { get; set; }

    [Column("location_id")]
    public long? LocationId { get; set; }

    [Column("location_code")]
    public string? LocationCode { get; set; }

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("remark")]
    public string? Remark { get; set; }
}

/// <summary>
/// 系统日志
/// </summary>
public class SystemLog : TimestampEntity
{
    [Column("module")]
    public string Module { get; set; } = string.Empty;

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("operator_name")]
    public string? OperatorName { get; set; }

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("old_value")]
    public string? OldValue { get; set; }

    [Column("new_value")]
    public string? NewValue { get; set; }

    [Column("result_code")]
    public int? ResultCode { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 订单结案
/// </summary>
public class OrderClosure : BaseEntity
{
    [Column("production_order_id")]
    public long ProductionOrderId { get; set; }

    [Column("actual_output")]
    public decimal ActualOutput { get; set; }

    [Column("good_qty")]
    public decimal GoodQty { get; set; }

    [Column("scrap_qty")]
    public decimal ScrapQty { get; set; }

    [Column("total_loss")]
    public decimal TotalLoss { get; set; }

    [Column("part_remain_details")]
    public string? PartRemainDetails { get; set; }

    [Column("close_note")]
    public string? CloseNote { get; set; }

    [Column("closed_at")]
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}
