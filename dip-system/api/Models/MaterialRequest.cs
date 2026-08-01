using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 叫料申请表 — 库位上料不足时向部管叫料
/// Status: 0=待处理 1=已处理 2=已取消
/// </summary>
[Table("material_requests")]
public class MaterialRequest : BaseEntity
{
    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    [Column("part_id")]
    public long PartId { get; set; }

    [Column("location_code")]
    public string LocationCode { get; set; } = string.Empty;

    [Column("status")]
    public int Status { get; set; } = 0;

    [Column("operator_id")]
    public long OperatorId { get; set; }
}
