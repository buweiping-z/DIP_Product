using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class LocationService
{
    private readonly AppDbContext _db;

    public LocationService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(string? warehouse = null, string? zone = null, int? status = null,
        string? locationCode = null, int page = 1, int pageSize = 20)
    {
        var query = _db.WarehouseLocations.AsQueryable();
        if (!string.IsNullOrEmpty(warehouse)) query = query.Where(l => l.Warehouse == warehouse);
        if (!string.IsNullOrEmpty(zone)) query = query.Where(l => l.Zone == zone);
        if (!string.IsNullOrEmpty(locationCode)) query = query.Where(l => l.LocationCode.Contains(locationCode));
        if (status.HasValue) query = query.Where(l => l.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(l => l.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long locId)
    {
        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locId);
        if (loc == null) throw AppException.NotFound($"库位 {locId} 不存在");
        return ToDict(loc);
    }

    public async Task<object> CreateAsync(Dictionary<string, object?> data)
    {
        var loc = new WarehouseLocation
        {
            LocationCode = data.GetStr("location_code")!,
            Warehouse = data.GetStr("warehouse") ?? "",
            Zone = data.GetStr("zone") ?? "",
            Row = data.GetStr("row") ?? "",
            Column = data.GetStr("column") ?? "",
            MaxCapacity = data.GetDecimal("max_capacity"),
            Status = 1
        };
        _db.WarehouseLocations.Add(loc);
        await _db.SaveChangesAsync();
        return ToDict(loc);
    }

    public async Task<object> UpdateAsync(long locId, Dictionary<string, object?> data)
    {
        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locId);
        if (loc == null) throw AppException.NotFound($"库位 {locId} 不存在");

        data.ApplyTo(loc, new[] { "warehouse", "zone", "row", "column", "max_capacity", "status" });

        await _db.SaveChangesAsync();
        return ToDict(loc);
    }

    public async Task DeleteAsync(long locId)
    {
        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locId);
        if (loc == null) throw AppException.NotFound($"库位 {locId} 不存在");
        loc.IsDeleted = true;

        // 清理该库位的所有库存记录
        var inventories = await _db.Inventories.Where(i => i.LocationId == locId).ToListAsync();
        foreach (var inv in inventories) inv.IsDeleted = true;

        // 清理该库位的所有批次记录
        var lots = await _db.InventoryLots.Where(l => l.LocationId == locId).ToListAsync();
        foreach (var lot in lots) lot.IsDeleted = true;

        // 库位数量清零
        loc.CurrentQty = 0;

        await _db.SaveChangesAsync();
    }

    public async Task<List<object>> GetLinesAsync()
    {
        var lines = await _db.ProductionLines.Where(l => l.Status == 1).ToListAsync();
        return lines.Select(l => (object)new { l.Id, line_no = l.LineNo, line_name = l.LineName }).ToList();
    }

    public async Task<List<string>> GetWarehousesAsync()
    {
        return await _db.WarehouseLocations.Select(l => l.Warehouse).Distinct().Where(w => w != "").ToListAsync();
    }

    public async Task<int> ImportFromExcelAsync(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        int count = 0;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var code = row.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(code)) continue;
            var wh = row.Cell(2).GetString().Trim();
            var existing = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == code);
            if (existing != null)
            {
                if (!string.IsNullOrEmpty(wh)) existing.Warehouse = wh;
                existing.Zone = row.Cell(3).GetString().Trim();
                existing.Row = row.Cell(4).GetString().Trim();
                existing.Column = row.Cell(5).GetString().Trim();
                if (row.Cell(6).TryGetValue(out decimal cap)) existing.MaxCapacity = cap;
            }
            else
            {
                _db.WarehouseLocations.Add(new WarehouseLocation
                {
                    LocationCode = code, Warehouse = wh,
                    Zone = row.Cell(3).GetString().Trim(), Row = row.Cell(4).GetString().Trim(),
                    Column = row.Cell(5).GetString().Trim(),
                    MaxCapacity = row.Cell(6).TryGetValue(out decimal cap2) ? cap2 : 10000,
                    Status = 1
                });
            }
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    public async Task<byte[]> ExportTemplateAsync()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("库位导入模板");
        ws.Cell(1, 1).Value = "库位编码";
        ws.Cell(1, 2).Value = "仓库";
        ws.Cell(1, 3).Value = "库区";
        ws.Cell(1, 4).Value = "排";
        ws.Cell(1, 5).Value = "列";
        ws.Cell(1, 6).Value = "最大容量";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static object ToDict(WarehouseLocation l) => new
    {
        l.Id, location_code = l.LocationCode, warehouse = l.Warehouse,
        zone = l.Zone, row = l.Row, column = l.Column,
        max_capacity = l.MaxCapacity, current_qty = l.CurrentQty, status = l.Status
    };
}
