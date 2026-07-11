using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/online")]

public class OnlineController : ControllerBase {
    private readonly OnlineService _svc;

    public OnlineController(OnlineService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? part_no,
        [FromQuery] string? station_no,
        [FromQuery] DateTime? start_date,
        [FromQuery] DateTime? end_date,
        [FromQuery] long? prep_order_id,
        [FromQuery] long? part_id,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(part_no, station_no, start_date, end_date,
            prep_order_id, part_id, page, page_size)));

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] OnlineConfirmRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.ConfirmAsync(req.DetailId, req.Barcode, req.Quantity,
            req.StationId, req.EquipmentId, userId)));
    }
}


public class OnlineConfirmRequest
{
    public long DetailId { get; set; }
    public string Barcode { get; set; } = "";
    public decimal Quantity { get; set; }
    public long? StationId { get; set; }
    public long? EquipmentId { get; set; }
}
