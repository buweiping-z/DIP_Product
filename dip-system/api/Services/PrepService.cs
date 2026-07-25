using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class KitCheckResult
{
    public long PrepDetailId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = "";
    public decimal RequiredQty { get; set; }
    public decimal AvailableQty { get; set; }
    public int Status { get; set; }
}

public class PrepService
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;

    public PrepService(AppDbContext db, IServiceProvider sp) { _db = db; _sp = sp; }

    public async Task<object> GetListAsync(int? status = null, long? lineId = null, int page = 1, int pageSize = 20)
    {
        var query = _db.PrepOrders.AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (lineId.HasValue) query = query.Where(p => p.LineId == lineId.Value);
        var total = await query.CountAsync();

        // Load product names via order_products (multi-product orders)
        var prepOrders = await query.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(p => p.Details).ToListAsync();
        var orderIds = prepOrders.Select(p => p.ProductionOrderId).Distinct().ToList();
        var productNames = await _db.OrderProducts
            .Where(op => orderIds.Contains(op.OrderId))
            .GroupBy(op => op.OrderId)
            .Select(g => new { OrderId = g.Key, Names = string.Join(" / ", g.Select(op => op.ProductName)) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Names);
        // Fallback: old single-product orders
        var fallbackNames = await _db.ProductionOrders
            .Where(o => orderIds.Contains(o.Id) && o.ProductName != null)
            .ToDictionaryAsync(o => o.Id, o => o.ProductName!);

        return new { total, page, page_size = pageSize, items = prepOrders.Select(p => ToDict(p, productNames, fallbackNames)) };
    }

    public async Task<object> GetByIdAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
        return new
        {
            prep.Id, order_no = prep.OrderNo, production_order_id = prep.ProductionOrderId,
            line_id = prep.LineId, status = prep.Status, kit_check_result = prep.KitCheckResult,
            completed_at = prep.CompletedAt, created_at = prep.CreatedAt,
            details = details.Select(DetailToDict)
        };
    }

    public async Task<object> GetDetailAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == prep.ProductionOrderId);
        var planQty = order?.PlanQty ?? 1;
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();

        // 查询所有明细的上线消耗量
        var detailIds = details.Select(d => d.Id).ToList();
        var onlineConsumed = await _db.OnlineConfirms
            .Where(c => detailIds.Contains(c.PrepDetailId))
            .GroupBy(c => c.PrepDetailId)
            .Select(g => new { DetailId = g.Key, Consumed = g.Sum(c => c.LoadedQty) })
            .ToListAsync();

        var detailList = new List<object>();
        foreach (var d in details)
        {
            var consumed = onlineConsumed.FirstOrDefault(oc => oc.DetailId == d.Id)?.Consumed ?? 0;
            var item = new Dictionary<string, object?>
            {
                ["id"] = d.Id, ["part_id"] = d.PartId, ["part_no"] = d.PartNo,
                ["required_qty"] = d.RequiredQty, ["actual_qty"] = d.ActualQty,
                ["status"] = d.Status, ["substitute_flag"] = d.SubstituteFlag,
                ["total_required_qty"] = d.RequiredQty, ["online_consumed_qty"] = consumed
            };

            var inventories = await _db.Inventories.Where(i => i.PartId == d.PartId).ToListAsync();
            var stocks = new List<object>();
            foreach (var inv in inventories)
            {
                var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
                stocks.Add(new { location_code = loc?.LocationCode ?? "", location_id = inv.LocationId, available_qty = inv.AvailableQty });
            }
            item["stocks"] = stocks;
            detailList.Add(item);
        }

        // 按库位排序，同一库位的物料排在一起方便取料
        detailList = detailList.OrderBy(d =>
        {
            var stocks = (d as Dictionary<string, object?>)!["stocks"] as List<object>;
            return (stocks?.FirstOrDefault() as dynamic)?.location_code ?? "";
        }).ToList();

        return new
        {
            prep.Id, order_no = prep.OrderNo, production_order_id = prep.ProductionOrderId,
            line_id = prep.LineId, status = prep.Status, kit_check_result = prep.KitCheckResult,
            product_name = order?.ProductName ?? "", plan_qty = planQty,
            completed_at = prep.CompletedAt, created_at = prep.CreatedAt, details = detailList
        };
    }

    public async Task<object> KitCheckAsync(long prepId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");

        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
        var invSvc = _sp.GetRequiredService<InventoryService>();
        var results = new List<object>();

        foreach (var d in details)
        {
            var totalAvail = await _db.Inventories
                .Where(i => i.PartId == d.PartId)
                .SumAsync(i => i.AvailableQty);
            int st = totalAvail >= d.RequiredQty ? 1 : 2;
            if (st != 1)
            {
                var subs = await _db.PartSubstitutes
                    .Where(s => s.OriginalPartId == d.PartId && s.Status == 1)
                    .OrderByDescending(s => s.ValidTo).ToListAsync();
                foreach (var sub in subs)
                {
                    var subTotalAvail = await _db.Inventories
                        .Where(i => i.PartId == sub.SubstitutePartId)
                        .SumAsync(i => i.AvailableQty);
                    if (subTotalAvail >= d.RequiredQty) { st = 3; break; }
                }
            }
            results.Add(new KitCheckResult
            {
                PrepDetailId = d.Id, PartId = d.PartId, PartNo = d.PartNo,
                RequiredQty = d.RequiredQty, AvailableQty = totalAvail, Status = st
            });
        }

        var typedResults = results.Cast<KitCheckResult>().ToList();
        int overall = typedResults.All(r => r.Status == 1 || r.Status == 3) ? 1
            : typedResults.All(r => r.Status >= 2) ? 3 : 2;
        prep.KitCheckResult = overall;
        prep.KitCheckTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { prep_order_id = prepId, overall_status = overall,
            items = typedResults.Select(r => new { prep_detail_id = r.PrepDetailId, part_id = r.PartId,
                part_no = r.PartNo, required_qty = r.RequiredQty, available_qty = r.AvailableQty, status = r.Status }) };
    }

    public async Task<object> ScanPrepAsync(long prepId, string barcode, long? detailId, long operatorId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null || prep.Status != 1) throw AppException.Business("备料单状态不允许操作");

        barcode = barcode.Trim();
        PrepDetail? detail = null;
        if (detailId.HasValue)
            detail = await _db.PrepDetails.FirstOrDefaultAsync(d => d.Id == detailId.Value && d.PrepOrderId == prepId);
        if (detail == null)
        {
            // 大小写不敏感 + 空格容错：条码包含料号即匹配
            var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prepId).ToListAsync();
            detail = details.FirstOrDefault(d =>
                string.Equals(d.PartNo.Trim(), barcode, StringComparison.OrdinalIgnoreCase) ||
                barcode.Contains(d.PartNo.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (detail == null) return new { matched = false, message = "未匹配到备料明细" };

        if (detail.Status == 3) return new { matched = false, message = "该物料库存不足，请先上架补货" };

        // 扫描阶段只核对，手机端自行计数
        await _db.SaveChangesAsync();

        return new { matched = true, prep_detail_id = detail.Id, part_no = detail.PartNo };
    }

    /// <summary>手动退出时跑数据处理：手机端传来已扫明细ID列表</summary>
    public async Task FinishPrepAsync(long prepId, List<long> detailIds, long operatorId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.Business("备料单不存在");

        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prepId).ToListAsync();
        // 已扫过的标记为完成
        foreach (var d in details)
        {
            if (detailIds.Contains(d.Id) && d.Status == 1) d.Status = 2;
        }
        // 全部完成 → 备料单完成 + 订单状态 → 待上线
        if (details.All(d => d.Status == 2))
        {
            prep.Status = 2;
            prep.KitCheckResult = 1;
            prep.CompletedAt = DateTime.UtcNow;

            var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == prep.ProductionOrderId);
            if (order != null && order.Status == 1)
            {
                order.Status = 2;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task<object> GetRefillsAsync(string? partNo = null, string? locationCode = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 50)
    {
        var query = _db.PrepScanRecords.AsQueryable();

        if (!string.IsNullOrEmpty(partNo))
        {
            var detailIdsByPart = await _db.PrepDetails
                .Where(d => d.PartNo.Contains(partNo))
                .Select(d => d.Id)
                .ToListAsync();
            query = query.Where(s => detailIdsByPart.Contains(s.PrepDetailId));
        }

        if (!string.IsNullOrEmpty(locationCode))
        {
            var filterLocIds = await _db.WarehouseLocations
                .Where(l => l.LocationCode.Contains(locationCode))
                .Select(l => l.Id)
                .ToListAsync();
            query = query.Where(s => filterLocIds.Contains(s.SourceLocationId));
        }

        if (startDate.HasValue)
            query = query.Where(s => s.CreatedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(s => s.CreatedAt < endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var scans = await query.OrderByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var detailIds = scans.Select(s => s.PrepDetailId).Distinct().ToList();
        var details = await _db.PrepDetails.Where(d => detailIds.Contains(d.Id)).ToListAsync();
        var detailMap = details.ToDictionary(d => d.Id);
        var prepIds = details.Select(d => d.PrepOrderId).Distinct().ToList();
        var preps = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToListAsync();
        var prepMap = preps.ToDictionary(p => p.Id);

        // 批量取库位编码和操作者姓名
        var locIds = scans.Select(s => s.SourceLocationId).Where(id => id > 0).Distinct().ToList();
        var userIds = scans.Select(s => s.OperatorId).Distinct().ToList();
        var locsMap = (await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToListAsync())
            .ToDictionary(l => l.Id);
        var usersMap = (await _db.Operators.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        return new
        {
            total, page, page_size = pageSize,
            items = scans.Select(s =>
            {
                detailMap.TryGetValue(s.PrepDetailId, out var detail);
                var prep = detail != null && prepMap.TryGetValue(detail.PrepOrderId, out var p) ? p : null;
                return (object)new
                {
                    s.Id, prep_order_id = detail?.PrepOrderId ?? 0,
                    prep_order_no = prep?.OrderNo ?? "",
                    prep_detail_id = s.PrepDetailId, part_no = detail?.PartNo ?? "",
                    source_location_id = s.SourceLocationId,
                    source_location_code = locsMap.TryGetValue(s.SourceLocationId, out var l) ? l.LocationCode : "",
                    quantity = s.Quantity,
                    operator_id = s.OperatorId,
                    operator_name = usersMap.TryGetValue(s.OperatorId, out var u) ? u.RealName : "",
                    created_at = s.CreatedAt
                };
            })
        };
    }

    /// <summary>
    /// 【取消备料单 → 全部解冻】
    /// 遍历该备料单所有 PrepDetail，将该料号的全部 FrozenQty 释放回 AvailableQty
    /// </summary>
    public async Task CancelAsync(long prepId, long operatorId)
    {
        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == prepId);
        if (prep == null) throw AppException.NotFound($"备料单 {prepId} 不存在");
        if (prep.Status != 1 && prep.Status != 2) throw AppException.Business("备料单状态不允许撤销");

        var invSvc = _sp.GetRequiredService<InventoryService>();
        var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prepId).ToListAsync();
        foreach (var d in details)
        {
            // 旧流程兼容：PrepScanRecords 有记录就从记录解冻
            var scans = await _db.PrepScanRecords.Where(s => s.PrepDetailId == d.Id).ToListAsync();
            if (scans.Any())
            {
                foreach (var scan in scans)
                    await invSvc.ThawCoreAsync(d.PartId, scan.SourceLocationId, scan.Quantity, operatorId, "PrepThaw", prepId);
            }
            else
            {
                // 新流程：直接全部释放该料号的 FrozenQty（不考虑 ActualQty，因为迁移 SQL 可能已清零）
                var frozenInvs = await _db.Inventories
                    .Where(i => i.PartId == d.PartId && i.FrozenQty > 0).ToListAsync();
                foreach (var inv in frozenInvs)
                    await invSvc.ThawCoreAsync(d.PartId, inv.LocationId, inv.FrozenQty, operatorId, "PrepThaw", prepId);
            }
        }
        prep.Status = 3; // 3=已撤销
        await _db.SaveChangesAsync();
    }

    public async Task<List<object>> GetPendingItemsAsync()
    {
        var details = await _db.PrepDetails
            .Include(d => d.PrepOrder)
            .Where(d => d.PrepOrder!.Status == 1 && d.Status == 1 && d.ActualQty < d.RequiredQty)
            .OrderBy(d => d.Id).ToListAsync();

        var result = new List<object>();
        foreach (var d in details)
        {
            var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == d.PrepOrder!.ProductionOrderId);
            result.Add(new
            {
                prep_detail_id = d.Id, prep_order_id = d.PrepOrderId, prep_order_no = d.PrepOrder!.OrderNo,
                product_name = order?.ProductName ?? "", part_id = d.PartId, part_no = d.PartNo,
                required_qty = d.RequiredQty, actual_qty = d.ActualQty, remaining = d.RequiredQty - d.ActualQty
            });
        }
        return result;
    }

    private static object ToDict(PrepOrder p, Dictionary<long, string>? productNames = null, Dictionary<long, string>? fallbackNames = null)
    {
        var pn = "";
        if (productNames != null && productNames.TryGetValue(p.ProductionOrderId, out var names))
            pn = names;
        else if (fallbackNames != null && fallbackNames.TryGetValue(p.ProductionOrderId, out var fb))
            pn = fb;

        return new
        {
            p.Id, order_no = p.OrderNo, product_name = pn,
            production_order_id = p.ProductionOrderId,
            line_id = p.LineId, status = p.Status, kit_check_result = p.KitCheckResult,
            completed_at = p.CompletedAt, created_at = p.CreatedAt,
            total_required_qty = p.Details.Sum(d => d.RequiredQty)
        };
    }

    private static object DetailToDict(PrepDetail d) => new
    {
        d.Id, part_id = d.PartId, part_no = d.PartNo,
        required_qty = d.RequiredQty, actual_qty = d.ActualQty,
        status = d.Status, substitute_flag = d.SubstituteFlag
    };
}
