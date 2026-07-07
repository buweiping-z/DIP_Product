using ClosedXML.Excel;
using DIP.Application.DTOs.Inventory;
using DIP.Application.Services;
using DIP.Domain.Entities;
using DIP.Infrastructure.Data;
using DIP.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;

namespace DIP.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly DIPDbContext _db;
    private readonly RedisCacheService _redis;

    public InventoryService(DIPDbContext db, RedisCacheService redis)
    {
        _db = db;
        _redis = redis;
    }

    /// <summary>
    /// 从 Excel 导入库存（列：料号 | 库位编码 | 批次号 | 数量）
    /// 第1行为表头（自动跳过）
    /// </summary>
    public async Task<int> ImportInventoryAsync(Stream excelStream, long operatorId)
    {
        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1).ToList();

        if (rows.Count == 0)
            throw new InvalidOperationException("Excel 文件无数据行");

        int count = 0;
        foreach (var row in rows)
        {
            var partNo = row.Cell(1).GetString().Trim();
            var locationCode = row.Cell(2).GetString().Trim();
            var batchNo = row.Cell(3).GetString().Trim();
            var qtyStr = row.Cell(4).GetString().Trim();

            if (string.IsNullOrWhiteSpace(partNo) || string.IsNullOrWhiteSpace(locationCode))
                continue;

            if (!decimal.TryParse(qtyStr, out var qty) || qty <= 0)
                continue;

            // Find or create part
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == partNo);
            if (part == null)
            {
                part = new Part { PartNo = partNo, PartName = partNo, Unit = "PCS", PartType = 1, Status = 1 };
                _db.Parts.Add(part);
                await _db.SaveChangesAsync();
            }

            // Find or create location
            var location = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode);
            if (location == null)
            {
                location = new WarehouseLocation
                {
                    LocationCode = locationCode,
                    Warehouse = "线边仓",
                    Zone = "A",
                    Row = "01",
                    Column = "01",
                    Layer = "01",
                    MaxCapacity = 10000,
                    Status = 1
                };
                _db.WarehouseLocations.Add(location);
                await _db.SaveChangesAsync();
            }

            var batch = string.IsNullOrWhiteSpace(batchNo) ? $"BATCH-{DateTime.UtcNow:yyyyMMdd}" : batchNo;

            try
            {
                await AddAsync(part.Id, location.Id, qty, batch, operatorId, "Import", null);
            }
            catch (Exception ex)
            {
                // Skip individual row errors
                System.Diagnostics.Debug.WriteLine($"Import row error: {ex.Message}");
                continue;
            }
            count++;
        }

        return count;
    }

    private async Task<T> ExecuteWithLockAndRetryAsync<T>(
        string resourceKey,
        Func<Task<T>> action,
        int maxRetries = 3)
    {
        var lockValue = Guid.NewGuid().ToString("N");
        var acquired = await _redis.TryLockAsync(resourceKey, lockValue, TimeSpan.FromSeconds(5));
        if (!acquired)
            throw new InvalidOperationException("库存操作繁忙，请稍后重试");

        try
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var result = await action();
                    return result;
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
                {
                    await Task.Delay(50 * (attempt + 1));
                }
            }
        }
        finally
        {
            await _redis.ReleaseLockAsync(resourceKey, lockValue);
        }

        throw new InvalidOperationException("库存并发冲突，请重试");
    }

    private async Task ExecuteWithLockAndRetryAsync(
        string resourceKey,
        Func<Task> action,
        int maxRetries = 3)
    {
        await ExecuteWithLockAndRetryAsync(resourceKey, async () =>
        {
            await action();
            return 0;
        }, maxRetries);
    }

    // ========================================================================
    //  Add — Core + Facade
    // ========================================================================

    /// <summary>
    /// 纯内存入库：只改 Quantity 和 Inventory 汇总，不调用 SaveChangesAsync。
    /// 供编排方法在显式 IDbContextTransaction 中调用。
    /// </summary>
    internal async Task AddCoreAsync(long partId, long locationId, decimal qty, string batchNo, long operatorId, string referenceType, long? referenceId)
    {
        if (qty <= 0)
            throw new ArgumentException("数量必须大于0", nameof(qty));

        var part = await _db.Parts.FindAsync(partId);
        if (part == null)
            throw new KeyNotFoundException($"部品 {partId} 不存在");

        var inventory = await _db.Inventories
            .FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);

        if (inventory == null)
        {
            inventory = new Inventory
            {
                PartId = partId,
                LocationId = locationId,
                TotalQty = qty,
                AvailableQty = qty,
                FrozenQty = 0,
                InspectingQty = 0
            };
            _db.Inventories.Add(inventory);
            _db.Entry(inventory).Property(i => i.Version).IsModified = false;
        }
        else
        {
            inventory.TotalQty += qty;
            inventory.AvailableQty += qty;
        }

        if (!string.IsNullOrWhiteSpace(batchNo))
        {
            var existingLot = await _db.InventoryLots
                .FirstOrDefaultAsync(l => l.InventoryId == inventory.Id
                    && l.BatchNo == batchNo
                    && l.Status == 1);

            if (existingLot != null)
            {
                existingLot.Quantity += qty;
            }
            else
            {
                var lot = new InventoryLot
                {
                    InventoryId = inventory.Id,
                    PartId = partId,
                    LocationId = locationId,
                    BatchNo = batchNo,
                    Quantity = qty,
                    Status = 1,
                    ReceiptDate = DateTime.UtcNow,
                    OriginType = 1
                };
                _db.InventoryLots.Add(lot);
            }
        }

        var movement = new StockMovement
        {
            PartId = partId,
            PartNo = part.PartNo,
            LocationId = locationId,
            BatchNo = batchNo,
            MovementType = referenceType == "ReturnIn" ? 4 : 1,
            Quantity = qty,
            BalanceAfter = inventory.TotalQty,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OperatorId = operatorId
        };
        _db.StockMovements.Add(movement);

        var location = await _db.WarehouseLocations.FindAsync(locationId);
        if (location != null)
        {
            location.CurrentQty += qty;
        }
    }

    public async Task AddAsync(long partId, long locationId, decimal qty, string batchNo, long operatorId, string referenceType, long? referenceId)
    {
        var lockKey = $"inv:{partId}:{locationId}";
        await ExecuteWithLockAndRetryAsync(lockKey, async () =>
        {
            await AddCoreAsync(partId, locationId, qty, batchNo, operatorId, referenceType, referenceId);
            await _db.SaveChangesAsync();
        });
    }

    // ========================================================================
    //  Freeze — Core + Facade
    // ========================================================================

    /// <summary>
    /// 纯内存冻结：只改 Status（2=Frozen）和 Inventory 汇总，不扣减 Quantity，
    /// 不调用 SaveChangesAsync。供编排方法在显式 IDbContextTransaction 中调用。
    /// </summary>
    internal async Task FreezeCoreAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0)
            throw new ArgumentException("数量必须大于0", nameof(qty));

        var part = await _db.Parts.FindAsync(partId);
        if (part == null)
            throw new KeyNotFoundException($"部品 {partId} 不存在");

        var inventory = await _db.Inventories
            .FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);

        if (inventory == null || inventory.AvailableQty < qty)
            throw new InvalidOperationException("可用库存不足");

        inventory.AvailableQty -= qty;
        inventory.FrozenQty += qty;
        inventory.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inventory.Id && l.Status == 1)
            .OrderBy(l => l.ReceiptDate)
            .ToListAsync();

        decimal remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            lot.Status = 2;  // 仅冻结，数量不变
            lot.Version++;
            remaining -= lot.Quantity;
        }

        if (remaining > 0)
            throw new InvalidOperationException("可用批次不足");

        var movement = new StockMovement
        {
            PartId = partId,
            PartNo = part.PartNo,
            LocationId = locationId,
            MovementType = 2,
            Quantity = qty,
            BalanceAfter = inventory.TotalQty,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OperatorId = operatorId
        };
        _db.StockMovements.Add(movement);
    }

    public async Task FreezeAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        var lockKey = $"inv:{partId}:{locationId}";
        await ExecuteWithLockAndRetryAsync(lockKey, async () =>
        {
            await FreezeCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
            await _db.SaveChangesAsync();
        });
    }

    // ========================================================================
    //  Deduct — Core + Facade
    // ========================================================================

    /// <summary>
    /// 纯内存出库：扣减 lot.Quantity 和 Inventory 汇总，不调用 SaveChangesAsync。
    /// 仅从已冻结（Status=2）批次中扣除。供编排方法在显式 IDbContextTransaction 中调用。
    /// </summary>
    internal async Task DeductCoreAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0)
            throw new ArgumentException("数量必须大于0", nameof(qty));

        var part = await _db.Parts.FindAsync(partId);
        if (part == null)
            throw new KeyNotFoundException($"部品 {partId} 不存在");

        var inventory = await _db.Inventories
            .FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);

        if (inventory == null || inventory.FrozenQty < qty)
            throw new InvalidOperationException("冻结库存不足");

        inventory.FrozenQty -= qty;
        inventory.TotalQty -= qty;
        inventory.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inventory.Id && l.Status == 2)
            .OrderBy(l => l.ReceiptDate)
            .ToListAsync();

        decimal remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            var deductFromLot = Math.Min(remaining, lot.Quantity);
            lot.Quantity -= deductFromLot;
            lot.Version++;
            if (lot.Quantity <= 0)
                lot.Status = 3; // 已消耗
            remaining -= deductFromLot;
        }

        var movement = new StockMovement
        {
            PartId = partId,
            PartNo = part.PartNo,
            LocationId = locationId,
            MovementType = 3,
            Quantity = qty,
            BalanceAfter = inventory.TotalQty,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OperatorId = operatorId
        };
        _db.StockMovements.Add(movement);

        var location = await _db.WarehouseLocations.FindAsync(locationId);
        if (location != null)
        {
            location.CurrentQty -= qty;
        }
    }

    public async Task DeductAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        var lockKey = $"inv:{partId}:{locationId}";
        await ExecuteWithLockAndRetryAsync(lockKey, async () =>
        {
            await DeductCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
            await _db.SaveChangesAsync();
        });
    }

    // ========================================================================
    //  Thaw — Core + Facade
    // ========================================================================

    /// <summary>
    /// 纯内存解冻：恢复 Status（1=Available）和 Inventory 汇总，不调用 SaveChangesAsync。
    /// 不修改 Quantity（freeze 时未扣减）。供编排方法在显式 IDbContextTransaction 中调用。
    /// </summary>
    internal async Task ThawCoreAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        if (qty <= 0)
            throw new ArgumentException("数量必须大于0", nameof(qty));

        var part = await _db.Parts.FindAsync(partId);
        if (part == null)
            throw new KeyNotFoundException($"部品 {partId} 不存在");

        var inventory = await _db.Inventories
            .FirstOrDefaultAsync(i => i.PartId == partId && i.LocationId == locationId);

        if (inventory == null || inventory.FrozenQty < qty)
            throw new InvalidOperationException("冻结库存不足");

        inventory.FrozenQty -= qty;
        inventory.AvailableQty += qty;
        inventory.Version++;

        var lots = await _db.InventoryLots
            .Where(l => l.InventoryId == inventory.Id && l.Status == 2)
            .OrderBy(l => l.ReceiptDate)
            .ToListAsync();

        decimal remaining = qty;
        foreach (var lot in lots)
        {
            if (remaining <= 0) break;
            lot.Status = 1;  // 恢复为可用
            lot.Version++;
            // 不需要改 Quantity（freeze 时就没改）
            remaining -= lot.Quantity;
        }

        var movement = new StockMovement
        {
            PartId = partId,
            PartNo = part.PartNo,
            LocationId = locationId,
            MovementType = 8,
            Quantity = qty,
            BalanceAfter = inventory.TotalQty,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OperatorId = operatorId
        };
        _db.StockMovements.Add(movement);
    }

    public async Task ThawAsync(long partId, long locationId, decimal qty, long operatorId, string referenceType, long referenceId)
    {
        var lockKey = $"inv:{partId}:{locationId}";
        await ExecuteWithLockAndRetryAsync(lockKey, async () =>
        {
            await ThawCoreAsync(partId, locationId, qty, operatorId, referenceType, referenceId);
            await _db.SaveChangesAsync();
        });
    }

    // ========================================================================
    //  Query Methods
    // ========================================================================

    public async Task<List<InventoryDto>> GetAvailableAsync(long partId)
    {
        var part = await _db.Parts.FindAsync(partId);
        if (part == null)
            throw new KeyNotFoundException($"部品 {partId} 不存在");

        return await _db.Inventories
            .Where(i => i.PartId == partId && i.AvailableQty > 0)
            .Join(_db.WarehouseLocations, i => i.LocationId, l => l.Id, (i, l) => new InventoryDto(
                i.Id, i.PartId, part.PartNo, part.PartName,
                i.LocationId, l.LocationCode,
                i.TotalQty, i.AvailableQty, i.FrozenQty, i.InspectingQty))
            .ToListAsync();
    }

    public async Task<InventoryDto?> GetByIdAsync(long inventoryId)
    {
        var result = await _db.Inventories
            .Where(i => i.Id == inventoryId)
            .Join(_db.Parts, i => i.PartId, p => p.Id, (i, p) => new { inv = i, part = p })
            .Join(_db.WarehouseLocations, x => x.inv.LocationId, l => l.Id, (x, l) => new InventoryDto(
                x.inv.Id, x.inv.PartId, x.part.PartNo, x.part.PartName,
                x.inv.LocationId, l.LocationCode,
                x.inv.TotalQty, x.inv.AvailableQty, x.inv.FrozenQty, x.inv.InspectingQty))
            .FirstOrDefaultAsync();
        return result;
    }

    public async Task<List<InventoryDto>> GetByLocationAsync(long locationId, int page, int pageSize)
    {
        var location = await _db.WarehouseLocations.FindAsync(locationId);
        if (location == null)
            throw new KeyNotFoundException($"库位 {locationId} 不存在");

        return await _db.Inventories
            .Where(i => i.LocationId == locationId)
            .OrderByDescending(i => i.TotalQty)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(_db.Parts, i => i.PartId, p => p.Id, (i, p) => new InventoryDto(
                i.Id, i.PartId, p.PartNo, p.PartName,
                i.LocationId, location.LocationCode,
                i.TotalQty, i.AvailableQty, i.FrozenQty, i.InspectingQty))
            .ToListAsync();
    }

    public async Task<List<InventoryLotDto>> GetFifoLotsAsync(long partId, decimal requiredQty)
    {
        var lots = await _db.InventoryLots
            .Where(l => l.PartId == partId && l.Status == 1 && l.Quantity > 0)
            .OrderBy(l => l.ReceiptDate)
            .Select(l => new InventoryLotDto(
                l.Id, l.InventoryId, l.BatchNo, l.Quantity,
                l.Status, l.ReceiptDate, l.ExpiryDate, l.MSLExposureTime))
            .ToListAsync();

        decimal accumulated = 0;
        var result = new List<InventoryLotDto>();
        foreach (var lot in lots)
        {
            result.Add(lot);
            accumulated += lot.Quantity;
            if (accumulated >= requiredQty) break;
        }

        return result;
    }
}
