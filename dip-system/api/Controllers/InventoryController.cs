using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/inventory")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _svc;

    public InventoryController(InventoryService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.QueryAsync(part_no, location_code, page, page_size)));

    [HttpGet("available/{partId}")]
    public async Task<IActionResult> GetAvailable(long partId)
        => Ok(ApiResponse.Ok(await _svc.GetAvailableAsync(partId)));

    [HttpGet("lots/{partId}")]
    public async Task<IActionResult> GetFifoLots(long partId, [FromQuery] decimal required_qty)
        => Ok(ApiResponse.Ok(await _svc.GetFifoLotsAsync(partId, required_qty)));

    [AllowAnonymous]
    [HttpGet("template")]
    public async Task<IActionResult> Template()
    {
        var bytes = await _svc.ExportTemplateAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "inventory_template.xlsx");
    }

    [HttpGet("check-location")]
    public async Task<IActionResult> CheckLocation([FromQuery] string location_code, [FromQuery] long part_id)
        => Ok(ApiResponse.Ok(await _svc.CheckLocationAsync(location_code, part_id)));

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var result = await _svc.ImportFromExcelAsync(ms.ToArray(), userId);
        return Ok(ApiResponse.Ok(result, "导入完成"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateInventoryRequest req)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.TotalQty, req.AvailableQty, req.LocationCode), "更新成功"));

    [HttpGet("substitute")]
    public async Task<IActionResult> GetSubstituteList([FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetSubstituteListAsync(page, page_size)));

    [HttpGet("substitute/{id}")]
    public async Task<IActionResult> GetSubstitute(long id)
        => Ok(ApiResponse.Ok(await _svc.GetSubstituteByIdAsync(id)));

    [HttpPost("substitute")]
    public async Task<IActionResult> Substitute([FromBody] SubstituteRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "1");
        var result = await _svc.SubstituteCoreAsync(req.OriginalPartId, req.SubstitutePartId,
            req.SourceLocationId, req.TargetLocationId, req.Quantity, userId);
        return Ok(ApiResponse.Ok(result, "替代料移库已创建，待确认"));
    }

    [HttpPost("substitute/{recordId}/confirm")]
    public async Task<IActionResult> ConfirmSubstitute(long recordId)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "1");
        await _svc.ConfirmSubstituteAsync(recordId, userId);
        return Ok(ApiResponse.Ok(null, "替代料移库已确认"));
    }

    [HttpDelete("substitute/{id}")]
    public async Task<IActionResult> DeleteSubstitute(long id)
    {
        await _svc.DeleteSubstituteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }
}

public class UpdateInventoryRequest
{
    public decimal? TotalQty { get; set; }
    public decimal? AvailableQty { get; set; }
    public string? LocationCode { get; set; }
}

public class SubstituteRequest
{
    public long OriginalPartId { get; set; }
    public long SubstitutePartId { get; set; }
    public long SourceLocationId { get; set; }
    public long TargetLocationId { get; set; }
    public decimal Quantity { get; set; }
}
