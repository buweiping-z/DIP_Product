using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class StockCountService
{
    private readonly AppDbContext _db;

    public StockCountService(AppDbContext db) { _db = db; }

    public async Task<byte[]> ExportTemplateAsync()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("盘点模板");
        ws.Cell(1, 1).Value = "部品编号";
        ws.Cell(1, 2).Value = "库位编码";
        ws.Cell(1, 3).Value = "实盘数量";

        var inventories = await _db.Inventories.Where(i => i.TotalQty > 0).OrderBy(i => i.PartId).ThenBy(i => i.LocationId).ToListAsync();
        int row = 2;
        foreach (var inv in inventories)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == inv.PartId);
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
            ws.Cell(row, 1).Value = part?.PartNo ?? "";
            ws.Cell(row, 2).Value = loc?.LocationCode ?? "";
            row++;
        }
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 22;
        ws.Column(3).Width = 14;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<object> ImportResultAsync(byte[] fileBytes, long operatorId)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);

        var updates = new List<(string partNo, string locationCode, decimal actualQty)>();
        var updatedKeys = new HashSet<string>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var pn = row.Cell(1).GetString().Trim();
            var lc = row.Cell(2).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(lc)) continue;
            var actualStr = row.Cell(3).GetString().Trim();
            if (string.IsNullOrEmpty(actualStr)) continue;
            try
            {
                var actualQty = decimal.Parse(actualStr);
                if (actualQty < 0) actualQty = 0;
                updatedKeys.Add($"{pn}|{lc}");
                updates.Add((pn, lc, actualQty));
            }
            catch { continue; }
        }

        if (updates.Count == 0) throw AppException.Business("未检测到有效的实盘数据");

        var countNo = $"SC{DateTime.UtcNow:yyyyMMddHHmmss}";
        var count = new StockCount { CountNo = countNo, Status = 1 };
        _db.StockCounts.Add(count);
        await _db.SaveChangesAsync();

        int updated = 0, zeroed = 0;

        foreach (var (partNo, locationCode, actualQty) in updates)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == partNo);
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode);
            if (part == null || loc == null) continue;

            var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == part.Id && i.LocationId == loc.Id);
            var oldQty = inv?.TotalQty ?? 0;
            if (inv == null)
            {
                inv = new Inventory { PartId = part.Id, LocationId = loc.Id };
                _db.Inventories.Add(inv);
                await _db.SaveChangesAsync();
            }

            inv.TotalQty = actualQty;
            inv.AvailableQty = actualQty;
            inv.Version++;
            loc.CurrentQty += (actualQty - oldQty);

            _db.StockMovements.Add(new StockMovement
            {
                PartId = part.Id, PartNo = part.PartNo, LocationId = loc.Id,
                MovementType = 6, Quantity = actualQty, BalanceAfter = actualQty,
                ReferenceType = "StockCount", ReferenceId = count.Id, OperatorId = operatorId
            });
            _db.StockCountItems.Add(new StockCountItem
            {
                StockCountId = count.Id, PartId = part.Id, PartNo = part.PartNo,
                LocationId = loc.Id, SystemQty = oldQty, ActualQty = actualQty, DifferenceQty = actualQty - oldQty
            });
            updated++;
        }

        // Zero out inventory items NOT in the Excel
        var allInvs = await _db.Inventories.Where(i => i.TotalQty > 0).ToListAsync();
        foreach (var inv in allInvs)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == inv.PartId);
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
            var key = $"{part?.PartNo ?? ""}|{loc?.LocationCode ?? ""}";
            if (updatedKeys.Contains(key)) continue;

            var sysQty = inv.TotalQty;
            inv.TotalQty = 0;
            inv.AvailableQty = 0;
            inv.FrozenQty = 0;
            inv.Version++;
            if (loc != null) loc.CurrentQty -= sysQty;

            _db.StockMovements.Add(new StockMovement
            {
                PartId = inv.PartId, PartNo = part?.PartNo ?? "", LocationId = inv.LocationId,
                MovementType = 6, Quantity = 0, BalanceAfter = 0,
                ReferenceType = "StockCountZero", ReferenceId = count.Id, OperatorId = operatorId
            });
            _db.StockCountItems.Add(new StockCountItem
            {
                StockCountId = count.Id, PartId = inv.PartId, PartNo = part?.PartNo ?? "",
                LocationId = inv.LocationId, SystemQty = sysQty, ActualQty = 0, DifferenceQty = -sysQty
            });
            zeroed++;
        }

        count.Status = 2;
        count.ConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new { count_no = countNo, updated, zeroed, total = updated + zeroed };
    }

    public async Task<object> GetListAsync(int? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.StockCounts.AsQueryable();
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long countId)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == countId);
        if (count == null) throw AppException.NotFound($"盘点单 {countId} 不存在");
        return ToDict(count);
    }

    private object ToDict(StockCount c)
    {
        var items = _db.StockCountItems.Where(i => i.StockCountId == c.Id).ToList();
        return new
        {
            c.Id, count_no = c.CountNo, status = c.Status,
            confirmed_at = c.ConfirmedAt, created_at = c.CreatedAt,
            items = items.Select(i => (object)new
            {
                i.Id, part_no = i.PartNo, location_id = i.LocationId,
                system_qty = i.SystemQty, actual_qty = i.ActualQty ?? 0, difference_qty = i.DifferenceQty ?? 0
            })
        };
    }
}
