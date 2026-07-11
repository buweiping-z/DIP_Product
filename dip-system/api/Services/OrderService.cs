using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db) { _db = db; }

    /* ===== 库存冻结计算逻辑说明 =====
     *
     * 【核心公式】
     *   TotalQty(总库存) = AvailableQty(可用) + FrozenQty(冻结)
     *   仪表盘"可用库存" = AvailableQty
     *
     * 【订单生命周期与冻结的关系】
     *   创建订单 → 冻结库存 → 备料核实 → 上线消耗
     *   Status: 1=待备料 2=待上线 3=已完成 4=已取消
     *
     * 【PrepDetail 状态】
     *   1=正常(已冻结够)  3=待补货(冻结不足，需上架后补冻)
     *
     * 【冻结/解冻触发时机】
     *   新建订单: Freeze(冻结全部可用)
     *   取消订单: Thaw(全部解冻) → AutoRefill(补给其他待补货订单)
     *   上架入库: AutoRefill(补给待补货订单)
     *   编辑订单: Thaw全部 → SaveChanges → Freeze新数量
     */

    private static object ToDict(ProductionOrder o) => new
    {
        o.Id, order_no = o.OrderNo, line_id = o.LineId,
        product_name = o.ProductName, plan_qty = o.PlanQty,
        priority = o.Priority, status = o.Status,
        created_at = o.CreatedAt
    };

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

    /// <summary>
    /// 【新建订单 → 立即冻结库存】
    /// 1. 遍历 BOM 料号，计算 totalReq = 单台用量 × planQty
    /// 2. 查 Inventories 表 AvailableQty > 0，逐个 FreezeCoreAsync
    /// 3. ActualQty = 实际冻结总量
    /// 4. ActualQty < RequiredQty → Status=3(待补货)
    /// </summary>
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
            PlanQty = planQty, Priority = data.GetInt("priority") ?? 2, Status = 1
        };
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();

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
                    ReferenceDesignator = "", LossRate = 0, IsCritical = 0
                });
            }
        }
        await _db.SaveChangesAsync();

        var prepOrderNo = $"PO-{order.OrderNo}";
        var prep = new PrepOrder { OrderNo = prepOrderNo, ProductionOrderId = order.Id, LineId = order.LineId, Status = 1 };
        _db.PrepOrders.Add(prep);
        await _db.SaveChangesAsync();

        var allBomItems = await _db.BomItems.Where(b => b.OrderId == order.Id).ToListAsync();
        foreach (var b in allBomItems)
        {
            var totalReq = b.RequiredQty * planQty * (1 + b.LossRate / 100);
            _db.PrepDetails.Add(new PrepDetail
            {
                PrepOrderId = prep.Id, PartId = b.PartId, PartNo = b.PartNo,
                RequiredQty = totalReq, ActualQty = 0, LossQty = 0,
                SubstituteFlag = b.SubstitutePartId.HasValue ? 1 : 0,
                SubstitutePartId = b.SubstitutePartId, Status = 1,
                ReferenceDesignator = b.ReferenceDesignator
            });
        }
        await _db.SaveChangesAsync();

        // 统一走 RefreezeActiveOrdersAsync 冻结（先到先得）
        await RefreezeActiveOrdersAsync(operatorId);
        return await GetDetailAsync(order.Id);
    }

    /// <summary>
    /// 【编辑订单 → 更新数据后统一走 RefreezeActiveOrdersAsync 重新冻结】
    /// </summary>
    public async Task<object> UpdateAsync(long orderId, Dictionary<string, object?> data)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        var oldPlanQty = order.PlanQty;
        var newPlanQty = data.ContainsKey("plan_qty") ? data.GetDecimal("plan_qty") : oldPlanQty;
        if (newPlanQty <= 0) newPlanQty = oldPlanQty;

        // plan_qty 变更时更新 RequiredQty
        if (newPlanQty != oldPlanQty)
        {
            var ratio = newPlanQty / oldPlanQty;
            var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId && p.Status != 3).ToListAsync();
            foreach (var prep in preps)
            {
                var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
                foreach (var d in details)
                    d.RequiredQty = Math.Round(d.RequiredQty * ratio, 2); // 按比例更新
                prep.Status = 1;
                prep.KitCheckResult = 0;
            }
            order.Status = 1;
        }

        data.ApplyTo(order, new[] { "product_name", "plan_qty", "priority", "status" });
        await _db.SaveChangesAsync();

        // 统一重新冻结
        await RefreezeActiveOrdersAsync(0);
        return ToDict(order);
    }

    /// <summary>
    /// 【取消订单 → 解冻 + 其他活跃订单从早到晚重新冻结】
    /// 1. 释放当前订单所有冻结库存
    /// 2. 所有活跃订单(Status=1/2)清空冻结，从早到晚重新跑一遍新建冻结流程
    /// </summary>
    public async Task CancelAsync(long orderId, long operatorId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        if (order.Status == 4) throw AppException.Business("订单已取消");
        if (order.Status == 3) throw AppException.Business("订单已完成，无法取消");

        // 1. 释放当前订单全部冻结
        var prepSvc = new PrepService(_db);
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var p in preps)
        {
            if (p.Status != 3)
                await prepSvc.CancelAsync(p.Id, operatorId);
        }

        order.Status = 4;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // 2. 其他活跃订单从早到晚重新冻结
        await RefreezeActiveOrdersAsync(operatorId);
    }

    /// <summary>
    /// 【活跃订单重新冻结】从早到晚遍历所有 Status=1 或 2 的订单，清空冻结后按顺序重冻
    /// 先到先得，后面的订单库存不够就标记 Status=3(待补货)
    /// </summary>
    public async Task RefreezeActiveOrdersAsync(long operatorId)
    {
        // 先全部解冻
        var frozenInvs = await _db.Inventories.Where(i => i.FrozenQty > 0).ToListAsync();
        var invSvc = new InventoryService(_db);
        foreach (var inv in frozenInvs)
        {
            if (inv.FrozenQty <= 0) continue;
            try { await invSvc.ThawCoreAsync(inv.PartId, inv.LocationId, inv.FrozenQty, operatorId, "Refreeze", 0); } catch { }
        }
        await _db.SaveChangesAsync();

        // 活跃订单按创建时间从早到晚排序
        var activeOrders = await _db.ProductionOrders
            .Where(o => o.Status == 1 || o.Status == 2).OrderBy(o => o.CreatedAt).ToListAsync();
        foreach (var order in activeOrders)
        {
            var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == order.Id && p.Status != 3).ToListAsync();
            foreach (var prep in preps)
            {
                var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
                foreach (var d in details)
                {
                    d.ActualQty = 0; // 清零
                    d.Status = 1;
                    // 重新冻结：查 AvailableQty > 0，先到先得
                    var allInvs = await _db.Inventories
                        .Where(i => i.PartId == d.PartId && i.AvailableQty > 0).ToListAsync();
                    var totalFrozen = 0m;
                    foreach (var inv in allInvs)
                    {
                        if (totalFrozen >= d.RequiredQty) break;
                        var qty = Math.Min(inv.AvailableQty, d.RequiredQty - totalFrozen);
                        await invSvc.FreezeCoreAsync(d.PartId, inv.LocationId, qty, operatorId, "Refreeze", order.Id);
                        totalFrozen += qty;
                    }
                    d.ActualQty = totalFrozen;           // 本轮冻到多少
                    if (totalFrozen < d.RequiredQty) d.Status = 3; // 不够 → 待补货
                }
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long orderId, long operatorId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        if (order.Status != 4) await CancelAsync(orderId, operatorId); // 先取消再软删除
        order.IsDeleted = true;
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == order.Id).ToListAsync();
        foreach (var p in preps) p.IsDeleted = true;
        var details = await _db.PrepDetails.Where(d => preps.Select(p => p.Id).Contains(d.PrepOrderId)).ToListAsync();
        foreach (var d in details) d.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ImportBomAsync(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        var rows = new List<(string productName, string partNo, decimal qty)>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var pn = row.Cell(1).GetString().Trim();
            var pno = row.Cell(2).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(pno)) continue;
            if (!row.Cell(3).TryGetValue(out decimal qty) || qty <= 0) continue;
            rows.Add((pn, pno, qty));
        }
        var productNames = rows.Select(r => r.productName).Distinct().ToList();
        var oldBoms = await _db.ProductBoms.Where(b => productNames.Contains(b.ProductName)).ToListAsync();
        if (oldBoms.Any()) { _db.ProductBoms.RemoveRange(oldBoms); await _db.SaveChangesAsync(); }
        int count = 0;
        var allPartNos = rows.Select(r => r.partNo).Distinct().ToList();
        var existingParts = await _db.Parts.Where(p => allPartNos.Contains(p.PartNo)).ToDictionaryAsync(p => p.PartNo);
        var newParts = new List<Part>();
        foreach (var (pn, pno, qty) in rows)
        {
            if (!existingParts.TryGetValue(pno, out var part))
            {
                part = newParts.FirstOrDefault(p => p.PartNo == pno);
                if (part == null)
                {
                    part = new Part { PartNo = pno, PartName = pno, Unit = "PCS", PartType = 1, Status = 1 };
                    _db.Parts.Add(part); newParts.Add(part); existingParts[pno] = part;
                }
            }
            _db.ProductBoms.Add(new ProductBom { ProductName = pn, PartId = part.Id, PartNo = pno, Quantity = qty });
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    public async Task<object> GetProductBomAsync(string productName)
    {
        var boms = await _db.ProductBoms.Where(b => b.ProductName == productName).ToListAsync();
        return boms.Select(b => new
        {
            part_id = b.PartId, part_no = b.PartNo, quantity = b.Quantity,
            stock = _db.Inventories.Where(i => i.PartId == b.PartId).Sum(i => (decimal?)i.AvailableQty) ?? 0m
        }).ToList();
    }

    public async Task<List<string>> GetProductNamesAsync()
        => await _db.ProductBoms.Select(b => b.ProductName).Distinct().ToListAsync();

    public async Task<List<object>> GetBomStatusAsync(long orderId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        var boms = await _db.ProductBoms.Where(b => b.ProductName == order.ProductName).ToListAsync();
        var prepDetails = await _db.PrepDetails
            .Where(d => d.PrepOrder != null && d.PrepOrder.ProductionOrderId == orderId).ToListAsync();
        var detailByPart = prepDetails.GroupBy(d => d.PartId).ToDictionary(g => g.Key, g => g.Sum(x => x.ActualQty));
        return boms.Select(b =>
        {
            var totalReq = b.Quantity * order.PlanQty;           // 总需求量
            var frozen = detailByPart.GetValueOrDefault(b.PartId, 0m); // 已冻结量
            var avail = _db.Inventories.Where(i => i.PartId == b.PartId).Sum(i => (decimal?)i.AvailableQty) ?? 0m; // 可用库存
            var net = frozen + avail - totalReq;                 // 净库存 = 已冻结 + 可用 - 需求
            return (object)new
            {
                part_id = b.PartId, part_no = b.PartNo,
                quantity = b.Quantity, required_qty = totalReq,
                frozen_qty = frozen, available_qty = avail, net,
                remaining = totalReq - frozen
            };
        }).ToList();
    }
}
