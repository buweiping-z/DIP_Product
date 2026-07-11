using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
public class LocationsController : ControllerBase
{
    private readonly LocationService _svc;

    public LocationsController(LocationService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? warehouse, [FromQuery] string? zone,
        [FromQuery] int? status, [FromQuery] string? location_code,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(warehouse, zone, status, location_code, page, page_size)));

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
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "locations_template.xlsx");
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses() => Ok(ApiResponse.Ok(await _svc.GetWarehousesAsync()));
}

[ApiController]
[Route("api/v1/lines")]
public class LinesController : ControllerBase
{
    private readonly LocationService _svc;

    public LinesController(LocationService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetLines() => Ok(ApiResponse.Ok(await _svc.GetLinesAsync()));
}
