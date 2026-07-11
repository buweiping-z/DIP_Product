using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class ShelvingService
{
    private readonly AppDbContext _db;

    public ShelvingService(AppDbContext db) { _db = db; }

    public async Task<object> CreateBatchAsync(long targetLocationId, long operatorId)
    {
        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == targetLocationId);
        if (loc == null) throw AppException.NotFound("目标库位不存在");

        var batchNo = $"LB{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        var batch = new ShelvingBatch { BatchNo = batchNo, TargetLocationId = targetLocationId, OperatorId = operatorId, Status = 1 };
        _db.ShelvingBatches.Add(batch);
        await _db.SaveChangesAsync();
        return new { id = batch.Id, batch_no = batch.BatchNo, target_location_id = batch.TargetLocationId, status = batch.Status };
    }

    public async Task<object> AddItemAsync(long batchId, string barcode, long operatorId)
    {
        var batch = await _db.ShelvingBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null || batch.Status != 1) throw AppException.Business("上架批次状态不允许操作");

        barcode = barcode.Trim();
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == barcode || p.PartNo.Contains(barcode));
        if (part == null) throw AppException.NotFound("未找到对应物料");

        var existing = await _db.ShelvingBatchItems.FirstOrDefaultAsync(i => i.BatchId == batchId && i.PartId == part.Id);
        if (existing != null)
        {
            existing.Quantity += 1;
            existing.ScannedBarcode = barcode;
            await _db.SaveChangesAsync();
            return new { id = existing.Id, part_id = existing.PartId, part_no = existing.PartNo, quantity = existing.Quantity };
        }

        var item = new ShelvingBatchItem { BatchId = batchId, PartId = part.Id, PartNo = part.PartNo, Quantity = 1, ScannedBarcode = barcode };
        _db.ShelvingBatchItems.Add(item);
        await _db.SaveChangesAsync();
        return new { id = item.Id, part_id = item.PartId, part_no = item.PartNo, quantity = (decimal)1 };
    }

    public async Task ConfirmBatchAsync(long batchId, long operatorId)
    {
        var batch = await _db.ShelvingBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null || batch.Status != 1) throw AppException.Business("上架批次状态不允许确认");

        var items = await _db.ShelvingBatchItems.Where(i => i.BatchId == batchId).ToListAsync();
        if (items.Count == 0) throw AppException.Business("批次无项目");

        var invSvc = new InventoryService(_db);
        foreach (var item in items)
        {
            if (item.SourceLocationId.HasValue)
                await invSvc.TransferOutCoreAsync(item.PartId, item.SourceLocationId.Value, item.Quantity, operatorId, "ShelvingOut", batchId);
            await invSvc.AddCoreAsync(item.PartId, batch.TargetLocationId, item.Quantity,
                item.BatchNo ?? $"LB-{DateTime.UtcNow:yyyyMMdd}", operatorId, "ShelvingIn", batchId);

            _db.MaterialShelvings.Add(new MaterialShelving
            {
                PartId = item.PartId, PartNo = item.PartNo, SourceLocationId = item.SourceLocationId,
                TargetLocationId = batch.TargetLocationId, BatchNo = item.BatchNo, Quantity = item.Quantity,
                ScannedBarcode = item.ScannedBarcode, OperatorId = operatorId, Status = 1, LoadedAt = DateTime.UtcNow
            });
        }
        batch.Status = 2;
        batch.ConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task CancelBatchAsync(long batchId, long operatorId)
    {
        var batch = await _db.ShelvingBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null || batch.Status != 2) throw AppException.Business("上架批次状态不允许撤销");

        var invSvc = new InventoryService(_db);
        var loadings = await _db.MaterialShelvings.Where(m => m.TargetLocationId == batch.TargetLocationId && m.Status == 1).ToListAsync();
        foreach (var loading in loadings)
        {
            await invSvc.TransferOutCoreAsync(loading.PartId, loading.TargetLocationId, loading.Quantity, operatorId, "ShelvingCancel", batchId);
            if (loading.SourceLocationId.HasValue)
                await invSvc.AddCoreAsync(loading.PartId, loading.SourceLocationId.Value, loading.Quantity,
                    loading.BatchNo ?? "", operatorId, "ReturnIn", batchId);
            loading.Status = 3;
        }
        batch.Status = 3;
        await _db.SaveChangesAsync();
    }

    public async Task<object> GetBatchAsync(long batchId)
    {
        var batch = await _db.ShelvingBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null) throw AppException.NotFound($"上架批次 {batchId} 不存在");
        var items = await _db.ShelvingBatchItems.Where(i => i.BatchId == batchId).ToListAsync();
        return new
        {
            batch.Id, batch_no = batch.BatchNo, target_location_id = batch.TargetLocationId,
            operator_id = batch.OperatorId, status = batch.Status, confirmed_at = batch.ConfirmedAt,
            items = items.Select(i => (object)new { i.Id, part_id = i.PartId, part_no = i.PartNo, quantity = i.Quantity, batch_no = i.BatchNo })
        };
    }

    public async Task<object> GetBatchListAsync(int? status = null, long? locationId = null, int page = 1, int pageSize = 20)
    {
        var query = _db.ShelvingBatches.AsQueryable();
        if (status.HasValue) query = query.Where(b => b.Status == status.Value);
        if (locationId.HasValue) query = query.Where(b => b.TargetLocationId == locationId.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(b => b.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(b => (object)new { b.Id, batch_no = b.BatchNo, status = b.Status, confirmed_at = b.ConfirmedAt })
        };
    }

    public async Task<object> DirectShelvingAsync(string barcode, string targetLocationCode,
        decimal quantity, long operatorId)
    {
        // 1. 条码匹配部品（去空格 + 不区分大小写，MySQL 默认 ci）
        barcode = barcode.Trim();
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == barcode);
        if (part == null) throw AppException.NotFound($"未找到部品: {barcode}");

        // 2. 编码匹配库位
        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == targetLocationCode);
        if (loc == null) throw AppException.NotFound($"未找到库位: {targetLocationCode}");

        // 2.5 同库位已有不同料号 → 拒绝
        var conflict = await _db.Inventories.FirstOrDefaultAsync(i =>
            i.LocationId == loc.Id && i.PartId != part.Id && i.TotalQty > 0);
        if (conflict != null)
        {
            var conflictPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == conflict.PartId);
            throw AppException.Business($"库位 {targetLocationCode} 已有料号 {conflictPart?.PartNo ?? "?"}，不能放入不同料号 {barcode}");
        }

        // 3. 库存入库（生成默认批次号确保 InventoryLot 创建）
        var invSvc = new InventoryService(_db);
        var batchNo = $"SN{DateTime.UtcNow:yyyyMMddHHmmss}";
        await invSvc.AddCoreAsync(part.Id, loc.Id, quantity, batchNo, operatorId, "ShelvingDirect");

        // ★ 上架后触发活跃订单重新冻结（新库存按先到先得分给待补货订单）
        var orderSvc = new OrderService(_db);
        await orderSvc.RefreezeActiveOrdersAsync(operatorId);

        // 4. 写上架记录
        var record = new MaterialShelving
        {
            PartId = part.Id,
            PartNo = part.PartNo,
            PartName = part.PartName,
            TargetLocationId = loc.Id,
            Quantity = quantity,
            OperatorId = operatorId,
            Status = 1,
            LoadedAt = DateTime.UtcNow
        };
        _db.MaterialShelvings.Add(record);
        await _db.SaveChangesAsync();

        return new
        {
            id = record.Id,
            part_no = record.PartNo,
            part_name = record.PartName,
            target_location_id = record.TargetLocationId,
            target_location_code = loc.LocationCode,
            quantity = record.Quantity,
            operator_id = record.OperatorId,
            loaded_at = record.LoadedAt
        };
    }

    public async Task<object> GetRecordsAsync(string? partName, string? locationCode,
        DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 20)
    {
        var query = _db.MaterialShelvings.AsQueryable();

        if (!string.IsNullOrEmpty(partName))
            query = query.Where(m => m.PartName.Contains(partName));

        if (!string.IsNullOrEmpty(locationCode))
        {
            var locIds = await _db.WarehouseLocations
                .Where(l => l.LocationCode.Contains(locationCode))
                .Select(l => l.Id)
                .ToListAsync();
            query = query.Where(m => locIds.Contains(m.TargetLocationId));
        }

        if (startDate.HasValue)
            query = query.Where(m => m.LoadedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(m => m.LoadedAt < endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // 批量取库位编码和操作者姓名
        var targetLocIds = items.Select(i => i.TargetLocationId).Distinct().ToList();
        var userIds = items.Select(i => i.OperatorId).Distinct().ToList();
        var locsMap = (await _db.WarehouseLocations.Where(l => targetLocIds.Contains(l.Id)).ToListAsync())
            .ToDictionary(l => l.Id);
        var usersMap = (await _db.Operators.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(m => (object)new
            {
                m.Id,
                part_no = m.PartNo,
                part_name = m.PartName,
                target_location_id = m.TargetLocationId,
                target_location_code = locsMap.TryGetValue(m.TargetLocationId, out var l) ? l.LocationCode : "",
                quantity = m.Quantity,
                operator_id = m.OperatorId,
                operator_name = usersMap.TryGetValue(m.OperatorId, out var u) ? u.RealName : "",
                loaded_at = m.LoadedAt
            })
        };
    }
}
