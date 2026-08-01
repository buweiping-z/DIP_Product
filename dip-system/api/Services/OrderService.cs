using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text.Json;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OrderService
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;
    private readonly ILogger<OrderService> _logger;
    private readonly IMemoryCache _cache;

    public OrderService(AppDbContext db, IServiceProvider sp, ILogger<OrderService> logger, IMemoryCache cache) { _db = db; _sp = sp; _logger = logger; _cache = cache; }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _productCacheKeys = new();

    private void InvalidateProductCache()
    {
        foreach (var key in _productCacheKeys.Keys)
        {
            _cache.Remove(key);
            _productCacheKeys.TryRemove(key, out _);
        }
    }

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
        production_month = o.ProductionMonth,
        created_at = o.CreatedAt
    };

    /// <summary>
    /// 向前查找规则：找到产品在指定月份应使用的 BOM 版本月份。
    /// 精确匹配 → 向前 ≤ 查找 → NULL（通用版本）
    /// 返回 null 表示应使用通用版本
    /// </summary>
    private async Task<string?> GetEffectiveBomMonthAsync(string productName, string? productionMonth)
    {
        if (string.IsNullOrEmpty(productionMonth)) return null;
        var nameLower = productName.ToLower().Trim();

        // 1. 精确匹配当月
        var exactExists = await _db.ProductBoms.AnyAsync(b =>
            b.ProductName.ToLower().Trim() == nameLower && b.ProductionMonth == productionMonth);
        if (exactExists) return productionMonth;

        // 2. 向前查找 ≤ 当前月份的最新非 NULL 月份
        var recentMonth = await _db.ProductBoms
            .Where(b => b.ProductName.ToLower().Trim() == nameLower
                && b.ProductionMonth != null
                && b.ProductionMonth.CompareTo(productionMonth) <= 0)
            .OrderByDescending(b => b.ProductionMonth)
            .Select(b => b.ProductionMonth)
            .FirstOrDefaultAsync();
        if (recentMonth != null) return recentMonth;

        // 3. 没有月份版本 → 返回 null，调用方将查询 NULL（通用版本）
        return null;
    }

    /// <summary>按月份查 BOM：使用向前查找规则</summary>
    private async Task<List<ProductBom>> GetBomWithFallbackAsync(string productName, string? productionMonth)
    {
        var nameLower = productName.ToLower().Trim();
        var effectiveMonth = await GetEffectiveBomMonthAsync(productName, productionMonth);

        // 如果向前查找返回 null 且无通用版本 → 返回空
        // 如果 effectiveMonth 为 null → 查通用版本
        // 如果 effectiveMonth 有值 → 查该月份
        return await _db.ProductBoms
            .Where(b => b.ProductName.ToLower().Trim() == nameLower && b.ProductionMonth == effectiveMonth)
            .ToListAsync();
    }

    public async Task<object> GetListAsync(int? status = null, long? lineId = null,
        string? productName = null, string? productionMonth = null, int page = 1, int pageSize = 20)
    {
        var query = _db.ProductionOrders.AsQueryable();
        // 默认排除已取消(status=4)，除非显式查询已取消
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        else query = query.Where(o => o.Status != 4);
        if (lineId.HasValue) query = query.Where(o => o.LineId == lineId.Value);
        if (!string.IsNullOrEmpty(productName))
        {
            var search = productName.ToLower();
            query = query.Where(o => o.ProductName.ToLower().Contains(search));
        }
        if (!string.IsNullOrEmpty(productionMonth))
        {
            var search = productionMonth.ToLower();
            query = query.Where(o => o.ProductionMonth != null && o.ProductionMonth.ToLower().Contains(search));
        }
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

    public async Task<object> GetByOrderNoAsync(string orderNo)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
        if (order == null) throw AppException.NotFound($"订单 {orderNo} 不存在");
        return new
        {
            order_id = order.Id, order_no = order.OrderNo,
            product_name = order.ProductName,
            production_month = order.ProductionMonth,
            status = order.Status, line_id = order.LineId,
            plan_qty = order.PlanQty
        };
    }

    public async Task<object> GetDetailAsync(long orderId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");
        var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == order.LineId);
        var bomItems = await _db.BomItems.Where(b => b.OrderId == order.Id).ToListAsync();
        var orderProducts = await _db.OrderProducts
            .Where(op => op.OrderId == order.Id)
            .ToListAsync();
        var prepOrders = await _db.PrepOrders.Where(p => p.ProductionOrderId == order.Id).ToListAsync();
        return new
        {
            order.Id, order_no = order.OrderNo, line_id = order.LineId,
            product_name = order.ProductName, plan_qty = order.PlanQty,
            priority = order.Priority, status = order.Status,
            production_month = order.ProductionMonth,
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
            }).ToList(),
            order_products = orderProducts.Select(op => (object)new
            {
                product_id = op.ProductId,
                product_name = op.ProductName,
                plan_qty = op.PlanQty
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
        var productionMonth = data.GetStr("production_month");

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

            var order = await CreateSingleOrder(lineId, priority, productionMonth,
                new List<(long productId, string name, decimal qty)> { (0, productName, planQty) });
            await RefreezeActiveOrdersAsync(operatorId);
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

        // 空 BOM 拦截：逐产品检查（优先月份匹配，fallback 通用版本）
        foreach (var p in products)
        {
            var boms = await GetBomWithFallbackAsync(p.name, productionMonth);
            if (!boms.Any())
                throw AppException.Business($"产品 {p.name} 没有 BOM 数据（月份: {productionMonth ?? "通用"}），请先导入 BOM");
        }

        // 分组：按 BOM 料号集合（part_no 去重排序后拼接为签名）
        var groups = new Dictionary<string, List<(long productId, string name, decimal qty)>>();
        foreach (var p in products)
        {
            var partNos = (await GetBomWithFallbackAsync(p.name, productionMonth))
                .Select(b => b.PartNo)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            var signature = string.Join(",", partNos);  // 料号集合签名
            if (!groups.ContainsKey(signature))
                groups[signature] = new List<(long, string, decimal)>();
            groups[signature].Add(p);
        }

        // 每组生成一个订单
        var createdOrders = new List<object>();
        foreach (var kv in groups)
        {
            var order = await CreateSingleOrder(lineId, priority, productionMonth, kv.Value);
            createdOrders.Add(ToDict(order));
        }

        // 统一冻结（缺料自动标记待补货，FreezeCoreAsync 内部不抛异常）
        await RefreezeActiveOrdersAsync(operatorId);

        return new { orders = createdOrders, total = createdOrders.Count };
    }

    /// <summary>
    /// 创建一个订单（含 BOM 合并 + PrepOrder + order_products 写入）
    /// products 列表中的产品必须属于同一 BOM 分组，调用方保证
    /// </summary>
    private async Task<ProductionOrder> CreateSingleOrder(long lineId, int priority,
        string? productionMonth,
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
            Status = 1,
            ProductionMonth = productionMonth
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

        // 合并 BOM：按 part_no 汇总，跨所有产品（按月份 fallback）
        var allBoms = new List<ProductBom>();
        foreach (var pn in products.Select(p => p.name).Distinct())
        {
            allBoms.AddRange(await GetBomWithFallbackAsync(pn, productionMonth));
        }

        // part_id → (part_no, total_required_qty)
        var merged = new Dictionary<long, (string partNo, decimal totalQty)>();
        foreach (var bom in allBoms)
        {
            var productPlanQty = products.First(p => string.Equals(p.name, bom.ProductName, StringComparison.OrdinalIgnoreCase)).planQty;
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
    /// 【编辑订单 → BOM一致性校验 + 数量简化更新 + 重新冻结】
    /// 1. 如果传了 products 数组 → 校验BOM分组一致 → 更新order_products → 重建BOM/PrepDetails
    /// 2. 如果没传 → 走旧逻辑（兼容）
    /// </summary>
    public async Task<object> UpdateAsync(long orderId, Dictionary<string, object?> data)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        // 判断新旧格式
        var productsRaw = data.TryGetValue("products", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array
            ? je.EnumerateArray().ToList()
            : null;

        if (productsRaw != null)
        {
            if (productsRaw.Count == 0)
                throw AppException.Business("请至少保留一个产品");

            // === 新格式：多产品编辑 ===
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
                throw AppException.Business("请至少保留一个产品");

            // BOM 分组一致性校验：所有产品必须属于同一分组（按月份 fallback）
            var editMonth = order.ProductionMonth;
            string? groupSignature = null;
            foreach (var p in products)
            {
                var boms = await GetBomWithFallbackAsync(p.name, editMonth);
                if (!boms.Any())
                    throw AppException.Business($"产品 {p.name} 没有 BOM 数据（月份: {editMonth ?? "通用"}），请先导入 BOM");
                var partNos = boms.Select(b => b.PartNo).Distinct().OrderBy(x => x).ToList();
                var sig = string.Join(",", partNos);
                if (groupSignature == null)
                    groupSignature = sig;
                else if (groupSignature != sig)
                    throw AppException.Business("编辑后的产品 BOM 不一致，请删除当前订单并重新创建");
            }

            var totalPlanQty = products.Sum(p => p.qty);
            var displayName = string.Join(" / ", products.Select(p => p.name).Distinct());

            order.ProductName = displayName;
            order.PlanQty = totalPlanQty;
            data.ApplyTo(order, new[] { "priority" });

            // 更新 order_products：删旧 + 写新
            var oldProducts = await _db.OrderProducts.Where(op => op.OrderId == orderId).ToListAsync();
            foreach (var op in oldProducts) op.IsDeleted = true;
            foreach (var p in products)
            {
                _db.OrderProducts.Add(new OrderProduct
                {
                    OrderId = order.Id,
                    ProductId = p.productId,
                    ProductName = p.name,
                    PlanQty = p.qty
                });
            }

            // 重建 BOM：先删旧 BomItems
            var oldBomItems = await _db.BomItems.Where(b => b.OrderId == orderId).ToListAsync();
            foreach (var bi in oldBomItems) bi.IsDeleted = true;

            // 合并新 BOM（按月份 fallback）
            var allBoms = new List<ProductBom>();
            foreach (var pn in products.Select(p => p.name).Distinct())
            {
                allBoms.AddRange(await GetBomWithFallbackAsync(pn, editMonth));
            }
            var merged = new Dictionary<long, (string partNo, decimal totalQty)>();
            foreach (var bom in allBoms)
            {
                var productPlanQty = products.First(p => string.Equals(p.name, bom.ProductName, StringComparison.OrdinalIgnoreCase)).qty;
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

            // 更新 PrepDetails：删旧 + 建新
            var preps = await _db.PrepOrders
                .Where(p => p.ProductionOrderId == orderId && p.Status != 3)
                .ToListAsync();
            foreach (var prep in preps)
            {
                var oldDetails = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
                foreach (var d in oldDetails) d.IsDeleted = true;

                // 解冻旧 PrepOrder 的库存
                var prepSvc = _sp.GetRequiredService<PrepService>();
                try { await prepSvc.CancelAsync(prep.Id, 0); } catch { /* 如果之前没有冻结则忽略 */ }

                // 建新的 PrepDetails（简化：直接覆盖 required_qty，不校验已备料量）
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
                prep.Status = 1;
                prep.KitCheckResult = 0;
            }
            order.Status = 1;

            await _db.SaveChangesAsync();
            await RefreezeActiveOrdersAsync(0);

            return new { orders = new[] { ToDict(order) }, total = 1 };
        }

        // === 旧格式：兼容原编辑逻辑 ===
        var oldPlanQty = order.PlanQty;
        var newPlanQty = data.ContainsKey("plan_qty") ? data.GetDecimal("plan_qty") : oldPlanQty;
        if (newPlanQty <= 0) newPlanQty = oldPlanQty;

        if (newPlanQty != oldPlanQty)
        {
            var ratio = newPlanQty / oldPlanQty;
            var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId && p.Status != 3).ToListAsync();
            foreach (var prep in preps)
            {
                var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
                foreach (var d in details)
                    d.RequiredQty = Math.Round(d.RequiredQty * ratio, 2);
                prep.Status = 1;
                prep.KitCheckResult = 0;
            }
            order.Status = 1;
        }

        data.ApplyTo(order, new[] { "product_name", "plan_qty", "priority", "status" });
        await _db.SaveChangesAsync();
        await RefreezeActiveOrdersAsync(0);

        return new { orders = new[] { ToDict(order) }, total = 1 };
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

        // 1. 释放当前订单全部冻结（逐条容错，一条失败不中断整体）
        var prepSvc = _sp.GetRequiredService<PrepService>();
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var p in preps)
        {
            if (p.Status != 3)
            {
                try { await prepSvc.CancelAsync(p.Id, operatorId); }
                catch (Exception ex) { _logger.LogWarning(ex, "取消备料单失败 PrepId={PrepId}", p.Id); }
            }
        }

        order.Status = 4;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // 2. 其他活跃订单从早到晚重新冻结
        await RefreezeActiveOrdersAsync(operatorId);
    }

    /// <summary>
    /// 【已完成订单调整计划数量】
    /// 已完成订单库存已扣减（FrozenQty=0），调整 plan_qty 需直接操作 TotalQty/AvailableQty。
    /// 调整前先解冻全部活跃订单，调整后再重新冻结，保证活跃订单拿到的冻结量基于正确库存。
    /// </summary>
    public async Task<object> UpdatePlanQtyAsync(long orderId, Dictionary<string, object?> data, long operatorId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");
        if (order.Status != 3) throw AppException.Business("只有已完成的订单才能调整计划数量");

        // 解析 products 数组
        var productsRaw = data.TryGetValue("products", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array
            ? je.EnumerateArray().ToList() : null;
        if (productsRaw == null || productsRaw.Count == 0)
            throw AppException.Business("请提供产品计划数量");

        var newPlanQtys = new Dictionary<string, decimal>();
        foreach (var item in productsRaw)
        {
            var name = item.TryGetProperty("product_name", out var np) ? np.GetString() ?? "" : "";
            var qty = item.TryGetProperty("plan_qty", out var qp) && qp.ValueKind == JsonValueKind.Number ? qp.GetDecimal() : 0;
            if (string.IsNullOrEmpty(name) || qty <= 0) continue;
            newPlanQtys[name] = qty;
        }
        if (newPlanQtys.Count == 0) throw AppException.Business("计划数量必须大于0");

        var orderProducts = await _db.OrderProducts.Where(op => op.OrderId == orderId).ToListAsync();
        foreach (var kv in newPlanQtys)
        {
            if (!orderProducts.Any(op => op.ProductName == kv.Key))
                throw AppException.Business($"产品 {kv.Key} 不在当前订单中");
        }

        // 计算每个料号的库存差值（基于 BOM 用量 × plan_qty 变化）
        var partDeltas = new Dictionary<long, decimal>(); // part_id → total_delta (正=需多扣, 负=需退回)
        foreach (var op in orderProducts)
        {
            if (!newPlanQtys.TryGetValue(op.ProductName, out var newQty)) continue;
            var qtyDelta = newQty - op.PlanQty;
            if (qtyDelta == 0) continue;

            var boms = await GetBomWithFallbackAsync(op.ProductName, order.ProductionMonth);
            foreach (var bom in boms)
            {
                var partDelta = bom.Quantity * qtyDelta;
                if (partDelta == 0) continue;
                partDeltas[bom.PartId] = partDeltas.GetValueOrDefault(bom.PartId) + partDelta;
            }
        }

        if (partDeltas.Count == 0)
            throw AppException.Business("计划数量未发生变化");

        // Phase 1: 解冻全部活跃订单的冻结库存
        var invSvc = _sp.GetRequiredService<InventoryService>();
        var frozenInvs = await _db.Inventories.Where(i => i.FrozenQty > 0).ToListAsync();
        foreach (var inv in frozenInvs)
        {
            if (inv.FrozenQty <= 0) continue;
            try { await invSvc.ThawCoreAsync(inv.PartId, inv.LocationId, inv.FrozenQty, operatorId, "PlanQtyAdjust", 0); } catch (Exception ex) { _logger.LogWarning(ex, "解冻失败 PartId={PartId} LocId={LocId}", inv.PartId, inv.LocationId); }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // 清空 Phase 1 实体快照，保证 Phase 2 从 DB 全新加载

        // Phase 2: 按差值调整真实库存
        foreach (var kv in partDeltas)
        {
            var partId = kv.Key;
            var delta = kv.Value;
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
            var partNo = part?.PartNo ?? partId.ToString();

            if (delta > 0)
            {
                // 计划上调 → 需要多扣库存（检查解冻后的真实可用量）
                var allInvs = await _db.Inventories.Where(i => i.PartId == partId).ToListAsync();
                var totalAvail = allInvs.Sum(i => i.AvailableQty);
                if (totalAvail < delta)
                    throw AppException.Business($"料号 {partNo} 库存不足，需要 {delta}，当前可用仅 {totalAvail}");

                var remaining = delta;
                foreach (var inv in allInvs.Where(i => i.AvailableQty > 0))
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(inv.AvailableQty, remaining);
                    inv.TotalQty -= take;
                    inv.AvailableQty -= take;
                    remaining -= take;
                    // 同步库位计数器
                    var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
                    if (loc != null) loc.CurrentQty -= take;
                }
            }
            else
            {
                // 计划下调 → 退回库存
                var returnQty = -delta;
                var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId);
                if (inv != null)
                {
                    inv.TotalQty += returnQty;
                    inv.AvailableQty += returnQty;
                    var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
                    if (loc != null) loc.CurrentQty += returnQty;
                }
            }
        }

        // Phase 3: 更新订单数据
        foreach (var op in orderProducts)
        {
            if (newPlanQtys.TryGetValue(op.ProductName, out var newQty))
                op.PlanQty = newQty;
        }
        order.PlanQty = orderProducts.Sum(op => op.PlanQty);
        order.UpdatedAt = DateTime.UtcNow;

        // 更新 BomItems.RequiredQty 和 PrepDetails.RequiredQty
        var bomItems = await _db.BomItems.Where(b => b.OrderId == orderId).ToListAsync();
        foreach (var bi in bomItems)
        {
            if (partDeltas.TryGetValue(bi.PartId, out var d))
            {
                bi.RequiredQty += d;
                if (bi.RequiredQty < 0) bi.RequiredQty = 0;
            }
        }
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var prep in preps)
        {
            var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
            foreach (var d in details)
            {
                if (partDeltas.TryGetValue(d.PartId, out var pd))
                {
                    d.RequiredQty += pd;
                    if (d.RequiredQty < 0) d.RequiredQty = 0;
                }
            }
        }

        await _db.SaveChangesAsync();

        // Phase 4: 重新冻结活跃订单
        await RefreezeActiveOrdersAsync(operatorId);

        return new
        {
            order_id = orderId,
            plan_qty = order.PlanQty,
            products = orderProducts.Select(op => (object)new { op.ProductName, op.PlanQty }).ToList()
        };
    }

    /// <summary>
    /// 【活跃订单重新冻结】从早到晚遍历所有 Status=1 或 2 的订单，清空冻结后按顺序重冻
    /// 先到先得，后面的订单库存不够就标记 Status=3(待补货)
    /// </summary>
    public async Task RefreezeActiveOrdersAsync(long operatorId)
    {
        // 先全部解冻
        var frozenInvs = await _db.Inventories.Where(i => i.FrozenQty > 0).ToListAsync();
        var invSvc = _sp.GetRequiredService<InventoryService>();
        foreach (var inv in frozenInvs)
        {
            if (inv.FrozenQty <= 0) continue;
            try { await invSvc.ThawCoreAsync(inv.PartId, inv.LocationId, inv.FrozenQty, operatorId, "Refreeze", 0); } catch (Exception ex) { _logger.LogWarning(ex, "Refreeze解冻失败 PartId={PartId}", inv.PartId); }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // 清空 Phase 1 实体快照，保证 Phase 2 从 DB 全新加载

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
                    if (d.Status == 2) continue; // 已扫描确认的明细不重置
                    d.ActualQty = 0; // 清零
                    d.Status = 1;
                    // 重新冻结：查 AvailableQty > 0，先到先得（ChangeTracker 已清空，保证读到 DB 实值）
                    var allInvs = await _db.Inventories
                        .Where(i => i.PartId == d.PartId && i.AvailableQty > 0).ToListAsync();
                    var totalFrozen = 0m;
                    foreach (var inv in allInvs)
                    {
                        if (totalFrozen >= d.RequiredQty) break;
                        var qty = Math.Min(inv.AvailableQty, d.RequiredQty - totalFrozen);
                        try { await invSvc.FreezeCoreAsync(d.PartId, inv.LocationId, qty, operatorId, "Refreeze", order.Id); totalFrozen += qty; } catch (Exception ex) { _logger.LogWarning(ex, "Refreeze冻结失败 PartId={PartId}", d.PartId); }
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

        if (order.Status != 4)
        {
            try { await CancelAsync(orderId, operatorId); }
            catch { /* 关联数据已不存在时仍允许删除 */ }
        }

        order.IsDeleted = true;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = operatorId;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ImportBomAsync(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        var rows = new List<(string productName, string? productionMonth, string partNo, decimal qty)>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var pn = row.Cell(1).GetString().Trim();
            var month = row.Cell(2).GetString().Trim();
            var pno = row.Cell(3).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(pno)) continue;
            if (!row.Cell(4).TryGetValue(out decimal qty) || qty <= 0) continue;
            // 月份空字符串视为 NULL（通用版本）
            var pm = string.IsNullOrEmpty(month) ? null : month;
            rows.Add((pn, pm, pno, qty));
        }
        // 按 (产品名, 月份) 分组，每组全量替换
        var groups = rows.GroupBy(r => (r.productName, r.productionMonth)).ToList();
        foreach (var g in groups)
        {
            var oldBoms = await _db.ProductBoms
                .Where(b => b.ProductName == g.Key.productName && b.ProductionMonth == g.Key.productionMonth)
                .ToListAsync();
            if (oldBoms.Any()) { _db.ProductBoms.RemoveRange(oldBoms); await _db.SaveChangesAsync(); }
        }
        // 同批次内去重：同一(产品,月份,料号)只保留最后一条
        rows = rows.GroupBy(r => (r.productName, r.productionMonth, r.partNo))
            .Select(g => g.Last()).ToList();
        int count = 0;
        var allPartNos = rows.Select(r => r.partNo).Distinct().ToList();
        var existingParts = await _db.Parts.Where(p => allPartNos.Contains(p.PartNo)).ToDictionaryAsync(p => p.PartNo);
        var newParts = new List<Part>();
        foreach (var (pn, pm, pno, qty) in rows)
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
            _db.ProductBoms.Add(new ProductBom { ProductName = pn, ProductionMonth = pm, PartId = part.Id, PartNo = pno, Quantity = qty });
            count++;
        }
        await _db.SaveChangesAsync();
        InvalidateProductCache();
        await RebuildProductMonthIndexAsync();  // BOM 导入后自动维护索引表
        return count;
    }

    public async Task<object> GetProductBomAsync(string productName, string? productionMonth = null)
    {
        var boms = await GetBomWithFallbackAsync(productName, productionMonth);
        var partIds = boms.Select(b => b.PartId).Distinct().ToList();
        var stockDict = await _db.Inventories
            .Where(i => partIds.Contains(i.PartId))
            .GroupBy(i => i.PartId)
            .Select(g => new { PartId = g.Key, Stock = g.Sum(i => i.AvailableQty) })
            .ToDictionaryAsync(x => x.PartId, x => x.Stock);

        return boms.Select(b => new
        {
            part_id = b.PartId, part_no = b.PartNo, quantity = b.Quantity,
            stock = stockDict.GetValueOrDefault(b.PartId, 0m)
        }).ToList();
    }

    public async Task<List<object>> GetProductNamesAsync(string? productionMonth = null)
    {
        var cacheKey = $"products_month_{productionMonth ?? "all"}";
        if (_cache.TryGetValue(cacheKey, out List<object>? cached) && cached != null)
            return cached;

        // 加载全部 BOM，在内存中按向前查找规则筛选
        var allBoms = await _db.ProductBoms.ToListAsync();
        var result = new List<object>();

        foreach (var name in allBoms.Select(b => b.ProductName).Distinct())
        {
            var nameLower = name.ToLower().Trim();
            var productBoms = allBoms.Where(b => b.ProductName.ToLower().Trim() == nameLower).ToList();

            // 向前查找规则确定有效月份
            string? effectiveMonth = null;
            if (!string.IsNullOrEmpty(productionMonth))
            {
                // 1. 精确匹配当月
                if (productBoms.Any(b => b.ProductionMonth == productionMonth))
                    effectiveMonth = productionMonth;
                else
                    // 2. 向前查找 ≤ 当前月份的最新 BOM
                    effectiveMonth = productBoms
                        .Where(b => b.ProductionMonth != null
                            && string.Compare(b.ProductionMonth!, productionMonth) <= 0)
                        .Select(b => b.ProductionMonth)
                        .Distinct()
                        .OrderByDescending(m => m)
                        .FirstOrDefault();
                // 3. effectiveMonth 为 null → 使用通用版本(NULL)，下面 Where 自动匹配
            }

            var effectiveBoms = productBoms.Where(b => b.ProductionMonth == effectiveMonth).ToList();
            if (!effectiveBoms.Any()) continue;

            var partNos = effectiveBoms.Select(b => b.PartNo).Distinct().OrderBy(x => x).ToList();
            result.Add((object)new
            {
                product_name = name,
                product_id = 0L,
                bom_count = partNos.Count,
                bom_signature = string.Join(",", partNos),
                bom_month = effectiveMonth ?? "通用"
            });
        }

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        _productCacheKeys.TryAdd(cacheKey, 0);
        return result;
    }

    /// <summary>
    /// 从 product_boms 全量重建机种-生连索引表
    /// </summary>
    public async Task RebuildProductMonthIndexAsync()
    {
        // 从 product_boms 加载必要字段，在内存中分组统计（避免 EF Core MySQL GroupBy 翻译问题）
        var allBoms = await _db.ProductBoms
            .Select(b => new { b.ProductName, b.ProductionMonth, b.PartNo })
            .ToListAsync();

        var grouped = allBoms
            .GroupBy(b => (Name: b.ProductName.ToUpper().Trim(), b.ProductionMonth))
            .Select(g => new { Name = g.Key.Name, g.Key.ProductionMonth, BomCount = g.Select(x => x.PartNo).Distinct().Count() })
            .ToList();

        // 全量替换：清空旧索引，写入新数据
        var oldRows = await _db.ProductMonthIndices.IgnoreQueryFilters().ToListAsync();
        if (oldRows.Any()) _db.ProductMonthIndices.RemoveRange(oldRows);

        foreach (var g in grouped)
        {
            _db.ProductMonthIndices.Add(new ProductMonthIndex
            {
                ProductName = g.Name,
                ProductionMonth = g.ProductionMonth,
                BomCount = g.BomCount
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 从索引表快速查询指定月份可用的产品列表（向前查找规则）
    /// </summary>
    public async Task<List<object>> GetProductIndexAsync(string? productionMonth)
    {
        var allIndex = await _db.ProductMonthIndices.ToListAsync();
        var result = new List<object>();

        foreach (var name in allIndex.Select(i => i.ProductName).Distinct())
        {
            var entries = allIndex.Where(i => i.ProductName == name).ToList();

            // 向前查找规则确定有效月份
            string? effectiveMonth = null;
            if (!string.IsNullOrEmpty(productionMonth))
            {
                if (entries.Any(i => i.ProductionMonth == productionMonth))
                    effectiveMonth = productionMonth;
                else
                    effectiveMonth = entries
                        .Where(i => i.ProductionMonth != null && string.Compare(i.ProductionMonth!, productionMonth) <= 0)
                        .Select(i => i.ProductionMonth)
                        .Distinct()
                        .OrderByDescending(m => m)
                        .FirstOrDefault();
            }

            var match = entries.FirstOrDefault(i => i.ProductionMonth == effectiveMonth);
            if (match == null || match.BomCount == 0) continue;

            result.Add(new
            {
                product_name = name,
                product_id = 0L,
                bom_count = match.BomCount,
                bom_month = effectiveMonth ?? "通用"
            });
        }
        return result;
    }

    public async Task<List<object>> GetBomStatusAsync(long orderId)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

        // 从 order_products 获取实际产品名 → 兼容单产品和多产品订单
        var orderProducts = await _db.OrderProducts
            .Where(op => op.OrderId == orderId).ToListAsync();
        var productNames = orderProducts.Any()
            ? orderProducts.Select(op => op.ProductName).Distinct().ToList()
            : new List<string> { order.ProductName };  // fallback: 旧订单没有 order_products

        // 按产品名+月份加载 BOM，按 part_no 合并（fallback 通用版本）
        var allBoms = new List<ProductBom>();
        foreach (var pn in productNames)
        {
            allBoms.AddRange(await GetBomWithFallbackAsync(pn, order.ProductionMonth));
        }
        var productPlanMap = orderProducts.Any()
            ? orderProducts.ToDictionary(op => op.ProductName, op => op.PlanQty)
            : new Dictionary<string, decimal> { [order.ProductName] = order.PlanQty };

        // 合并 BOM: part_id → total_required_qty
        var merged = new Dictionary<long, decimal>();
        foreach (var bom in allBoms)
        {
            var planQty = productPlanMap.TryGetValue(bom.ProductName, out var q) ? q : order.PlanQty;
            var req = bom.Quantity * planQty;
            merged[bom.PartId] = merged.GetValueOrDefault(bom.PartId) + req;
        }

        // 关联 part_no
        var partIds = merged.Keys.ToList();
        var partsMap = (await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync())
            .ToDictionary(p => p.Id, p => p.PartNo);

        // 已冻结量
        var prepDetails = await _db.PrepDetails
            .Where(d => d.PrepOrder != null && d.PrepOrder.ProductionOrderId == orderId).ToListAsync();
        var detailByPart = prepDetails.GroupBy(d => d.PartId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ActualQty));

        return merged.Select(kv =>
        {
            var partId = kv.Key;
            var totalReq = kv.Value;
            var frozen = detailByPart.GetValueOrDefault(partId, 0m);
            var avail = _db.Inventories.Where(i => i.PartId == partId).Sum(i => (decimal?)i.AvailableQty) ?? 0m;
            var net = frozen + avail - totalReq;
            return (object)new
            {
                part_id = partId,
                part_no = partsMap.TryGetValue(partId, out var pno) ? pno : "",
                quantity = 1,  // 合并后单台用量无意义，保留占位
                required_qty = totalReq,
                frozen_qty = frozen,
                available_qty = avail,
                net,
                remaining = totalReq - frozen
            };
        }).ToList();
    }

    // ===== 导出产品 BOM =====

    public async Task<byte[]> ExportProductBomAsync()
    {
        var boms = await _db.ProductBoms
            .OrderBy(b => b.ProductionMonth).ThenBy(b => b.ProductName).ThenBy(b => b.PartNo)
            .ToListAsync();

        // 去重：同一(产品,月份,料号)只保留第一条，删除多余重复记录
        var seen = new HashSet<(string, string?, string)>();
        var duplicates = new List<ProductBom>();
        var uniqueBoms = new List<ProductBom>();
        foreach (var b in boms)
        {
            var key = (b.ProductName, b.ProductionMonth, b.PartNo);
            if (seen.Add(key)) uniqueBoms.Add(b);
            else duplicates.Add(b);
        }
        if (duplicates.Any())
        {
            _db.ProductBoms.RemoveRange(duplicates);
            await _db.SaveChangesAsync();
            InvalidateProductCache();
        }

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("产品BOM");
        ws.Cell(1, 1).Value = "产品名称";
        ws.Cell(1, 2).Value = "生连";
        ws.Cell(1, 3).Value = "料号";
        ws.Cell(1, 4).Value = "用量";
        int row = 2;
        foreach (var b in uniqueBoms)
        {
            ws.Cell(row, 1).Value = b.ProductName;
            ws.Cell(row, 2).Value = b.ProductionMonth ?? "通用";
            ws.Cell(row, 3).Value = b.PartNo;
            ws.Cell(row, 4).Value = (double)b.Quantity;
            row++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
