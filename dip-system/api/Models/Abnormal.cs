using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 异常记录
/// </summary>
public class AbnormalRecord : BaseEntity
{
    [Column("type")]
    public int Type { get; set; }

    [Column("severity")]
    public int Severity { get; set; }

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("prep_order_id")]
    public long? PrepOrderId { get; set; }

    [Column("part_id")]
    public long? PartId { get; set; }

    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("handler_id")]
    public long? HandlerId { get; set; }

    [Column("handle_note")]
    public string? HandleNote { get; set; }

    [Column("handled_at")]
    public DateTime? HandledAt { get; set; }
}
