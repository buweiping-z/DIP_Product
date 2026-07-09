using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/shelving")]
public class ShelvingController : ControllerBase
{
    private readonly ShelvingService _svc;

    public ShelvingController(ShelvingService svc) { _svc = svc; }

    [HttpGet("batch")]
    public async Task<IActionResult> GetList([FromQuery] int? status, [FromQuery] long? location_id,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetBatchListAsync(status, location_id, page, page_size)));

    [HttpGet("batch/{id}")]
    public async Task<IActionResult> GetBatch(long id) => Ok(ApiResponse.Ok(await _svc.GetBatchAsync(id)));

    [HttpPost("batch")]
    [Authorize]
    public async Task<IActionResult> CreateBatch([FromBody] CreateBatchRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.CreateBatchAsync(req.TargetLocationId, userId), "上架批次创建成功"));
    }

    [HttpPost("batch/{id}/scan")]
    [Authorize]
    public async Task<IActionResult> ScanItem(long id, [FromBody] ScanItemRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.AddItemAsync(id, req.Barcode, userId)));
    }

    [HttpPost("batch/{id}/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmBatch(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.ConfirmBatchAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "上架批次已确认"));
    }

    [HttpPost("batch/{id}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelBatch(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.CancelBatchAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "上架批次已撤销"));
    }

    [HttpPost("direct")]
    [Authorize]
    public async Task<IActionResult> DirectShelving([FromBody] DirectShelvingRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.DirectShelvingAsync(req.Barcode, req.TargetLocationCode, req.Quantity, userId), "上架成功"));
    }

    [HttpGet("records")]
    [Authorize]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? part_name,
        [FromQuery] string? location_code,
        [FromQuery] DateTime? start_date,
        [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetRecordsAsync(part_name, location_code, start_date, end_date, page, page_size)));
}

public class CreateBatchRequest { public long TargetLocationId { get; set; } }
public class ScanItemRequest { public string Barcode { get; set; } = ""; }
public class DirectShelvingRequest
{
    public string Barcode { get; set; } = "";
    public string TargetLocationCode { get; set; } = "";
    public decimal Quantity { get; set; }
}
