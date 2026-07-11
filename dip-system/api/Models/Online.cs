using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 上线确认记录
/// </summary>
public class OnlineConfirm : BaseEntity
{
    [Column("prep_order_id")]
    public long PrepOrderId { get; set; }

    [Column("prep_detail_id")]
    public long PrepDetailId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("loaded_qty")]
    public decimal LoadedQty { get; set; }

    [Column("station_id")]
    public long? StationId { get; set; }

    [Column("station_no")]
    public string StationNo { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long SourceLocationId { get; set; }

    [Column("source_location_code")]
    public string? SourceLocationCode { get; set; }

    [Column("equipment_id")]
    public long? EquipmentId { get; set; }

    [Column("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("confirmed_at")]
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}
