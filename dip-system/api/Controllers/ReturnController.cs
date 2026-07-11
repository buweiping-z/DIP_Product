using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/return")]

public class ReturnController : ControllerBase {
    private readonly ReturnService _svc;

    public ReturnController(ReturnService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? status, int page = 1, int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpPost("scan")]
    public async Task<IActionResult> ScanReturn([FromBody] ScanReturnRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.ScanReturnAsync(req.Barcode, req.TargetLocationId, userId)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.CreateAsync(data, userId), "退料单创建成功"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] Dictionary<string, object?> data)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, data), "退料单已更新"));
}


public class ScanReturnRequest
{
    public string Barcode { get; set; } = "";
    public long TargetLocationId { get; set; }
}
