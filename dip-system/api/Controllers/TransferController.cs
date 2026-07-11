using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/transfer")]

public class TransferController : ControllerBase {
    private readonly TransferService _svc;

    public TransferController(TransferService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? status, int page = 1, int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.CreateAsync(data, userId), "调拨单创建成功"));
    }

    [HttpPost("{id}/execute")]
    public async Task<IActionResult> Execute(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.ExecuteAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "调拨已执行"));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.CancelAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "调拨已取消"));
    }
}
