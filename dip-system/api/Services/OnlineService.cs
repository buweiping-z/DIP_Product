using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OnlineService
{
    private readonly AppDbContext _db;

    public OnlineService(AppDbContext db) { _db = db; }

    public async Task<object> ConfirmAsync(long detailId, string barcode, decimal reqQty,
        long? stationId, long? equipmentId, long operatorId)
    {
        if (reqQty <= 0) throw AppException.Business("数量必须大于0");

        var detail = await _db.PrepDetails.FirstOrDefaultAsync(d => d.Id == detailId);
        if (detail == null) throw AppException.NotFound($"备料明细 {detailId} 不存在");

        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == detail.PrepOrderId);
        if (prep == null || prep.Status != 2) throw AppException.Business("备料单未完成");

        // 直接从冻结库存扣减（兼容新旧流程，不依赖 PrepScanRecords）
        var frozenInvs = await _db.Inventories
            .Where(i => i.PartId == detail.PartId && i.FrozenQty > 0).ToListAsync();
        var totalFrozen = frozenInvs.Sum(i => (decimal?)i.FrozenQty) ?? 0m;
        if (totalFrozen < reqQty) throw AppException.Business("冻结库存不足");

        var firstInv = frozenInvs.First();
        var firstLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == firstInv.LocationId);

        string stationNo = "";
        if (stationId.HasValue)
        {
            var st = await _db.Stations.FirstOrDefaultAsync(s => s.Id == stationId.Value);
            if (st != null) stationNo = st.StationNo;
        }

        var confirm = new OnlineConfirm
        {
            PrepOrderId = detail.PrepOrderId, PrepDetailId = detailId,
            PartId = detail.PartId, PartNo = detail.PartNo, BatchNo = "",
            LoadedQty = reqQty, StationId = stationId, StationNo = stationNo,
            SourceLocationId = firstInv.LocationId, Barcode = barcode,
            EquipmentId = equipmentId, OperatorId = operatorId, Status = 1
        };
        _db.OnlineConfirms.Add(confirm);
        await _db.SaveChangesAsync();

        var invSvc = new InventoryService(_db);
        var remaining = reqQty;
        foreach (var inv in frozenInvs)
        {
            if (remaining <= 0) break;
            var deduct = Math.Min(remaining, inv.FrozenQty);
            await invSvc.DeductCoreAsync(detail.PartId, inv.LocationId, deduct, operatorId, "OnlineOut", confirm.Id);
            remaining -= deduct;
        }
        await _db.SaveChangesAsync();

        // 检查订单所有备料明细是否已全部上线消耗完毕 → 订单完成
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == prep.ProductionOrderId);
        if (order != null && order.Status == 2)
        {
            var allPrepIds = await _db.PrepOrders
                .Where(p => p.ProductionOrderId == order.Id && p.Status == 2)
                .Select(p => p.Id).ToListAsync();
            var allDetailIds = await _db.PrepDetails
                .Where(d => allPrepIds.Contains(d.PrepOrderId))
                .Select(d => d.Id).ToListAsync();
            var totalRequired = await _db.PrepDetails
                .Where(d => allPrepIds.Contains(d.PrepOrderId))
                .SumAsync(d => d.RequiredQty);
            var totalConsumed = await _db.OnlineConfirms
                .Where(c => allDetailIds.Contains(c.PrepDetailId))
                .SumAsync(c => c.LoadedQty);
            if (totalConsumed >= totalRequired)
            {
                order.Status = 3;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        return new
        {
            id = confirm.Id, prep_order_id = confirm.PrepOrderId,
            part_no = confirm.PartNo, loaded_qty = confirm.LoadedQty, confirmed_at = confirm.ConfirmedAt
        };
    }

    public async Task<object> GetListAsync(string? partNo = null, string? stationNo = null,
        DateTime? startDate = null, DateTime? endDate = null,
        long? prepOrderId = null, long? partId = null, int page = 1, int pageSize = 20)
    {
        var query = _db.OnlineConfirms.AsQueryable();

        if (!string.IsNullOrEmpty(partNo))
            query = query.Where(c => c.PartNo.Contains(partNo));

        if (!string.IsNullOrEmpty(stationNo))
            query = query.Where(c => c.StationNo.Contains(stationNo));

        if (startDate.HasValue)
            query = query.Where(c => c.ConfirmedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(c => c.ConfirmedAt < endDate.Value.AddDays(1));

        if (prepOrderId.HasValue) query = query.Where(c => c.PrepOrderId == prepOrderId.Value);
        if (partId.HasValue) query = query.Where(c => c.PartId == partId.Value);

        var total = await query.CountAsync();
        var rawItems = await query.OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // 批量加载关联数据，避免 N+1
        var prepIds = rawItems.Select(c => c.PrepOrderId).Distinct().ToList();
        var prepOrders = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToListAsync();
        var prodOrderIds = prepOrders.Select(p => p.ProductionOrderId).Distinct().ToList();
        var prodOrders = await _db.ProductionOrders.Where(o => prodOrderIds.Contains(o.Id)).ToListAsync();

        var items = rawItems.Select(c =>
        {
            var prep = prepOrders.FirstOrDefault(p => p.Id == c.PrepOrderId);
            var prod = prep != null ? prodOrders.FirstOrDefault(o => o.Id == prep.ProductionOrderId) : null;
            return (object)new
            {
                c.Id, prep_order_id = c.PrepOrderId, prep_order_no = prep?.OrderNo ?? "",
                prod_order_no = prod?.OrderNo ?? "", product_name = prod?.ProductName ?? "",
                prep_detail_id = c.PrepDetailId, part_id = c.PartId, part_no = c.PartNo,
                batch_no = c.BatchNo, loaded_qty = c.LoadedQty, station_no = c.StationNo,
                source_location_code = c.SourceLocationCode, barcode = c.Barcode,
                status = c.Status, confirmed_at = c.ConfirmedAt
            };
        }).ToList();

        return new { total, page, page_size = pageSize, items };
    }
}
