using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;

namespace DIP.Api.Services;

public class DashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) { _db = db; }

    public async Task<object> GetStatsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var todayStart = today;

        // 订单状态分布
        var orders = await _db.ProductionOrders.Where(o => !o.IsDeleted).ToListAsync();
        var orderStats = new
        {
            total = orders.Count,
            pending = orders.Count(o => o.Status == 1),
            in_progress = orders.Count(o => o.Status == 2),
            done = orders.Count(o => o.Status == 3),
            cancelled = orders.Count(o => o.Status == 4)
        };

        // 备料统计
        var preps = await _db.PrepOrders.Where(p => !p.IsDeleted).ToListAsync();
        var prepStats = new
        {
            total = preps.Count,
            pending = preps.Count(p => p.Status == 1),
            done = preps.Count(p => p.Status == 2),
            cancelled = preps.Count(p => p.Status == 3)
        };

        // 备料完成率
        var prepRate = prepStats.total > 0
            ? Math.Round((double)prepStats.done / prepStats.total * 100, 1)
            : 0;

        // 库存预警（基于可用数量，非总数量）
        var lowStock = await _db.Inventories
            .CountAsync(i => !i.IsDeleted && i.AvailableQty > 0 && i.AvailableQty < 10);
        var outOfStock = await _db.Inventories
            .CountAsync(i => !i.IsDeleted && i.AvailableQty == 0);

        var pendingReplenish = await _db.PrepDetails
            .CountAsync(d => !d.IsDeleted && d.Status == 3);

        var pendingReplenishItems = await GetPendingReplenishItemsAsync();

        var inventoryAlerts = new { low_stock = lowStock, out_of_stock = outOfStock, pending_replenish = pendingReplenish, pending_replenish_items = pendingReplenishItems };

        // 今日操作统计
        var todayPrepScans = await _db.PrepScanRecords
            .CountAsync(s => s.CreatedAt >= todayStart);
        var todayReturns = await _db.ReturnOrders
            .CountAsync(r => !r.IsDeleted && r.CreatedAt >= todayStart);
        var todayShelving = await _db.ShelvingBatches
            .CountAsync(b => !b.IsDeleted && b.ConfirmedAt >= todayStart);

        var todayOps = new
        {
            prep_scans = todayPrepScans,
            returns = todayReturns,
            shelving = todayShelving
        };

        // 补料统计：按批次号分组，整批全部step=3才算完成
        var refillRecords = await _db.RefillRecords
            .Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.BatchNo)).ToListAsync();
        var refillBatches = refillRecords.GroupBy(r => r.BatchNo).Select(g => new {
            done = g.Any(r => r.Step >= 3),  // 有一笔核对完成即算完成
            today = g.Any(r => r.CreatedAt >= todayStart)
        }).ToList();
        var refillStats = new {
            active = refillBatches.Count(b => !b.done),
            done = refillBatches.Count(b => b.done),
            today = refillBatches.Count(b => b.today)
        };

        return new
        {
            order_stats = orderStats,
            prep_stats = prepStats,
            prep_rate = prepRate,
            inventory_alerts = inventoryAlerts,
            today_ops = todayOps,
            refill_stats = refillStats
        };
    }

    public async Task<List<object>> GetPendingReplenishItemsAsync()
    {
        var pendingItems = await _db.PrepDetails
            .Where(d => !d.IsDeleted && d.Status == 3)
            .Select(d => new
            {
                d.PartNo, d.RequiredQty, d.ActualQty,
                shortage = d.RequiredQty - d.ActualQty,
                prep_order_id = d.PrepOrderId
            }).ToListAsync();

        var prepOrderIds = pendingItems.Select(p => p.prep_order_id).Distinct().ToList();
        var prepOrders = await _db.PrepOrders.Where(p => prepOrderIds.Contains(p.Id)).ToListAsync();
        var prodOrderIds = prepOrders.Select(p => p.ProductionOrderId).Distinct().ToList();
        var prodOrders = await _db.ProductionOrders.Where(o => prodOrderIds.Contains(o.Id)).ToListAsync();

        var partNos = pendingItems.Select(p => p.PartNo).Distinct().ToList();
        var parts = await _db.Parts.Where(p => partNos.Contains(p.PartNo)).ToListAsync();
        var partIds = parts.Select(p => p.Id).ToList();
        var inventories = await _db.Inventories.Where(i => partIds.Contains(i.PartId)).ToListAsync();
        var locIds = inventories.Select(i => i.LocationId).Distinct().ToList();
        var locations = await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToListAsync();

        return pendingItems.Select(p =>
        {
            var prep = prepOrders.FirstOrDefault(po => po.Id == p.prep_order_id);
            var prod = prodOrders.FirstOrDefault(o => prep != null && o.Id == prep.ProductionOrderId);
            var part = parts.FirstOrDefault(pt => pt.PartNo == p.PartNo);
            var relatedInvs = inventories.Where(i => i.PartId == part?.Id).ToList();
            var locCodes = relatedInvs.Select(i =>
            {
                var loc = locations.FirstOrDefault(l => l.Id == i.LocationId);
                return loc?.LocationCode ?? "";
            }).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

            return (object)new
            {
                part_no = p.PartNo,
                required_qty = p.RequiredQty,
                frozen_qty = p.ActualQty,
                shortage = p.shortage,
                location_codes = locCodes,
                order_no = prod?.OrderNo ?? "",
                product_name = prod?.ProductName ?? ""
            };
        }).ToList();
    }
}
