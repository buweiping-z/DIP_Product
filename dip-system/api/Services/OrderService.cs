using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(int? status = null, long? lineId = null, int page = 1, int pageSize = 20)
    {
        var query = _db.ProductionOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (lineId.HasValue) query = query.Where(o => o.LineId == lineId.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long orderId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        return ToDict(order);
    }

    public async Task<object> GetDetailAsync(long orderId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == order.LineId);
        var bomItems = await _db.BomItems.Where(b => b.OrderId == order.Id).ToListAsync();
        var prepOrders = await _db.PrepOrders.Where(p => p.ProductionOrderId == order.Id).ToListAsync();

        return new
        {
            order.Id, order_no = order.OrderNo, line_id = order.LineId,
            product_name = order.ProductName, plan_qty = order.PlanQty,
            priority = order.Priority, status = order.Status,
            created_at = order.CreatedAt, line_name = line?.LineName ?? "",
            bom_items = bomItems.Select(b => (object)new
            {
                b.Id, part_id = b.PartId, part_no = b.PartNo,
                required_qty = b.RequiredQty, reference_designator = b.ReferenceDesignator,
                substitute_part_id = b.SubstitutePartId, is_critical = b.IsCritical
            }).ToList(),
            prep_orders = prepOrders.Select(p => (object)new
            {
                p.Id, order_no = p.OrderNo, status = p.Status, kit_check_result = p.KitCheckResult
            }).ToList()
        };
    }

    public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
    {
        var lineId = data.GetLong("line_id") ?? 0;
        var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == lineId);
        if (line == null) throw AppException.NotFound("产线不存在");

        var orderNo = $"WO{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var planQty = data.GetDecimal("plan_qty");
        if (planQty == 0) planQty = 1;

        var order = new ProductionOrder
        {
            OrderNo = orderNo, LineId = lineId,
            ProductName = data.GetStr("product_name") ?? "",
            PlanQty = planQty,
            Priority = data.GetInt("priority") ?? 2,
            Status = 1
        };
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();

        // BOM items from ProductBom if product_name matches
        var productName = order.ProductName;
        if (!string.IsNullOrEmpty(productName))
        {
            var boms = await _db.ProductBoms.Where(b => b.ProductName == productName).ToListAsync();
            int seq = 0;
            foreach (var b in boms)
            {
                seq++;
                var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == b.PartId);
                _db.BomItems.Add(new BomItem
                {
                    OrderId = order.Id, PartId = b.PartId,
                    PartNo = part?.PartNo ?? b.PartNo,
                    RequiredQty = b.Quantity, SeqNo = seq,
                    ReferenceDesignator = "", LossRate = 0,
                    IsCritical = 0
                });
            }
        }
        await _db.SaveChangesAsync();

        // Auto-create prep order
        var prepOrderNo = $"PO-{order.OrderNo}";
        var prep = new PrepOrder { OrderNo = prepOrderNo, ProductionOrderId = order.Id, LineId = order.LineId, Status = 1 };
        _db.PrepOrders.Add(prep);
        await _db.SaveChangesAsync();

        var allBomItems = await _db.BomItems.Where(b => b.OrderId == order.Id).ToListAsync();
        foreach (var b in allBomItems)
        {
            _db.PrepDetails.Add(new PrepDetail
            {
                PrepOrderId = prep.Id, PartId = b.PartId, PartNo = b.PartNo,
                RequiredQty = b.RequiredQty * (1 + b.LossRate / 100),
                ActualQty = 0, LossQty = 0,
                SubstituteFlag = b.SubstitutePartId.HasValue ? 1 : 0,
                SubstitutePartId = b.SubstitutePartId, Status = 1,
                ReferenceDesignator = b.ReferenceDesignator
            });
        }
        await _db.SaveChangesAsync();

        return await GetDetailAsync(order.Id);
    }

    public async Task<object> UpdateAsync(long orderId, Dictionary<string, object?> data)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        data.ApplyTo(order, new[] { "product_name", "plan_qty", "priority", "status" });

        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task CancelAsync(long orderId, long operatorId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        if (order.Status == 4) throw AppException.Business("订单已取消");
        if (order.Status == 3) throw AppException.Business("订单已完成，无法取消");

        // 取消所有关联备料单（释放冻结库存）
        var prepSvc = new PrepService(_db);
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var p in preps)
        {
            if (p.Status != 3) // 未被撤销的备料单才需要取消
            {
                try { await prepSvc.CancelAsync(p.Id, operatorId); } catch { }
            }
        }

        order.Status = 4;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long orderId, long operatorId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        // 先取消（释放库存）再软删除
        if (order.Status != 4)
        {
            try { await CancelAsync(orderId, operatorId); } catch { }
        }

        order.IsDeleted = true;
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var p in preps)
        {
            p.IsDeleted = true;
            var details = await _db.PrepDetails.Where(d => d.PrepOrderId == p.Id).ToListAsync();
            foreach (var d in details) d.IsDeleted = true;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<int> ImportBomAsync(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        var existing = await _db.ProductBoms.ToListAsync();
        var lookup = existing.ToDictionary(b => $"{b.ProductName}|{b.PartNo}", b => b);
        int count = 0;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var pn = row.Cell(1).GetString().Trim();
            var pno = row.Cell(2).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(pno)) continue;
            if (!row.Cell(3).TryGetValue(out decimal qty) || qty <= 0) continue;

            var key = $"{pn}|{pno}";
            if (lookup.TryGetValue(key, out var bom))
            {
                bom.Quantity = qty;
                count++;
                continue;
            }
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == pno);
            if (part == null)
            {
                part = new Part { PartNo = pno, PartName = pno, Unit = "PCS", PartType = 1, Status = 1 };
                _db.Parts.Add(part);
                await _db.SaveChangesAsync();
            }
            _db.ProductBoms.Add(new ProductBom { ProductName = pn, PartId = part.Id, PartNo = pno, Quantity = qty });
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    public async Task<List<string>> GetProductNamesAsync()
        => await _db.ProductBoms.Select(b => b.ProductName).Distinct().ToListAsync();

    public async Task<List<object>> GetProductBomAsync(string name)
    {
        var boms = await _db.ProductBoms.Where(b => b.ProductName == name).ToListAsync();
        var result = new List<object>();
        foreach (var b in boms)
        {
            var stock = await _db.Inventories.Where(i => i.PartId == b.PartId).SumAsync(i => i.AvailableQty);
            result.Add(new { part_id = b.PartId, part_no = b.PartNo, quantity = b.Quantity, stock });
        }
        return result;
    }

    private static object ToDict(ProductionOrder o) => new
    {
        o.Id, order_no = o.OrderNo, line_id = o.LineId, product_name = o.ProductName,
        plan_qty = o.PlanQty, priority = o.Priority, status = o.Status, created_at = o.CreatedAt
    };
}
