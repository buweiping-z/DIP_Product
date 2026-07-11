using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 替代料移库记录
/// </summary>
public class SubstituteRecord : TimestampEntity
{
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

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    [Column("confirmed_by")]
    public long? ConfirmedBy { get; set; }
}
