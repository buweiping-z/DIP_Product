using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/refill")]
public class RefillController : ControllerBase
{
    private readonly RefillService _svc;

    public RefillController(RefillService svc) { _svc = svc; }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive() => Ok(ApiResponse.Ok(await _svc.GetActiveBatchesAsync()));

    [HttpGet("batch/{batchNo}")]
    public async Task<IActionResult> GetBatch(string batchNo) => Ok(ApiResponse.Ok(await _svc.GetBatchDetailAsync(batchNo)));

    [HttpGet("parts")]
    public async Task<IActionResult> GetParts([FromQuery] string product_name)
        => Ok(ApiResponse.Ok(await _svc.GetPartsByProductAsync(product_name)));

    [HttpGet]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] DateTime? start_date, [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 50)
        => Ok(ApiResponse.Ok(await _svc.GetRecordsAsync(part_no, location_code, start_date, end_date, page, page_size)));

    [HttpPost("batch-start")]
    public async Task<IActionResult> BatchStart([FromBody] RefillBatchStartRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.BatchStartAsync(req.Items, req.BatchNo, userId)));
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] RefillScanRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.ScanAsync(
            req.PrepDetailId, req.PrepOrderId, req.PartNo, req.PartName,
            req.LocationCode, req.Barcode, req.BatchNo, req.Step, userId)));
    }
}

public class RefillBatchStartRequest
{
    public List<RefillStartItem> Items { get; set; } = new();
    public string BatchNo { get; set; } = "";
}

public class RefillScanRequest
{
    public long PrepDetailId { get; set; }
    public long PrepOrderId { get; set; }
    public string PartNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public string LocationCode { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public int Step { get; set; }
}
