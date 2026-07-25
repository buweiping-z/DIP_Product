using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class SubstituteService
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _sp;
    public SubstituteService(AppDbContext db, IServiceProvider sp) { _db = db; _sp = sp; }

    private string GenOrderNo()
    {
        var now = DateTime.UtcNow.AddHours(8); // 北京时间
        return $"SUB-{now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
    }

    private static object ToDict(SubstituteOrder o) => new
    {
        o.Id, order_no = o.OrderNo, o.Status, o.OperatorId,
        detail_count = o.Details.Count(d => !d.IsDeleted),
        confirmed_count = o.Details.Count(d => !d.IsDeleted && d.Status == 2),
        created_at = o.CreatedAt
    };

    // ===== 查询 =====

    public async Task<object> GetListAsync(int? status = null, string? search = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 20)
    {
        var query = _db.SubstituteOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(o => o.Details.Any(d =>
                d.SubstitutePartNo.ToLower().Contains(s) ||
                d.OriginalPartNo.ToLower().Contains(s)));
        }
        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt <= endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Details)
            .ToListAsync();
        return new { total, page, page_size = pageSize,
            items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long id)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw AppException.NotFound($"替代料移库订单 {id} 不存在");

        return new
        {
            order.Id, order_no = order.OrderNo, order.Status, order.OperatorId,
            detail_count = order.Details.Count(d => !d.IsDeleted),
            confirmed_count = order.Details.Count(d => !d.IsDeleted && d.Status == 2),
            created_at = order.CreatedAt,
            details = order.Details.OrderBy(d => d.SourceLocationCode).Select(d => new
            {
                d.Id, d.OrderId,
                original_part_id = d.OriginalPartId, original_part_no = d.OriginalPartNo,
                substitute_part_id = d.SubstitutePartId, substitute_part_no = d.SubstitutePartNo,
                source_location_id = d.SourceLocationId, source_location_code = d.SourceLocationCode,
                target_location_id = d.TargetLocationId, target_location_code = d.TargetLocationCode,
                d.Quantity, d.Status
            })
        };
    }

    /// <summary>
    /// 手机端用：获取待确认明细列表（按来源库位排序）
    /// </summary>
    public async Task<object> GetDetailsAsync(long orderId)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        return new
        {
            order_id = order.Id, order_no = order.OrderNo, order.Status,
            confirmed_count = order.Details.Count(d => !d.IsDeleted && d.Status == 2),
            total_count = order.Details.Count(d => !d.IsDeleted),
            // 按来源库位排序，一趟走完
            details = order.Details.OrderBy(d => d.SourceLocationCode).Select(d => new
            {
                d.Id, d.OrderId,
                original_part_no = d.OriginalPartNo,
                substitute_part_no = d.SubstitutePartNo,
                source_location_code = d.SourceLocationCode,
                target_location_code = d.TargetLocationCode,
                d.Quantity, d.Status
            })
        };
    }

    // ===== 创建订单 =====

    public async Task<object> CreateAsync(List<SubstituteDetailInput> details, long operatorId)
    {
        if (details == null || details.Count == 0)
            throw AppException.Business("至少需要一条明细");

        var order = new SubstituteOrder
        {
            OrderNo = GenOrderNo(), Status = 1, OperatorId = operatorId
        };
        _db.SubstituteOrders.Add(order);
        await _db.SaveChangesAsync();

        foreach (var d in details)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.OriginalPartId)
                ?? throw AppException.NotFound($"缺料部品 {d.OriginalPartId} 不存在");
            var subPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.SubstitutePartId)
                ?? throw AppException.NotFound($"替代部品 {d.SubstitutePartId} 不存在");
            var srcLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.SourceLocationId)
                ?? throw AppException.NotFound($"来源库位 {d.SourceLocationId} 不存在");
            var tgtLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.TargetLocationId)
                ?? throw AppException.NotFound($"目标库位 {d.TargetLocationId} 不存在");

            if (d.OriginalPartId == d.SubstitutePartId && d.SourceLocationId == d.TargetLocationId)
                throw AppException.Business("来源和目标不能完全相同");

            _db.SubstituteDetails.Add(new SubstituteDetail
            {
                OrderId = order.Id,
                OriginalPartId = d.OriginalPartId, OriginalPartNo = part.PartNo,
                SubstitutePartId = d.SubstitutePartId, SubstitutePartNo = subPart.PartNo,
                SourceLocationId = d.SourceLocationId, SourceLocationCode = srcLoc.LocationCode,
                TargetLocationId = d.TargetLocationId, TargetLocationCode = tgtLoc.LocationCode,
                Quantity = d.Quantity, Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(order.Id);
    }

    // ===== 编辑订单（仅 status=1 的订单，仅未确认明细可修改）=====

    public async Task<object> UpdateAsync(long orderId, List<SubstituteDetailInput> newDetails)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("仅待确认订单可编辑");

        // 删除未确认明细
        var unconfirmedDetails = order.Details.Where(d => d.Status == 1).ToList();
        _db.SubstituteDetails.RemoveRange(unconfirmedDetails);

        // 追加新明细
        foreach (var d in newDetails)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.OriginalPartId)
                ?? throw AppException.NotFound($"缺料部品 {d.OriginalPartId} 不存在");
            var subPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.SubstitutePartId)
                ?? throw AppException.NotFound($"替代部品 {d.SubstitutePartId} 不存在");
            var srcLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.SourceLocationId)
                ?? throw AppException.NotFound($"来源库位 {d.SourceLocationId} 不存在");
            var tgtLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.TargetLocationId)
                ?? throw AppException.NotFound($"目标库位 {d.TargetLocationId} 不存在");

            _db.SubstituteDetails.Add(new SubstituteDetail
            {
                OrderId = order.Id,
                OriginalPartId = d.OriginalPartId, OriginalPartNo = part.PartNo,
                SubstitutePartId = d.SubstitutePartId, SubstitutePartNo = subPart.PartNo,
                SourceLocationId = d.SourceLocationId, SourceLocationCode = srcLoc.LocationCode,
                TargetLocationId = d.TargetLocationId, TargetLocationCode = tgtLoc.LocationCode,
                Quantity = d.Quantity, Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(order.Id);
    }

    // ===== 取消订单 =====

    public async Task<object> CancelAsync(long orderId)
    {
        var order = await _db.SubstituteOrders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("仅待确认订单可取消");

        order.Status = 3;
        // 已确认明细回退为待确认
        var confirmedDetails = await _db.SubstituteDetails
            .Where(d => d.OrderId == orderId && d.Status == 2).ToListAsync();
        foreach (var d in confirmedDetails)
            d.Status = 1;

        await _db.SaveChangesAsync();

        return new { order_id = orderId, status = 3 };
    }

    // ===== 扫码确认单条明细（不执行移库）=====

    public async Task<object> ConfirmDetailAsync(long orderId, long detailId)
    {
        var detail = await _db.SubstituteDetails
            .FirstOrDefaultAsync(d => d.Id == detailId && d.OrderId == orderId)
            ?? throw AppException.NotFound($"明细 {detailId} 不存在或不属于此订单");

        if (detail.Status != 1)
            throw AppException.Business(detail.Status == 2 ? "该明细已确认" : "该明细状态异常");

        detail.Status = 2;
        await _db.SaveChangesAsync();

        return new { detail_id = detail.Id, confirmed = true };
    }

    // ===== 全部确认完成：执行移库 =====

    public async Task<object> ConfirmAllAsync(long orderId, long operatorId)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("订单状态不允许确认");

        var details = order.Details.Where(d => !d.IsDeleted).ToList();
        var allConfirmed = details.All(d => d.Status == 2);
        if (!allConfirmed)
            throw AppException.Business($"尚有 {details.Count(d => d.Status == 1)} 条明细未确认");

        var invSvc = _sp.GetRequiredService<InventoryService>();

        // 数据库事务：任何一条失败全部回滚
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var d in details)
            {
                // 替代料出库（扣减可用库存）
                await invSvc.TransferOutCoreAsync(d.SubstitutePartId, d.SourceLocationId,
                    d.Quantity, operatorId, "SubstituteOut", d.Id);
                // 缺料入库（移入目标库位）
                await invSvc.AddCoreAsync(d.OriginalPartId, d.TargetLocationId,
                    d.Quantity, "", operatorId, "SubstituteIn", d.Id);
            }

            order.Status = 2;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // 刷新订单冻结库存
        await _sp.GetRequiredService<OrderService>().RefreezeActiveOrdersAsync(operatorId);

        return new { order_id = order.Id, status = 2, message = "移库完成" };
    }

    // ===== 导出 Excel =====

    public async Task<byte[]> ExportAsync(string? search = null)
    {
        var query = _db.SubstituteOrders
            .Include(o => o.Details)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(o => o.Details.Any(d =>
                d.SubstitutePartNo.ToLower().Contains(s) ||
                d.OriginalPartNo.ToLower().Contains(s)));
        }

        var orders = await query.OrderByDescending(o => o.Id).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("替代料移库明细");
        // 表头
        ws.Cell(1, 1).Value = "订单号";
        ws.Cell(1, 2).Value = "订单状态";
        ws.Cell(1, 3).Value = "替代料号";
        ws.Cell(1, 4).Value = "来源库位";
        ws.Cell(1, 5).Value = "缺料料号";
        ws.Cell(1, 6).Value = "目标库位";
        ws.Cell(1, 7).Value = "数量";
        ws.Cell(1, 8).Value = "明细状态";
        ws.Cell(1, 9).Value = "创建时间";

        int row = 2;
        var statusMap = new Dictionary<int, string> { { 1, "待确认" }, { 2, "已完成" }, { 3, "已取消" } };
        foreach (var order in orders)
        {
            foreach (var d in order.Details.Where(d => !d.IsDeleted).OrderBy(d => d.SourceLocationCode))
            {
                ws.Cell(row, 1).Value = order.OrderNo;
                ws.Cell(row, 2).Value = statusMap.GetValueOrDefault(order.Status, order.Status.ToString());
                ws.Cell(row, 3).Value = d.SubstitutePartNo;
                ws.Cell(row, 4).Value = d.SourceLocationCode;
                ws.Cell(row, 5).Value = d.OriginalPartNo;
                ws.Cell(row, 6).Value = d.TargetLocationCode;
                ws.Cell(row, 7).Value = (double)d.Quantity;
                ws.Cell(row, 8).Value = d.Status == 2 ? "已确认" : "待确认";
                ws.Cell(row, 9).Value = order.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public class SubstituteDetailInput
{
    public long OriginalPartId { get; set; }
    public long SubstitutePartId { get; set; }
    public long SourceLocationId { get; set; }
    public long TargetLocationId { get; set; }
    public decimal Quantity { get; set; }
}
