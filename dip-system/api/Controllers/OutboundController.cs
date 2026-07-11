using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/outbound")]
public class OutboundController : ControllerBase
{
    private readonly OutboundService _svc;

    public OutboundController(OutboundService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status, [FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] DateTime? start_date, [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, part_no, location_code, start_date, end_date, page, page_size)));

    [HttpGet("available-parts")]
    public async Task<IActionResult> GetAvailableParts()
        => Ok(ApiResponse.Ok(await _svc.GetAvailablePartsAsync()));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OutboundCreateRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.CreateAsync(
            req.PartId, req.PartNo, req.PartName, req.LocationId, req.LocationCode, req.Quantity, userId), "出库单创建成功"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] OutboundCreateRequest req)
    {
        return Ok(ApiResponse.Ok(await _svc.UpdateAsync(
            id, req.PartId, req.PartNo, req.PartName, req.LocationId, req.LocationCode, req.Quantity), "更新成功"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(long id, [FromBody] OutboundConfirmRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.ConfirmAsync(id, req.Barcode, userId), "出库核销成功"));
    }
}

public class OutboundCreateRequest
{
    public long PartId { get; set; }
    public string PartNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public long LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public decimal Quantity { get; set; }
}

public class OutboundConfirmRequest
{
    public string Barcode { get; set; } = "";
}
