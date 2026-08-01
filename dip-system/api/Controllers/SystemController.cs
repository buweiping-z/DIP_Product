using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var role = User.FindFirstValue("role")?.ToLower();
        if (role != "admin" && role != "leader")
            return Ok(new { code = 403, data = (object?)null, message = "仅管理员可执行此操作" });
        var tables = new[]
        {
            "bom_items", "order_products", "order_closures",
            "prep_scan_records", "prep_details", "prep_orders", "production_orders",
            "loading_batch_items", "loading_batches", "material_loadings",
            "online_confirms",
            "return_order_items", "return_orders",
            "transfer_order_items", "transfer_orders",
            "substitute_details", "substitute_orders", "substitute_records",
            "outbound_details", "outbound_orders",
            "abnormal_records",
            "stock_count_items", "stock_counts",
            "scan_records", "system_logs", "refill_records",
            "stock_movements", "inventory_lots",
            "inline_changeovers", "changeover_batches",
            "refresh_tokens",
        };

        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=0");
            foreach (var t in tables)
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM `" + t + "`");
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=1");
            return Ok(new { code = 0, data = new { remaining = 0 }, message = "业务数据已清空，基础数据已保留" });
        }
        catch (Exception ex)
        {
            return Ok(new { code = 500, data = (object?)null, message = $"清空失败: {ex.InnerException?.Message ?? ex.Message}" });
        }
    }
}
