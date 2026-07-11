using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/prep")]

public class PrepController : ControllerBase {
    private readonly PrepService _svc;

    public PrepController(PrepService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? status, [FromQuery] long? line_id,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, line_id, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("{id}/details")]
    public async Task<IActionResult> GetDetail(long id) => Ok(ApiResponse.Ok(await _svc.GetDetailAsync(id)));

    [HttpGet("refills")]
    public async Task<IActionResult> GetRefills(
        [FromQuery] string? part_no,
        [FromQuery] string? location_code,
        [FromQuery] DateTime? start_date,
        [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
        => Ok(ApiResponse.Ok(await _svc.GetRefillsAsync(part_no, location_code, start_date, end_date, page, page_size)));

    [HttpPost("{id}/kit-check")]
    public async Task<IActionResult> KitCheck(long id) => Ok(ApiResponse.Ok(await _svc.KitCheckAsync(id)));

    [HttpPost("{id}/scan")]
    public async Task<IActionResult> ScanPrep(long id, [FromBody] ScanPrepRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _svc.ScanPrepAsync(id, req.Barcode, req.DetailId, userId);
        return Ok(ApiResponse.Ok(result, "备料扫描完成"));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.CancelAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "备料单已撤销"));
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingItems() => Ok(ApiResponse.Ok(await _svc.GetPendingItemsAsync()));
}


public class ScanPrepRequest
{
    public string Barcode { get; set; } = "";
    public long? DetailId { get; set; }
}
