using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/report")]
public class ReportController : ControllerBase
{
    private readonly ReportService _svc;

    public ReportController(ReportService svc) { _svc = svc; }

    [HttpGet("daily")]
    public async Task<IActionResult> DailyReport([FromQuery] string? date)
    {
        var reportDate = string.IsNullOrEmpty(date)
            ? DateTime.UtcNow.AddHours(8).Date
            : DateTime.Parse(date);

        var pdf = await _svc.GenerateDailyReportAsync(reportDate);
        var fileName = $"daily_report_{reportDate:yyyyMMdd}.pdf";
        return File(pdf, "application/pdf", fileName);
    }
}
