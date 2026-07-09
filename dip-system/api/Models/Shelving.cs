using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 上架批次
/// </summary>
public class ShelvingBatch : BaseEntity
{
    [Column("batch_no")]
    public string BatchNo { get; set; } = string.Empty;

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    public List<ShelvingBatchItem> Items { get; set; } = new();
}

/// <summary>
/// 上架批次明细
/// </summary>
public class ShelvingBatchItem : BaseEntity
{
    [Column("batch_id")]
    public long BatchId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long? SourceLocationId { get; set; }

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; } = 1;

    [Column("scanned_barcode")]
    public string ScannedBarcode { get; set; } = string.Empty;

    [ForeignKey(nameof(BatchId))]
    public ShelvingBatch? Batch { get; set; }
}

/// <summary>
/// 物料上架记录
/// </summary>
public class MaterialShelving : BaseEntity
{
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long? SourceLocationId { get; set; }

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("scanned_barcode")]
    public string ScannedBarcode { get; set; } = string.Empty;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("loaded_at")]
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
}
