using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/parts")]
public class PartsController : ControllerBase
{
    private readonly PartService _svc;

    public PartsController(PartService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? keyword, [FromQuery] string? part_no, [FromQuery] string? part_name,
        [FromQuery] long? supplier_id, [FromQuery] int? part_type, [FromQuery] int? status,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
    {
        var result = await _svc.GetListAsync(keyword, part_no, part_name, supplier_id, part_type, status, page, page_size);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(data), "创建成功"));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] Dictionary<string, object?> data)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, data), "更新成功"));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var count = await _svc.ImportFromExcelAsync(ms.ToArray());
        return Ok(ApiResponse.Ok(new { count }, $"导入 {count} 条"));
    }

    [HttpGet("template")]
    public async Task<IActionResult> Template()
    {
        var bytes = await _svc.ExportTemplateAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "parts_template.xlsx");
    }

    [HttpGet("{id}/substitutes")]
    public async Task<IActionResult> GetSubstitutes(long id) => Ok(ApiResponse.Ok(await _svc.GetSubstitutesAsync(id)));
}
