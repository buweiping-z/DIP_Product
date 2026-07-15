# 替代料移库功能重做 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重构替代料移库为订单→明细→扫码确认模式，支持多明细行、搜索筛选、扫码匹配、部分确认退出恢复，完成后自动执行移库并刷新冻结。

**Architecture:** 后端新增 SubstituteOrder/SubstituteDetail 模型 + SubstituteService + SubstituteController；网页端重写 SubstituteList.tsx 为订单列表+多明细表单；手机端重写 SubstituteScreen 为订单列表→扫码确认（仅扫替代料号匹配），复用现有扫码组件。

**Tech Stack:** C# ASP.NET Core 8.0 + EF Core 8, React 18 + TypeScript + Vite, Android Kotlin + Jetpack Compose + CameraX + ML Kit

## Global Constraints

- JSON 全局 `SnakeCaseLower`，所有字段用 snake_case
- 所有 API 返回 `{ code: 0, data: ..., message: "ok" }` 格式
- EF Core 无全局 NoTracking，写操作依赖 ChangeTracker
- 旧 `substitute_records` 表保留不动，新功能用新表
- 手机端扫码模块复用现有 QrCodeScanner / BarcodeAnalyzer / ScannerOverlay
- 料号解析规则：≤14位取全部，>14位取 length-4（去末尾4位）
- 数量仅用于操作员核对，不修改订单明细的 quantity
- detail_count / confirmed_count 不维护冗余字段，实时 COUNT
- 移库执行用数据库事务包裹，部分失败全部回滚

---

### Task 1: 后端 — 新增 SubstituteOrder / SubstituteDetail 模型

**Files:**
- Create: `dip-system/api/Models/SubstituteOrder.cs`

**Interfaces:**
- Produces: `SubstituteOrder` class (Id, OrderNo, Status, OperatorId, + BaseEntity fields), `SubstituteDetail` class (Id, OrderId, OriginalPartId, OriginalPartNo, SubstitutePartId, SubstitutePartNo, SourceLocationId, SourceLocationCode, TargetLocationId, TargetLocationCode, Quantity, Status, + BaseEntity fields)

- [ ] **Step 1: 创建模型文件**

创建 `dip-system/api/Models/SubstituteOrder.cs`：

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace DIP.Api.Models;

/// <summary>
/// 替代料移库订单
/// </summary>
public class SubstituteOrder : BaseEntity
{
    [Column("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>1=待确认, 2=已完成, 3=已取消</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [Column("operator_id")]
    public long OperatorId { get; set; }

    public List<SubstituteDetail> Details { get; set; } = new();
}

/// <summary>
/// 替代料移库明细
/// </summary>
public class SubstituteDetail : BaseEntity
{
    [Column("order_id")]
    public long OrderId { get; set; }

    [Column("original_part_id")]
    public long OriginalPartId { get; set; }

    [Column("original_part_no")]
    public string OriginalPartNo { get; set; } = string.Empty;

    [Column("substitute_part_id")]
    public long SubstitutePartId { get; set; }

    [Column("substitute_part_no")]
    public string SubstitutePartNo { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long SourceLocationId { get; set; }

    [Column("source_location_code")]
    public string SourceLocationCode { get; set; } = string.Empty;

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    [Column("target_location_code")]
    public string TargetLocationCode { get; set; } = string.Empty;

    [Column("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>1=待确认, 2=已确认</summary>
    [Column("status")]
    public int Status { get; set; } = 1;

    [ForeignKey(nameof(OrderId))]
    public SubstituteOrder? Order { get; set; }
}
```

- [ ] **Step 2: 编译验证**

```bash
cd dip-system/api && dotnet build
```

---

### Task 2: 后端 — AppDbContext 注册新实体

**Files:**
- Modify: `dip-system/api/Data/AppDbContext.cs`

**Interfaces:**
- Consumes: `SubstituteOrder`, `SubstituteDetail` from Task 1
- Produces: `DbSet<SubstituteOrder>`, `DbSet<SubstituteDetail>` (available to DI consumers)

- [ ] **Step 1: 添加 DbSet 声明**

在 `AppDbContext.cs` 中 `// ===== 替代料记录 =====` 附近添加：

```csharp
// ===== 替代料移库订单 =====
public DbSet<SubstituteOrder> SubstituteOrders { get; set; }
public DbSet<SubstituteDetail> SubstituteDetails { get; set; }
```

- [ ] **Step 2: 添加全局查询过滤器**

在 `OnModelCreating` 的过滤器区域添加：

```csharp
modelBuilder.Entity<SubstituteOrder>().HasQueryFilter(e => !e.IsDeleted);
modelBuilder.Entity<SubstituteDetail>().HasQueryFilter(e => !e.IsDeleted);
```

- [ ] **Step 3: 添加表名映射**

在 `OnModelCreating` 的表名映射区域添加：

```csharp
modelBuilder.Entity<SubstituteOrder>(e => e.ToTable("substitute_orders"));
modelBuilder.Entity<SubstituteDetail>(e => e.ToTable("substitute_details"));
```

- [ ] **Step 4: 添加唯一索引 + 外键级联**

在 `OnModelCreating` 的索引区域添加：

```csharp
modelBuilder.Entity<SubstituteOrder>().HasIndex(e => e.OrderNo).IsUnique().HasDatabaseName("uq_substitute_orders_no");
```

在 `OnModelCreating` 的级联删除区域添加：

```csharp
modelBuilder.Entity<SubstituteDetail>()
    .HasOne(e => e.Order)
    .WithMany(o => o.Details)
    .HasForeignKey(e => e.OrderId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: 编译验证**

```bash
cd dip-system/api && dotnet build
```

---

### Task 3: 后端 — SubstituteService 完整业务逻辑

**Files:**
- Create: `dip-system/api/Services/SubstituteService.cs`

**Interfaces:**
- Consumes: `AppDbContext` (injected), `SubstituteOrder`, `SubstituteDetail` from Task 1, `InventoryService.TransferOutCoreAsync`, `InventoryService.AddCoreAsync`, `OrderService.RefreezeActiveOrdersAsync`
- Produces: `GetListAsync`, `GetByIdAsync`, `GetDetailsAsync`, `CreateAsync`, `UpdateAsync`, `CancelAsync`, `ConfirmDetailAsync`, `ConfirmAllAsync` — all returning `Task<object>`

- [ ] **Step 1: 创建 SubstituteService.cs**

```csharp
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class SubstituteService
{
    private readonly AppDbContext _db;
    public SubstituteService(AppDbContext db) { _db = db; }

    private string GenOrderNo()
    {
        var now = DateTime.UtcNow.AddHours(8); // 北京时间
        return $"SUB-{now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
    }

    private static object ToDict(SubstituteOrder o) => new
    {
        o.Id, order_no = o.OrderNo, o.Status, o.OperatorId,
        detail_count = o.Details.Count(d => !d.IsDeleted),
        confirmed_count = o.Details.Count(d => !d.IsDeleted && d.Status == 2),
        created_at = o.CreatedAt
    };

    // ===== 查询 =====

    public async Task<object> GetListAsync(int? status = null, string? search = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 20)
    {
        var query = _db.SubstituteOrders.AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(o => o.Details.Any(d =>
                d.SubstitutePartNo.ToLower().Contains(s) ||
                d.OriginalPartNo.ToLower().Contains(s)));
        }
        if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(o => o.CreatedAt <= endDate.Value.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Details)
            .ToListAsync();
        return new { total, page, page_size = pageSize,
            items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long id)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw AppException.NotFound($"替代料移库订单 {id} 不存在");

        return new
        {
            order.Id, order_no = order.OrderNo, order.Status, order.OperatorId,
            detail_count = order.Details.Count(d => !d.IsDeleted),
            confirmed_count = order.Details.Count(d => !d.IsDeleted && d.Status == 2),
            created_at = order.CreatedAt,
            details = order.Details.OrderBy(d => d.SourceLocationCode).Select(d => new
            {
                d.Id, d.OrderId,
                original_part_id = d.OriginalPartId, original_part_no = d.OriginalPartNo,
                substitute_part_id = d.SubstitutePartId, substitute_part_no = d.SubstitutePartNo,
                source_location_id = d.SourceLocationId, source_location_code = d.SourceLocationCode,
                target_location_id = d.TargetLocationId, target_location_code = d.TargetLocationCode,
                d.Quantity, d.Status
            })
        };
    }

    /// <summary>
    /// 手机端用：获取待确认明细列表（按来源库位排序）
    /// </summary>
    public async Task<object> GetDetailsAsync(long orderId)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        return new
        {
            order_id = order.Id, order_no = order.OrderNo, order.Status,
            confirmed_count = order.Details.Count(d => !d.IsDeleted && d.Status == 2),
            total_count = order.Details.Count(d => !d.IsDeleted),
            // 按来源库位排序，一趟走完
            details = order.Details.OrderBy(d => d.SourceLocationCode).Select(d => new
            {
                d.Id, d.OrderId,
                original_part_no = d.OriginalPartNo,
                substitute_part_no = d.SubstitutePartNo,
                source_location_code = d.SourceLocationCode,
                target_location_code = d.TargetLocationCode,
                d.Quantity, d.Status
            })
        };
    }

    // ===== 创建订单 =====

    public async Task<object> CreateAsync(List<SubstituteDetailInput> details, long operatorId)
    {
        if (details == null || details.Count == 0)
            throw AppException.Business("至少需要一条明细");

        var order = new SubstituteOrder
        {
            OrderNo = GenOrderNo(), Status = 1, OperatorId = operatorId
        };
        _db.SubstituteOrders.Add(order);
        await _db.SaveChangesAsync();

        foreach (var d in details)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.OriginalPartId);
            var subPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.SubstitutePartId);
            var srcLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.SourceLocationId);
            var tgtLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.TargetLocationId);

            if (d.OriginalPartId == d.SubstitutePartId && d.SourceLocationId == d.TargetLocationId)
                throw AppException.Business("来源和目标不能完全相同");

            _db.SubstituteDetails.Add(new SubstituteDetail
            {
                OrderId = order.Id,
                OriginalPartId = d.OriginalPartId, OriginalPartNo = part?.PartNo ?? "",
                SubstitutePartId = d.SubstitutePartId, SubstitutePartNo = subPart?.PartNo ?? "",
                SourceLocationId = d.SourceLocationId, SourceLocationCode = srcLoc?.LocationCode ?? "",
                TargetLocationId = d.TargetLocationId, TargetLocationCode = tgtLoc?.LocationCode ?? "",
                Quantity = d.Quantity, Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return GetByIdAsync(order.Id);
    }

    // ===== 编辑订单（仅 status=1 的订单，仅未确认明细可修改）=====

    public async Task<object> UpdateAsync(long orderId, List<SubstituteDetailInput> newDetails)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("仅待确认订单可编辑");

        // 保留已确认明细（不可修改）
        var confirmedDetails = order.Details.Where(d => d.Status == 2).ToList();

        // 删除未确认明细
        var unconfirmedDetails = order.Details.Where(d => d.Status == 1).ToList();
        _db.SubstituteDetails.RemoveRange(unconfirmedDetails);

        // 追加新明细
        foreach (var d in newDetails)
        {
            var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.OriginalPartId);
            var subPart = await _db.Parts.FirstOrDefaultAsync(p => p.Id == d.SubstitutePartId);
            var srcLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.SourceLocationId);
            var tgtLoc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == d.TargetLocationId);

            _db.SubstituteDetails.Add(new SubstituteDetail
            {
                OrderId = order.Id,
                OriginalPartId = d.OriginalPartId, OriginalPartNo = part?.PartNo ?? "",
                SubstitutePartId = d.SubstitutePartId, SubstitutePartNo = subPart?.PartNo ?? "",
                SourceLocationId = d.SourceLocationId, SourceLocationCode = srcLoc?.LocationCode ?? "",
                TargetLocationId = d.TargetLocationId, TargetLocationCode = tgtLoc?.LocationCode ?? "",
                Quantity = d.Quantity, Status = 1
            });
        }
        await _db.SaveChangesAsync();

        return GetByIdAsync(order.Id);
    }

    // ===== 取消订单 =====

    public async Task CancelAsync(long orderId)
    {
        var order = await _db.SubstituteOrders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("仅待确认订单可取消");

        order.Status = 3;
        // 已确认明细回退为待确认
        var confirmedDetails = await _db.SubstituteDetails
            .Where(d => d.OrderId == orderId && d.Status == 2).ToListAsync();
        foreach (var d in confirmedDetails)
            d.Status = 1;

        await _db.SaveChangesAsync();
    }

    // ===== 扫码确认单条明细（不执行移库）=====

    public async Task<object> ConfirmDetailAsync(long orderId, long detailId)
    {
        var detail = await _db.SubstituteDetails
            .FirstOrDefaultAsync(d => d.Id == detailId && d.OrderId == orderId)
            ?? throw AppException.NotFound($"明细 {detailId} 不存在或不属于此订单");

        if (detail.Status != 1)
            throw AppException.Business(detail.Status == 2 ? "该明细已确认" : "该明细状态异常");

        detail.Status = 2;
        await _db.SaveChangesAsync();

        return new { detail_id = detail.Id, confirmed = true };
    }

    // ===== 全部确认完成：执行移库 =====

    public async Task<object> ConfirmAllAsync(long orderId, long operatorId)
    {
        var order = await _db.SubstituteOrders
            .Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw AppException.NotFound($"订单 {orderId} 不存在");

        if (order.Status != 1) throw AppException.Business("订单状态不允许确认");

        var details = order.Details.Where(d => !d.IsDeleted).ToList();
        var allConfirmed = details.All(d => d.Status == 2);
        if (!allConfirmed)
            throw AppException.Business($"尚有 {details.Count(d => d.Status == 1)} 条明细未确认");

        var invSvc = new InventoryService(_db);

        // 数据库事务：任何一条失败全部回滚
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var d in details)
            {
                // 替代料出库（扣减可用库存）
                await invSvc.TransferOutCoreAsync(d.SubstitutePartId, d.SourceLocationId,
                    d.Quantity, operatorId, "SubstituteOut", d.Id);
                // 缺料入库（移入目标库位）
                await invSvc.AddCoreAsync(d.OriginalPartId, d.TargetLocationId,
                    d.Quantity, "", operatorId, "SubstituteIn", d.Id);
            }

            order.Status = 2;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // 刷新订单冻结库存
        await new OrderService(_db).RefreezeActiveOrdersAsync(operatorId);

        return new { order_id = order.Id, status = 2, message = "移库完成" };
    }
}

public class SubstituteDetailInput
{
    public long OriginalPartId { get; set; }
    public long SubstitutePartId { get; set; }
    public long SourceLocationId { get; set; }
    public long TargetLocationId { get; set; }
    public decimal Quantity { get; set; }
}
```

- [ ] **Step 2: 编译验证**

```bash
cd dip-system/api && dotnet build
```

---

### Task 4: 后端 — SubstituteController

**Files:**
- Create: `dip-system/api/Controllers/SubstituteController.cs`

**Interfaces:**
- Consumes: `SubstituteService` from Task 3
- Produces: 8 API 端点 (REST)

- [ ] **Step 1: 创建 SubstituteController.cs**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/substitute/orders")]
public class SubstituteController : ControllerBase
{
    private readonly SubstituteService _svc;

    public SubstituteController(SubstituteService svc) { _svc = svc; }

    private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status, [FromQuery] string? search,
        [FromQuery] DateTime? start_date, [FromQuery] DateTime? end_date,
        [FromQuery] int page = 1, [FromQuery] int page_size = 20)
        => Ok(ApiResponse.Ok(await _svc.GetListAsync(status, search, start_date, end_date, page, page_size)));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
        => Ok(ApiResponse.Ok(await _svc.GetByIdAsync(id)));

    [HttpGet("{id}/details")]
    public async Task<IActionResult> GetDetails(long id)
        => Ok(ApiResponse.Ok(await _svc.GetDetailsAsync(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubstituteOrderRequest req)
        => Ok(ApiResponse.Ok(await _svc.CreateAsync(req.Details, GetUserId()), "替代料移库订单已创建"));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] CreateSubstituteOrderRequest req)
        => Ok(ApiResponse.Ok(await _svc.UpdateAsync(id, req.Details), "订单已更新"));

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        await _svc.CancelAsync(id);
        return Ok(ApiResponse.Ok(null, "订单已取消"));
    }

    [HttpPost("{id}/details/{detailId}/confirm")]
    public async Task<IActionResult> ConfirmDetail(long id, long detailId)
        => Ok(ApiResponse.Ok(await _svc.ConfirmDetailAsync(id, detailId), "明细已确认"));

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> ConfirmAll(long id)
        => Ok(ApiResponse.Ok(await _svc.ConfirmAllAsync(id, GetUserId()), "移库完成"));
}

public class CreateSubstituteOrderRequest
{
    public List<SubstituteDetailInput> Details { get; set; } = new();
}
```

- [ ] **Step 2: 注册服务到 DI**

在 `dip-system/api/Program.cs` 中找到 `builder.Services.AddScoped` 区域，添加：

```csharp
builder.Services.AddScoped<SubstituteService>();
```

- [ ] **Step 3: 编译验证**

```bash
cd dip-system/api && dotnet build
```

---

### Task 5: 网页端 — SubstituteList.tsx 重写

**Files:**
- Modify: `dip-system/frontend-web/src/pages/SubstituteList.tsx`

**Interfaces:**
- Consumes: `api.get/post/put/delete` from `../lib/api`, `showToast` from `../lib/toast`, `HelpButton` from `../lib/HelpButton`
- Produces: 完整的替代料移库管理页面（订单列表 + 新建/编辑弹窗 + 详情弹窗）

- [ ] **Step 1: 重写 SubstituteList.tsx**

整个文件替换为新实现：

```tsx
import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP: Record<number, string> = { 1: '待确认', 2: '已完成', 3: '已取消' };

interface DetailRow {
  key: number; // 前端临时ID
  original_part_id: number; substitute_part_id: number;
  source_location_id: number; target_location_id: number;
  quantity: number;
}

export default function SubstituteList() {
  const [orders, setOrders] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [showDetail, setShowDetail] = useState(false);
  const [detailData, setDetailData] = useState<any>(null);
  const [msg, setMsg] = useState('');
  // 新建/编辑表单
  const [parts, setParts] = useState<any[]>([]);
  const [rows, setRows] = useState<DetailRow[]>([emptyRow(0)]);
  const [searchText, setSearchText] = useState('');
  // 编辑时已确认的明细（只读）
  const [existingConfirmed, setExistingConfirmed] = useState<any[]>([]);

  function emptyRow(key: number): DetailRow {
    return { key, original_part_id: 0, substitute_part_id: 0, source_location_id: 0, target_location_id: 0, quantity: 0 };
  }

  // 加载库存（用于某部品的可选库位）
  const loadStocks = useCallback(async (partId: number): Promise<any[]> => {
    if (!partId) return [];
    try { return (await api.get(`/inventory/available/${partId}`)).data || []; } catch { return []; }
  }, []);

  // 加载部品列表
  const loadParts = useCallback(async () => {
    try { setParts((await api.get('/parts?page=1&page_size=500')).data?.items || []); } catch {}
  }, []);

  const fetchOrders = useCallback(async () => {
    setLoading(true);
    try { setOrders((await api.get('/substitute/orders?page=1&page_size=50')).data?.items || []); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { fetchOrders(); }, []);

  const openCreate = () => {
    setEditId(null); setExistingConfirmed([]);
    setRows([emptyRow(0)]); setSearchText('');
    loadParts(); setShowDialog(true);
  };

  const openEdit = async (orderId: number) => {
    try {
      const res = await api.get(`/substitute/orders/${orderId}`);
      if (res.code !== 0) return;
      const order = res.data;
      setEditId(orderId);
      await loadParts();
      // 区分已确认和未确认
      const details = order.details || [];
      const confirmed = details.filter((d: any) => d.status === 2);
      const unconfirmed = details.filter((d: any) => d.status === 1);
      setExistingConfirmed(confirmed);
      setRows(unconfirmed.length > 0
        ? unconfirmed.map((d: any, i: number) => ({
            key: i,
            original_part_id: d.original_part_id, substitute_part_id: d.substitute_part_id,
            source_location_id: d.source_location_id, target_location_id: d.target_location_id,
            quantity: d.quantity
          }))
        : [emptyRow(0)]);
      setSearchText('');
      setShowDialog(true);
    } catch {}
  };

  const showDetailFn = async (id: number) => {
    try {
      const res = await api.get(`/substitute/orders/${id}`);
      setDetailData(res.data || {});
      setShowDetail(true);
    } catch {}
  };

  const addRow = () => setRows(prev => [...prev, emptyRow(Math.max(...prev.map(r => r.key), 0) + 1)]);

  const delRow = (key: number) => {
    if (rows.length <= 1 && existingConfirmed.length === 0) return;
    setRows(prev => prev.filter(r => r.key !== key));
  };

  const updateRow = (key: number, field: keyof DetailRow, value: number) => {
    setRows(prev => prev.map(r => r.key === key ? { ...r, [field]: value } : r));
  };

  // 按料号搜索过滤部品
  const filteredParts = searchText
    ? parts.filter((p: any) =>
        (p.part_no || '').toLowerCase().includes(searchText.toLowerCase()) ||
        (p.part_name || '').toLowerCase().includes(searchText.toLowerCase()))
    : parts;

  const handleSubmit = async () => {
    const validRows = rows.filter(r =>
      r.original_part_id > 0 && r.substitute_part_id > 0 &&
      r.source_location_id > 0 && r.target_location_id > 0 && r.quantity > 0);
    if (validRows.length === 0 && existingConfirmed.length === 0) {
      setMsg('至少需要一条有效明细'); return;
    }
    setMsg('');
    try {
      const payload = { details: validRows.map(r => ({
        original_part_id: r.original_part_id, substitute_part_id: r.substitute_part_id,
        source_location_id: r.source_location_id, target_location_id: r.target_location_id,
        quantity: r.quantity
      })) };
      if (editId) {
        await api.put(`/substitute/orders/${editId}`, payload);
        showToast('订单更新成功', 'success');
      } else {
        await api.post('/substitute/orders', payload);
        showToast('订单创建成功', 'success');
      }
      setShowDialog(false); fetchOrders();
    } catch {}
  };

  const handleCancel = async (id: number) => {
    if (!confirm('确认取消此订单？')) return;
    try { await api.post(`/substitute/orders/${id}/cancel`); showToast('订单已取消', 'success'); fetchOrders(); } catch {}
  };

  const statusTag = (s: number) => {
    const cls = s === 1 ? 'bg-yellow-100 text-yellow-700' : s === 2 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600';
    return <span className={`px-2 py-0.5 rounded text-xs ${cls}`}>{STATUS_MAP[s] || s}</span>;
  };

  // ===== 渲染 =====
  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">替代料移库</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-1.5 rounded text-sm">新建移库订单</button>
          <HelpButton title="替代料移库" sections={[
            { title: '功能概述', items: ['管理替代料移库订单，一个订单可包含多条移库明细', '网页端创建订单后，手机端逐袋扫码确认', '全部确认后系统自动执行移库并刷新冻结'] },
            { title: '操作流程', items: ['1. 网页端新建订单 → 添加多行明细（替代部品+来源库位 → 缺料部品+目标库位 → 数量）', '2. 手机端选择订单 → 扫替代部品条码匹配明细 → 逐一确认', '3. 全部确认后提交 → 系统自动完成库存移库'] }
          ]} />
        </div>
      </div>

      {msg && <div className={`p-3 rounded mb-4 text-sm ${msg.includes('成功') ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700'}`}>{msg}</div>}

      {/* 订单列表 */}
      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow text-sm">
          <thead><tr className="bg-gray-50 text-left">
            <th className="p-3">订单号</th><th className="p-3 text-center">明细数/已确认</th>
            <th className="p-3">状态</th><th className="p-3">创建时间</th><th className="p-3 w-40">操作</th>
          </tr></thead>
          <tbody>{orders.length === 0 ? <tr><td colSpan={5} className="p-6 text-center text-gray-400">暂无记录</td></tr> :
            orders.map(o => (
            <tr key={o.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-xs">{o.order_no}</td>
              <td className="p-3 text-center">{o.confirmed_count}/{o.detail_count}</td>
              <td className="p-3">{statusTag(o.status)}</td>
              <td className="p-3 text-xs text-gray-500">{o.created_at?.slice(0, 19)}</td>
              <td className="p-3 space-x-1 whitespace-nowrap">
                <button onClick={() => showDetailFn(o.id)} className="text-blue-600 hover:text-blue-800 text-xs">详情</button>
                {o.status === 1 && <>
                  <button onClick={() => openEdit(o.id)} className="text-orange-500 hover:text-orange-700 text-xs">编辑</button>
                  <button onClick={() => handleCancel(o.id)} className="text-red-500 hover:text-red-700 text-xs">取消</button>
                </>}
              </td>
            </tr>
          ))}</tbody>
        </table>
      )}

      {/* 新建/编辑弹窗 */}
      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[900px] max-h-[85vh] overflow-auto">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑移库订单' : '新建移库订单'}</h2>

            {/* 已确认明细（只读） */}
            {existingConfirmed.length > 0 && (
              <div className="mb-4">
                <p className="text-sm font-medium text-gray-500 mb-1">已确认明细（只读）</p>
                <table className="w-full text-sm border">
                  <thead><tr className="bg-gray-100">
                    <th className="p-1">替代部品</th><th className="p-1">来源库位</th>
                    <th className="p-1">缺料部品</th><th className="p-1">目标库位</th>
                    <th className="p-1">数量</th>
                  </tr></thead>
                  <tbody>
                    {existingConfirmed.map((d: any) => (
                      <tr key={d.id} className="text-gray-400 bg-gray-50">
                        <td className="p-1 font-mono text-xs">{d.substitute_part_no}</td>
                        <td className="p-1 font-mono text-xs">{d.source_location_code}</td>
                        <td className="p-1 font-mono text-xs">{d.original_part_no}</td>
                        <td className="p-1 font-mono text-xs">{d.target_location_code}</td>
                        <td className="p-1 text-right">{d.quantity}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* 搜索筛选 */}
            <input
              className="w-full border rounded px-3 py-1.5 mb-2 text-sm"
              placeholder="输入料号或名称筛选部品..."
              value={searchText}
              onChange={e => setSearchText(e.target.value)}
            />

            {/* 可编辑明细表 */}
            <div className="border rounded max-h-48 overflow-auto mb-2">
              <table className="w-full text-sm">
                <thead><tr className="bg-gray-50 sticky top-0">
                  <th className="p-1 text-left">替代部品</th><th className="p-1 text-left">来源库位</th>
                  <th className="p-1 text-left">缺料部品</th><th className="p-1 text-left">目标库位</th>
                  <th className="p-1 text-right w-16">数量</th>
                  <th className="p-1 w-10"></th>
                </tr></thead>
                <tbody>
                  {rows.map((row) => (
                    <RowEditor key={row.key} row={row} parts={filteredParts}
                      loadStocks={loadStocks} updateRow={updateRow} delRow={delRow} />
                  ))}
                </tbody>
              </table>
            </div>
            <button onClick={addRow} className="text-blue-600 text-sm hover:text-blue-800 mb-4">+ 添加一行</button>

            <div className="flex justify-end gap-3">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                {editId ? '保存修改' : '创建订单'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 详情弹窗 */}
      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[80vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold">移库订单详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <div className="grid grid-cols-2 gap-3 text-sm mb-4">
              <div><span className="text-gray-500">订单号</span><p className="font-mono">{detailData.order_no}</p></div>
              <div><span className="text-gray-500">状态</span><p>{statusTag(detailData.status)}</p></div>
              <div><span className="text-gray-500">明细数</span><p>{detailData.detail_count}</p></div>
              <div><span className="text-gray-500">已确认</span><p>{detailData.confirmed_count}</p></div>
            </div>
            <table className="w-full text-sm border">
              <thead><tr className="bg-gray-50">
                <th className="p-2 text-left">替代部品</th><th className="p-2 text-left">来源库位</th>
                <th className="p-2 text-left">缺料部品</th><th className="p-2 text-left">目标库位</th>
                <th className="p-2 text-right">数量</th>
                <th className="p-2">状态</th>
              </tr></thead>
              <tbody>{(detailData.details || []).map((d: any) => (
                <tr key={d.id} className="border-t">
                  <td className="p-2 font-mono text-xs">{d.substitute_part_no}</td>
                  <td className="p-2 font-mono text-xs">{d.source_location_code}</td>
                  <td className="p-2 font-mono text-xs">{d.original_part_no}</td>
                  <td className="p-2 font-mono text-xs">{d.target_location_code}</td>
                  <td className="p-2 text-right">{d.quantity}</td>
                  <td className="p-2">{d.status === 2 ? <span className="text-green-600">✓</span> : '待确认'}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ===== 单行编辑器子组件 =====
function RowEditor({ row, parts, loadStocks, updateRow, delRow }: {
  row: DetailRow;
  parts: any[];
  loadStocks: (partId: number) => Promise<any[]>;
  updateRow: (key: number, field: keyof DetailRow, value: number) => void;
  delRow: (key: number) => void;
}) {
  const [subStocks, setSubStocks] = useState<any[]>([]);
  const [origStocks, setOrigStocks] = useState<any[]>([]);

  useEffect(() => {
    loadStocks(row.substitute_part_id).then(setSubStocks);
  }, [row.substitute_part_id]);

  useEffect(() => {
    loadStocks(row.original_part_id).then(setOrigStocks);
  }, [row.original_part_id]);

  return (
    <tr className="border-t">
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.substitute_part_id}
          onChange={e => { const v = Number(e.target.value); updateRow(row.key, 'substitute_part_id', v); updateRow(row.key, 'source_location_id', 0); }}>
          <option value={0}>--</option>
          {parts.map(p => <option key={p.id} value={p.id}>{p.part_no}</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.source_location_id}
          onChange={e => updateRow(row.key, 'source_location_id', Number(e.target.value))}>
          <option value={0}>--</option>
          {subStocks.map((s: any) => <option key={s.location_id} value={s.location_id}>{s.location_code}(可用{s.available_qty})</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.original_part_id}
          onChange={e => { const v = Number(e.target.value); updateRow(row.key, 'original_part_id', v); updateRow(row.key, 'target_location_id', 0); }}>
          <option value={0}>--</option>
          {parts.map(p => <option key={p.id} value={p.id}>{p.part_no}</option>)}
        </select>
      </td>
      <td className="p-1">
        <select className="w-full border rounded text-xs p-1" value={row.target_location_id}
          onChange={e => updateRow(row.key, 'target_location_id', Number(e.target.value))}>
          <option value={0}>--</option>
          {origStocks.map((s: any) => <option key={s.location_id} value={s.location_id}>{s.location_code}(现存{s.available_qty})</option>)}
        </select>
      </td>
      <td className="p-1">
        <input type="number" className="w-16 border rounded text-xs p-1 text-right" min={0}
          value={row.quantity || ''} onChange={e => updateRow(row.key, 'quantity', Number(e.target.value))} />
      </td>
      <td className="p-1">
        <button onClick={() => delRow(row.key)} className="text-red-400 hover:text-red-600 text-xs">✕</button>
      </td>
    </tr>
  );
}
```

- [ ] **Step 2: 启动前端验证编译**

```bash
cd dip-system/frontend-web && npx tsc --noEmit
```

---

### Task 6: Android — Models.kt 新增数据结构

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/models/Models.kt`

**Interfaces:**
- Consumes: None
- Produces: `SubstituteOrderItem`, `SubstituteOrderDetail`, `SubstituteDetailItem`, `ConfirmAllResponse` 数据类

- [ ] **Step 1: 在 Models.kt 末尾添加数据类**

```kotlin
// ===== Substitute Orders =====
data class SubstituteOrderItem(
    val id: Int = 0,
    @SerializedName("order_no") val orderNo: String = "",
    val status: Int = 0,
    @SerializedName("detail_count") val detailCount: Int = 0,
    @SerializedName("confirmed_count") val confirmedCount: Int = 0,
    @SerializedName("created_at") val createdAt: String? = null
)

data class SubstituteOrderDetail(
    @SerializedName("order_id") val orderId: Int = 0,
    @SerializedName("order_no") val orderNo: String = "",
    val status: Int = 0,
    @SerializedName("confirmed_count") val confirmedCount: Int = 0,
    @SerializedName("total_count") val totalCount: Int = 0,
    val details: List<SubstituteDetailItem> = emptyList()
)

data class SubstituteDetailItem(
    val id: Int = 0,
    @SerializedName("order_id") val orderId: Int = 0,
    @SerializedName("original_part_no") val originalPartNo: String = "",
    @SerializedName("substitute_part_no") val substitutePartNo: String = "",
    @SerializedName("source_location_code") val sourceLocationCode: String = "",
    @SerializedName("target_location_code") val targetLocationCode: String = "",
    val quantity: Double = 0.0,
    val status: Int = 0  // 1=待确认, 2=已确认
)
```

---

### Task 7: Android — ApiService + AppRepository 新增接口

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/network/ApiService.kt`
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/repository/AppRepository.kt`

**Interfaces:**
- Consumes: Models from Task 6
- Produces: Retrofit 端点 + Repository 封装方法

- [ ] **Step 1: ApiService.kt — 替换 Substitute 区域**

将现有的 substitute 端点（第110-112行）替换为：

```kotlin
    // ===== Substitute Orders =====
    @GET("api/v1/substitute/orders")
    suspend fun getSubstituteOrders(
        @Query("status") status: Int? = 1, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50
    ): ApiResponse<PageResult<SubstituteOrderItem>>

    @GET("api/v1/substitute/orders/{id}/details")
    suspend fun getSubstituteOrderDetails(@Path("id") orderId: Int): ApiResponse<SubstituteOrderDetail>

    @POST("api/v1/substitute/orders/{id}/details/{detailId}/confirm")
    suspend fun confirmSubstituteDetail(@Path("id") orderId: Int, @Path("detailId") detailId: Int): ApiResponse<Map<String, Any?>>

    @POST("api/v1/substitute/orders/{id}/confirm")
    suspend fun confirmSubstituteAll(@Path("id") orderId: Int): ApiResponse<Map<String, Any?>>
```

- [ ] **Step 2: AppRepository.kt — 替换 Substitute 区域**

将现有的 `createSubstitute` 方法替换为：

```kotlin
    // Substitute Orders
    suspend fun getSubstituteOrders(status: Int = 1): Result<ApiResponse<PageResult<SubstituteOrderItem>>> =
        call { api.getSubstituteOrders(status = status) }

    suspend fun getSubstituteOrderDetails(orderId: Int): Result<ApiResponse<SubstituteOrderDetail>> =
        call { api.getSubstituteOrderDetails(orderId) }

    suspend fun confirmSubstituteDetail(orderId: Int, detailId: Int): Result<ApiResponse<Map<String, Any?>>> =
        call { api.confirmSubstituteDetail(orderId, detailId) }

    suspend fun confirmSubstituteAll(orderId: Int): Result<ApiResponse<Map<String, Any?>>> =
        call { api.confirmSubstituteAll(orderId) }
```

- [ ] **Step 3: 编译验证**

```bash
cd mobile-android && ./gradlew :app:compileDebugKotlin
```

---

### Task 8: Android — SubstituteViewModel 重写

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/ui/substitute/SubstituteViewModel.kt`

**Interfaces:**
- Consumes: `AppRepository`, Models from Task 6
- Produces: `SubstituteUiState` 数据类 + `SubstituteViewModel` 类（订单列表加载、选择订单、扫码匹配、确认明细、提交全部）

- [ ] **Step 1: 重写 SubstituteViewModel.kt**

```kotlin
package com.dip.material.ui.substitute

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import com.dip.material.data.models.SubstituteOrderDetail
import com.dip.material.data.models.SubstituteDetailItem
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SubstituteUiState(
    val orders: List<SubstituteOrderItem> = emptyList(),
    val selectedOrder: SubstituteOrderDetail? = null,
    val scanMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false,
    val isLoading: Boolean = false,
    val allDone: Boolean = false,
    // 当前匹配结果（扫码后到确认前）
    val matchedDetail: SubstituteDetailItem? = null,
    // 匹配到多条时的候选列表
    val matchCandidates: List<SubstituteDetailItem> = emptyList(),
    val showCandidates: Boolean = false
) {
    // 实时计算已确认数和总数
    val confirmedCount: Int get() = selectedOrder?.details?.count { it.status == 2 } ?: 0
    val totalCount: Int get() = selectedOrder?.totalCount ?: 0
}

class SubstituteViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(SubstituteUiState())
    val state: StateFlow<SubstituteUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    private suspend fun loadOrders() {
        _state.update { it.copy(isLoading = true) }
        repo.getSubstituteOrders(status = 1).fold(
            onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
            onFailure = { _state.update { it.copy(isLoading = false) } }
        )
    }

    fun selectOrder(orderId: Int) {
        viewModelScope.launch {
            repo.getSubstituteOrderDetails(orderId).fold(
                onSuccess = { res ->
                    if (res.code == 0 && res.data != null) {
                        val done = res.data.details.all { it.status == 2 }
                        _state.update { it.copy(selectedOrder = res.data, allDone = done,
                            matchedDetail = null, matchCandidates = emptyList(), showCandidates = false) }
                    }
                },
                onFailure = {}
            )
        }
    }

    fun scanBarcode(barcode: String) {
        val trimmed = barcode.trim()
        val detailList = _state.value.selectedOrder?.details ?: return

        // 条码须>14位
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val partNo = parsePartNo(trimmed)

        // 在未确认明细中匹配替代料号
        val matches = detailList.filter {
            it.status == 1 && it.substitutePartNo.trim().equals(partNo, ignoreCase = true)
        }

        when {
            matches.isEmpty() -> {
                ScanSoundManager.playError()
                _state.update { it.copy(
                    scanMsg = "无匹配明细 (扫码料号: $partNo)", matchedDetail = null,
                    matchCandidates = emptyList(), showCandidates = false,
                    scanEventId = it.scanEventId + 1, lastScanOk = false) }
            }
            matches.size == 1 -> {
                // 唯一匹配，自动选中
                val m = matches.first()
                ScanSoundManager.playSuccess()
                _state.update { it.copy(
                    matchedDetail = m, matchCandidates = emptyList(), showCandidates = false,
                    scanMsg = "匹配: ${m.substitutePartNo} ← ${m.originalPartNo}",
                    scanEventId = it.scanEventId + 1, lastScanOk = true) }
            }
            else -> {
                // 多条匹配，弹出选择列表
                ScanSoundManager.playSuccess()
                _state.update { it.copy(
                    matchedDetail = null, matchCandidates = matches, showCandidates = true,
                    scanMsg = "找到 ${matches.size} 条匹配，请选择",
                    scanEventId = it.scanEventId + 1, lastScanOk = true) }
            }
        }
    }

    fun selectCandidate(detail: SubstituteDetailItem) {
        _state.update { it.copy(
            matchedDetail = detail, matchCandidates = emptyList(), showCandidates = false,
            scanMsg = "匹配: ${detail.substitutePartNo} ← ${detail.originalPartNo}") }
    }

    fun cancelCurrentMatch() {
        _state.update { it.copy(matchedDetail = null, matchCandidates = emptyList(), showCandidates = false, scanMsg = null) }
    }

    fun confirmDetail() {
        val detail = _state.value.matchedDetail ?: return
        val orderId = _state.value.selectedOrder?.orderId ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteDetail(orderId, detail.id).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        // 更新本地明细状态
                        val updatedDetails = _state.value.selectedOrder?.details?.map {
                            if (it.id == detail.id) it.copy(status = 2) else it
                        } ?: emptyList()
                        val updatedOrder = _state.value.selectedOrder?.copy(details = updatedDetails)
                        val allDone = updatedDetails.all { it.status == 2 }
                        _state.update { it.copy(isLoading = false, selectedOrder = updatedOrder,
                            matchedDetail = null, allDone = allDone,
                            scanMsg = "✓ 确认成功: ${detail.substitutePartNo}",
                            scanEventId = it.scanEventId + 1, lastScanOk = true) }
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "确认失败",
                            scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message,
                        scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun confirmAll() {
        val orderId = _state.value.selectedOrder?.orderId ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteAll(orderId).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        _state.update { it.copy(isLoading = false,
                            scanMsg = "移库完成！",
                            scanEventId = it.scanEventId + 1, lastScanOk = true) }
                        // 刷新订单列表
                        loadOrders()
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "提交失败",
                            scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message,
                        scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun clearSelection() {
        _state.update { it.copy(selectedOrder = null, allDone = false,
            matchedDetail = null, matchCandidates = emptyList(), showCandidates = false, scanMsg = null) }
        viewModelScope.launch { loadOrders() }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
```

- [ ] **Step 2: 编译验证**

```bash
cd mobile-android && ./gradlew :app:compileDebugKotlin
```

---

### Task 9: Android — SubstituteScreen 重写

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/ui/substitute/SubstituteScreen.kt`

**Interfaces:**
- Consumes: `SubstituteViewModel` from Task 8, `QrCodeScanner` from components
- Produces: 订单列表界面 + 扫码确认界面

- [ ] **Step 1: 重写 SubstituteScreen.kt**

```kotlin
package com.dip.material.ui.substitute

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.QrCodeScanner
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SubstituteScreen(onBack: () -> Unit, viewModel: SubstituteViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var showScanner by remember { mutableStateOf(false) }

    // 扫码结果音效
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (state.selectedOrder != null) "替代料移库" else "替代料移库") },
                navigationIcon = {
                    IconButton(onClick = {
                        if (state.selectedOrder != null) viewModel.clearSelection() else onBack()
                    }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            if (state.selectedOrder != null) {
                // ===== 扫码确认界面 =====
                val order = state.selectedOrder!!

                // 相机预览
                if (showScanner) {
                    Box(Modifier.fillMaxWidth().fillMaxHeight(0.35f)) {
                        QrCodeScanner(onBarcodeScanned = { viewModel.scanBarcode(it.trim()) })
                        Row(Modifier.align(Alignment.TopEnd).padding(8.dp)) {
                            Button(onClick = { showScanner = false },
                                colors = ButtonDefaults.buttonColors(containerColor = Color.Red)) { Text("关闭扫码") }
                        }
                    }
                }

                // 扫码按钮
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                    horizontalArrangement = Arrangement.Center) {
                    Button(onClick = { showScanner = !showScanner },
                        modifier = Modifier.fillMaxWidth().height(52.dp)) {
                        Icon(Icons.Default.QrCodeScanner, contentDescription = null, modifier = Modifier.size(24.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(if (showScanner) "关闭扫码" else "扫码替代部品", fontSize = 16.sp)
                    }
                }

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                // 消息
                state.scanMsg?.let { msg ->
                    val isError = msg.contains("无匹配") || msg.contains("失败") || msg.contains("无效")
                    Surface(
                        color = if (isError) Color(0xFFD32F2F) else Color(0xFF388E3C),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) { Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp) }
                }

                // 进度
                Spacer(Modifier.height(4.dp))
                Text("已完成: ${state.confirmedCount} / ${state.totalCount}",
                    modifier = Modifier.padding(horizontal = 16.dp),
                    style = MaterialTheme.typography.titleMedium,
                    color = if (state.allDone) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)

                // 匹配结果显示
                if (state.showCandidates && state.matchCandidates.isNotEmpty()) {
                    // 多条候选列表
                    Text("请选择匹配的明细：", modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.bodyMedium)
                    LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp),
                        verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(state.matchCandidates) { c ->
                            Card(onClick = { viewModel.selectCandidate(c) }, Modifier.fillMaxWidth()) {
                                Column(Modifier.padding(12.dp)) {
                                    Text("替代料: ${c.substitutePartNo} → 缺料: ${c.originalPartNo}",
                                        style = MaterialTheme.typography.bodyMedium)
                                    Text("来源: ${c.sourceLocationCode} → 目标: ${c.targetLocationCode} | 数量: ${c.quantity.toInt()}",
                                        style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                }
                            }
                        }
                    }
                } else if (state.matchedDetail != null) {
                    // 单条匹配结果
                    val m = state.matchedDetail!!
                    Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                        colors = CardDefaults.cardColors(containerColor = Color(0xFFE3F2FD))) {
                        Column(Modifier.padding(12.dp)) {
                            Text("替代料: ${m.substitutePartNo}", style = MaterialTheme.typography.titleSmall)
                            Text("来源库位: ${m.sourceLocationCode}", style = MaterialTheme.typography.bodySmall)
                            Text("缺料: ${m.originalPartNo}", style = MaterialTheme.typography.titleSmall)
                            Text("目标库位: ${m.targetLocationCode}", style = MaterialTheme.typography.bodySmall)
                            Text("数量: ${m.quantity.toInt()}", style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.primary)
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { viewModel.cancelCurrentMatch() },
                            modifier = Modifier.weight(1f)) { Text("取消重扫") }
                        Button(onClick = { viewModel.confirmDetail() },
                            modifier = Modifier.weight(1f)) { Text("确认") }
                    }
                }

                Spacer(Modifier.height(8.dp))

                // 未确认明细列表（按来源库位排列）
                Text("明细列表", style = MaterialTheme.typography.titleSmall,
                    modifier = Modifier.padding(horizontal = 16.dp))
                LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    items(order.details) { d ->
                        val isDone = d.status == 2
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isDone) MaterialTheme.colorScheme.primaryContainer
                                else MaterialTheme.colorScheme.surface)) {
                            Row(Modifier.padding(8.dp), horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text("${d.substitutePartNo} [${d.sourceLocationCode}] → ${d.originalPartNo} [${d.targetLocationCode}]",
                                        fontSize = 12.sp)
                                    Text("数量: ${d.quantity.toInt()}", fontSize = 11.sp, color = Color.Gray)
                                }
                                Text(if (isDone) "✓" else "待确认",
                                    color = if (isDone) Color(0xFF4CAF50) else Color.Gray, fontSize = 13.sp)
                            }
                        }
                    }
                }

                // 提交按钮（全部确认后显示）
                if (state.allDone) {
                    Button(onClick = { viewModel.confirmAll() },
                        modifier = Modifier.fillMaxWidth().padding(16.dp).height(48.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) {
                        Text("提交并完成移库", fontSize = 16.sp)
                    }
                }
            } else {
                // ===== 订单列表界面 =====
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待确认移库订单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待确认订单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order.id) }, Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(12.dp)) {
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically) {
                                    Text(order.orderNo, style = MaterialTheme.typography.titleMedium)
                                    Surface(shape = MaterialTheme.shapes.small,
                                        color = MaterialTheme.colorScheme.primaryContainer) {
                                        Text("待确认", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                                            fontSize = 12.sp)
                                    }
                                }
                                Text("已确认: ${order.confirmedCount} / ${order.detailCount}",
                                    style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                            }
                        }
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
cd mobile-android && ./gradlew :app:compileDebugKotlin
```

---

### Task 10: 集成测试验证

- [ ] **Step 1: 启动后端**

```bash
cd dip-system/api && dotnet run
```
预期：Swagger 中出现 `/api/v1/substitute/orders` 相关端点

- [ ] **Step 2: 启动前端**

```bash
cd dip-system/frontend-web && npm run dev
```
预期：替代料移库页面正常显示，新建/编辑弹窗功能正常

- [ ] **Step 3: 编译 Android**

```bash
cd mobile-android && ./gradlew :app:assembleDebug
```
预期：编译成功，生成 APK

---

### Task 11: Git 提交

- [ ] **Step 1: 提交**

```bash
git add dip-system/api/Models/SubstituteOrder.cs
git add dip-system/api/Data/AppDbContext.cs
git add dip-system/api/Services/SubstituteService.cs
git add dip-system/api/Controllers/SubstituteController.cs
git add dip-system/api/Program.cs
git add dip-system/frontend-web/src/pages/SubstituteList.tsx
git add mobile-android/app/src/main/java/com/dip/material/data/models/Models.kt
git add mobile-android/app/src/main/java/com/dip/material/data/network/ApiService.kt
git add mobile-android/app/src/main/java/com/dip/material/data/repository/AppRepository.kt
git add mobile-android/app/src/main/java/com/dip/material/ui/substitute/SubstituteViewModel.kt
git add mobile-android/app/src/main/java/com/dip/material/ui/substitute/SubstituteScreen.kt
git add dip-system/docs/superpowers/specs/2026-07-13-substitute-redesign-design.md
git add dip-system/docs/superpowers/plans/2026-07-13-substitute-redesign-plan.md
git commit -m "feat: 替代料移库功能重做 — 订单+明细+扫码确认+自动移库
Co-Authored-By: Claude <noreply@anthropic.com>"
```
