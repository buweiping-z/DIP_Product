using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OnlineService
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;

    public OnlineService(AppDbContext db, IServiceProvider sp) { _db = db; _sp = sp; }

    public async Task<object> ConfirmAsync(long detailId, string barcode, decimal reqQty,
        long? stationId, long? equipmentId, long operatorId)
    {
        if (reqQty <= 0) throw AppException.Business("数量必须大于0");

        var detail = await _db.PrepDetails.FirstOrDefaultAsync(d => d.Id == detailId);
        if (detail == null) throw AppException.NotFound($"备料明细 {detailId} 不存在");

        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == detail.PrepOrderId);
        if (prep == null || prep.Status != 2) throw AppException.Business("备料单未完成");

        string stationNo = "";
        if (stationId.HasValue)
        {
            var st = await _db.Stations.FirstOrDefaultAsync(s => s.Id == stationId.Value);
            if (st != null) stationNo = st.StationNo;
        }

        // 仅记录上线确认（审计追溯），不在此处扣库存
        var confirm = new OnlineConfirm
        {
            PrepOrderId = detail.PrepOrderId, PrepDetailId = detailId,
            PartId = detail.PartId, PartNo = detail.PartNo, BatchNo = "",
            LoadedQty = reqQty, StationId = stationId, StationNo = stationNo,
            Barcode = barcode, EquipmentId = equipmentId, OperatorId = operatorId, Status = 1
        };
        _db.OnlineConfirms.Add(confirm);
        await _db.SaveChangesAsync();

        // 检查订单是否全部确认完毕 → 订单完成时一次性将冻结库存转为实际扣减
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == prep.ProductionOrderId);
        if (order != null && order.Status == 2)
        {
            var allPrepIds = await _db.PrepOrders
                .Where(p => p.ProductionOrderId == order.Id && p.Status == 2)
                .Select(p => p.Id).ToListAsync();
            var allDetails = await _db.PrepDetails
                .Where(d => allPrepIds.Contains(d.PrepOrderId)).ToListAsync();
            var allDetailIds = allDetails.Select(d => d.Id).ToList();
            // 上线只防错不管理数量：所有明细都被确认过至少一次 → 订单完成
            var confirmedCount = await _db.OnlineConfirms
                .Where(c => allDetailIds.Contains(c.PrepDetailId))
                .Select(c => c.PrepDetailId).Distinct()
                .CountAsync();

            Console.WriteLine($"[Online] 订单 {order.OrderNo}: 已确认明细={confirmedCount}/{allDetails.Count}");
            if (confirmedCount >= allDetails.Count)
            {
                Console.WriteLine($"[Online] 订单 {order.OrderNo} → 已完成，开始扣减冻结库存");
                // 按订单 RequiredQty 逐料号扣减，不扣其他订单的冻结量
                var invSvc = _sp.GetRequiredService<InventoryService>();
                var orderSvc = _sp.GetRequiredService<OrderService>();
                foreach (var d in allDetails)
                {
                    var remaining = d.RequiredQty;
                    if (remaining <= 0) continue;
                    var frozenInvs = await _db.Inventories
                        .Where(i => i.PartId == d.PartId && i.FrozenQty > 0)
                        .OrderByDescending(i => i.FrozenQty).ToListAsync();
                    foreach (var inv in frozenInvs)
                    {
                        if (remaining <= 0) break;
                        var qty = Math.Min(inv.FrozenQty, remaining);
                        try { await invSvc.DeductCoreAsync(d.PartId, inv.LocationId, qty, operatorId, "OnlineComplete", order.Id); remaining -= qty; }
                        catch (Exception ex) { Console.WriteLine($"[Online] Deduct失败 PartId={d.PartId}: {ex.Message}"); }
                    }
                }

                order.Status = 3;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // 扣减后重新冻结其他活跃订单（先到先得）
                await orderSvc.RefreezeActiveOrdersAsync(operatorId);
            }
            else
            {
                Console.WriteLine($"[Online] 订单 {order.OrderNo} 尚未完成（差 {allDetails.Count - confirmedCount} 个明细）");
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
                status = c.Status, prep_status = prep?.Status ?? 0, confirmed_at = c.ConfirmedAt
            };
        }).ToList();

        return new { total, page, page_size = pageSize, items };
    }
}
