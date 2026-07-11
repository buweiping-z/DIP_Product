using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 盘点单
/// </summary>
public class StockCount : BaseEntity
{
    [Column("count_no")]
    public string CountNo { get; set; } = string.Empty;

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    public List<StockCountItem> Items { get; set; } = new();
}

/// <summary>
/// 盘点明细
/// </summary>
public class StockCountItem : BaseEntity
{
    [Column("stock_count_id")]
    public long StockCountId { get; set; }

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("location_id")]
    public long LocationId { get; set; }

    [Column("system_qty")]
    public decimal SystemQty { get; set; }

    [Column("actual_qty")]
    public decimal? ActualQty { get; set; }

    [Column("difference_qty")]
    public decimal? DifferenceQty { get; set; }

    [ForeignKey(nameof(StockCountId))]
    public StockCount? StockCount { get; set; }
}
