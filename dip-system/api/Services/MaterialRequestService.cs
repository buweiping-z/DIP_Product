using DIP.Api.Data;
using DIP.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DIP.Api.Services;

public class MaterialRequestService
{
    private readonly AppDbContext _db;

    public MaterialRequestService(AppDbContext db) { _db = db; }

    /// <summary>
    /// 手机端批量上传叫料请求
    /// </summary>
    public async Task<object> BatchCreateAsync(List<Dictionary<string, object?>> items, long operatorId)
    {
        var created = 0;
        foreach (var item in items)
        {
            var partNo = DictHelper.GetStr(item, "part_no") ?? "";
            var partId = DictHelper.GetLong(item, "part_id") ?? 0;
            var locationCode = DictHelper.GetStr(item, "location_code") ?? "";

            if (string.IsNullOrEmpty(partNo)) continue;

            var request = new MaterialRequest
            {
                PartNo = partNo,
                PartId = partId,
                LocationCode = locationCode,
                Status = 0,
                OperatorId = operatorId,
                CreatedAt = DateTime.UtcNow
            };
            _db.MaterialRequests.Add(request);
            created++;
        }
        await _db.SaveChangesAsync();
        return new { count = created };
    }

    /// <summary>
    /// 网页端分页列表（搜索：料号/库位/状态/日期范围）
    /// </summary>
    public async Task<object> GetListAsync(string? partNo, string? locationCode, int? status,
        string? startDate, string? endDate, int page = 1, int pageSize = 20)
    {
        var query = _db.MaterialRequests.AsQueryable();

        if (!string.IsNullOrEmpty(partNo))
            query = query.Where(r => r.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(locationCode))
            query = query.Where(r => r.LocationCode.Contains(locationCode));
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var sd))
            query = query.Where(r => r.CreatedAt >= sd);
        if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var ed))
            query = query.Where(r => r.CreatedAt < ed.AddDays(1));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.PartNo,
                r.PartId,
                r.LocationCode,
                r.Status,
                r.OperatorId,
                r.CreatedAt
            })
            .ToListAsync();

        return new { items, total };
    }

    /// <summary>
    /// 更新叫料状态
    /// </summary>
    public async Task UpdateStatusAsync(long id, int status)
    {
        var request = await _db.MaterialRequests.FindAsync(id)
            ?? throw new AppException("叫料记录不存在");
        request.Status = status;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 删除叫料记录
    /// </summary>
    public async Task DeleteAsync(long id)
    {
        var request = await _db.MaterialRequests.FindAsync(id)
            ?? throw new AppException("叫料记录不存在");
        _db.MaterialRequests.Remove(request);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 导出 Excel
    /// </summary>
    public async Task<byte[]> ExportAsync(string? partNo, string? locationCode, int? status,
        string? startDate, string? endDate)
    {
        var query = _db.MaterialRequests.AsQueryable();

        if (!string.IsNullOrEmpty(partNo))
            query = query.Where(r => r.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(locationCode))
            query = query.Where(r => r.LocationCode.Contains(locationCode));
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var sd))
            query = query.Where(r => r.CreatedAt >= sd);
        if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var ed))
            query = query.Where(r => r.CreatedAt < ed.AddDays(1));

        var raw = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.PartNo, r.LocationCode, r.Status, r.CreatedAt })
            .ToListAsync();

        var items = raw.Select(r => new
        {
            编号 = r.Id,
            料号 = r.PartNo,
            库位 = r.LocationCode,
            状态 = r.Status == 0 ? "待处理" : r.Status == 1 ? "已处理" : "已取消",
            叫料时间 = r.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        }).ToList();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("叫料记录");
        ws.Cell(1, 1).InsertTable(items);
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
