using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/stockcount")]

public class StockCountController : ControllerBase {
    private readonly StockCountService _svc;

    public StockCountController(StockCountService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? status, int page = 1, int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("export/template")]
    public async Task<IActionResult> ExportTemplate()
    {
        var bytes = await _svc.ExportTemplateAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "stock_count_template.xlsx");
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var result = await _svc.ImportResultAsync(ms.ToArray(), userId);
        return Ok(ApiResponse.Ok(result, "盘点导入完成"));
    }
}
