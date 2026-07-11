using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 库存主表
/// </summary>
public class Inventory : BaseEntity
{
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("total_qty")]
    public decimal TotalQty { get; set; }

    [Column("available_qty")]
    public decimal AvailableQty { get; set; }

    [Column("frozen_qty")]
    public decimal FrozenQty { get; set; }

    [Column("inspecting_qty")]
    public decimal InspectingQty { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }

    [ForeignKey(nameof(LocationId))]
    public WarehouseLocation? Location { get; set; }
}

/// <summary>
/// 库存批次表
/// </summary>
public class InventoryLot : BaseEntity
{
    [Column("inventory_id")]
    public long InventoryId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("batch_no")]
    public string BatchNo { get; set; } = string.Empty;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("receipt_date")]
    public DateTime ReceiptDate { get; set; } = DateTime.UtcNow;

    [Column("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("msl_exposure_time")]
    public DateTime? MslExposureTime { get; set; }

    [Column("origin_type")]
    public int OriginType { get; set; } = 1;

    [Column("version")]
    public int Version { get; set; }

    [ForeignKey(nameof(InventoryId))]
    public Inventory? Inventory { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }
}

/// <summary>
/// 库存流水表
/// </summary>
public class StockMovement : TimestampEntity
{
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("location_code")]
    public string? LocationCode { get; set; }

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("movement_type")]
    public int MovementType { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("balance_after")]
    public decimal BalanceAfter { get; set; }

    [Column("reference_type")]
    public string? ReferenceType { get; set; }

    [Column("reference_id")]
    public long? ReferenceId { get; set; }

    [Column("operator_id")]
    public long OperatorId { get; set; }

    [Column("remark")]
    public string? Remark { get; set; }
}
