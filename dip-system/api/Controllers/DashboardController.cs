using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _svc;

    public DashboardController(DashboardService svc) { _svc = svc; }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() => Ok(ApiResponse.Ok(await _svc.GetStatsAsync()));

    [HttpGet("export-replenish")]
    public async Task<IActionResult> ExportReplenish()
    {
        var items = await _svc.GetPendingReplenishItemsAsync();
        // 按料号合并缺料数量
        var grouped = items.GroupBy(i => (string)((dynamic)i).part_no)
            .Select(g => new { part_no = g.Key, shortage = g.Sum(i => (decimal)((dynamic)i).shortage) })
            .OrderByDescending(g => g.shortage).ToList();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("待补货清单");
        ws.Cell(1, 1).Value = "料号";
        ws.Cell(1, 2).Value = "缺料数量";
        var headerRow = ws.Range(1, 1, 1, 2);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FFF3CD");

        int row = 2;
        foreach (var g in grouped)
        {
            ws.Cell(row, 1).Value = g.part_no;
            ws.Cell(row, 2).Value = g.shortage;
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "待补货清单.xlsx");
    }
}
