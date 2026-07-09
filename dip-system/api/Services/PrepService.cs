using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class KitCheckResult
{
    public long PrepDetailId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = "";
    public decimal RequiredQty { get; set; }
    public decimal AvailableQty { get; set; }
    public int Status { get; set; }
}

public class PrepService
{
    private readonly AppDbContext _db;

    public PrepService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(int? status = null, long? lineId = null, int page = 1, int pageSize = 20)
    {
        var query = _db.PrepOrders.AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (lineId.HasValue) query = query.Where(p => p.LineId == lineId.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
        return new
        {
            prep.Id, order_no = prep.OrderNo, production_order_id = prep.ProductionOrderId,
            line_id = prep.LineId, status = prep.Status, kit_check_result = prep.KitCheckResult,
            completed_at = prep.CompletedAt, created_at = prep.CreatedAt,
            details = details.Select(DetailToDict)
        };
    }

    public async Task<object> GetDetailAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == prep.ProductionOrderId);
        var planQty = order?.PlanQty ?? 1;
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();

        var detailList = new List<object>();
        foreach (var d in details)
        {
            var item = new Dictionary<string, object?>
            {
                ["id"] = d.Id, ["part_id"] = d.PartId, ["part_no"] = d.PartNo,
                ["required_qty"] = d.RequiredQty, ["actual_qty"] = d.ActualQty,
                ["status"] = d.Status, ["substitute_flag"] = d.SubstituteFlag,
                ["total_required_qty"] = d.RequiredQty * planQty
            };

            var inventories = await _db.Inventories.Where(i => i.PartId == d.PartId).ToListAsync();
            var stocks = new List<object>();
            foreach (var inv in inventories)
            {
                var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
                stocks.Add(new { location_code = loc?.LocationCode ?? "", location_id = inv.LocationId, available_qty = inv.AvailableQty });
            }
            item["stocks"] = stocks;
            detailList.Add(item);
        }

        return new
        {
            prep.Id, order_no = prep.OrderNo, production_order_id = prep.ProductionOrderId,
            line_id = prep.LineId, status = prep.Status, kit_check_result = prep.KitCheckResult,
            product_name = order?.ProductName ?? "", plan_qty = planQty,
            completed_at = prep.CompletedAt, created_at = prep.CreatedAt, details = detailList
        };
    }

    public async Task<object> KitCheckAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");

        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
        var invSvc = new InventoryService(_db);
        var results = new List<object>();

        foreach (var d in details)
        {
            var totalAvail = await _db.Inventories
                .Where(i => i.PartId == d.PartId)
                .SumAsync(i => i.AvailableQty);
            int st = totalAvail >= d.RequiredQty ? 1 : 2;
            if (st != 1)
            {
                var subs = await _db.PartSubstitutes
                    .Where(s => s.OriginalPartId == d.PartId && s.Status == 1)
                    .OrderByDescending(s => s.ValidTo).ToListAsync();
                foreach (var sub in subs)
                {
                    var subTotalAvail = await _db.Inventories
                        .Where(i => i.PartId == sub.SubstitutePartId)
                        .SumAsync(i => i.AvailableQty);
                    if (subTotalAvail >= d.RequiredQty) { st = 3; break; }
                }
            }
            results.Add(new KitCheckResult
            {
                PrepDetailId = d.Id, PartId = d.PartId, PartNo = d.PartNo,
                RequiredQty = d.RequiredQty, AvailableQty = totalAvail, Status = st
            });
        }

        var typedResults = results.Cast<KitCheckResult>().ToList();
        int overall = typedResults.All(r => r.Status == 1 || r.Status == 3) ? 1
            : typedResults.All(r => r.Status >= 2) ? 3 : 2;
        prep.KitCheckResult = overall;
        prep.KitCheckTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { prep_order_id = prepId, overall_status = overall,
            items = typedResults.Select(r => new { prep_detail_id = r.PrepDetailId, part_id = r.PartId,
                part_no = r.PartNo, required_qty = r.RequiredQty, available_qty = r.AvailableQty, status = r.Status }) };
    }

    public async Task ScanPrepAsync(long prepId, string barcode, long? detailId, long operatorId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null || prep.Status != 1) throw AppException.Business("备料单状态不允许操作");

        PrepDetail? detail = null;
        if (detailId.HasValue)
            detail = await _db.PrepDetails.FirstOrDefaultAsync(d => d.Id == detailId.Value && d.PrepOrderId == prepId);
        if (detail == null)
            detail = await _db.PrepDetails.FirstOrDefaultAsync(d => d.PrepOrderId == prepId && d.PartNo.Contains(barcode));
        if (detail == null) throw AppException.NotFound("未匹配到备料明细");

        var remaining = detail.RequiredQty - detail.ActualQty;
        if (remaining <= 0) throw AppException.Business("该物料已备齐");

        var invSvc = new InventoryService(_db);
        var lots = await invSvc.GetFifoLotsAsync(detail.PartId, remaining);
        if (lots.Count == 0) throw AppException.Business("可用库存不足");

        var firstLot = lots[0];
        var scanQty = Math.Min(firstLot.Quantity, remaining);
        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.Id == firstLot.InventoryId);
        var locId = inv?.LocationId ?? 0;

        _db.PrepScanRecords.Add(new PrepScanRecord
        {
            PrepDetailId = detail.Id, SourceLocationId = locId,
            BatchNo = firstLot.BatchNo, Quantity = scanQty,
            ScannedBarcode = barcode, OperatorId = operatorId
        });

        await invSvc.FreezeCoreAsync(detail.PartId, locId, scanQty, operatorId, "PrepFreeze", prepId);
        detail.ActualQty += scanQty;
        if (detail.ActualQty >= detail.RequiredQty) detail.Status = 2;
        await _db.SaveChangesAsync();

        var remainingDetails = await _db.PrepDetails.CountAsync(d => d.PrepOrderId == prepId && d.Status != 2);
        if (remainingDetails == 0)
        {
            prep.Status = 2;
            prep.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<object> GetRefillsAsync(string? partNo = null, string? locationCode = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 50)
    {
        var query = _db.PrepScanRecords.AsQueryable();

        if (!string.IsNullOrEmpty(partNo))
        {
            var detailIdsByPart = await _db.PrepDetails
                .Where(d => d.PartNo.Contains(partNo))
                .Select(d => d.Id)
                .ToListAsync();
            query = query.Where(s => detailIdsByPart.Contains(s.PrepDetailId));
        }

        if (!string.IsNullOrEmpty(locationCode))
        {
            var locIds = await _db.WarehouseLocations
                .Where(l => l.LocationCode.Contains(locationCode))
                .Select(l => l.Id)
                .ToListAsync();
            query = query.Where(s => locIds.Contains(s.SourceLocationId));
        }

        if (startDate.HasValue)
            query = query.Where(s => s.CreatedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(s => s.CreatedAt < endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var scans = await query.OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var detailIds = scans.Select(s => s.PrepDetailId).Distinct().ToList();
        var details = await _db.PrepDetails.Where(d => detailIds.Contains(d.Id)).ToListAsync();
        var detailMap = details.ToDictionary(d => d.Id);
        var prepIds = details.Select(d => d.PrepOrderId).Distinct().ToList();
        var preps = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToListAsync();
        var prepMap = preps.ToDictionary(p => p.Id);

        // 批量取库位编码和操作者姓名
        var locIds = scans.Select(s => s.SourceLocationId).Where(id => id > 0).Distinct().ToList();
        var userIds = scans.Select(s => s.OperatorId).Distinct().ToList();
        var locsMap = (await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToListAsync())
            .ToDictionary(l => l.Id);
        var usersMap = (await _db.Operators.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        return new
        {
            total, page, page_size = pageSize,
            items = scans.Select(s =>
            {
                detailMap.TryGetValue(s.PrepDetailId, out var detail);
                var prep = detail != null && prepMap.TryGetValue(detail.PrepOrderId, out var p) ? p : null;
                return (object)new
                {
                    s.Id, prep_order_id = detail?.PrepOrderId ?? 0,
                    prep_order_no = prep?.OrderNo ?? "",
                    prep_detail_id = s.PrepDetailId, part_no = detail?.PartNo ?? "",
                    source_location_id = s.SourceLocationId,
                    source_location_code = locsMap.TryGetValue(s.SourceLocationId, out var l) ? l.LocationCode : "",
                    quantity = s.Quantity,
                    operator_id = s.OperatorId,
                    operator_name = usersMap.TryGetValue(s.OperatorId, out var u) ? u.RealName : "",
                    created_at = s.CreatedAt
                };
            })
        };
    }

    public async Task CancelAsync(long prepId, long operatorId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");
        if (prep.Status != 1 && prep.Status != 2) throw AppException.Business("备料单状态不允许撤销");

        var invSvc = new InventoryService(_db);
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prepId && d.ActualQty > 0).ToListAsync();
        foreach (var d in details)
        {
            var scans = await _db.PrepScanRecords.Where(s => s.PrepDetailId == d.Id).ToListAsync();
            foreach (var scan in scans)
                await invSvc.ThawCoreAsync(d.PartId, scan.SourceLocationId, scan.Quantity, operatorId, "PrepThaw", prepId);
        }
        prep.Status = 3;
        await _db.SaveChangesAsync();
    }

    public async Task<List<object>> GetPendingItemsAsync()
    {
        var details = await _db.PrepDetails
            .Include(d => d.PrepOrder)
            .Where(d => d.PrepOrder!.Status == 1 && d.Status == 1 && d.ActualQty < d.RequiredQty)
            .OrderBy(d => d.Id).ToListAsync();

        var result = new List<object>();
        foreach (var d in details)
        {
            var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == d.PrepOrder!.ProductionOrderId);
            result.Add(new
            {
                prep_detail_id = d.Id, prep_order_id = d.PrepOrderId, prep_order_no = d.PrepOrder!.OrderNo,
                product_name = order?.ProductName ?? "", part_id = d.PartId, part_no = d.PartNo,
                required_qty = d.RequiredQty, actual_qty = d.ActualQty, remaining = d.RequiredQty - d.ActualQty
            });
        }
        return result;
    }

    private static object ToDict(PrepOrder p) => new
    {
        p.Id, order_no = p.OrderNo, production_order_id = p.ProductionOrderId,
        line_id = p.LineId, status = p.Status, kit_check_result = p.KitCheckResult,
        completed_at = p.CompletedAt, created_at = p.CreatedAt
    };

    private static object DetailToDict(PrepDetail d) => new
    {
        d.Id, part_id = d.PartId, part_no = d.PartNo,
        required_qty = d.RequiredQty, actual_qty = d.ActualQty,
        status = d.Status, substitute_flag = d.SubstituteFlag
    };
}
