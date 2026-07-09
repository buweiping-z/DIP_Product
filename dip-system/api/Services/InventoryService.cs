using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class FifoLotResult
{
    public long Id { get; set; }
    public long InventoryId { get; set; }
    public string BatchNo { get; set; } = "";
    public decimal Quantity { get; set; }
    public int Status { get; set; }
    public DateTime ReceiptDate { get; set; }
}

/// <summary>
/// 库存服务 — Core/Facade 双层模式
/// Core 方法: 内存操作，不调用 SaveChanges，供事务编排
/// Facade 方法: 调用 Core + SaveChangesAsync，供独立调用
/// </summary>
public class InventoryService
{
    private readonly AppDbContext _db;

    public InventoryService(AppDbContext db) { _db = db; }

    // ===== Core Methods =====

    public async Task AddCoreAsync(long partId, long locationId, decimal qty, string batchNo,
        long operatorId, string referenceType = "Import", long? referenceId = null)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");

        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"部品 {partId} 不存在");

        var inv = _db.Inventories.Local.FirstOrDefault(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null)
            inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null)
        {
            inv = new Inventory { PartId = partId, LocationId = locationId };
            _db.Inventories.Add(inv);
        }

        inv.TotalQty += qty;
        inv.AvailableQty += qty;
        if (!string.IsNullOrEmpty(batchNo))
            await AddLotAsync(inv, partId, locationId, batchNo, qty);

        int mt = referenceType == "ReturnIn" ? 4 : 1;
        _db.StockMovements.Add(new StockMovement
        {
            PartId = partId, PartNo = part.PartNo, LocationId = locationId,
            BatchNo = batchNo, MovementType = mt, Quantity = qty, BalanceAfter = inv.TotalQty,
            ReferenceType = referenceType, ReferenceId = referenceId, OperatorId = operatorId
        });

        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locationId);
        if (loc != null) loc.CurrentQty += qty;
    }

    public async Task FreezeCoreAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"部品 {partId} 不存在");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.AvailableQty < qty) throw AppException.Business("可用库存不足");

        inv.AvailableQty -= qty;
        inv.FrozenQty += qty;
        inv.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 1)
            .OrderBy(l => l.ReceiptDate).ToListAsync();
        var remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            lot.Status = 2;
            lot.Version++;
            remaining -= lot.Quantity;
        }
        if (remaining > 0) throw AppException.Business("可用批次不足");

        _db.StockMovements.Add(new StockMovement
        {
            PartId = partId, PartNo = part.PartNo, LocationId = locationId,
            MovementType = 2, Quantity = qty, BalanceAfter = inv.TotalQty,
            ReferenceType = referenceType, ReferenceId = referenceId, OperatorId = operatorId
        });
    }

    public async Task DeductCoreAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"部品 {partId} 不存在");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.FrozenQty < qty) throw AppException.Business("冻结库存不足");

        inv.FrozenQty -= qty;
        inv.TotalQty -= qty;
        inv.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 2)
            .OrderBy(l => l.ReceiptDate).ToListAsync();
        var remaining = qty;
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
            PartId = partId, PartNo = part.PartNo, LocationId = locationId,
            MovementType = 3, Quantity = qty, BalanceAfter = inv.TotalQty,
            ReferenceType = referenceType, ReferenceId = referenceId, OperatorId = operatorId
        });

        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locationId);
        if (loc != null) loc.CurrentQty -= qty;
    }

    public async Task TransferOutCoreAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"部品 {partId} 不存在");

        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.AvailableQty < qty) throw AppException.Business("可用库存不足");

        inv.AvailableQty -= qty;
        inv.TotalQty -= qty;
        inv.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 1)
            .OrderBy(l => l.ReceiptDate).ToListAsync();
        var remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            var deduct = Math.Min(remaining, lot.Quantity);
            lot.Quantity -= deduct;
            lot.Version++;
            if (lot.Quantity <= 0) lot.Status = 3;
            remaining -= deduct;
        }
        if (remaining > 0) throw AppException.Business("可用批次不足");

        _db.StockMovements.Add(new StockMovement
        {
            PartId = partId, PartNo = part.PartNo, LocationId = locationId,
            MovementType = 5, Quantity = qty, BalanceAfter = inv.TotalQty,
            ReferenceType = referenceType, ReferenceId = referenceId, OperatorId = operatorId
        });

        var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == locationId);
        if (loc != null) loc.CurrentQty -= qty;
    }

    public async Task ThawCoreAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");
        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);
        if (inv == null || inv.FrozenQty < qty) throw AppException.Business("冻结库存不足");

        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        inv.FrozenQty -= qty;
        inv.AvailableQty += qty;
        inv.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inv.Id && l.Status == 2)
            .OrderBy(l => l.ReceiptDate).ToListAsync();
        var remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            lot.Status = 1;
            lot.Version++;
            remaining -= lot.Quantity;
        }

        _db.StockMovements.Add(new StockMovement
        {
            PartId = partId, PartNo = part?.PartNo ?? "", LocationId = locationId,
            MovementType = 8, Quantity = qty, BalanceAfter = inv.TotalQty,
            ReferenceType = referenceType, ReferenceId = referenceId, OperatorId = operatorId
        });
    }

    // ===== Facade Methods =====

    public async Task AddAsync(long partId, long locationId, decimal qty, string batchNo,
        long operatorId, string referenceType = "Import", long? referenceId = null)
    {
        await AddCoreAsync(partId, locationId, qty, batchNo, operatorId, referenceType, referenceId);
        await _db.SaveChangesAsync();
    }

    public async Task FreezeAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        await FreezeCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
        await _db.SaveChangesAsync();
    }

    public async Task DeductAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        await DeductCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
        await _db.SaveChangesAsync();
    }

    public async Task ThawAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        await ThawCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
        await _db.SaveChangesAsync();
    }

    public async Task TransferOutAsync(long partId, long locationId, decimal qty,
        long operatorId, string referenceType, long referenceId)
    {
        await TransferOutCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
        await _db.SaveChangesAsync();
    }

    // ===== Substitute =====

    public async Task<object> SubstituteCoreAsync(long originalPartId, long substitutePartId,
        long sourceLocationId, long targetLocationId, decimal qty, long operatorId)
    {
        if (qty <= 0) throw AppException.Business("数量必须大于0");
        if (originalPartId == substitutePartId) throw AppException.Business("原部品和替代部品不能相同");

        var subInv = await _db.Inventories.FirstOrDefaultAsync(i =>
            i.PartId == substitutePartId && i.LocationId == sourceLocationId);
        if (subInv == null || subInv.AvailableQty < qty)
            throw AppException.Business("替代部品库存不足");

        var origPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == originalPartId);
        var subPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == substitutePartId);
        var srcLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == sourceLocationId);
        var tgtLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == targetLocationId);

        var record = new SubstituteRecord
        {
            OriginalPartId = originalPartId, OriginalPartNo = origPart?.PartNo ?? "",
            SubstitutePartId = substitutePartId, SubstitutePartNo = subPart?.PartNo ?? "",
            SourceLocationId = sourceLocationId, SourceLocationCode = srcLoc?.LocationCode ?? "",
            TargetLocationId = targetLocationId, TargetLocationCode = tgtLoc?.LocationCode ?? "",
            Quantity = qty, Status = 1, OperatorId = operatorId
        };
        _db.SubstituteRecords.Add(record);
        await _db.SaveChangesAsync();

        return new { id = record.Id, status = record.Status, original_part_no = record.OriginalPartNo,
            substitute_part_no = record.SubstitutePartNo, quantity = record.Quantity };
    }

    public async Task ConfirmSubstituteAsync(long recordId, long operatorId)
    {
        var record = await _db.SubstituteRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record == null) throw AppException.NotFound($"移库记录 {recordId} 不存在");
        if (record.Status != 1) throw AppException.Business("该记录已处理");

        await TransferOutCoreAsync(record.SubstitutePartId, record.SourceLocationId,
            record.Quantity, operatorId, "SubstituteOut", recordId);
        await AddCoreAsync(record.OriginalPartId, record.TargetLocationId,
            record.Quantity, "", operatorId, "SubstituteIn", recordId);

        record.Status = 2;
        record.ConfirmedAt = DateTime.UtcNow;
        record.ConfirmedBy = operatorId;
        await _db.SaveChangesAsync();
    }

    // ===== Queries =====

    public async Task<object> UpdateAsync(long inventoryId, decimal? totalQty, decimal? availableQty, string? locationCode)
    {
        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.Id == inventoryId);
        if (inv == null) throw AppException.NotFound($"库存记录 {inventoryId} 不存在");

        var oldLocationId = inv.LocationId;
        var oldTotalQty = inv.TotalQty;

        if (totalQty.HasValue) inv.TotalQty = totalQty.Value;
        if (availableQty.HasValue) inv.AvailableQty = availableQty.Value;

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            var newLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode);
            if (newLoc == null) throw AppException.NotFound($"库位 {locationCode} 不存在");
            if (newLoc.Id != oldLocationId)
            {
                var oldLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == oldLocationId);
                if (oldLoc != null) oldLoc.CurrentQty -= oldTotalQty;
                newLoc.CurrentQty += inv.TotalQty;
                inv.LocationId = newLoc.Id;
            }
            else if (totalQty.HasValue)
            {
                // 同库位只改数量
                newLoc.CurrentQty += (inv.TotalQty - oldTotalQty);
            }
        }
        else if (totalQty.HasValue)
        {
            var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == oldLocationId);
            if (loc != null) loc.CurrentQty += (inv.TotalQty - oldTotalQty);
        }

        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var finalLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == inv.LocationId);
        return new { inv.Id, inv.PartId, location_id = inv.LocationId, location_code = finalLoc?.LocationCode ?? "", total_qty = inv.TotalQty, available_qty = inv.AvailableQty, frozen_qty = inv.FrozenQty };
    }

    public async Task<object> QueryAsync(string? partNo, string? locationCode, int page = 1, int pageSize = 20)
    {
        var query = _db.Inventories.AsQueryable();
        if (!string.IsNullOrEmpty(partNo))
        {
            var partIds = _db.Parts.Where(p => p.PartNo.Contains(partNo)).Select(p => p.Id);
            query = query.Where(i => partIds.Contains(i.PartId));
        }
        if (!string.IsNullOrEmpty(locationCode))
        {
            var locIds = _db.WarehouseLocations.Where(l => l.LocationCode.Contains(locationCode)).Select(l => l.Id);
            query = query.Where(i => locIds.Contains(i.LocationId));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(i => i.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var partIdsSet = items.Select(i => i.PartId).Distinct().ToList();
        var locIdsSet = items.Select(i => i.LocationId).Distinct().ToList();
        var partsMap = (await _db.Parts.Where(p => partIdsSet.Contains(p.Id)).ToListAsync()).ToDictionary(p => p.Id);
        var locsMap = (await _db.WarehouseLocations.Where(l => locIdsSet.Contains(l.Id)).ToListAsync()).ToDictionary(l => l.Id);

        var result = items.Select(i => new
        {
            i.Id, part_id = i.PartId,
            part_no = partsMap.TryGetValue(i.PartId, out var p) ? p.PartNo : "",
            part_name = p?.PartName ?? "",
            location_id = i.LocationId,
            location_code = locsMap.TryGetValue(i.LocationId, out var l) ? l.LocationCode : "",
            total_qty = i.TotalQty, available_qty = i.AvailableQty, frozen_qty = i.FrozenQty
        }).ToList();

        return new { total, page, page_size = pageSize, items = result };
    }

    public async Task<List<object>> GetAvailableAsync(long partId)
    {
        var invs = await _db.Inventories.Where(i => i.PartId == partId && i.AvailableQty > 0).ToListAsync();
        var result = new List<object>();
        foreach (var i in invs)
        {
            var p = await _db.Parts.FirstOrDefaultAsync(pr => pr.Id == i.PartId);
            var l = await _db.WarehouseLocations.FirstOrDefaultAsync(lc => lc.Id == i.LocationId);
            result.Add(new
            {
                i.Id, part_id = i.PartId, part_no = p?.PartNo ?? "", part_name = p?.PartName ?? "",
                location_id = i.LocationId, location_code = l?.LocationCode ?? "",
                total_qty = i.TotalQty, available_qty = i.AvailableQty, frozen_qty = i.FrozenQty, inspecting_qty = i.InspectingQty
            });
        }
        return result;
    }

    public async Task<List<FifoLotResult>> GetFifoLotsAsync(long partId, decimal requiredQty)
    {
        var lots = await _db.InventoryLots
            .Where(l => l.PartId == partId && l.Status == 1 && l.Quantity > 0)
            .OrderBy(l => l.ReceiptDate).ToListAsync();

        var accumulated = 0m;
        var result = new List<FifoLotResult>();
        foreach (var lot in lots)
        {
            result.Add(new FifoLotResult
            {
                Id = lot.Id, InventoryId = lot.InventoryId, BatchNo = lot.BatchNo,
                Quantity = lot.Quantity, Status = lot.Status, ReceiptDate = lot.ReceiptDate
            });
            accumulated += lot.Quantity;
            if (accumulated >= requiredQty) break;
        }
        return result;
    }

    // ===== Excel =====

    public async Task<object> ImportFromExcelAsync(byte[] fileBytes, long operatorId)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        var validRows = new List<(int idx, string partNo, string locationCode, string batchNo, decimal qty)>();

        int rowIdx = 2;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var pn = row.Cell(1).GetString().Trim();
            var lc = row.Cell(2).GetString().Trim();
            var bn = row.Cell(3).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(lc)) { rowIdx++; continue; }
            if (!row.Cell(4).TryGetValue(out decimal q) || q <= 0) { rowIdx++; continue; }
            validRows.Add((rowIdx, pn, lc, bn, q));
            rowIdx++;
        }

        var partNos = validRows.Select(r => r.partNo).Distinct().ToList();
        var locCodes = validRows.Select(r => r.locationCode).Distinct().ToList();
        var pmap = (await _db.Parts.Where(p => partNos.Contains(p.PartNo)).ToListAsync()).ToDictionary(p => p.PartNo);
        var lmap = (await _db.WarehouseLocations.Where(l => locCodes.Contains(l.LocationCode)).ToListAsync()).ToDictionary(l => l.LocationCode);

        // 预加载已有库存：同一库位已有不同料号则跳过
        var locIds = lmap.Values.Select(l => l.Id).ToList();
        var existingInvs = await _db.Inventories.Where(i => locIds.Contains(i.LocationId)).ToListAsync();
        var locParts = existingInvs.GroupBy(i => i.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.PartId).ToHashSet());

        int success = 0, skip = 0;
        var details = new List<object>();
        foreach (var r in validRows)
        {
            var reason = "";
            if (!pmap.TryGetValue(r.partNo, out var part)) { reason = "料号不存在"; skip++; }
            else if (!lmap.TryGetValue(r.locationCode, out var loc)) { reason = "库位不存在"; skip++; }
            else if (locParts.TryGetValue(loc.Id, out var existParts) && existParts.Count > 0 && !existParts.Contains(part.Id))
            { reason = $"库位已有其他料号"; skip++; }
            else
            {
                try
                {
                    var batch = string.IsNullOrEmpty(r.batchNo) ? $"BATCH-{DateTime.UtcNow:yyyyMMdd}" : r.batchNo;
                    await AddCoreAsync(part.Id, loc.Id, r.qty, batch, operatorId, "Import", null);
                    success++;
                }
                catch (Exception ex) { reason = ex.Message; skip++; }
            }
            if (!string.IsNullOrEmpty(reason))
                details.Add(new { row = r.idx, part_no = r.partNo, location_code = r.locationCode, reason });
        }

        if (success > 0) await _db.SaveChangesAsync();
        return new { success_count = success, skip_count = skip, details };
    }

    public async Task<byte[]> ExportTemplateAsync()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("库存导入模板");
        ws.Cell(1, 1).Value = "料号";
        ws.Cell(1, 2).Value = "库位编码";
        ws.Cell(1, 3).Value = "批次号";
        ws.Cell(1, 4).Value = "数量";
        ws.Cell(2, 1).Value = "RES-0805-10K";
        ws.Cell(2, 2).Value = "WH-A-01-01-01";
        ws.Cell(2, 3).Value = "B2026001";
        ws.Cell(2, 4).Value = 500;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<object> GetSubstituteListAsync(int page = 1, int pageSize = 20)
    {
        var total = await _db.SubstituteRecords.CountAsync();
        var items = await _db.SubstituteRecords.OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(r => (object)new
            {
                r.Id, original_part_id = r.OriginalPartId, original_part_no = r.OriginalPartNo,
                substitute_part_id = r.SubstitutePartId, substitute_part_no = r.SubstitutePartNo,
                source_location_id = r.SourceLocationId, source_location_code = r.SourceLocationCode,
                target_location_id = r.TargetLocationId, target_location_code = r.TargetLocationCode,
                quantity = r.Quantity, status = r.Status, operator_id = r.OperatorId,
                created_at = r.CreatedAt, confirmed_at = r.ConfirmedAt
            })
        };
    }

    public async Task<object> GetSubstituteByIdAsync(long id)
    {
        var r = await _db.SubstituteRecords.FirstOrDefaultAsync(s => s.Id == id);
        if (r == null) throw AppException.NotFound($"移库记录 {id} 不存在");
        return new
        {
            r.Id, original_part_id = r.OriginalPartId, original_part_no = r.OriginalPartNo,
            substitute_part_id = r.SubstitutePartId, substitute_part_no = r.SubstitutePartNo,
            source_location_id = r.SourceLocationId, source_location_code = r.SourceLocationCode,
            target_location_id = r.TargetLocationId, target_location_code = r.TargetLocationCode,
            quantity = r.Quantity, status = r.Status, operator_id = r.OperatorId,
            created_at = r.CreatedAt, confirmed_at = r.ConfirmedAt
        };
    }

    // ===== Private =====

    private async Task AddLotAsync(Inventory inv, long partId, long locationId, string batchNo, decimal qty)
    {
        var localLot = _db.InventoryLots.Local.FirstOrDefault(l => l.InventoryId == inv.Id && l.BatchNo == batchNo);
        if (localLot != null)
        {
            localLot.Quantity += qty;
            return;
        }
        var lot = await _db.InventoryLots.FirstOrDefaultAsync(l =>
            l.InventoryId == inv.Id && l.BatchNo == batchNo && l.Status == 1);
        if (lot != null)
            lot.Quantity += qty;
        else
            _db.InventoryLots.Add(new InventoryLot
            {
                InventoryId = inv.Id, PartId = partId, LocationId = locationId,
                BatchNo = batchNo, Quantity = qty, Status = 1, ReceiptDate = DateTime.UtcNow,
                OriginType = 1, Inventory = inv
            });
    }
}
