using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class RefillService
{
    private readonly AppDbContext _db;

    public RefillService(AppDbContext db) { _db = db; }

    /// <summary>
    /// 扫描订单号获取补料清单：从订单反查产品名+月份，再加载待补料明细
    /// </summary>
    public async Task<List<object>> GetPartsByOrderNoAsync(string orderNo)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
        if (order == null) throw AppException.NotFound($"订单 {orderNo} 不存在");

        // 直接从订单的 PrepOrders → PrepDetails 取待补料明细，不绕产品名
        var prepIds = await _db.PrepOrders
            .Where(p => p.ProductionOrderId == order.Id && p.Status != 3)
            .Select(p => p.Id).ToListAsync();

        var details = await _db.PrepDetails
            .Where(d => prepIds.Contains(d.PrepOrderId))
            .OrderBy(d => d.Id).ToListAsync();

        var prepOrders = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var partIds = details.Select(d => d.PartId).Distinct().ToList();
        var invs = await _db.Inventories.Where(i => partIds.Contains(i.PartId)).ToListAsync();
        var locIds = invs.Select(i => i.LocationId).Distinct().ToList();
        var locations = await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id);
        var prodOrders = await _db.ProductionOrders.Where(o => o.Id == order.Id).ToDictionaryAsync(o => o.Id);

        var result = new List<object>();
        foreach (var d in details)
        {
            prepOrders.TryGetValue(d.PrepOrderId, out var prep);
            prodOrders.TryGetValue(prep?.ProductionOrderId ?? 0, out var prodOrder);
            var partInvs = invs.Where(i => i.PartId == d.PartId).ToList();
            var locCodes = partInvs.Select(i => locations.TryGetValue(i.LocationId, out var loc) ? loc.LocationCode : "")
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

            result.Add(new
            {
                prep_detail_id = d.Id, prep_order_id = d.PrepOrderId,
                prep_order_no = prep?.OrderNo ?? "",
                product_name = prodOrder?.ProductName ?? "",
                part_id = d.PartId, part_no = d.PartNo,
                required_qty = d.RequiredQty, actual_qty = d.ActualQty,
                remaining = d.RequiredQty - d.ActualQty,
                location_codes = locCodes
            });
        }
        return result;
    }

    /// 扫描产品名称获取补料清单：当前生产中的产品的所有料号（备料完成的订单）
    /// 同时搜索 product_boms（产品名注册表）、order_products（新多产品模式）、production_orders.product_name（旧单产品模式）
    /// </summary>
    public async Task<List<object>> GetPartsByProductAsync(string productName, string? productionMonth = null)
    {
        // 1. 从 product_boms 查找匹配的产品名称（产品名注册表，最全）
        var bomProductNames = await _db.ProductBoms
            .Where(b => b.ProductName.Contains(productName))
            .Select(b => b.ProductName).Distinct().ToListAsync();

        // 合并原始输入和 BOM 匹配到的精确名称
        var searchNames = new List<string> { productName };
        searchNames.AddRange(bomProductNames.Where(n => n != productName));

        // 2. 先从 order_products 表查匹配的订单ID（多产品模式）
        var orderProductIds = await _db.OrderProducts
            .Where(op => searchNames.Contains(op.ProductName) || op.ProductName.Contains(productName))
            .Select(op => op.OrderId).Distinct().ToListAsync();

        // 3. 同时搜索 production_orders.product_name（旧单产品模式兼容）和 order_products
        var orderQuery = _db.ProductionOrders
            .Where(o => searchNames.Contains(o.ProductName) || o.ProductName.Contains(productName) || orderProductIds.Contains(o.Id))
            .Where(o => o.Status == 1 || o.Status == 2);
        // 有生连月份时只匹配同月份的订单
        if (!string.IsNullOrEmpty(productionMonth))
            orderQuery = orderQuery.Where(o => o.ProductionMonth == productionMonth || o.ProductionMonth == null);
        var orderIds = await orderQuery.OrderByDescending(o => o.CreatedAt).Select(o => o.Id).ToListAsync();
        if (!orderIds.Any())
        {
            var fallbackQuery = _db.ProductionOrders
                .Where(o => searchNames.Contains(o.ProductName) || o.ProductName.Contains(productName) || orderProductIds.Contains(o.Id));
            if (!string.IsNullOrEmpty(productionMonth))
                fallbackQuery = fallbackQuery.Where(o => o.ProductionMonth == productionMonth || o.ProductionMonth == null);
            orderIds = await fallbackQuery.OrderByDescending(o => o.CreatedAt)
                .Take(1).Select(o => o.Id).ToListAsync();
        }

        var prepIds = await _db.PrepOrders
            .Where(p => orderIds.Contains(p.ProductionOrderId) && p.Status != 3)
            .Select(p => p.Id).ToListAsync();
        var details = await _db.PrepDetails
            .Where(d => prepIds.Contains(d.PrepOrderId))
            .OrderBy(d => d.Id).ToListAsync();

        // 批量加载关联数据
        var prepOrders = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var partIds = details.Select(d => d.PartId).Distinct().ToList();
        var invs = await _db.Inventories.Where(i => partIds.Contains(i.PartId)).ToListAsync();
        var locIds = invs.Select(i => i.LocationId).Distinct().ToList();
        var locations = await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id);
        var prodOrderIds = prepOrders.Values.Select(p => p.ProductionOrderId).Distinct().ToList();
        var prodOrders = await _db.ProductionOrders.Where(o => prodOrderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);

        var result = new List<object>();
        foreach (var d in details)
        {
            prepOrders.TryGetValue(d.PrepOrderId, out var prep);
            prodOrders.TryGetValue(prep?.ProductionOrderId ?? 0, out var order);
            var partInvs = invs.Where(i => i.PartId == d.PartId).ToList();
            var locCodes = partInvs.Select(i => locations.TryGetValue(i.LocationId, out var loc) ? loc.LocationCode : "")
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

            result.Add(new
            {
                prep_detail_id = d.Id, prep_order_id = d.PrepOrderId,
                prep_order_no = prep?.OrderNo ?? "",
                product_name = order?.ProductName ?? "",
                part_id = d.PartId, part_no = d.PartNo, part_name = "",
                location_codes = locCodes
            });
        }
        return result;
    }

    /// <summary>
    /// 批量开始补料：勾选多个料号，一次性创建 step=1 记录
    /// </summary>
    public async Task<object> BatchStartAsync(List<RefillStartItem> items, string batchNo, long operatorId)
    {
        foreach (var item in items)
        {
            _db.RefillRecords.Add(new RefillRecord
            {
                PrepOrderId = item.PrepOrderId, PrepDetailId = item.PrepDetailId,
                PartNo = item.PartNo, PartName = item.PartName,
                LocationCode = item.LocationCode, Barcode = "",
                BatchNo = batchNo, Step = 1, OperatorId = operatorId
            });
        }
        await _db.SaveChangesAsync();
        return new { batch_no = batchNo, count = items.Count, message = $"已开始 {items.Count} 项补料" };
    }

    /// <summary>
    /// 取料/核对扫描：每次扫描新建一条记录（支持多次扫描同一料号）
    /// </summary>
    /// <summary>
    /// 获取所有未完成批次列表
    /// </summary>
    public async Task<object> GetActiveBatchesAsync()
    {
        var records = await _db.RefillRecords.Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.BatchNo))
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        var batches = records.GroupBy(r => r.BatchNo)
            .Where(g => !g.Any(r => r.Step >= 3))
            .Select(g => new {
                batch_no = g.Key,
                last = g.Max(r => r.CreatedAt),
                prepOrderId = g.First().PrepOrderId
            }).OrderByDescending(b => b.last).ToList();

        var prepIds = batches.Select(b => b.prepOrderId).Distinct().ToList();
        var preps = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var prodIds = preps.Values.Select(p => p.ProductionOrderId).Distinct().ToList();
        var prods = await _db.ProductionOrders.Where(o => prodIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);

        return batches.Select(b => new {
            batch_no = b.batch_no, last = b.last,
            product_name = prods.GetValueOrDefault(preps.GetValueOrDefault(b.prepOrderId)?.ProductionOrderId ?? 0)?.ProductName ?? ""
        }).ToList();
    }

    /// <summary>
    /// 按批次号加载补料详情
    /// </summary>
    public async Task<object?> GetBatchDetailAsync(string batchNo)
    {
        var allRecords = await _db.RefillRecords
            .Where(r => !r.IsDeleted && r.BatchNo == batchNo).OrderByDescending(r => r.CreatedAt).ToListAsync();
        if (!allRecords.Any()) return null;
        var pid = allRecords.First().PrepOrderId;

        var prep = await _db.PrepOrders.FirstOrDefaultAsync(p => p.Id == pid);
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => prep != null && o.Id == prep.ProductionOrderId);
        var refillDetailIds = allRecords.Select(r => r.PrepDetailId).Distinct().ToList();
        var allParts = await _db.PrepDetails
            .Where(d => d.PrepOrderId == pid && refillDetailIds.Contains(d.Id)).OrderBy(d => d.Id).ToListAsync();

        var partIds = allParts.Select(d => d.PartId).Distinct().ToList();
        var invs = await _db.Inventories.Where(i => partIds.Contains(i.PartId)).ToListAsync();
        var locIds = invs.Select(i => i.LocationId).Distinct().ToList();
        var locations = await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id);

        var parts = allParts.Select(d =>
        {
            var partInvs = invs.Where(i => i.PartId == d.PartId).ToList();
            var locCodes = partInvs.Select(i => locations.TryGetValue(i.LocationId, out var loc) ? loc.LocationCode : "").Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            return new {
                prep_detail_id = d.Id, prep_order_id = d.PrepOrderId,
                prep_order_no = prep?.OrderNo ?? "",
                product_name = order?.ProductName ?? "",
                part_id = d.PartId, part_no = d.PartNo, part_name = "",
                location_codes = locCodes
            };
        }).ToList();

        var pickedIds = allRecords.Where(r => r.Step >= 2).Select(r => r.PrepDetailId).Distinct().ToList();
        var maxStep = allRecords.Max(r => r.Step);

        return new {
            batch_no = batchNo,
            step = maxStep == 1 ? 2 : 3,
            product_name = order?.ProductName ?? "",
            parts,
            picked_ids = pickedIds,
            verified_ids = new List<long>(),
            selected_ids = parts.Select(p => (long)p.prep_detail_id).ToList()
        };
    }

    public async Task<object> ScanAsync(long detailId, long prepOrderId, string partNo, string partName,
        string locationCode, string barcode, string batchNo, int targetStep, long operatorId)
    {
        var record = new RefillRecord
        {
            PrepOrderId = prepOrderId, PrepDetailId = detailId,
            PartNo = partNo, PartName = partName,
            LocationCode = locationCode, Barcode = barcode,
            BatchNo = batchNo, Step = targetStep, OperatorId = operatorId
        };
        if (targetStep == 2) record.PickedAt = DateTime.UtcNow;
        else if (targetStep == 3) record.VerifiedAt = DateTime.UtcNow;
        _db.RefillRecords.Add(record);
        await _db.SaveChangesAsync();
        var label = targetStep == 2 ? "已取料" : "已核对";
        return new { step = targetStep, part_no = partNo, message = $"已{label}: {partNo}" };
    }

    public async Task<object> GetRecordsAsync(string? partNo, string? locationCode,
        DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 50)
    {
        var query = _db.RefillRecords.AsQueryable();
        if (!string.IsNullOrEmpty(partNo)) query = query.Where(r => r.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(locationCode)) query = query.Where(r => r.LocationCode.Contains(locationCode));
        if (startDate.HasValue) query = query.Where(r => r.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(r => r.CreatedAt < endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        // 批量加载产品名
        var prepIds = items.Select(r => r.PrepOrderId).Distinct().ToList();
        var prepOrders = await _db.PrepOrders.Where(p => prepIds.Contains(p.Id)).ToListAsync();
        var prodOrderIds = prepOrders.Select(p => p.ProductionOrderId).Distinct().ToList();
        var prodOrders = await _db.ProductionOrders.Where(o => prodOrderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);

        return new { total, page, page_size = pageSize, items = items.Select(r => (object)new
        {
            r.Id, prep_order_id = r.PrepOrderId, prep_detail_id = r.PrepDetailId,
            part_no = r.PartNo, part_name = r.PartName, location_code = r.LocationCode,
            barcode = r.Barcode, step = r.Step, operator_id = r.OperatorId,
            picked_at = r.PickedAt, verified_at = r.VerifiedAt, created_at = r.CreatedAt,
            product_name = prodOrders.GetValueOrDefault(prepOrders.FirstOrDefault(p => p.Id == r.PrepOrderId)?.ProductionOrderId ?? 0)?.ProductName ?? "",
            prep_order_no = prepOrders.FirstOrDefault(p => p.Id == r.PrepOrderId)?.OrderNo ?? ""
        }) };
    }
}
