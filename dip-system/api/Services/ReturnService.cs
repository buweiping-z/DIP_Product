using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class ReturnService
{
    private readonly AppDbContext _db;

    public ReturnService(AppDbContext db) { _db = db; }

    public async Task<object> ScanReturnAsync(string barcode, long targetLocationId, long operatorId)
    {
        barcode = barcode.Trim();
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == barcode || p.PartNo.Contains(barcode));
        if (part == null) throw AppException.NotFound("未识别到物料，条码: " + barcode);

        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == targetLocationId);
        if (loc == null) throw AppException.NotFound($"库位 {targetLocationId} 不存在");

        var orderNo = $"RT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var order = new ReturnOrder { OrderNo = orderNo, ReturnReason = "扫码退料", Status = 1 };
        _db.ReturnOrders.Add(order);
        await _db.SaveChangesAsync();

        _db.ReturnOrderItems.Add(new ReturnOrderItem
        {
            ReturnOrderId = order.Id, PartId = part.Id, PartNo = part.PartNo,
            BatchNo = "", Quantity = 1, TargetLocationId = targetLocationId
        });

        var invSvc = new InventoryService(_db);
        await invSvc.AddCoreAsync(part.Id, targetLocationId, 1, "", operatorId, "Return", order.Id);
        await _db.SaveChangesAsync();

        return new { id = order.Id, order_no = order.OrderNo, part_no = part.PartNo, location_code = loc.LocationCode, quantity = 1 };
    }

    public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
    {
        var orderNo = $"RT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var order = new ReturnOrder
        {
            OrderNo = orderNo, PrepOrderId = data.GetLong("prep_order_id"),
            ReturnReason = data.GetStr("return_reason") ?? "", Status = 1
        };
        _db.ReturnOrders.Add(order);
        await _db.SaveChangesAsync();

        var invSvc = new InventoryService(_db);
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
                    var tid = item.TryGetValue("target_location_id", out var te) ? te.GetInt64() : 0;
                    var qty = item.TryGetValue("quantity", out var qe) ? qe.GetDecimal() : 0;
                    var bn = item.TryGetValue("batch_no", out var bne) ? bne.GetString() : "";

                    _db.ReturnOrderItems.Add(new ReturnOrderItem
                    {
                        ReturnOrderId = order.Id, PartId = part.Id, PartNo = part.PartNo,
                        BatchNo = bn, Quantity = qty, TargetLocationId = tid
                    });
                    await invSvc.AddCoreAsync(part.Id, tid, qty, bn ?? "", operatorId, "Return", order.Id);
                }
            }
        }
        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task<object> UpdateAsync(long orderId, Dictionary<string, object?> data)
    {
        var order = await _db.ReturnOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"退料单 {orderId} 不存在");

        var invSvc = new InventoryService(_db);
        var oldItems = await _db.ReturnOrderItems.Where(i => i.ReturnOrderId == orderId).ToListAsync();
        foreach (var old in oldItems)
        {
            await invSvc.TransferOutCoreAsync(old.PartId, old.TargetLocationId, old.Quantity, 0, "ReturnEditReverse", orderId);
            _db.ReturnOrderItems.Remove(old);
        }
        await _db.SaveChangesAsync();

        order.ReturnReason = data.GetStr("return_reason") ?? "";
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
                    var tid = item.TryGetValue("target_location_id", out var te) ? te.GetInt64() : 0;
                    var qty = item.TryGetValue("quantity", out var qe) ? qe.GetDecimal() : 0;
                    var bn = item.TryGetValue("batch_no", out var bne) ? bne.GetString() : "";

                    _db.ReturnOrderItems.Add(new ReturnOrderItem
                    {
                        ReturnOrderId = order.Id, PartId = part.Id, PartNo = part.PartNo,
                        BatchNo = bn, Quantity = qty, TargetLocationId = tid
                    });
                    await invSvc.AddCoreAsync(part.Id, tid, qty, bn ?? "", 0, "ReturnEditApply", orderId);
                }
            }
        }
        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task<object> GetByIdAsync(long orderId)
    {
        var order = await _db.ReturnOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"退料单 {orderId} 不存在");
        return ToDict(order);
    }

    public async Task<object> GetListAsync(int? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.ReturnOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(o => ToDict(o)) };
    }

    private object ToDict(ReturnOrder order)
    {
        var items = _db.ReturnOrderItems.Where(i => i.ReturnOrderId == order.Id).ToList();
        return new
        {
            order.Id, order_no = order.OrderNo, prep_order_id = order.PrepOrderId,
            return_reason = order.ReturnReason, status = order.Status, created_at = order.CreatedAt,
            items = items.Select(i => (object)new
            {
                i.Id, part_id = i.PartId, part_no = i.PartNo,
                quantity = i.Quantity, target_location_id = i.TargetLocationId
            })
        };
    }

}
