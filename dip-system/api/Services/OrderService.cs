using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text.Json;
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
    /// 【新建订单 → 多产品分组 + BOM合并 + 立即冻结】
    /// 1. 如果传了 products 数组 → 按 BOM 料号集合分组，每组生成一个订单
    /// 2. 如果没传 → 走旧单产品逻辑（兼容）
    /// 3. 返回 { orders: [...], total }
    /// </summary>
    public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
    {
        var lineId = data.GetLong("line_id") ?? 0;
        var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == lineId);
        if (line == null) throw AppException.NotFound("产线不存在");

        var priority = data.GetInt("priority") ?? 2;

        // 判断新旧格式：是否有 products 数组
        var productsRaw = data.TryGetValue("products", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array
            ? je.EnumerateArray().ToList()
            : null;

        if (productsRaw == null || productsRaw.Count == 0)
        {
            // === 旧格式：单产品（兼容） ===
            var productName = data.GetStr("product_name") ?? "";
            var planQty = data.GetDecimal("plan_qty");
            if (planQty == 0) planQty = 1;
            if (string.IsNullOrEmpty(productName))
                throw AppException.Business("产品名称不能为空");

            var order = await CreateSingleOrder(lineId, priority,
                new List<(long productId, string name, decimal qty)> { (0, productName, planQty) });
            return new { orders = new[] { ToDict(order) }, total = 1 };
        }

        // === 新格式：多产品 → 分组 → 批量创建 ===
        var products = new List<(long productId, string name, decimal qty)>();
        foreach (var item in productsRaw)
        {
            var name = item.TryGetProperty("product_name", out var np) ? np.GetString() ?? "" : "";
            var qty = item.TryGetProperty("plan_qty", out var qp) && qp.ValueKind == JsonValueKind.Number ? qp.GetDecimal() : 1m;
            var pid = item.TryGetProperty("product_id", out var ip) && ip.ValueKind == JsonValueKind.Number ? ip.GetInt64() : 0L;
            if (string.IsNullOrEmpty(name)) continue;
            products.Add((pid, name, qty));
        }

        if (products.Count == 0)
            throw AppException.Business("请至少选择一个产品");

        // 空 BOM 拦截：逐产品检查
        foreach (var p in products)
        {
            var hasBom = await _db.ProductBoms.AnyAsync(b => b.ProductName == p.name);
            if (!hasBom)
                throw AppException.Business($"产品 {p.name} 没有 BOM 数据，请先导入 BOM");
        }

        // 分组：按 BOM 料号集合（part_no 去重排序后拼接为签名）
        var groups = new Dictionary<string, List<(long productId, string name, decimal qty)>>();
        foreach (var p in products)
        {
            var partNos = await _db.ProductBoms
                .Where(b => b.ProductName == p.name)
                .Select(b => b.PartNo)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
            var signature = string.Join(",", partNos);  // 料号集合签名
            if (!groups.ContainsKey(signature))
                groups[signature] = new List<(long, string, decimal)>();
            groups[signature].Add(p);
        }

        // 每组生成一个订单
        var createdOrders = new List<object>();
        foreach (var kv in groups)
        {
            var order = await CreateSingleOrder(lineId, priority, kv.Value);
            createdOrders.Add(ToDict(order));
        }

        // 统一冻结
        await RefreezeActiveOrdersAsync(operatorId);

        return new { orders = createdOrders, total = createdOrders.Count };
    }

    /// <summary>
    /// 创建一个订单（含 BOM 合并 + PrepOrder + order_products 写入）
    /// products 列表中的产品必须属于同一 BOM 分组，调用方保证
    /// </summary>
    private async Task<ProductionOrder> CreateSingleOrder(long lineId, int priority,
        List<(long productId, string name, decimal planQty)> products)
    {
        var orderNo = $"WO{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var totalPlanQty = products.Sum(p => p.planQty);
        var productNames = products.Select(p => p.name).Distinct().ToList();
        var displayName = string.Join(" / ", productNames);

        var order = new ProductionOrder
        {
            OrderNo = orderNo,
            LineId = lineId,
            ProductName = displayName,
            PlanQty = totalPlanQty,
            Priority = priority,
            Status = 1
        };
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();

        // 写入 order_products
        foreach (var p in products)
        {
            _db.OrderProducts.Add(new OrderProduct
            {
                OrderId = order.Id,
                ProductId = p.productId,
                ProductName = p.name,
                PlanQty = p.planQty
            });
        }

        // 合并 BOM：按 part_no 汇总，跨所有产品
        var allProductNames = products.Select(p => p.name).Distinct().ToList();
        var allBoms = await _db.ProductBoms
            .Where(b => allProductNames.Contains(b.ProductName))
            .ToListAsync();

        // part_id → (part_no, total_required_qty)
        var merged = new Dictionary<long, (string partNo, decimal totalQty)>();
        foreach (var bom in allBoms)
        {
            var productPlanQty = products.First(p => p.name == bom.ProductName).planQty;
            var qty = bom.Quantity * productPlanQty;
            if (merged.ContainsKey(bom.PartId))
                merged[bom.PartId] = (bom.PartNo, merged[bom.PartId].totalQty + qty);
            else
                merged[bom.PartId] = (bom.PartNo, qty);
        }

        int seq = 0;
        foreach (var kv in merged)
        {
            seq++;
            _db.BomItems.Add(new BomItem
            {
                OrderId = order.Id,
                PartId = kv.Key,
                PartNo = kv.Value.partNo,
                RequiredQty = kv.Value.totalQty,
                SeqNo = seq,
                ReferenceDesignator = "",
                LossRate = 0,
                IsCritical = 0
            });
        }
        await _db.SaveChangesAsync();

        // 创建 PrepOrder + PrepDetails
        var prepOrderNo = $"PO-{order.OrderNo}";
        var prep = new PrepOrder
        {
            OrderNo = prepOrderNo,
            ProductionOrderId = order.Id,
            LineId = order.LineId,
            Status = 1
        };
        _db.PrepOrders.Add(prep);
        await _db.SaveChangesAsync();

        foreach (var kv in merged)
        {
            _db.PrepDetails.Add(new PrepDetail
            {
                PrepOrderId = prep.Id,
                PartId = kv.Key,
                PartNo = kv.Value.partNo,
                RequiredQty = kv.Value.totalQty,
                ActualQty = 0,
                LossQty = 0,
                SubstituteFlag = 0,
                Status = 1,
                ReferenceDesignator = ""
            });
        }
        await _db.SaveChangesAsync();

        return order;
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
            var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == order.Id && p.Status == 1).ToListAsync();
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

    public async Task<List<object>> GetProductNamesAsync()
    {
        var boms = await _db.ProductBoms.ToListAsync();
        return boms
            .GroupBy(b => new { b.ProductName, b.PartId })
            .GroupBy(g => g.Key.ProductName)
            .Select(g => (object)new
            {
                product_name = g.Key,
                product_id = g.First().First().PartId,
                bom_count = g.Count()
            })
            .ToList();
    }

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
