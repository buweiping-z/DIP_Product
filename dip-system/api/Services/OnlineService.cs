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

        var scans = await _db.PrepScanRecords
            .Where(s => s.PrepDetailId == detailId).OrderBy(s => s.CreatedAt).ToListAsync();
        if (scans.Count == 0) throw AppException.Business("无备料扫描记录");

        var totalFrozen = scans.Sum(s => s.Quantity);
        if (totalFrozen < reqQty) throw AppException.Business("冻结库存不足");

        string stationNo = "";
        if (stationId.HasValue)
        {
            var st = await _db.Stations.FirstOrDefaultAsync(s => s.Id == stationId.Value);
            if (st != null) stationNo = st.StationNo;
        }

        var confirm = new OnlineConfirm
        {
            PrepOrderId = detail.PrepOrderId, PrepDetailId = detailId,
            PartId = detail.PartId, PartNo = detail.PartNo, BatchNo = scans[0].BatchNo ?? "",
            LoadedQty = reqQty, StationId = stationId, StationNo = stationNo,
            SourceLocationId = scans[0].SourceLocationId, Barcode = barcode,
            EquipmentId = equipmentId, OperatorId = operatorId, Status = 1
        };
        _db.OnlineConfirms.Add(confirm);
        await _db.SaveChangesAsync();

        var invSvc = new InventoryService(_db);
        var remaining = reqQty;
        foreach (var scan in scans)
        {
            if (remaining <= 0) break;
            var deduct = Math.Min(remaining, scan.Quantity);
            await invSvc.DeductCoreAsync(detail.PartId, scan.SourceLocationId, deduct, operatorId, "OnlineOut", confirm.Id);
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
        var items = await query.OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    private static object ToDict(OnlineConfirm c) => new
    {
        c.Id, prep_order_id = c.PrepOrderId, prep_detail_id = c.PrepDetailId,
        part_id = c.PartId, part_no = c.PartNo, batch_no = c.BatchNo,
        loaded_qty = c.LoadedQty, station_no = c.StationNo, status = c.Status, confirmed_at = c.ConfirmedAt
    };
}
