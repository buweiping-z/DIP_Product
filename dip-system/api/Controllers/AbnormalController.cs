using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/abnormal")]

public class AbnormalController : ControllerBase {
    private readonly AbnormalService _svc;

    public AbnormalController(AbnormalService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? type, [FromQuery] int? severity,
        [FromQuery] int? status, int page = 1, int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(type, severity, status, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(ApiResponse.Ok(await _svc.CreateAsync(data, userId), "异常记录已创建"));
    }

    [HttpPost("{id}/handle")]
    public async Task<IActionResult> Handle(long id, [FromBody] HandleAbnormalRequest req)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.HandleAsync(id, userId, req.HandleNote);
        return Ok(ApiResponse.Ok(null, "已处理"));
    }
}


public class HandleAbnormalRequest { public string HandleNote { get; set; } = ""; }
