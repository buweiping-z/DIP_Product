using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class ChangeoverService
{
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public ChangeoverService(AppDbContext db) { _db = db; }

    /// <summary>根据产品名称获取 BOM 清单（按月份 fallback）</summary>
    public async Task<List<object>> GetBomByProductNameAsync(string name, string? productionMonth = null)
    {
        var nameLower = name.Trim().ToLower();
        // 优先精确匹配月份，未命中 fallback 通用版本(NULL)
        var boms = await _db.ProductBoms
            .Where(b => b.ProductName != null && b.ProductName.Trim().ToLower() == nameLower && b.PartId > 0
                && b.ProductionMonth == productionMonth)
            .Include(b => b.Part)
            .ToListAsync();
        if (!boms.Any())
        {
            boms = await _db.ProductBoms
                .Where(b => b.ProductName != null && b.ProductName.Trim().ToLower() == nameLower && b.PartId > 0
                    && b.ProductionMonth == null)
                .Include(b => b.Part)
                .ToListAsync();
        }
        return boms.Select(b => (object)new
        {
            part_no = b.Part!.PartNo,
            part_name = b.Part!.PartName,
            required_qty = b.Quantity
        }).ToList();
    }

    /// <summary>根据订单号获取 BOM 清单 + 产品名称：直接从订单的 BomItems 取</summary>
    public async Task<Dictionary<string, object?>> GetBomByOrderNoAsync(string orderNo)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
        if (order == null) throw AppException.NotFound($"订单 {orderNo} 不存在");

        // 取 order_products 的产品名，兜底用 order.ProductName
        var productNames = await _db.OrderProducts
            .Where(op => op.OrderId == order.Id)
            .Select(op => op.ProductName)
            .ToListAsync();
        var displayName = productNames.Any()
            ? string.Join(" / ", productNames)
            : order.ProductName;

        var bomItems = await _db.BomItems
            .Where(b => b.OrderId == order.Id && !b.IsDeleted)
            .Include(b => b.Part)
            .ToListAsync();

        return new Dictionary<string, object?>
        {
            ["product_name"] = displayName,
            ["bom"] = bomItems.Select(b => (object)new
            {
                part_no = b.PartNo,
                part_name = b.Part?.PartName ?? "",
                required_qty = b.RequiredQty
            }).ToList()
        };
    }

    // ─── 批次管理 ───

    /// <summary>获取活跃批次列表（状态=1）</summary>
    public async Task<List<object>> GetActiveBatchesAsync()
    {
        var batches = await _db.ChangeoverBatches
            .Where(b => b.Status == 1)
            .OrderByDescending(b => b.Id)
            .ToListAsync();
        return batches.Select(b => (object)new
        {
            b.Id, b.BatchNo, b.ProductName,
            bom_count = DeserializeBom(b.BomJson).Count,
            scanned_count = DeserializeScanned(b.ScannedJson).Values.Sum()
        }).ToList();
    }

    /// <summary>创建批次（扫码产品名 → 存 BOM）</summary>
    public async Task<object> CreateBatchAsync(string productName, List<BomItemDto> bom, long operatorId)
    {
        var batchNo = $"CO{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var now = DateTime.UtcNow;
        var batch = new ChangeoverBatch
        {
            BatchNo = batchNo,
            ProductName = productName,
            BomJson = JsonSerializer.Serialize(bom, JsonOpts),
            ScannedJson = "{}",
            Status = 1,
            OperatorId = operatorId,
            CreatedAt = now
        };
        _db.ChangeoverBatches.Add(batch);
        await _db.SaveChangesAsync();
        return new { batch.Id, batch.BatchNo, batch.ProductName, bom };
    }

    /// <summary>获取批次详情（含 BOM + 已扫计数）</summary>
    public async Task<object?> GetBatchDetailAsync(string batchNo)
    {
        var b = await _db.ChangeoverBatches.FirstOrDefaultAsync(x => x.BatchNo == batchNo);
        if (b == null) return null;
        var bom = DeserializeBom(b.BomJson);
        var scanned = DeserializeScanned(b.ScannedJson);
        return new
        {
            b.Id, b.BatchNo, b.ProductName, b.Status,
            bom = bom.Select(x => new
            {
                x.PartNo, x.PartName, x.RequiredQty,
                scanned_count = scanned.TryGetValue(x.PartNo, out var c) ? c : 0
            })
        };
    }

    /// <summary>扫描确认一个料号（更新批次进度 + 记录明细）</summary>
    public async Task<object?> ScanAsync(string batchNo, string partNo, long operatorId)
    {
        var b = await _db.ChangeoverBatches.FirstOrDefaultAsync(x => x.BatchNo == batchNo && x.Status == 1);
        if (b == null) return null;

        var scanned = DeserializeScanned(b.ScannedJson);
        scanned[partNo] = scanned.TryGetValue(partNo, out var c) ? c + 1 : 1;
        b.ScannedJson = JsonSerializer.Serialize(scanned, JsonOpts);
        b.UpdatedAt = DateTime.UtcNow;

        // 记录明细
        _db.InlineChangeovers.Add(new InlineChangeover
        {
            BatchNo = batchNo,
            ProductName = b.ProductName,
            PartNo = partNo,
            OperatorId = operatorId,
            ScannedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var bom = DeserializeBom(b.BomJson);
        var allDone = bom.All(x => scanned.TryGetValue(x.PartNo, out var v) && v > 0);
        return new { all_done = allDone, scanned, bom_count = bom.Count, done_count = scanned.Count(x => x.Value > 0) };
    }

    /// <summary>标记批次完成</summary>
    public async Task CompleteBatchAsync(string batchNo)
    {
        var b = await _db.ChangeoverBatches.FirstOrDefaultAsync(x => x.BatchNo == batchNo && x.Status == 1);
        if (b != null) { b.Status = 2; b.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    }

    /// <summary>删除批次及其关联的扫描明细</summary>
    public async Task DeleteBatchAsync(string batchNo)
    {
        var b = await _db.ChangeoverBatches.FirstOrDefaultAsync(x => x.BatchNo == batchNo);
        if (b == null) throw AppException.NotFound($"批次 {batchNo} 不存在");

        // 删除关联的扫描明细
        var records = await _db.InlineChangeovers.Where(x => x.BatchNo == batchNo).ToListAsync();
        _db.InlineChangeovers.RemoveRange(records);

        _db.ChangeoverBatches.Remove(b);
        await _db.SaveChangesAsync();
    }

    // ─── 查询批次列表（所有状态）───

    public async Task<object> GetBatchListAsync(string? productName = null, int page = 1, int pageSize = 20)
    {
        var query = _db.ChangeoverBatches.AsQueryable();
        if (!string.IsNullOrEmpty(productName))
        {
            var pn = productName.ToLower().Trim();
            query = query.Where(b => b.ProductName.ToLower().Trim().Contains(pn));
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(b =>
            {
                var bom = DeserializeBom(b.BomJson);
                var scanned = DeserializeScanned(b.ScannedJson);
                return (object)new
                {
                    b.Id, b.BatchNo, b.ProductName, b.Status,
                    bom_count = bom.Count,
                    scanned_count = scanned.Values.Sum(),
                    created_at = b.CreatedAt
                };
            })
        };
    }

    // ─── 查询记录列表 ───

    public async Task<object> GetListAsync(string? productName = null, string? partNo = null, int page = 1, int pageSize = 20)
    {
        var baseQuery = _db.InlineChangeovers.AsQueryable();
        if (!string.IsNullOrEmpty(productName))
        {
            var pn = productName.ToLower().Trim();
            baseQuery = baseQuery.Where(r => r.ProductName.ToLower().Trim().Contains(pn));
        }
        if (!string.IsNullOrEmpty(partNo))
        {
            var p = partNo.ToLower().Trim();
            baseQuery = baseQuery.Where(r => r.PartNo.ToLower().Trim().Contains(p));
        }

        var query = from r in baseQuery
                    join op in _db.Operators on r.OperatorId equals op.Id into opJoin
                    from op in opJoin.DefaultIfEmpty()
                    orderby r.Id descending
                    select new { Record = r, OperatorName = op != null ? op.Username : "" };
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(x => new
        {
            x.Record.Id, product_name = x.Record.ProductName, part_no = x.Record.PartNo,
            operator_name = x.OperatorName, scanned_at = x.Record.ScannedAt, created_at = x.Record.CreatedAt
        }) };
    }

    public async Task<List<object>> GetPendingBatchesAsync()
    {
        var batches = await _db.ChangeoverBatches
            .Where(b => b.Status == 1)
            .OrderByDescending(b => b.Id)
            .ToListAsync();
        return batches.Select(b =>
        {
            var bom = DeserializeBom(b.BomJson);
            var scanned = DeserializeScanned(b.ScannedJson);
            var doneCount = bom.Count(x => scanned.TryGetValue(x.PartNo, out var v) && v > 0);
            return (object)new
            {
                b.Id, b.BatchNo, b.ProductName,
                total_items = bom.Count, done_items = doneCount,
                created_at = b.CreatedAt
            };
        }).ToList();
    }

    // ─── helpers ───

    private static List<BomItemDto> DeserializeBom(string json)
        => JsonSerializer.Deserialize<List<BomItemDto>>(json, JsonOpts) ?? new();

    private static Dictionary<string, int> DeserializeScanned(string json)
        => JsonSerializer.Deserialize<Dictionary<string, int>>(json, JsonOpts) ?? new();
}

public class BomItemDto
{
    public string PartNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public decimal RequiredQty { get; set; }
}
