# DIP物料管理系统 重架构 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复后端 P0 数据完整性问题，添加多租户支持，重架构 PC 前端（TypeScript + Composable 复用 + 补全缺失页面），对齐 Android 端 API 契约

**Architecture:** .NET 9.0 Clean Architecture 后端 + Vue 3 TypeScript PC 前端 + Kotlin Compose Android 端。API 通过 ApiResponse<T> 统一信封，Swagger → openapi-typescript 生成前端类型。多租户通过共享库 + TenantId + HasQueryFilter 实现隔离。

**Tech Stack:** C# .NET 9.0 + EF Core + MySQL 8.0 + Redis 7 + RabbitMQ 3.12 + JWT | Vue 3 + TypeScript + Element Plus + Pinia + SignalR | Kotlin + Compose + Retrofit + Room

**Design Spec:** `docs/superpowers/specs/2026-07-07-dip-rearchitecture-design.md`

**Prerequisites:** 后端可编译（`dotnet build` 通过），MySQL/Redis/RabbitMQ 可用（`docker-compose up -d`），前端可启动（`npm run dev`）

## Global Constraints

- 所有 API 响应使用 `ApiResponse<T>` 信封：`{ code: number, data?: T, message: string }`，`code: 0` 为成功
- API 路径统一规则：新增路径单数名词，已有复数路径（orders、parts）保持不变
- 前端使用 TypeScript（`api/` 和 `composables/` 层强制 `.ts`，views 用 `<script setup lang="ts">`）
- 多租户通过 `X-Tenant-Id` Header 传递，JWT claims 含 `tenantId`
- 所有库存写操作必须在显式 `IDbContextTransaction` 中执行
- Version 并发控制：每次 SaveChanges 前手动 `entity.Version++`

---

## Phase 1: 地基修复（P0 Bug + API 规范化）

### Task 1: 修复 FreezeAsync 双重扣减 + Core/Facade 拆分

**Files:**
- Modify: `backend/DIP.Infrastructure/Services/InventoryService.cs`

**Interfaces:**
- Produces: `FreezeCoreAsync(long partId, long locationId, decimal qty)` — 纯内存操作，不改 Quantity，只设 Status=2；不调 SaveChanges
- Produces: `FreezeAsync(long partId, long locationId, decimal qty)` — Facade，调 FreezeCoreAsync + SaveChangesAsync
- Produces: `DeductCoreAsync(long partId, long locationId, decimal qty)` — 纯内存：lot.Quantity -= qty, lot.Version++；不 SaveChanges
- Produces: `DeductAsync(long partId, long locationId, decimal qty)` — Facade

- [ ] **Step 1: 读取当前 InventoryService.cs 的 FreezeAsync 和 DeductAsync 实现**

```bash
# 确认当前文件路径和行号
grep -n "FreezeAsync\|DeductAsync\|private.*async.*Freeze\|private.*async.*Deduct" backend/DIP.Infrastructure/Services/InventoryService.cs
```

- [ ] **Step 2: 重写 FreezeAsync — 移除 Quantity 扣减**

找到 FreezeAsync 中类似以下代码：
```csharp
lot.Quantity -= deductFromLot;
if (lot.Quantity <= 0) lot.Status = 3;
```

替换为：
```csharp
// 仅冻结状态，不改变 Quantity。数量扣减在 DeductAsync 中进行
lot.Status = 2; // 冻结
lot.Version++;
```

- [ ] **Step 3: 新增 FreezeCoreAsync / DeductCoreAsync（纯内存版本）**

在 InventoryService.cs 中添加：

```csharp
/// <summary>
/// 纯内存冻结：只改 Status，不改 Quantity，不 SaveChanges。
/// 供编排方法在显式事务中调用。
/// </summary>
internal async Task FreezeCoreAsync(long partId, long locationId, decimal qty)
{
    var lot = await _db.InventoryLots
        .Where(l => l.PartId == partId && l.LocationId == locationId
                    && l.Status == 1 && l.Quantity >= qty)
        .OrderBy(l => l.ReceiptDate)
        .FirstOrDefaultAsync();

    if (lot == null)
        throw new BusinessException("库存不足", 30001);

    lot.Status = 2; // 冻结
    lot.Version++;

    // 更新 Inventory 汇总
    var inventory = await _db.Inventories
        .FirstAsync(i => i.PartId == partId && i.LocationId == locationId);
    inventory.AvailableQty -= qty;
    inventory.FrozenQty += qty;
    inventory.Version++;
}

/// <summary>
/// 纯内存出库：扣减 Quantity，不 SaveChanges。
/// </summary>
internal async Task DeductCoreAsync(long partId, long locationId, decimal qty)
{
    var lot = await _db.InventoryLots
        .Where(l => l.PartId == partId && l.LocationId == locationId
                    && l.Status == 2 && l.Quantity >= qty)
        .FirstOrDefaultAsync();

    if (lot == null)
        throw new BusinessException("无已冻结库存可供出库", 30003);

    lot.Quantity -= qty;
    if (lot.Quantity <= 0)
        lot.Status = 3; // 已消耗
    lot.Version++;

    var inventory = await _db.Inventories
        .FirstAsync(i => i.PartId == partId && i.LocationId == locationId);
    inventory.FrozenQty -= qty;
    inventory.TotalQty -= qty;
    // AvailableQty 不变 — 已在冻结时扣减
    inventory.Version++;
}
```

- [ ] **Step 4: 重构现有 FreezeAsync / DeductAsync 为 Facade**

```csharp
public async Task FreezeAsync(long partId, long locationId, decimal qty)
{
    await FreezeCoreAsync(partId, locationId, qty);
    await _db.SaveChangesAsync();
}

public async Task DeductAsync(long partId, long locationId, decimal qty)
{
    await DeductCoreAsync(partId, locationId, qty);
    await _db.SaveChangesAsync();
}
```

- [ ] **Step 5: 同样拆解其他需要编排的库存方法（Thaw、Add、Cancel）**

按相同模式：每个方法拆为 `XxxCoreAsync`（不保存）和 `XxxAsync`（保存）。命名规则：Core 后缀 = 纯内存操作。

```csharp
internal async Task ThawCoreAsync(long partId, long locationId, decimal qty) { /* FrozenQty-=, AvailableQty+= */ }
internal async Task AddCoreAsync(long partId, long locationId, decimal qty, string batchNo, DateTime receiptDate) { /* TotalQty+=, AvailableQty+= */ }
internal async Task CancelCoreAsync(long movementId) { /* 根据 StockMovement 反向操作 */ }
```

- [ ] **Step 6: 编译验证**

```bash
cd D:\DIP_Product\backend
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: 运行现有集成测试**

```bash
dotnet test backend/DIP.IntegrationTests
```

Expected: All tests pass. 如果 FreezeAsync 测试依赖旧行为（Quantity 扣减），更新测试以验证 Status 变更。

- [ ] **Step 8: Commit**

```bash
git add backend/DIP.Infrastructure/Services/InventoryService.cs
git commit -m "fix: remove double-deduction in FreezeAsync, split into Core/Facade pattern

FreezeAsync now only changes Status to 2 (frozen), Quantity deduction
only happens in DeductAsync. Added Core versions (no SaveChanges) for
use within explicit transactions in orchestration methods.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 2: 修复 MySQL 乐观锁 + 异常中间件兜底

**Files:**
- Modify: `backend/DIP.Infrastructure/Data/DIPDbContext.cs`
- Modify: `backend/DIP.API/Middleware/ExceptionHandlingMiddleware.cs`

**Interfaces:**
- Consumes: Entity `Version` property (existing in Inventory, InventoryLot)
- Produces: `IsConcurrencyToken()` 配置替代 `IsRowVersion()`；中间件捕获 `DbUpdateConcurrencyException` 返回 409

- [ ] **Step 1: 修改 DIPDbContext — 移除 IsRowVersion()，改为 IsConcurrencyToken()**

在 `OnModelCreating` 中找到：
```csharp
e.Property(i => i.Version).IsRowVersion();
```

替换为：
```csharp
// MySQL 不支持 IsRowVersion()，使用 IsConcurrencyToken()
// 需在 SaveChanges 前手动 entity.Version++
e.Property(i => i.Version).IsConcurrencyToken();
```

对 `Inventory` 和 `InventoryLot` 两个实体的配置都做修改。

- [ ] **Step 2: 修改异常中间件 — 捕获并发冲突**

读取 `backend/DIP.API/Middleware/ExceptionHandlingMiddleware.cs`，在 `try-catch` 链中添加（放在 `catch (Exception ex)` 之前）：

```csharp
catch (DbUpdateConcurrencyException)
{
    ctx.Response.StatusCode = 409;
    ctx.Response.ContentType = "application/json";
    var response = new
    {
        code = 30005,
        message = "数据已被其他用户修改，请刷新后重试",
        data = (object?)null,
        timestamp = DateTime.UtcNow
    };
    await ctx.Response.WriteAsJsonAsync(response);
}
```

需要添加 using：
```csharp
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 3: 编译并运行测试**

```bash
cd D:\DIP_Product\backend
dotnet build
dotnet test backend/DIP.IntegrationTests
```

- [ ] **Step 4: 手动验证并发冲突场景**

启动后端，用两个并发请求测试库存扣减，预期返回 `code: 30005` + HTTP 409。

```bash
# Terminal 1: 启动后端
dotnet run --project backend/DIP.API
```

- [ ] **Step 5: Commit**

```bash
git add backend/DIP.Infrastructure/Data/DIPDbContext.cs backend/DIP.API/Middleware/ExceptionHandlingMiddleware.cs
git commit -m "fix: replace IsRowVersion with IsConcurrencyToken for MySQL, catch DbUpdateConcurrencyException in middleware

MySQL does not support IsRowVersion(); use IsConcurrencyToken + manual
Version++ instead. Exception middleware now returns HTTP 409 with
code 30005 for concurrency conflicts instead of 500.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 3: 显式事务包装（9 个编排方法）

**Files:**
- Modify: `backend/DIP.Infrastructure/Services/PrepService.cs`
- Modify: `backend/DIP.Infrastructure/Services/LoadingService.cs`
- Modify: `backend/DIP.Infrastructure/Services/OnlineService.cs`
- Modify: `backend/DIP.Infrastructure/Services/ReturnService.cs`
- Modify: `backend/DIP.Infrastructure/Services/StockCountService.cs`
- Modify: `backend/DIP.Infrastructure/Services/TransferService.cs`

**Interfaces:**
- Consumes: 各 Service 的 `XxxCoreAsync` 方法（Task 1 产出）
- Produces: 所有编排方法内使用 `await using var tx = await _db.Database.BeginTransactionAsync()`

- [ ] **Step 1: 改造 PrepService.ScanPrepAsync — 作为模板**

```csharp
public async Task ScanPrepAsync(PrepScanRequest request)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        // Step 1: 冻结库存（不保存）
        await _inventory.FreezeCoreAsync(request.PartId, request.LocationId, request.Quantity);

        // Step 2: 写入备料记录
        var scanRecord = new PrepScanRecord
        {
            PrepDetailId = request.PrepDetailId,
            SourceLocationId = request.LocationId,
            SourceLocationCode = request.LocationCode,
            BatchNo = request.BatchNo,
            Quantity = request.Quantity,
            ScannedBarcode = request.Barcode,
            OperatorId = request.OperatorId
        };
        _db.PrepScanRecords.Add(scanRecord);

        // Step 3: 更新备料明细
        var detail = await _db.PrepDetails.FindAsync(request.PrepDetailId);
        detail.ActualQty += request.Quantity;
        if (detail.ActualQty >= detail.RequiredQty)
            detail.Status = 2; // 已备料

        // Step 4: 写入库存流水
        var movement = new StockMovement
        {
            PartId = request.PartId,
            PartNo = request.PartNo,
            LocationId = request.LocationId,
            LocationCode = request.LocationCode,
            BatchNo = request.BatchNo,
            MovementType = 2, // 备料冻结
            Quantity = request.Quantity,
            ReferenceType = "PrepDetail",
            ReferenceId = request.PrepDetailId,
            OperatorId = request.OperatorId
        };
        _db.StockMovements.Add(movement);

        // Step 5: 一次保存
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}
```

- [ ] **Step 2: 对以下方法应用相同事务模式**

| Service | 方法 | 调用 Core 方法 |
|---------|------|---------------|
| PrepService | `CancelDetailAsync` | ThawCoreAsync |
| LoadingService | `ConfirmAsync` | AddCoreAsync |
| LoadingService | `CancelAsync` | CancelCoreAsync |
| OnlineService | `ConfirmAsync` | DeductCoreAsync |
| OnlineService | `CancelAsync` | CancelCoreAsync |
| ReturnService | `ConfirmAsync` | AddCoreAsync |
| StockCountService | `ConfirmAsync` | AddCoreAsync / DeductCoreAsync |
| TransferService | `ExecuteAsync` | DeductCoreAsync + AddCoreAsync |

每个方法改为：
```csharp
await using var tx = await _db.Database.BeginTransactionAsync();
try { /* 编排逻辑 + SaveChangesAsync */ await tx.CommitAsync(); }
catch { await tx.RollbackAsync(); throw; }
```

- [ ] **Step 3: 编译并运行集成测试**

```bash
dotnet build
dotnet test backend/DIP.IntegrationTests
```

- [ ] **Step 4: Commit**

```bash
git add backend/DIP.Infrastructure/Services/
git commit -m "fix: wrap all orchestration methods in explicit IDbContextTransaction

9 methods across PrepService, LoadingService, OnlineService, ReturnService,
StockCountService, TransferService now use Core-pattern methods + single
SaveChangesAsync within an explicit transaction. Prevents partial-commit
inventory corruption.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 4: ApiResponse<T> 统一封装 + 改写 Controller 返回类型

**Files:**
- Create: `backend/DIP.API/Models/ApiResponse.cs`
- Modify: `backend/DIP.API/Controllers/AuthController.cs`
- Modify: `backend/DIP.API/Controllers/PrepController.cs`
- Modify: `backend/DIP.API/Controllers/PartController.cs`
- Modify: `backend/DIP.API/Controllers/InventoryController.cs`
- Modify: `backend/DIP.API/Controllers/OrderController.cs`
- Modify: `backend/DIP.API/Controllers/LoadingController.cs`
- Modify: `backend/DIP.API/Controllers/OnlineController.cs`
- Modify: `backend/DIP.API/Controllers/LocationController.cs`
- Modify: `backend/DIP.API/Controllers/AbnormalController.cs`
- Modify: `backend/DIP.API/Controllers/ReturnController.cs`
- Modify: `backend/DIP.API/Controllers/StockCountController.cs`
- Modify: `backend/DIP.API/Controllers/TransferController.cs`

**Interfaces:**
- Produces: `ApiResponse<T>` record, `ApiResult` static factory
- Produces: 所有 Controller 方法返回 `ApiResponse<T>` 而非匿名对象

- [ ] **Step 1: 创建 ApiResponse.cs**

```csharp
// backend/DIP.API/Models/ApiResponse.cs
namespace DIP.API.Models;

public record ApiResponse<T>(int Code, T? Data, string Message);

public static class ApiResult
{
    public static ApiResponse<T> Success<T>(T data) => new(0, data, "ok");
    public static ApiResponse<T> Fail<T>(int code, string message) => new(code, default, message);
    public static ApiResponse<object?> Fail(int code, string message) => new(code, null, message);
}
```

- [ ] **Step 2: 改写 AuthController.Login 作为模板**

修改前：
```csharp
return Ok(new { code = 0, data = result, message = "ok" });
```

修改后：
```csharp
return Ok(ApiResult.Success(result));
```

错误场景：
```csharp
if (user == null)
    return Unauthorized(ApiResult.Fail(401, "用户名或密码错误"));
```

- [ ] **Step 3: 对所有 Controller 进行同样改写**

找到所有 `return Ok(new { code = ... })` 模式，替换为 `return Ok(ApiResult.Success(...))`。
分页列表返回 `ApiResult.Success(new { items, total, page, pageSize })`。

- [ ] **Step 4: 编译验证**

```bash
dotnet build
```

Expected: 0 errors。如果有编译错误（匿名类型不兼容），逐个修正。

- [ ] **Step 5: 运行集成测试**

```bash
dotnet test backend/DIP.IntegrationTests
```

- [ ] **Step 6: Commit**

```bash
git add backend/DIP.API/Models/ backend/DIP.API/Controllers/
git commit -m "refactor: replace anonymous response objects with ApiResponse<T> wrapper

All controllers now return typed ApiResponse<T> via ApiResult.Success/Fail.
Swashbuckle can now correctly infer the 'data' type in OpenAPI schema,
enabling type-safe code generation for frontend and Android.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 5: API 路径统一

**Files:**
- Modify: `frontend-web/src/api/prep.js` → 路径从 `/preps` 改为 `/prep`
- Modify: `backend/DIP.API/Controllers/PrepController.cs` → Route 确认为 `api/v1/prep`

- [ ] **Step 1: 修改前端 API 路径**

在 `frontend-web/src/api/prep.js` 中找到所有 `/preps` 替换为 `/prep`：

```javascript
// 修改前
const BASE = '/api/v1/preps'

// 修改后
const BASE = '/api/v1/prep'
```

- [ ] **Step 2: 确认后端 PrepController Route 一致**

检查 `backend/DIP.API/Controllers/PrepController.cs`：
```csharp
[Route("api/v1/prep")]  // 确认是单数
```

- [ ] **Step 3: 修改 Dashboard.vue — API 路径修正**

`frontend-web/src/views/Dashboard.vue` 第 75 行：
```javascript
// 修改前
request.get('/prep', { params: { page: 1, pageSize: 1 } })

// 修改后
import { getPrepList } from '@/api/prep'
getPrepList({ page: 1, pageSize: 1 })
```

- [ ] **Step 4: 启动前后端验证**

```bash
# Terminal 1
dotnet run --project backend/DIP.API

# Terminal 2
cd frontend-web && npm run dev
```

验证 Dashboard 仪表盘统计数字正确显示（非 0）。

- [ ] **Step 5: Commit**

```bash
git add frontend-web/src/api/prep.js frontend-web/src/views/Dashboard.vue
git commit -m "fix: unify API path to /api/v1/prep (singular), fix Dashboard stats

Frontend prep API path changed from /preps to /prep. Dashboard now uses
getPrepList() instead of raw request.get(), fixing the zero-stats bug.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Phase 2: 多租户

### Task 6: BaseEntity 加 TenantId + ITenantProvider + TenantMiddleware

**Files:**
- Modify: `backend/DIP.Domain/Entities/BaseEntity.cs`
- Create: `backend/DIP.Domain/Services/ITenantProvider.cs`
- Create: `backend/DIP.Infrastructure/Services/TenantProvider.cs`
- Create: `backend/DIP.API/Middleware/TenantMiddleware.cs`
- Modify: `backend/DIP.API/Program.cs`
- Modify: `backend/DIP.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: BaseEntity 加 TenantId**

```csharp
// backend/DIP.Domain/Entities/BaseEntity.cs
public abstract class BaseEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }        // ← 新增
    public bool IsDeleted { get; set; }
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: 创建 ITenantProvider 接口和实现**

```csharp
// backend/DIP.Domain/Services/ITenantProvider.cs
namespace DIP.Domain.Services;

public interface ITenantProvider
{
    long CurrentTenantId { get; }
    void SetCurrentTenantId(long tenantId);
}

// backend/DIP.Infrastructure/Services/TenantProvider.cs
namespace DIP.Infrastructure.Services;

public class TenantProvider : ITenantProvider
{
    private readonly AsyncLocal<long> _tenantId = new();

    public long CurrentTenantId => _tenantId.Value;

    public void SetCurrentTenantId(long tenantId) => _tenantId.Value = tenantId;
}
```

- [ ] **Step 3: 创建 TenantMiddleware**

```csharp
// backend/DIP.API/Middleware/TenantMiddleware.cs
using DIP.Domain.Services;
using System.Security.Claims;

namespace DIP.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ITenantProvider tenantProvider)
    {
        // 从 Header 提取租户 ID
        var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(header) || !long.TryParse(header, out var tenantId))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new
            {
                code = 400,
                message = "缺少 X-Tenant-Id 请求头",
                data = (object?)null
            });
            return;
        }

        // 校验 JWT tenantId claim 与 Header 一致（防冒充）
        var jwtTenant = ctx.User.FindFirst("tenantId")?.Value;
        if (!string.IsNullOrEmpty(jwtTenant) && jwtTenant != header)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new
            {
                code = 403,
                message = "租户身份校验失败",
                data = (object?)null
            });
            return;
        }

        tenantProvider.SetCurrentTenantId(tenantId);
        await _next(ctx);
    }
}
```

- [ ] **Step 4: 注册中间件和 TenantProvider**

在 `DIP.Infrastructure/DependencyInjection.cs` 中：
```csharp
services.AddScoped<ITenantProvider, TenantProvider>();
```

在 `DIP.API/Program.cs` 中，Authentication 之后、Authorization 之前：
```csharp
app.UseMiddleware<TenantMiddleware>();
```

- [ ] **Step 5: 编译**

```bash
dotnet build
```

- [ ] **Step 6: Commit**

```bash
git add backend/
git commit -m "feat: add TenantId to BaseEntity, ITenantProvider, TenantMiddleware

Multi-tenant foundation: TenantId column on all entities, AsyncLocal-based
tenant provider, middleware extracting X-Tenant-Id header with JWT validation.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 7: TenantSaveChangesInterceptor + HasQueryFilter

**Files:**
- Create: `backend/DIP.Infrastructure/Data/TenantSaveChangesInterceptor.cs`
- Modify: `backend/DIP.Infrastructure/Data/DIPDbContext.cs`
- Modify: `backend/DIP.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: 创建 TenantSaveChangesInterceptor**

```csharp
// backend/DIP.Infrastructure/Data/TenantSaveChangesInterceptor.cs
using DIP.Domain.Entities;
using DIP.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DIP.Infrastructure.Data;

public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenantProvider;

    public TenantSaveChangesInterceptor(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        var context = eventData.Context;
        if (context == null) return result;

        var tenantId = _tenantProvider.CurrentTenantId;
        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>()
                     .Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.TenantId == 0)
                entry.Entity.TenantId = tenantId;
        }

        return result;
    }
}
```

- [ ] **Step 2: DIPDbContext 添加 HasQueryFilter**

修改 `OnModelCreating`，在方法开头获取 `_tenantProvider`：

```csharp
// DIPDbContext.cs — 构造函数增加参数
private readonly ITenantProvider _tenantProvider;

public DIPDbContext(DbContextOptions<DIPDbContext> options, ITenantProvider tenantProvider)
    : base(options)
{
    _tenantProvider = tenantProvider;
}
```

在所有 `builder.Entity<T>` 配置中添加 HasQueryFilter。使用扩展方法减少重复：

```csharp
// OnModelCreating 末尾添加
foreach (var entityType in builder.Model.GetEntityTypes())
{
    if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
    {
        builder.Entity(entityType.ClrType)
            .HasQueryFilter(ConvertFilter(entityType.ClrType));
    }
}
```

或直接在每个实体配置中追加：
```csharp
e.HasQueryFilter(x => x.TenantId == _tenantProvider.CurrentTenantId);
```

> **注意：** EF Core 的 HasQueryFilter 会与现有的 `!IsDeleted` 过滤器自动 AND 组合，无需特殊处理。

- [ ] **Step 3: 注册拦截器**

在 `DIP.Infrastructure/DependencyInjection.cs` 的 `AddInfrastructure` 中：

```csharp
services.AddSingleton<TenantSaveChangesInterceptor>();

services.AddDbContext<DIPDbContext>((sp, options) =>
{
    var tenantProvider = sp.GetRequiredService<ITenantProvider>();
    var interceptor = sp.GetRequiredService<TenantSaveChangesInterceptor>();
    options.UseMySql(connectionString, serverVersion)
           .AddInterceptors(interceptor);
});
```

- [ ] **Step 4: 编译 + 创建并应用迁移**

```bash
dotnet build
dotnet ef migrations add AddTenantId --project backend/DIP.Infrastructure --startup-project backend/DIP.API
dotnet ef database update --project backend/DIP.Infrastructure --startup-project backend/DIP.API
```

- [ ] **Step 5: 运行集成测试**

```bash
dotnet test backend/DIP.IntegrationTests
```

> 如果测试失败（因 TenantId=0 被过滤器排除），需要在测试中设置 `TenantProvider.SetCurrentTenantId(1)`。

- [ ] **Step 6: Commit**

```bash
git add backend/
git commit -m "feat: add TenantSaveChangesInterceptor and HasQueryFilter for multi-tenant isolation

All BaseEntity types now auto-receive TenantId on insert via interceptor.
Global query filter ensures reads are tenant-isolated. Combined with
existing IsDeleted filter via automatic AND chaining.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 8: JWT tenantId + Redis 前缀 + 种子数据拆分

**Files:**
- Modify: `backend/DIP.Infrastructure/Services/AuthService.cs`
- Modify: `backend/DIP.Infrastructure/Services/JwtTokenService.cs`
- Modify: `backend/DIP.Infrastructure/Redis/RedisCacheService.cs`
- Modify: `backend/DIP.Infrastructure/Services/DatabaseSeeder.cs`
- Modify: `backend/DIP.API/Program.cs`

- [ ] **Step 1: JWT 加 tenantId claim**

在 AuthService.LoginAsync 中：
```csharp
var claims = new Dictionary<string, string>
{
    { "sub", user.Id.ToString() },
    { "username", user.Username },
    { "role", user.Role.RoleCode },
    { "tenantId", user.TenantId.ToString() },  // ← 新增
    { "lineId", user.LineId?.ToString() ?? "" }
};
```

JwtTokenService.GenerateAccessToken 签名改为接受 `Dictionary<string, string>`：
```csharp
public string GenerateAccessToken(Dictionary<string, string> claims)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var tokenClaims = claims.Select(kv => new Claim(kv.Key, kv.Value));
    var token = new JwtSecurityToken(
        claims: tokenClaims,
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

- [ ] **Step 2: Redis 缓存键加租户前缀**

在 RedisCacheService 中注入 `ITenantProvider`：

```csharp
public class RedisCacheService
{
    private readonly ITenantProvider _tenantProvider;
    // ...

    private string Key(string raw) => $"{_tenantProvider.CurrentTenantId}:{raw}";

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var fullKey = Key(key);
        // ... 原有实现
    }
}
```

- [ ] **Step 3: 种子数据拆分**

DatabaseSeeder.SeedAsync 拆分为：

```csharp
public async Task SeedAsync()
{
    // 全局数据（所有租户共享）
    await SeedRolesAsync();        // ADMIN, WAREHOUSE, SUPERVISOR, OPERATOR

    // 租户数据（按 TenantId=1 默认租户）
    await SeedOperatorAsync(1);    // admin/Admin123
    await SeedProductionLinesAsync(1);
    await SeedStationsAsync(1);
    await SeedWarehouseLocationsAsync(1);
}
```

- [ ] **Step 4: 编译并运行**

```bash
dotnet build
dotnet run --project backend/DIP.API
```

验证：登录后 JWT 中含 `tenantId` claim；Redis 键有租户前缀。

- [ ] **Step 5: Commit**

```bash
git add backend/
git commit -m "feat: add tenantId to JWT, Redis key prefix, split seed data

JWT now includes tenantId claim. Redis cache keys prefixed with {tenantId}:.
DatabaseSeeder split into global (roles) and per-tenant data.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Phase 3: 前端类型安全 + 消除重复

### Task 9: TypeScript 迁移 + openapi-typescript CI 集成

**Files:**
- Modify: `frontend-web/package.json`
- Modify: `frontend-web/tsconfig.json`
- Create: `frontend-web/scripts/generate-types.sh`
- Rename: `frontend-web/src/api/*.js` → `frontend-web/src/api/*.ts`
- Rename: `frontend-web/src/stores/*.js` → `frontend-web/src/stores/*.ts`

- [ ] **Step 1: 安装依赖**

```bash
cd D:\DIP_Product\frontend-web
npm install -D typescript @types/node openapi-typescript
npm install @microsoft/signalr
```

- [ ] **Step 2: 配置 tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "jsx": "preserve",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "esModuleInterop": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "skipLibCheck": true,
    "noEmit": true,
    "paths": {
      "@/*": ["./src/*"]
    },
    "baseUrl": "."
  },
  "include": ["src/**/*.ts", "src/**/*.tsx", "src/**/*.vue"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```

创建 `tsconfig.node.json`：
```json
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 3: 创建类型生成脚本**

```bash
#!/bin/bash
# frontend-web/scripts/generate-types.sh
set -e

echo "Starting backend for type generation..."
dotnet run --project ../backend/DIP.API &
BACKEND_PID=$!

for i in $(seq 1 30); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8400/swagger/v1/swagger.json 2>/dev/null || echo "000")
  if [ "$STATUS" = "200" ]; then break; fi
  sleep 1
done

curl -s http://localhost:8400/swagger/v1/swagger.json -o /tmp/swagger.json

if [ ! -s /tmp/swagger.json ]; then
  echo "ERROR: Swagger JSON 为空，后端可能启动失败" >&2
  kill $BACKEND_PID 2>/dev/null
  exit 1
fi

if ! jq empty /tmp/swagger.json 2>/dev/null; then
  echo "ERROR: Swagger JSON 格式无效" >&2
  kill $BACKEND_PID 2>/dev/null
  exit 1
fi

npx openapi-typescript /tmp/swagger.json -o src/api/types.ts
GEN_EXIT=$?

kill $BACKEND_PID 2>/dev/null

if [ $GEN_EXIT -ne 0 ] || [ ! -s src/api/types.ts ]; then
  echo "ERROR: openapi-typescript 生成失败" >&2
  exit 1
fi

echo "TypeScript types generated successfully"
```

- [ ] **Step 4: 重命名 API 文件为 .ts**

```bash
cd frontend-web/src/api
for f in *.js; do mv "$f" "${f%.js}.ts"; done

cd ../stores
for f in *.js; do mv "$f" "${f%.js}.ts"; done
```

为每个 API 文件添加类型导入：
```typescript
// api/prep.ts
import type { components } from './types'
type PrepOrderDto = components['schemas']['PrepOrderDto']
```

- [ ] **Step 5: 为现有 .vue 文件添加 `<script setup lang="ts">`**

逐个 .vue 文件，将 `<script setup>` 改为 `<script setup lang="ts">`。

- [ ] **Step 6: 验证编译**

```bash
npx vue-tsc --noEmit
```

Expected: 0 type errors。

- [ ] **Step 7: Commit**

```bash
git add frontend-web/
git commit -m "feat: migrate frontend to TypeScript, add openapi-typescript CI script

API and composable layers now in .ts. Vue SFCs use <script setup lang="ts">.
types.ts generated from Swagger JSON with fail-safe CI script.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 10: utils/statusMappers.ts + styles/global.css

**Files:**
- Create: `frontend-web/src/utils/statusMappers.ts`
- Create: `frontend-web/src/utils/format.ts`
- Create: `frontend-web/src/utils/constants.ts`
- Create: `frontend-web/src/styles/global.css`
- Modify: `frontend-web/src/main.ts`

- [ ] **Step 1: 创建 statusMappers.ts**

```typescript
// frontend-web/src/utils/statusMappers.ts
export const STATUS_MAPS = {
  prepStatus: {
    1: { text: '待备料', tag: 'info' as const },
    2: { text: '备料中', tag: 'warning' as const },
    3: { text: '已完成', tag: 'success' as const },
    4: { text: '已撤销', tag: 'danger' as const },
    5: { text: '已暂停', tag: '' as const },
  },
  kitCheck: {
    1: { text: '齐套', type: 'success' as const },
    2: { text: '部分齐套', type: 'warning' as const },
    3: { text: '不齐套', type: 'danger' as const },
  },
  priority: {
    1: { text: '普通', type: '' as const },
    2: { text: '加急', type: 'warning' as const },
    3: { text: '特急', type: 'danger' as const },
  },
  abnormalType: {
    1: '库存不足',
    2: '品质异常',
    3: '批次过期',
    4: 'MSL超时',
    5: '其他',
  } as Record<number, string>,
  severity: {
    1: { text: '低', type: 'info' as const },
    2: { text: '中', type: 'warning' as const },
    3: { text: '高', type: 'danger' as const },
  },
} as const
```

- [ ] **Step 2: 创建 format.ts 和 constants.ts**

```typescript
// format.ts
export function formatDateTime(date: string | Date): string {
  return new Date(date).toLocaleString('zh-CN')
}

export function formatNumber(n: number, decimals = 0): string {
  return n.toLocaleString('zh-CN', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
}

// constants.ts
export const API_BASE = '/api/v1'

export const API_PATHS = {
  prep: `${API_BASE}/prep`,
  orders: `${API_BASE}/orders`,
  inventory: `${API_BASE}/inventory`,
  parts: `${API_BASE}/parts`,
  loading: `${API_BASE}/loading`,
  online: `${API_BASE}/online`,
  locations: `${API_BASE}/locations`,
  abnormal: `${API_BASE}/abnormal`,
  return: `${API_BASE}/return`,
  count: `${API_BASE}/count`,
  transfer: `${API_BASE}/transfer`,
} as const
```

- [ ] **Step 3: 创建 global.css**

```css
/* frontend-web/src/styles/global.css */
.page-container { min-width: 1200px; }
.search-bar { margin-bottom: 16px; }
.table-toolbar { margin-bottom: 12px; }
.detail-card { margin-bottom: 16px; }
.stat-cards { margin-bottom: 20px; }
```

- [ ] **Step 4: 在 main.ts 中导入全局样式**

```typescript
import '@/styles/global.css'
```

- [ ] **Step 5: Commit**

```bash
git add frontend-web/src/utils/ frontend-web/src/styles/ frontend-web/src/main.ts
git commit -m "feat: add centralized statusMappers, format utils, API constants, global CSS

Eliminates duplicated status-to-text mapping across 5+ pages. Common CSS
patterns extracted to global.css. API paths centralized in constants.ts.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 11: usePagination composable + 改造现有列表页

**Files:**
- Create: `frontend-web/src/composables/usePagination.ts`
- Modify: `frontend-web/src/views/prep/PrepList.vue`
- Modify: `frontend-web/src/views/orders/OrderList.vue`
- Modify: `frontend-web/src/views/inventory/InventoryList.vue`
- Modify: `frontend-web/src/views/parts/PartList.vue`
- Modify: `frontend-web/src/views/locations/LocationList.vue`
- Modify: `frontend-web/src/views/online/OnlineList.vue`
- Modify: `frontend-web/src/views/abnormal/AbnormalList.vue`

- [ ] **Step 1: 创建 usePagination.ts**

```typescript
// frontend-web/src/composables/usePagination.ts
import { ref, reactive, onMounted } from 'vue'

interface PaginatedResponse<T> {
  items: T[]
  total: number
}

interface UsePaginationOptions<T> {
  fetchFn: (params: Record<string, any>) => Promise<{ data?: PaginatedResponse<T> }>
  transformParams?: (raw: Record<string, any>) => Record<string, any>
  immediate?: boolean
  onError?: () => void
}

export function usePagination<T>(options: UsePaginationOptions<T>) {
  const { immediate = true } = options
  const loading = ref(false)
  const data = ref<T[]>([])
  const total = ref(0)
  const queryParams = reactive<Record<string, any>>({ page: 1, pageSize: 20 })

  const fetchData = async () => {
    loading.value = true
    try {
      const params = options.transformParams
        ? options.transformParams({ ...queryParams })
        : { ...queryParams }
      const res = await options.fetchFn(params)
      data.value = res.data?.items ?? []
      total.value = res.data?.total ?? 0
    } catch {
      data.value = []
      total.value = 0
      options.onError?.()
    } finally {
      loading.value = false
    }
  }

  const search = () => { queryParams.page = 1; fetchData() }

  let debounceTimer: ReturnType<typeof setTimeout>
  const debouncedSearch = (delay = 300) => {
    clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => search(), delay)
  }

  const reset = () => {
    Object.keys(queryParams).forEach(k => {
      if (k !== 'page' && k !== 'pageSize') queryParams[k] = ''
    })
    queryParams.page = 1
    fetchData()
  }

  if (immediate) onMounted(fetchData)

  return { loading, data, total, queryParams, fetchData, search, debouncedSearch, reset }
}
```

- [ ] **Step 2: 改造 PrepList.vue 使用 usePagination**

`<script setup>` 中删除 `loading/tableData/total/queryParams/fetchData/handleSearch/handleReset` 的手动实现，替换为：

```typescript
import { usePagination } from '@/composables/usePagination'
import { getPrepList } from '@/api/prep'

const router = useRouter()
const { loading, data: tableData, total, queryParams, fetchData, search: handleSearch, reset: handleReset } = usePagination({
  fetchFn: (params) => getPrepList(params),
  onError: () => { tableData.value = []; total.value = 0 }
})
```

Template 中改为：
```html
<el-input v-model="queryParams.prepOrderNo" @input="debouncedSearch()" />
```

- [ ] **Step 3: 对其他列表页重复同样改造**

OrderList, InventoryList, PartList, LocationList, OnlineList, AbnormalList — 每个页面替换模板中 ~60 行重复逻辑为 usePagination。

- [ ] **Step 4: 启动前端验证**

```bash
npm run dev
```

验证每个列表页可以正常加载、搜索、分页。检查网络请求没有重复调用。

- [ ] **Step 5: Commit**

```bash
git add frontend-web/src/composables/usePagination.ts frontend-web/src/views/
git commit -m "refactor: extract usePagination composable, retrofit 7 list pages

Eliminates ~400 lines of duplicated pagination logic across 7 list views.
Includes debouncedSearch for input-bound search fields (300ms debounce).

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 12: useCrudDialog + useWebSocket composables

**Files:**
- Create: `frontend-web/src/composables/useCrudDialog.ts`
- Create: `frontend-web/src/composables/useWebSocket.ts`
- Create: `frontend-web/src/composables/useChart.ts`
- Modify: `frontend-web/src/views/parts/PartList.vue`
- Modify: `frontend-web/src/views/locations/LocationList.vue`
- Modify: `frontend-web/src/views/Dashboard.vue`

- [ ] **Step 1: 创建 useCrudDialog.ts**

```typescript
// frontend-web/src/composables/useCrudDialog.ts
import { ref, reactive, computed } from 'vue'

interface UseCrudDialogOptions<T> {
  createFn: (data: T) => Promise<void>
  updateFn: (id: number, data: T) => Promise<void>
  defaultForm: () => T
  onSuccess?: () => void
}

export function useCrudDialog<T extends Record<string, any>>(options: UseCrudDialogOptions<T>) {
  const visible = ref(false)
  const editId = ref<number | null>(null)
  const title = computed(() => editId.value ? '编辑' : '新增')
  const submitting = ref(false)
  const formData = reactive(options.defaultForm()) as T

  const open = (row?: T & { id?: number }) => {
    if (row?.id) {
      editId.value = row.id
      Object.assign(formData, structuredClone(row))
    } else {
      editId.value = null
      Object.assign(formData, options.defaultForm())
    }
    visible.value = true
  }

  const submit = async () => {
    submitting.value = true
    try {
      if (editId.value) {
        await options.updateFn(editId.value, { ...formData })
      } else {
        await options.createFn({ ...formData })
      }
      visible.value = false
      options.onSuccess?.()
    } finally {
      submitting.value = false
    }
  }

  return { visible, editId, title, submitting, formData, open, submit }
}
```

- [ ] **Step 2: 改造 PartList 使用 useCrudDialog**

删除手动实现的 `dialogVisible/dialogTitle/submitLoading/formData/editId/formRef` 逻辑，替换为：

```typescript
import { useCrudDialog } from '@/composables/useCrudDialog'

const dialog = useCrudDialog({
  createFn: (data) => createPart(data),
  updateFn: (id, data) => updatePart(id, data),
  defaultForm: () => ({ partNo: '', partName: '', unit: 'pcs', specification: '', minStock: 0, maxStock: 0, status: 1 }),
  onSuccess: () => fetchData()
})
```

Template 中绑定 `dialog.visible`, `dialog.title`, `dialog.submitting`, `dialog.formData`, `dialog.open`, `dialog.submit`。

- [ ] **Step 3: 同样改造 LocationList**

- [ ] **Step 4: 创建 useWebSocket.ts**

```typescript
// frontend-web/src/composables/useWebSocket.ts
import { ref, onMounted, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr'

interface AppNotification {
  id: string
  type: 'abnormal' | 'prep' | 'online'
  message: string
  createdAt: string
}

export function useWebSocket() {
  const connected = ref(false)
  const notifications = ref<AppNotification[]>([])

  let connection: HubConnection | null = null

  onMounted(async () => {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/notification')
      .withAutomaticReconnect([1000, 2000, 5000, 10000])
      .build()

    connection.on('AbnormalAlert', (msg: AppNotification) => {
      notifications.value.unshift(msg)
    })
    connection.on('PrepCompleted', (_msg: unknown) => { /* TODO: 刷新仪表盘 */ })
    connection.on('OnlineConfirmed', (_msg: unknown) => { /* TODO: 刷新仪表盘 */ })

    try {
      await connection.start()
      connected.value = true
    } catch { /* 重连交 withAutomaticReconnect */ }
  })

  onUnmounted(() => connection?.stop())

  return { connected, notifications }
}
```

- [ ] **Step 5: 改造 Dashboard.vue 使用 useWebSocket**

```typescript
import { useWebSocket } from '@/composables/useWebSocket'
const { connected, notifications } = useWebSocket()
```

在模板中添加连接状态指示器和通知计数。

- [ ] **Step 6: 创建 useChart.ts（骨架）**

```typescript
// frontend-web/src/composables/useChart.ts
import { ref, onMounted, onUnmounted } from 'vue'
import type { EChartsOption } from 'echarts'

export function useChart(option: EChartsOption) {
  const chartRef = ref<HTMLElement | null>(null)
  let instance: any = null

  onMounted(async () => {
    const { init } = await import('echarts')
    if (chartRef.value) {
      instance = init(chartRef.value)
      instance.setOption(option)
    }
  })

  onUnmounted(() => instance?.dispose())

  const resize = () => instance?.resize()
  return { chartRef, resize }
}
```

- [ ] **Step 7: Commit**

```bash
git add frontend-web/src/composables/ frontend-web/src/views/parts/ frontend-web/src/views/locations/ frontend-web/src/views/Dashboard.vue
git commit -m "feat: add useCrudDialog, useWebSocket, useChart composables

useCrudDialog standardizes create/edit dialog logic. useWebSocket manages
SignalR connection lifecycle. Dashboard now receives real-time notifications.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 13: client.ts 统一错误拦截 + 修复已知 Bug

**Files:**
- Modify: `frontend-web/src/api/client.ts`（或 `request.ts`）
- Modify: `frontend-web/src/views/Dashboard.vue`
- Modify: `frontend-web/src/views/prep/PrepList.vue`
- Modify: `frontend-web/src/views/inventory/InventoryList.vue`

- [ ] **Step 1: 实现统一错误拦截**

在 `client.ts` 的 Axios 响应拦截器中：

```typescript
import { ElMessage } from 'element-plus'
import axios from 'axios'

const client = axios.create({
  baseURL: '/api/v1',
  timeout: 10000,
})

// onFulfilled — HTTP 2xx 响应
client.interceptors.response.use(
  (response) => {
    const { code, message } = response.data
    if (code !== undefined && code !== 0) {
      ElMessage.error(message || '操作失败')
      return Promise.reject(new Error(message))
    }
    return response
  },
  // onRejected — HTTP 4xx/5xx 响应
  (error) => {
    if (error.response?.status === 401) {
      // 触发 Token 刷新（已有逻辑），不弹 toast
    } else if (error.response?.status === 403) {
      ElMessage.error('无权限执行此操作')
    } else if (error.response?.status >= 500) {
      ElMessage.error('服务器错误，请稍后重试')
    } else if (!error.response) {
      ElMessage.error('网络连接失败，请检查网络')
    } else {
      ElMessage.error(error.response.data?.message || '请求失败')
    }
    return Promise.reject(error)
  }
)

export default client
```

- [ ] **Step 2: 修复 Dashboard — API 路径**

```typescript
// 修改前（Dashboard.vue script）
import request from '@/api/request'
const res = await request.get('/prep', { params: { ... } })

// 修改后
import { getPrepList } from '@/api/prep'
const res = await getPrepList({ page: 1, pageSize: 1 })
```

- [ ] **Step 3: 修复 PrepList — 齐套检测调错 API**

找到 `handleKitCheck` 函数：
```javascript
// 修改前
const handleKitCheck = async (row) => {
  await cancelPrep(row.id)  // BUG: 调了取消接口
}

// 修改后
import { kitCheckPrep } from '@/api/prep'

const handleKitCheck = async (row: PrepOrderDto) => {
  try {
    await ElMessageBox.confirm('确认执行齐套检测？', '提示', { type: 'info' })
    await kitCheckPrep(row.id)
    ElMessage.success('齐套检测已完成')
    fetchData()
  } catch { /* 用户取消 */ }
}
```

确认 `api/prep.ts` 中有 `kitCheckPrep` 函数：
```typescript
export function kitCheckPrep(id: number) {
  return client.post(`/prep/${id}/kit-check`)
}
```

- [ ] **Step 4: 修复 InventoryList — 重复绑定**

找到并删除重复的 locationCode 输入框。

- [ ] **Step 5: 修复各页面产线硬编码**

PrepList.vue 中产线选择改为动态加载：
```typescript
import { getLines } from '@/api/location'

const lines = ref<{ id: number; lineName: string }[]>([])
onMounted(async () => {
  const res = await getLines()
  lines.value = res.data ?? []
})
```

Template：
```html
<el-select v-model="queryParams.lineId" placeholder="请选择产线" clearable>
  <el-option v-for="line in lines" :key="line.id" :label="line.lineName" :value="line.id" />
</el-select>
```

- [ ] **Step 6: Commit**

```bash
git add frontend-web/src/api/client.ts frontend-web/src/views/
git commit -m "fix: unified error handling in axios interceptor, fix 5 known bugs

- Dashboard now uses getPrepList() with correct API path
- PrepList kit-check button calls kitCheckPrep() instead of cancelPrep()
- InventoryList removed duplicate locationCode input
- Production line options loaded from API instead of hardcoded
- Axios interceptor handles code!=0, HTTP 4xx/5xx, and network errors centrally

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 14: 删除 material.js + Tenant Selector 组件

**Files:**
- Delete: `frontend-web/src/stores/material.js`
- Create: `frontend-web/src/stores/tenant.ts`
- Create: `frontend-web/src/components/TenantSelector.vue`
- Modify: `frontend-web/src/components/layout/AppHeader.vue`

- [ ] **Step 1: 删除 material.js**

```bash
rm frontend-web/src/stores/material.js
```

确认没有 import 引用该文件：
```bash
grep -r "material" frontend-web/src/ --include="*.ts" --include="*.vue" --include="*.js"
```

- [ ] **Step 2: 创建 tenant store**

```typescript
// frontend-web/src/stores/tenant.ts
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useTenantStore = defineStore('tenant', () => {
  const currentTenantId = ref<number>(1) // 默认租户
  const tenantName = ref<string>('默认工厂')

  const tenantHeader = computed(() => ({
    'X-Tenant-Id': String(currentTenantId.value)
  }))

  function setTenant(id: number, name: string) {
    currentTenantId.value = id
    tenantName.value = name
  }

  return { currentTenantId, tenantName, tenantHeader, setTenant }
})
```

- [ ] **Step 3: 创建 TenantSelector 组件**

```vue
<!-- frontend-web/src/components/TenantSelector.vue -->
<template>
  <el-select
    :model-value="tenant.currentTenantId"
    @update:model-value="handleChange"
    size="small"
    style="width: 160px"
  >
    <el-option :value="1" label="默认工厂" />
    <!-- 以后扩展更多租户 -->
  </el-select>
</template>

<script setup lang="ts">
import { useTenantStore } from '@/stores/tenant'

const tenant = useTenantStore()

function handleChange(id: number) {
  tenant.setTenant(id, '默认工厂')
  location.reload() // 切换租户刷新整个应用
}
</script>
```

- [ ] **Step 4: 将 TenantSelector 集成到 AppHeader**

在 AppHeader.vue 中添加 `<TenantSelector />`。

- [ ] **Step 5: 修改 client.ts 注入租户 Header**

```typescript
client.interceptors.request.use((config) => {
  const tenantStore = useTenantStore()
  config.headers['X-Tenant-Id'] = String(tenantStore.currentTenantId)
  return config
})
```

- [ ] **Step 6: Commit**

```bash
git add frontend-web/src/
git rm frontend-web/src/stores/material.js
git commit -m "refactor: remove unused material.js store, add tenant store and selector

material.js was a dead store with no consumers. Added tenant.ts store with
X-Tenant-Id header injection into axios requests. TenantSelector component
allows switching factories (reloads on switch).

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Phase 4: 补全缺失页面

### Task 15: Loading 上料记录页面

**Files:**
- Create: `frontend-web/src/api/loading.ts`
- Create: `frontend-web/src/views/loading/LoadingList.vue`
- Modify: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 创建 API 模块**

```typescript
// frontend-web/src/api/loading.ts
import client from './client'
import type { components } from './types'

type LoadingBatchDto = components['schemas']['LoadingBatchDto']
type PaginatedResponse<T> = { items: T[]; total: number }

export function getLoadingList(params: Record<string, any>) {
  return client.get<{ data: PaginatedResponse<LoadingBatchDto> }>('/loading/list', { params })
}
```

- [ ] **Step 2: 创建 LoadingList.vue**

```vue
<template>
  <div class="page-container">
    <el-card shadow="never">
      <div class="search-bar">
        <el-form :model="queryParams" inline>
          <el-form-item label="批次号">
            <el-input v-model="queryParams.batchNo" placeholder="请输入批次号" clearable @input="debouncedSearch()" />
          </el-form-item>
          <el-form-item label="状态">
            <el-select v-model="queryParams.status" placeholder="请选择状态" clearable @change="search">
              <el-option label="暂存" :value="1" />
              <el-option label="已确认" :value="2" />
              <el-option label="已撤销" :value="3" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" @click="search">查询</el-button>
            <el-button @click="reset">重置</el-button>
          </el-form-item>
        </el-form>
      </div>

      <el-table :data="data" v-loading="loading" border stripe>
        <el-table-column prop="batchNo" label="批次号" width="180" />
        <el-table-column prop="targetLocationCode" label="目标库位" width="120" />
        <el-table-column prop="operatorName" label="操作人" width="100" />
        <el-table-column prop="itemCount" label="明细数" width="80" />
        <el-table-column prop="status" label="状态" width="90" align="center">
          <template #default="{ row }">
            <StatusTag :status="row.status" />
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column prop="confirmedAt" label="确认时间" width="170">
          <template #default="{ row }">{{ row.confirmedAt ? formatDateTime(row.confirmedAt) : '-' }}</template>
        </el-table-column>
      </el-table>

      <PaginationBar v-model:page="queryParams.page" v-model:pageSize="queryParams.pageSize" :total="total" @change="fetchData" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { usePagination } from '@/composables/usePagination'
import { getLoadingList } from '@/api/loading'
import { formatDateTime } from '@/utils/format'
import StatusTag from '@/components/StatusTag.vue'
import PaginationBar from '@/components/PaginationBar.vue'

const { loading, data, total, queryParams, fetchData, search, debouncedSearch, reset } = usePagination({
  fetchFn: (params) => getLoadingList(params)
})
</script>
```

- [ ] **Step 3: 注册路由**

在 `router/index.ts` 中添加：
```typescript
{
  path: 'loading',
  name: 'Loading',
  component: () => import('@/views/loading/LoadingList.vue'),
  meta: { title: '上料管理' }
}
```

- [ ] **Step 4: Commit**

```bash
git add frontend-web/src/
git commit -m "feat: add Loading (上料记录) list page with API module and route

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 16: Return 退料管理页面

**Files:**
- Create: `frontend-web/src/api/return.ts`
- Create: `frontend-web/src/views/return/ReturnList.vue`
- Modify: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 创建 API 模块 + 页面**

退料列表页结构同 LoadingList，额外包含：
- 审核按钮（`status === 1` 时显示"确认退料"和"拒绝"按钮，仅班长/管理员可见）
- 关联备料单号列

API 方法：
```typescript
export function getReturnList(params: Record<string, any>) { /* ... */ }
export function confirmReturn(id: number) { return client.post(`/return/${id}/confirm`) }
export function rejectReturn(id: number) { return client.post(`/return/${id}/reject`) }
```

- [ ] **Step 2: 注册路由**

```typescript
{
  path: 'return',
  name: 'Return',
  component: () => import('@/views/return/ReturnList.vue'),
  meta: { title: '退料管理' }
}
```

- [ ] **Step 3: Commit**

```bash
git add frontend-web/src/
git commit -m "feat: add Return (退料) management page with approve/reject actions

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 17: StockCount 盘点 + Transfer 调拨页面

**Files:**
- Create: `frontend-web/src/api/count.ts`
- Create: `frontend-web/src/views/inventory/CountList.vue`
- Create: `frontend-web/src/api/transfer.ts`
- Create: `frontend-web/src/views/inventory/TransferList.vue`
- Modify: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 创建 CountList.vue**

盘点列表页：
- 盘点单号、类型（循环/全盘/抽盘）、状态、创建时间
- 盘点类型的 tag 使用 STATUS_MAPS
- 点击进入详情显示差异明细

- [ ] **Step 2: 创建 TransferList.vue**

调拨列表页：
- 调拨单号、源库位 → 目标库位、状态
- 状态筛选
- "执行调拨"操作按钮

- [ ] **Step 3: 注册路由**

```typescript
{
  path: 'inventory/count',
  name: 'StockCount',
  component: () => import('@/views/inventory/CountList.vue'),
  meta: { title: '盘点管理' }
},
{
  path: 'inventory/transfer',
  name: 'Transfer',
  component: () => import('@/views/inventory/TransferList.vue'),
  meta: { title: '调拨管理' }
}
```

- [ ] **Step 4: Commit**

```bash
git add frontend-web/src/
git commit -m "feat: add StockCount (盘点) and Transfer (调拨) list pages

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 18: Users 用户管理 + Lines 产线管理

**Files:**
- Create: `frontend-web/src/api/system.ts`
- Create: `frontend-web/src/views/system/UserList.vue`
- Create: `frontend-web/src/views/system/LineList.vue`
- Modify: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 创建 UserList.vue**

用户列表页：
- 用户名、姓名、角色、产线、状态
- 新增/编辑对话框（useCrudDialog）
- 状态开关

- [ ] **Step 2: 创建 LineList.vue**

产线管理页：
- 产线编号、名称、产能、状态
- 工位子表（展开行显示）
- 新增/编辑对话框

- [ ] **Step 3: 注册路由**

```typescript
{
  path: 'system/users',
  name: 'Users',
  component: () => import('@/views/system/UserList.vue'),
  meta: { title: '用户管理' }
},
{
  path: 'system/lines',
  name: 'Lines',
  component: () => import('@/views/system/LineList.vue'),
  meta: { title: '产线管理' }
}
```

- [ ] **Step 4: Commit**

```bash
git add frontend-web/src/
git commit -m "feat: add Users (用户管理) and Lines (产线管理) CRUD pages

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 19: Reports 报表页面

**Files:**
- Create: `frontend-web/src/api/reports.ts`
- Create: `frontend-web/src/views/reports/ReportList.vue`
- Modify: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 创建 ReportList.vue**

报表页面：
- 两个 Tab：操作日志 / 扫码记录
- 通用筛选（时间范围、操作人、模块）
- Excel 导出按钮
- 使用 `el-date-picker` 做时间范围筛选

- [ ] **Step 2: 注册路由**

```typescript
{
  path: 'reports',
  name: 'Reports',
  component: () => import('@/views/reports/ReportList.vue'),
  meta: { title: '报表中心' }
}
```

- [ ] **Step 3: Commit**

```bash
git add frontend-web/src/
git commit -m "feat: add Reports (报表中心) page with operation logs and scan records

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Phase 5: Android 端对齐

### Task 20: Android ApiResult + API 路径 + 多租户 Header

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/network/ApiService.kt`
- Modify: `mobile-android/app/src/main/java/com/dip/material/network/RetrofitClient.kt`
- Modify: `mobile-android/app/src/main/java/com/dip/material/network/interceptors/AuthInterceptor.kt`

- [ ] **Step 1: 修正 ApiResult 类型**

```kotlin
// 修改前
data class ApiResult<T>(val success: Boolean, val data: T?, val message: String)

// 修改后
data class ApiResult<T>(val code: Int, val data: T?, val message: String) {
    val isSuccess: Boolean get() = code == 0
}
```

- [ ] **Step 2: 统一 API 路径**

查找所有 API 路径定义，确认为单数名词（如 `/prep` 而非 `/preps`）：

```kotlin
interface ApiService {
    @GET("api/v1/prep")
    suspend fun getPrepList(@QueryMap params: Map<String, String>): ApiResult<PrepListResponse>

    @GET("api/v1/orders")
    suspend fun getOrders(@QueryMap params: Map<String, String>): ApiResult<OrderListResponse>
    // ...
}
```

- [ ] **Step 3: AuthInterceptor 追加多租户 Header**

```kotlin
class AuthInterceptor(private val tokenProvider: () -> String?) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request().newBuilder()
        tokenProvider()?.let {
            request.addHeader("Authorization", "Bearer $it")
        }
        // 追加租户 Header
        request.addHeader("X-Tenant-Id", "1") // TODO: 从登录用户信息获取
        return chain.proceed(request.build())
    }
}
```

- [ ] **Step 4: 编译 Android 项目**

```bash
cd mobile-android && ./gradlew build
```

- [ ] **Step 5: Commit**

```bash
git add mobile-android/
git commit -m "fix: align Android ApiResult with backend code convention, add tenant header

ApiResult now uses 'code' field (code==0 means success) instead of boolean
'success'. API paths unified with backend. AuthInterceptor adds X-Tenant-Id.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Self-Review Checklist

| 检查项 | 状态 | 说明 |
|--------|------|------|
| P0 Bug 修复（双重扣减/乐观锁/事务） | ✅ | Task 1-3 |
| ApiResponse<T> 封装 + Controller 改写 | ✅ | Task 4 |
| API 路径统一 | ✅ | Task 5 |
| 多租户（BaseEntity/Middleware/Interceptor/HasQueryFilter/JWT/Redis/Seed） | ✅ | Task 6-8 |
| TypeScript 迁移 + CI 类型生成 | ✅ | Task 9 |
| statusMappers/utils/styles | ✅ | Task 10 |
| usePagination + 改造 7 个列表页 | ✅ | Task 11 |
| useCrudDialog/useWebSocket/useChart | ✅ | Task 12 |
| 统一错误拦截 + 修复 5 个已知 Bug | ✅ | Task 13 |
| 删除 material.js + Tenant Store/Selector | ✅ | Task 14 |
| Loading/Return 页面 | ✅ | Task 15-16 |
| StockCount/Transfer 页面 | ✅ | Task 17 |
| Users/Lines 页面 | ✅ | Task 18 |
| Reports 页面 | ✅ | Task 19 |
| Android 对齐 | ✅ | Task 20 |

---

*计划结束*
