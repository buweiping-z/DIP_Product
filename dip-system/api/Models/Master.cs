using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 供应商
/// </summary>
public class Supplier : BaseEntity
{
    [Column("supplier_code")]
    public string SupplierCode { get; set; } = string.Empty;

    [Column("supplier_name")]
    public string SupplierName { get; set; } = string.Empty;

    [Column("contact")]
    public string? Contact { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;
}

/// <summary>
/// 部品/物料
/// </summary>
public class Part : BaseEntity
{
    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [Column("supplier_id")]
    public long? SupplierId { get; set; }

    [Column("supplier_name")]
    public string SupplierName { get; set; } = string.Empty;

    [Column("part_type")]
    public int PartType { get; set; } = 1;

    [Column("unit")]
    public string Unit { get; set; } = "PCS";

    [Column("unit_price")]
    public decimal UnitPrice { get; set; }

    [Column("specification")]
    public string Specification { get; set; } = string.Empty;

    [Column("msl_level")]
    public int MslLevel { get; set; }

    [Column("min_stock")]
    public int? MinStock { get; set; }

    [Column("max_stock")]
    public int? MaxStock { get; set; }

    [Column("barcode_rule")]
    public string? BarcodeRule { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [ForeignKey(nameof(SupplierId))]
    public Supplier? Supplier { get; set; }
}

/// <summary>
/// 替代料关系
/// </summary>
public class PartSubstitute : BaseEntity
{
    [Column("original_part_id")]
    public long OriginalPartId { get; set; }

    [Column("substitute_part_id")]
    public long SubstitutePartId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("valid_from")]
    public DateTime? ValidFrom { get; set; }

    [Column("valid_to")]
    public DateTime? ValidTo { get; set; }

    [Column("substitute_reason")]
    public string? SubstituteReason { get; set; }
}

/// <summary>
/// 产线
/// </summary>
public class ProductionLine : BaseEntity
{
    [Column("line_no")]
    public string LineNo { get; set; } = string.Empty;

    [Column("line_name")]
    public string LineName { get; set; } = string.Empty;

    [Column("capacity")]
    public int Capacity { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    public List<Station> Stations { get; set; } = new();
}

/// <summary>
/// 工位
/// </summary>
public class Station : BaseEntity
{
    [Column("station_no")]
    public string StationNo { get; set; } = string.Empty;

    [Column("line_id")]
    public long LineId { get; set; }

    [Column("station_name")]
    public string StationName { get; set; } = string.Empty;

    [Column("process_order")]
    public int ProcessOrder { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [ForeignKey(nameof(LineId))]
    public ProductionLine? Line { get; set; }
}

/// <summary>
/// 库位
/// </summary>
public class WarehouseLocation : BaseEntity
{
    [Column("location_code")]
    public string LocationCode { get; set; } = string.Empty;

    [Column("warehouse")]
    public string Warehouse { get; set; } = string.Empty;

    [Column("zone")]
    public string Zone { get; set; } = string.Empty;

    [Column("row")]
    public string Row { get; set; } = string.Empty;

    [Column("column")]
    public string Column { get; set; } = string.Empty;

    [Column("max_capacity")]
    public decimal MaxCapacity { get; set; }

    [Column("current_qty")]
    public decimal CurrentQty { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;
}

/// <summary>
/// 产品 BOM
/// </summary>
public class ProductBom : BaseEntity
{
    [Column("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }
}
