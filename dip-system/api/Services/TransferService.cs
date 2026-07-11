using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class TransferService
{
    private readonly AppDbContext _db;

    public TransferService(AppDbContext db) { _db = db; }

    public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
    {
        var orderNo = $"TR{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var order = new TransferOrder { OrderNo = orderNo, Status = 1 };
        _db.TransferOrders.Add(order);
        await _db.SaveChangesAsync();

        if (data.TryGetValue("items", out var itemsObj) && itemsObj is System.Text.Json.JsonElement je)
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(je.GetRawText());
            if (items != null)
            {
                foreach (var item in items)
                {
                    var pid = item.TryGetValue("part_id", out var pe) ? pe.GetInt64() : 0;
                    var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == pid);
                    if (part == null) throw AppException.NotFound($"物料 {pid} 不存在");

                    var slId = item.TryGetValue("source_location_id", out var sle) ? sle.GetInt64() : 0;
                    var tlId = item.TryGetValue("target_location_id", out var tle) ? tle.GetInt64() : 0;
                    var qty = item.TryGetValue("quantity", out var qe) ? qe.GetDecimal() : 0;

                    if (slId == tlId) throw AppException.Business("源库位和目标库位不能相同");

                    var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == part.Id && i.LocationId == slId);
                    if (inv == null || inv.AvailableQty < qty) throw AppException.Business("源库位可用库存不足");

                    _db.TransferOrderItems.Add(new TransferOrderItem
                    {
                        TransferOrderId = order.Id, PartId = part.Id, PartNo = part.PartNo,
                        SourceLocationId = slId, TargetLocationId = tlId, Quantity = qty, Status = 1
                    });
                }
            }
        }
        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task ExecuteAsync(long orderId, long operatorId)
    {
        var order = await _db.TransferOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null || order.Status != 1) throw AppException.Business("调拨单状态不允许执行");

        var invSvc = new InventoryService(_db);
        var items = await _db.TransferOrderItems.Where(i => i.TransferOrderId == orderId).ToListAsync();
        foreach (var item in items)
        {
            await invSvc.TransferOutCoreAsync(item.PartId, item.SourceLocationId, item.Quantity, operatorId, "TransferOut", order.Id);
            await invSvc.AddCoreAsync(item.PartId, item.TargetLocationId, item.Quantity, "", operatorId, "TransferIn", order.Id);
            item.Status = 2;
        }
        order.Status = 2;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(long orderId, long operatorId)
    {
        var order = await _db.TransferOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null || order.Status != 1) throw AppException.Business("调拨单状态不允许取消");
        order.Status = 3;
        await _db.SaveChangesAsync();
    }

    public async Task<object> GetByIdAsync(long orderId)
    {
        var order = await _db.TransferOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"调拨单 {orderId} 不存在");
        return ToDict(order);
    }

    public async Task<object> GetListAsync(int? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.TransferOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(o => ToDict(o)) };
    }

    private object ToDict(TransferOrder order)
    {
        var items = _db.TransferOrderItems.Where(i => i.TransferOrderId == order.Id).ToList();
        return new
        {
            order.Id, order_no = order.OrderNo, status = order.Status, created_at = order.CreatedAt,
            items = items.Select(i => (object)new
            {
                i.Id, part_id = i.PartId, part_no = i.PartNo,
                source_location_id = i.SourceLocationId, target_location_id = i.TargetLocationId,
                quantity = i.Quantity, status = i.Status
            })
        };
    }
}
