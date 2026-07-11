using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class AbnormalService
{
    private readonly AppDbContext _db;

    public AbnormalService(AppDbContext db) { _db = db; }

    public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
    {
        var record = new AbnormalRecord
        {
            Type = data.GetInt("type") ?? 1,
            Severity = data.GetInt("severity") ?? 1,
            Description = data.GetStr("description")!,
            PrepOrderId = data.GetLong("prep_order_id"),
            PartId = data.GetLong("part_id"),
            Status = 1
        };
        _db.AbnormalRecords.Add(record);
        await _db.SaveChangesAsync();
        return ToDict(record);
    }

    public async Task HandleAsync(long recordId, long handlerId, string handleNote)
    {
        var record = await _db.AbnormalRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record == null) throw AppException.NotFound($"异常记录 {recordId} 不存在");
        record.Status = 2;
        record.HandlerId = handlerId;
        record.HandleNote = handleNote;
        record.HandledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<object> GetByIdAsync(long recordId)
    {
        var record = await _db.AbnormalRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record == null) throw AppException.NotFound($"异常记录 {recordId} 不存在");
        return ToDict(record);
    }

    public async Task<object> GetListAsync(int? type = null, int? severity = null, int? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.AbnormalRecords.AsQueryable();
        if (type.HasValue) query = query.Where(r => r.Type == type.Value);
        if (severity.HasValue) query = query.Where(r => r.Severity == severity.Value);
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    private static object ToDict(AbnormalRecord r) => new
    {
        r.Id, type = r.Type, severity = r.Severity, description = r.Description,
        prep_order_id = r.PrepOrderId, part_id = r.PartId, status = r.Status,
        handler_id = r.HandlerId, handle_note = r.HandleNote,
        handled_at = r.HandledAt, created_at = r.CreatedAt
    };

}
