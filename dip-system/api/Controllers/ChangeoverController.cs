using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/changeover")]
public class ChangeoverController : ControllerBase
{
    private readonly ChangeoverService _svc;
    public ChangeoverController(ChangeoverService svc) { _svc = svc; }

    private long CurrentUserId =>
        long.Parse(User.FindFirstValue("nameid")!);

    /// <summary>根据产品名称或订单号获取 BOM</summary>
    [HttpGet("bom")]
    public async Task<IActionResult> GetBom([FromQuery] string? name, [FromQuery] string? order_no)
    {
        if (!string.IsNullOrEmpty(order_no))
            return Ok(ApiResponse.Ok(await _svc.GetBomByOrderNoAsync(order_no)));
        return Ok(ApiResponse.Ok(await _svc.GetBomByProductNameAsync(name ?? "")));
    }

    /// <summary>获取活跃批次</summary>
    [HttpGet("batches")]
    public async Task<IActionResult> GetActiveBatches()
        => Ok(ApiResponse.Ok(await _svc.GetActiveBatchesAsync()));

    /// <summary>查询批次列表（所有状态，分页+搜索）</summary>
    [HttpGet("batches/list")]
    public async Task<IActionResult> GetBatchList(
        [FromQuery] string? product_name = null,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetBatchListAsync(product_name, page, page_size)));

    /// <summary>创建新批次</summary>
    [HttpPost("batches")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateChangeoverBatchRequest req)
        => Ok(ApiResponse.Ok(await _svc.CreateBatchAsync(req.ProductName, req.Bom, CurrentUserId)));

    /// <summary>获取批次详情</summary>
    [HttpGet("batches/{batchNo}")]
    public async Task<IActionResult> GetBatchDetail(string batchNo)
    {
        var detail = await _svc.GetBatchDetailAsync(batchNo);
        return detail == null ? NotFound() : Ok(ApiResponse.Ok(detail));
    }

    /// <summary>扫描确认</summary>
    [HttpPost("batches/{batchNo}/scan")]
    public async Task<IActionResult> Scan(string batchNo, [FromBody] ChangeoverScanRequest req)
        => Ok(ApiResponse.Ok(await _svc.ScanAsync(batchNo, req.PartNo, CurrentUserId)));

    /// <summary>标记批次完成</summary>
    [HttpPost("batches/{batchNo}/complete")]
    public async Task<IActionResult> Complete(string batchNo)
    {
        await _svc.CompleteBatchAsync(batchNo);
        return Ok(ApiResponse.Ok(null, "切替完成"));
    }

    /// <summary>删除批次及关联明细</summary>
    [HttpDelete("batches/{batchNo}")]
    public async Task<IActionResult> DeleteBatch(string batchNo)
    {
        await _svc.DeleteBatchAsync(batchNo);
        return Ok(ApiResponse.Ok(null, "批次已删除"));
    }

    /// <summary>查询扫描记录</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? product_name = null, [FromQuery] string? part_no = null,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(product_name, part_no, page, page_size)));

    /// <summary>待完成批次（Dashboard 用）</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
        => Ok(ApiResponse.Ok(await _svc.GetPendingBatchesAsync()));
}

public class CreateChangeoverBatchRequest
{
    public string ProductName { get; set; } = "";
    public List<BomItemDto> Bom { get; set; } = new();
}

public class ChangeoverScanRequest
{
    public string PartNo { get; set; } = "";
}
