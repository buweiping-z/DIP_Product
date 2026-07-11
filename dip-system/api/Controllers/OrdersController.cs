using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
    [Authorize]
[Route("api/v1/orders")]

public class OrdersController : ControllerBase {
    private readonly OrderService _svc;

    public OrdersController(OrderService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int? status, [FromQuery] long? line_id,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, line_id, page, page_size)));

    [HttpGet("products")]
    public async Task<IActionResult> GetProductNames() => Ok(ApiResponse.Ok(await _svc.GetProductNamesAsync()));

    [HttpGet("product-bom")]
    public async Task<IActionResult> GetProductBom([FromQuery] string name) => Ok(ApiResponse.Ok(await _svc.GetProductBomAsync(name)));

    [HttpGet("{id}/bom-status")]
    public async Task<IActionResult> GetBomStatus(long id) => Ok(ApiResponse.Ok(await _svc.GetBomStatusAsync(id)));

    [HttpGet("bom-template")]
    public async Task<IActionResult> DownloadBomTemplate()
    {
        // Return a simple Excel template
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("产品BOM模板");
        ws.Cell(1, 1).Value = "产品名称";
        ws.Cell(1, 2).Value = "料号";
        ws.Cell(1, 3).Value = "用量";
        ws.Cell(2, 1).Value = "主板V2.2";
        ws.Cell(2, 2).Value = "RES-0805-10K";
        ws.Cell(2, 3).Value = 10;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "bom_template.xlsx");
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("{id}/details")]
    public async Task<IActionResult> GetDetail(long id) => Ok(ApiResponse.Ok(await _svc.GetDetailAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(data, 1), "订单创建成功"));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] Dictionary<string, object?> data)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, data), "更新成功"));

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.CancelAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "订单已取消，库存已释放"));
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] StatusRequest req)
    {
        await _svc.UpdateAsync(id, new Dictionary<string, object?> { ["status"] = req.Status });
        return Ok(ApiResponse.Ok(null, "状态更新成功"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _svc.DeleteAsync(id, userId);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPost("import-bom")]
    public async Task<IActionResult> ImportBom(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var count = await _svc.ImportBomAsync(ms.ToArray());
        return Ok(ApiResponse.Ok(new { count }, $"导入 {count} 条 BOM"));
    }
}


public class StatusRequest { public int Status { get; set; } }
