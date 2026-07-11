using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class OutboundService
{
    private readonly AppDbContext _db;

    public OutboundService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(int? status, string? partNo, string? locationCode,
        DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 20)
    {
        var query = _db.OutboundOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrEmpty(partNo)) query = query.Where(o => o.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(locationCode)) query = query.Where(o => o.LocationCode.Contains(locationCode));
        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt < endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> CreateAsync(long partId, string partNo, string partName,
        long locationId, string locationCode, decimal quantity, long operatorId)
    {
        if (quantity <= 0) throw AppException.Business("数量必须大于0");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i =>
            i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.AvailableQty < quantity)
            throw AppException.Business("可用库存不足");

        var orderNo = $"OUT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
        var order = new OutboundOrder
        {
            OrderNo = orderNo, PartId = partId, PartNo = partNo, PartName = partName,
            LocationId = locationId, LocationCode = locationCode,
            Quantity = quantity, Status = 1, OperatorId = operatorId
        };
        _db.OutboundOrders.Add(order);
        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task<object> UpdateAsync(long id, long partId, string partNo, string partName,
        long locationId, string locationCode, decimal quantity)
    {
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) throw AppException.NotFound("出库单不存在");
        if (order.Status != 1) throw AppException.Business("只能编辑待出库状态的订单");

        if (quantity <= 0) throw AppException.Business("数量必须大于0");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i =>
            i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.AvailableQty < quantity)
            throw AppException.Business("可用库存不足");

        order.PartId = partId; order.PartNo = partNo; order.PartName = partName;
        order.LocationId = locationId; order.LocationCode = locationCode;
        order.Quantity = quantity;
        await _db.SaveChangesAsync();
        return ToDict(order);
    }

    public async Task DeleteAsync(long id)
    {
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) throw AppException.NotFound("出库单不存在");
        if (order.Status != 1) throw AppException.Business("只能删除待出库状态的订单");
        order.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 扫描核销出库：直接扣减库存（无需冻结），一次扫完全部数量
    /// </summary>
    public async Task<object> ConfirmAsync(long orderId, string barcode, long operatorId)
    {
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw AppException.NotFound("出库单不存在");
        if (order.Status != 1) throw AppException.Business("该出库单状态不允许操作");

        // 大小写不敏感 + 条码包含料号即匹配
        if (!string.Equals(order.PartNo.Trim(), barcode.Trim(), StringComparison.OrdinalIgnoreCase)
            && !barcode.Trim().Contains(order.PartNo.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Business("条码与出库料号不匹配");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i =>
            i.PartId == order.PartId && i.LocationId == order.LocationId);
        if (inv == null || inv.AvailableQty < order.Quantity)
            throw AppException.Business("可用库存不足");

        // 直接扣减可用库存（不经过冻结）
        inv.AvailableQty -= order.Quantity;
        inv.TotalQty -= order.Quantity;
        inv.Version++;

        // 扣减批次（FIFO）
        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 1 && l.Quantity > 0)
            .OrderBy(l => l.ReceiptDate).ToListAsync();
        var remaining = order.Quantity;
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
            PartId = order.PartId, PartNo = order.PartNo, LocationId = order.LocationId,
            MovementType = 3, Quantity = order.Quantity, BalanceAfter = inv.TotalQty,
            ReferenceType = "Outbound", ReferenceId = order.Id, OperatorId = operatorId
        });

        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == order.LocationId);
        if (loc != null) loc.CurrentQty -= order.Quantity;

        await _db.SaveChangesAsync();

        order.Status = 2;
        order.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDict(order);
    }

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

    private static object ToDict(OutboundOrder o) => new
    {
        o.Id, order_no = o.OrderNo, part_id = o.PartId, part_no = o.PartNo,
        part_name = o.PartName, location_id = o.LocationId, location_code = o.LocationCode,
        quantity = o.Quantity, status = o.Status, operator_id = o.OperatorId,
        completed_at = o.CompletedAt, created_at = o.CreatedAt
    };
}
