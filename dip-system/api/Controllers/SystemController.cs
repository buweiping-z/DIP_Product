using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Data;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/system")]
public class SystemController : ControllerBase
{
    private readonly AppDbContext _db;

    public SystemController(AppDbContext db) { _db = db; }

    /// <summary>
    /// 一键清空业务数据（保留基础数据：零件/库位/产线/工位/用户/角色/库存/BOM模板）
    /// </summary>
    [HttpPost("clear-data")]
    public async Task<IActionResult> ClearData()
    {
        var role = User.FindFirstValue("role");
        if (role != "admin")
            return Ok(new { code = 403, data = (object?)null, message = "仅管理员可执行此操作" });
        // 订单 + BOM 明细 + 订单产品关联
        _db.BomItems.RemoveRange(_db.BomItems);
        _db.OrderProducts.RemoveRange(_db.OrderProducts);
        _db.OrderClosures.RemoveRange(_db.OrderClosures);
        // 备料相关
        _db.PrepScanRecords.RemoveRange(_db.PrepScanRecords);
        _db.PrepDetails.RemoveRange(_db.PrepDetails);
        _db.PrepOrders.RemoveRange(_db.PrepOrders);
        _db.ProductionOrders.RemoveRange(_db.ProductionOrders);
        // 上架
        _db.ShelvingBatchItems.RemoveRange(_db.ShelvingBatchItems);
        _db.ShelvingBatches.RemoveRange(_db.ShelvingBatches);
        _db.MaterialShelvings.RemoveRange(_db.MaterialShelvings);
        // 上线确认
        _db.OnlineConfirms.RemoveRange(_db.OnlineConfirms);
        // 退料
        _db.ReturnOrderItems.RemoveRange(_db.ReturnOrderItems);
        _db.ReturnOrders.RemoveRange(_db.ReturnOrders);
        // 移库
        _db.TransferOrderItems.RemoveRange(_db.TransferOrderItems);
        _db.TransferOrders.RemoveRange(_db.TransferOrders);
        // 替代料
        _db.SubstituteDetails.RemoveRange(_db.SubstituteDetails);
        _db.SubstituteOrders.RemoveRange(_db.SubstituteOrders);
        _db.SubstituteRecords.RemoveRange(_db.SubstituteRecords);
        // 出库
        _db.OutboundDetails.RemoveRange(_db.OutboundDetails);
        _db.OutboundOrders.RemoveRange(_db.OutboundOrders);
        // 异常记录
        _db.AbnormalRecords.RemoveRange(_db.AbnormalRecords);
        // 盘点
        _db.StockCountItems.RemoveRange(_db.StockCountItems);
        _db.StockCounts.RemoveRange(_db.StockCounts);
        // 日志 + 扫码 + 补货
        _db.ScanRecords.RemoveRange(_db.ScanRecords);
        _db.SystemLogs.RemoveRange(_db.SystemLogs);
        _db.RefillRecords.RemoveRange(_db.RefillRecords);

        // 库存流水（库存主数据保留，流水清空）
        _db.StockMovements.RemoveRange(_db.StockMovements);

        await _db.SaveChangesAsync();

        var count =
            _db.ProductionOrders.Count() + _db.PrepOrders.Count() +
            _db.ShelvingBatches.Count() + _db.OnlineConfirms.Count() +
            _db.ReturnOrders.Count() + _db.TransferOrders.Count() +
            _db.SubstituteOrders.Count() + _db.OutboundOrders.Count() +
            _db.AbnormalRecords.Count() + _db.StockCounts.Count();

        return Ok(new { code = 0, data = new { remaining = count }, message = "业务数据已清空，基础数据已保留" });
    }
}
