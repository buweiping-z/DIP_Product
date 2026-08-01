using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;

namespace DIP.Api.Services;

public class DashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) { _db = db; }

    private static DateTime BeijingTodayStart()
    {
        var beijingNow = DateTime.UtcNow.AddHours(8);
        return beijingNow.Date.AddHours(-8);
    }

    private static DateTime BeijingWeekStart()
    {
        var beijingNow = DateTime.UtcNow.AddHours(8);
        var dayOfWeek = (int)beijingNow.DayOfWeek;
        var daysSinceMonday = dayOfWeek == 0 ? 6 : dayOfWeek - 1;
        return beijingNow.Date.AddDays(-daysSinceMonday).AddHours(-8);
    }

    private static DateTime BeijingMonthStart()
    {
        var beijingNow = DateTime.UtcNow.AddHours(8);
        return new DateTime(beijingNow.Year, beijingNow.Month, 1).AddHours(-8);
    }

    public async Task<object> GetStatsAsync(long? lineId = null)
    {
        var todayStart = BeijingTodayStart();
        var weekStart = BeijingWeekStart();
        var monthStart = BeijingMonthStart();

        // 订单状态分布（按时间段）
        var orderQuery = _db.ProductionOrders.Where(o => !o.IsDeleted);
        if (lineId.HasValue)
            orderQuery = orderQuery.Where(o => o.LineId == lineId.Value);
        var orders = await orderQuery.ToListAsync();
        var orderStats = new
        {
            today = BuildOrderPeriod(orders, todayStart),
            week = BuildOrderPeriod(orders, weekStart),
            month = BuildOrderPeriod(orders, monthStart)
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

        var prepEffective = prepStats.total - prepStats.cancelled;
        var prepRate = prepEffective > 0
            ? Math.Round((double)prepStats.done / prepEffective * 100, 1)
            : 0;

        var prepTodayDone = preps.Count(p => p.Status == 2 && p.CompletedAt >= todayStart);

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
        var todayPrepScans = await _db.PrepDetails
            .CountAsync(d => !d.IsDeleted && d.Status == 2 && d.UpdatedAt >= todayStart);
        var todayReturns = await _db.ReturnOrders
            .CountAsync(r => !r.IsDeleted && r.CreatedAt >= todayStart);
        var todayShelving = await _db.MaterialShelvings
            .CountAsync(m => !m.IsDeleted && m.LoadedAt >= todayStart);

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
            done = g.Any(r => r.Step >= 3),
            today = g.Any(r => r.CreatedAt >= todayStart)
        }).ToList();
        var refillStats = new {
            active = refillBatches.Count(b => !b.done),
            done = refillBatches.Count(b => b.done),
            today = refillBatches.Count(b => b.today)
        };

        // 途中切替统计
        var changeoverBatches = await _db.ChangeoverBatches.Where(b => !b.IsDeleted).ToListAsync();
        var changeoverStats = new {
            active = changeoverBatches.Count(b => b.Status == 1),
            done = changeoverBatches.Count(b => b.Status == 2),
            today = changeoverBatches.Count(b => b.CreatedAt >= todayStart)
        };

        return new
        {
            order_stats = orderStats,
            prep_stats = prepStats,
            prep_rate = prepRate,
            prep_today_done = prepTodayDone,
            inventory_alerts = inventoryAlerts,
            today_ops = todayOps,
            refill_stats = refillStats,
            changeover_stats = changeoverStats
        };
    }

    private static object BuildOrderPeriod(List<DIP.Api.Models.ProductionOrder> orders, DateTime since)
    {
        var period = orders.Where(o => o.CreatedAt >= since).ToList();
        var pending = period.Count(o => o.Status == 1);
        var inProgress = period.Count(o => o.Status == 2);
        var done = period.Count(o => o.Status == 3);
        var effective = pending + inProgress + done;
        var rate = effective > 0 ? Math.Round((double)done / effective * 100, 1) : 0;
        return new { pending, in_progress = inProgress, done, rate };
    }

    public async Task<object> GetMobileCountsAsync()
    {
        var prep = await _db.PrepOrders.CountAsync(p => !p.IsDeleted && p.Status == 1);
        var online = await _db.ProductionOrders.CountAsync(o => !o.IsDeleted && o.Status == 2);
        var substitute = await _db.SubstituteOrders.CountAsync(o => !o.IsDeleted && o.Status == 1);
        var outbound = await _db.OutboundOrders.CountAsync(o => !o.IsDeleted && o.Status == 1);
        var changeover = await _db.ChangeoverBatches.CountAsync(b => !b.IsDeleted && b.Status == 1);

        // 活跃补料批次：有批次号且该批次无 step>=3 的记录
        var refill = await _db.RefillRecords
            .Where(r => !r.IsDeleted && r.BatchNo != "")
            .GroupBy(r => r.BatchNo)
            .Where(g => !g.Any(r => r.Step >= 3))
            .CountAsync();

        var callMaterial = await _db.MaterialRequests.CountAsync(r => !r.IsDeleted && r.Status == 0);

        return new { prep, refill, substitute, online, outbound, changeover, call_material = callMaterial };
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
