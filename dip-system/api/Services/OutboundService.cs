using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OutboundService
{
    private readonly AppDbContext _db;

    public OutboundService(AppDbContext db) { _db = db; }

    // ========== DTO ==========

    public class OutboundDetailInput
    {
        public long PartId { get; set; }
        public string PartNo { get; set; } = "";
        public string PartName { get; set; } = "";
        public long LocationId { get; set; }
        public string LocationCode { get; set; } = "";
        public decimal Quantity { get; set; }
    }

    // ========== 列表 ==========

    public async Task<object> GetListAsync(int? status, string? partNo, string? locationCode,
        DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 20)
    {
        var query = _db.OutboundOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt < endDate.Value.AddDays(1));

        // 按明细中的 part_no / location_code 搜索
        if (!string.IsNullOrEmpty(partNo) || !string.IsNullOrEmpty(locationCode))
        {
            var detailQuery = _db.OutboundDetails.AsQueryable();
            if (!string.IsNullOrEmpty(partNo)) detailQuery = detailQuery.Where(d => d.PartNo.Contains(partNo));
            if (!string.IsNullOrEmpty(locationCode)) detailQuery = detailQuery.Where(d => d.LocationCode.Contains(locationCode));
            var matchedOrderIds = await detailQuery.Select(d => d.OrderId).Distinct().ToListAsync();
            query = query.Where(o => matchedOrderIds.Contains(o.Id));
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var orderIds = items.Select(o => o.Id).ToList();
        var detailCounts = await _db.OutboundDetails
            .Where(d => orderIds.Contains(d.OrderId))
            .GroupBy(d => d.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Count() })
            .ToListAsync();

        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(o => ToDict(o, detailCounts.FirstOrDefault(d => d.OrderId == o.Id)?.Count ?? 0))
        };
    }

    // ========== 详情 ==========

    public async Task<object> GetByIdAsync(long id)
    {
        var order = await _db.OutboundOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw AppException.NotFound("出库单不存在");
        return ToDictWithDetails(order);
    }

    // ========== 创建 ==========

    public async Task<object> CreateAsync(List<OutboundDetailInput> details, long operatorId)
    {
        if (details == null || details.Count == 0)
            throw AppException.Business("至少需要一条出库明细");

        var orderNo = $"OUT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
        var order = new OutboundOrder { OrderNo = orderNo, Status = 1, OperatorId = operatorId };
        _db.OutboundOrders.Add(order);
        await _db.SaveChangesAsync();

        foreach (var d in details)
        {
            if (d.Quantity <= 0) throw AppException.Business("数量必须大于0");

            var inv = await _db.Inventories.FirstOrDefaultAsync(i =>
                i.PartId == d.PartId && i.LocationId == d.LocationId);
            if (inv == null || inv.AvailableQty < d.Quantity)
                throw AppException.Business($"{d.PartNo} 在库位 {d.LocationCode} 可用库存不足");

            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.PartId);
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.LocationId);

            _db.OutboundDetails.Add(new OutboundDetail
            {
                OrderId = order.Id,
                PartId = d.PartId,
                PartNo = part?.PartNo ?? d.PartNo,
                PartName = part?.PartName ?? d.PartName,
                LocationId = d.LocationId,
                LocationCode = loc?.LocationCode ?? d.LocationCode,
                Quantity = d.Quantity,
                Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(order.Id);
    }

    // ========== 编辑 ==========

    public async Task<object> UpdateAsync(long orderId, List<OutboundDetailInput> newDetails)
    {
        if (newDetails == null || newDetails.Count == 0)
            throw AppException.Business("至少需要一条出库明细");

        var order = await _db.OutboundOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound("出库单不存在");

        if (order.Status != 1) throw AppException.Business("只能编辑待出库状态的订单");

        // 删除所有待核销明细
        var pending = order.Details.Where(d => d.Status == 1).ToList();
        _db.OutboundDetails.RemoveRange(pending);

        // 添加新明细
        foreach (var d in newDetails)
        {
            if (d.Quantity <= 0) throw AppException.Business("数量必须大于0");

            var inv = await _db.Inventories.FirstOrDefaultAsync(i =>
                i.PartId == d.PartId && i.LocationId == d.LocationId);
            if (inv == null || inv.AvailableQty < d.Quantity)
                throw AppException.Business($"{d.PartNo} 在库位 {d.LocationCode} 可用库存不足");

            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.PartId);
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.LocationId);

            _db.OutboundDetails.Add(new OutboundDetail
            {
                OrderId = order.Id,
                PartId = d.PartId,
                PartNo = part?.PartNo ?? d.PartNo,
                PartName = part?.PartName ?? d.PartName,
                LocationId = d.LocationId,
                LocationCode = loc?.LocationCode ?? d.LocationCode,
                Quantity = d.Quantity,
                Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(order.Id);
    }

    // ========== 删除 ==========

    public async Task DeleteAsync(long id)
    {
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) throw AppException.NotFound("出库单不存在");
        if (order.Status != 1) throw AppException.Business("只能删除待出库状态的订单");
        order.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    // ========== 逐种核销 ==========

    public async Task<object> ConfirmDetailAsync(long orderId, long detailId, string barcode, long operatorId)
    {
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound("出库单不存在");
        if (order.Status != 1) throw AppException.Business("该出库单状态不允许操作");

        var detail = await _db.OutboundDetails.FirstOrDefaultAsync(d => d.Id == detailId && d.OrderId == orderId);
        if (detail == null) throw AppException.NotFound("出库明细不存在");
        if (detail.Status != 1) throw AppException.Business("该明细已核销");

        // 条码匹配
        if (!string.Equals(detail.PartNo.Trim(), barcode.Trim(), StringComparison.OrdinalIgnoreCase)
            && !barcode.Trim().Contains(detail.PartNo.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Business("条码与出库料号不匹配");

        // 扣减库存
        DeductInventory(detail, operatorId);
        detail.Status = 2;
        await _db.SaveChangesAsync();

        return new { detail_id = detail.Id, status = detail.Status, part_no = detail.PartNo };
    }

    // ========== 整单完成 ==========

    public async Task<object> ConfirmAllAsync(long orderId)
    {
        var order = await _db.OutboundOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound("出库单不存在");

        if (order.Status != 1) throw AppException.Business("该出库单状态不允许操作");

        var allDone = order.Details.All(d => d.Status == 2);
        if (!allDone) throw AppException.Business("还有明细未核销");

        order.Status = 2;
        order.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDictWithDetails(order);
    }

    // ========== 可用库存列表 ==========

    public async Task<object> GetAvailablePartsAsync()
    {
        var invs = await _db.Inventories.Where(i => i.AvailableQty > 0 && !i.IsDeleted).ToListAsync();
        var partIds = invs.Select(i => i.PartId).Distinct().ToList();
        var locIds = invs.Select(i => i.LocationId).Distinct().ToList();
        var parts = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync();
        var locs = await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToListAsync();

        return invs.Select(i =>
        {
            var part = parts.FirstOrDefault(p => p.Id == i.PartId);
            var loc = locs.FirstOrDefault(l => l.Id == i.LocationId);
            return (object)new
            {
                part_id = i.PartId, part_no = part?.PartNo ?? "", part_name = part?.PartName ?? "",
                location_id = i.LocationId, location_code = loc?.LocationCode ?? "",
                available_qty = i.AvailableQty
            };
        }).OrderBy(x => ((dynamic)x).part_no).ToList();
    }

    // ========== Private ==========

    private void DeductInventory(OutboundDetail detail, long operatorId)
    {
        var inv = _db.Inventories.FirstOrDefault(i =>
            i.PartId == detail.PartId && i.LocationId == detail.LocationId);
        if (inv == null || inv.AvailableQty < detail.Quantity)
            throw AppException.Business($"{detail.PartNo} 可用库存不足");

        inv.AvailableQty -= detail.Quantity;
        inv.TotalQty -= detail.Quantity;
        inv.Version++;

        // FIFO 扣批次
        var lots = _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 1 && l.Quantity > 0)
            .OrderBy(l => l.ReceiptDate).ToList();
        var remaining = detail.Quantity;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            var deduct = Math.Min(remaining, lot.Quantity);
            lot.Quantity -= deduct;
            lot.Version++;
            if (lot.Quantity <= 0) lot.Status = 3;
            remaining -= deduct;
        }

        _db.StockMovements.Add(new StockMovement
        {
            PartId = detail.PartId, PartNo = detail.PartNo, LocationId = detail.LocationId,
            MovementType = 3, Quantity = detail.Quantity, BalanceAfter = inv.TotalQty,
            ReferenceType = "Outbound", ReferenceId = detail.Id, OperatorId = operatorId
        });

        var loc = _db.WarehouseLocations.FirstOrDefault(l => l.Id == detail.LocationId);
        if (loc != null) loc.CurrentQty -= detail.Quantity;
    }

    private static object ToDict(OutboundOrder o, int detailCount) => new
    {
        o.Id, order_no = o.OrderNo, status = o.Status, operator_id = o.OperatorId,
        detail_count = detailCount, completed_at = o.CompletedAt, created_at = o.CreatedAt
    };

    private static object ToDictWithDetails(OutboundOrder o) => new
    {
        o.Id, order_no = o.OrderNo, status = o.Status, operator_id = o.OperatorId,
        completed_at = o.CompletedAt, created_at = o.CreatedAt,
        details = o.Details.Select(d => new
        {
            d.Id, d.OrderId, d.PartId, d.PartNo, d.PartName,
            d.LocationId, d.LocationCode, quantity = d.Quantity, d.Status
        })
    };
}
