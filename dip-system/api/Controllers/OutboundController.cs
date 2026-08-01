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

    private long GetUserId() => long.Parse(User.FindFirstValue("nameid")!);

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status, [FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] DateTime? start_date, [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, part_no, location_code, start_date, end_date, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
        => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("available-parts")]
    public async Task<IActionResult> GetAvailableParts()
        => Ok(ApiResponse.Ok(await _svc.GetAvailablePartsAsync()));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OutboundCreateRequest req)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(req.Details, GetUserId()), "出库单创建成功"));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] OutboundCreateRequest req)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.Details), "更新成功"));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPost("{id}/details/{detailId}/confirm")]
    public async Task<IActionResult> ConfirmDetail(long id, long detailId, [FromBody] OutboundConfirmRequest req)
        => Ok(ApiResponse.Ok(await _svc.ConfirmDetailAsync(id, detailId, req.Barcode, GetUserId()), "核销成功"));

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> ConfirmAll(long id)
        => Ok(ApiResponse.Ok(await _svc.ConfirmAllAsync(id), "出库整单完成"));
}

// ===== DTOs =====

public class OutboundCreateRequest
{
    public List<OutboundService.OutboundDetailInput> Details { get; set; } = new();
}

public class OutboundConfirmRequest
{
    public string Barcode { get; set; } = "";
}
