using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/call-material")]
public class MaterialRequestController : ControllerBase
{
    private readonly MaterialRequestService _svc;

    public MaterialRequestController(MaterialRequestService svc) { _svc = svc; }

    /// <summary>手机端批量上传叫料请求</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object?> data)
    {
        var userId = long.Parse(User.FindFirstValue("nameid")!);
        var items = new List<Dictionary<string, object?>>();
        if (data.TryGetValue("items", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in je.EnumerateArray())
            {
                if (elem.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in elem.EnumerateObject())
                        dict[prop.Name] = prop.Value;
                    items.Add(dict);
                }
            }
        }
        return Ok(ApiResponse.Ok(await _svc.BatchCreateAsync(items, userId), "叫料成功"));
    }

    /// <summary>网页端分页列表</summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] int? status, [FromQuery] string? start_date, [FromQuery] string? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(part_no, location_code, status, start_date, end_date, page, page_size)));

    /// <summary>更新状态</summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] Dictionary<string, object?> data)
    {
        var status = data.GetInt("status") ?? 0;
        await _svc.UpdateStatusAsync(id, status);
        return Ok(ApiResponse.Ok(null, "状态更新成功"));
    }

    /// <summary>删除记录</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse.Ok(null, "删除成功"));
    }

    /// <summary>导出 Excel</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? part_no, [FromQuery] string? location_code,
        [FromQuery] int? status, [FromQuery] string? start_date, [FromQuery] string? end_date)
    {
        var bytes = await _svc.ExportAsync(part_no, location_code, status, start_date, end_date);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "call_material_export.xlsx");
    }
}
