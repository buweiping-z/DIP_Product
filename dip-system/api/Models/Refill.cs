using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 补料扫描记录 — 纯验证流程，不涉及库存数量变更
/// Step: 1=待取料 2=已取料 3=已核对
/// </summary>
public class RefillRecord : BaseEntity
{
    [Column("prep_order_id")]
    public long PrepOrderId { get; set; }

    [Column("prep_detail_id")]
    public long PrepDetailId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [Column("location_code")]
    public string LocationCode { get; set; } = string.Empty;

    [Column("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Column("batch_no")]
    public string BatchNo { get; set; } = "";

    [Column("step")]
    public int Step { get; set; } = 1; // 1=待取料 2=已取料 3=已核对

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("picked_at")]
    public DateTime? PickedAt { get; set; }

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; }
}
