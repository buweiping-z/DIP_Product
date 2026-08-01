using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/substitute/orders")]
public class SubstituteController : ControllerBase
{
    private readonly SubstituteService _svc;

    public SubstituteController(SubstituteService svc) { _svc = svc; }

    private long GetUserId() => long.Parse(User.FindFirstValue("nameid") ?? "0");

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status, [FromQuery] string? search,
        [FromQuery] DateTime? start_date, [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, search, start_date, end_date, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
        => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("{id}/details")]
    public async Task<IActionResult> GetDetails(long id)
        => Ok(ApiResponse.Ok(await _svc.GetDetailsAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubstituteOrderRequest req)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(req.Details, GetUserId()), "替代料移库订单已创建"));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] CreateSubstituteOrderRequest req)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.Details), "订单已更新"));

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        await _svc.CancelAsync(id);
        return Ok(ApiResponse.Ok(null, "订单已取消"));
    }

    [HttpPost("{id}/details/{detailId}/confirm")]
    public async Task<IActionResult> ConfirmDetail(long id, long detailId)
        => Ok(ApiResponse.Ok(await _svc.ConfirmDetailAsync(id, detailId), "明细已确认"));

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> ConfirmAll(long id)
        => Ok(ApiResponse.Ok(await _svc.ConfirmAllAsync(id, GetUserId()), "移库完成"));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search)
    {
        var bytes = await _svc.ExportAsync(search);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "substitute_export.xlsx");
    }
}

public class CreateSubstituteOrderRequest
{
    public List<SubstituteDetailInput> Details { get; set; } = new();
}
