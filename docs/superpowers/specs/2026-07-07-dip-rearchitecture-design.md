# DIP物料管理系统 — 重架构设计

> **版本：** v1.0
> **日期：** 2026-07-07
> **状态：** 设计评审通过
> **前置审查：** 经前端架构、后端架构、前端实现三轮 agent 审查

---

## 目录

1. [背景与现状](#1-背景与现状)
2. [架构总览](#2-架构总览)
3. [API 层规范化](#3-api-层规范化)
4. [后端 P0 Bug 修复](#4-后端-p0-bug-修复)
5. [多租户设计](#5-多租户设计)
6. [前端重架构](#6-前端重架构)
7. [Android 端对齐](#7-android-端对齐)
8. [实施路线](#8-实施路线)

---

## 1. 背景与现状

### 1.1 当前状态

| 层 | 完成度 | 评估 |
|----|--------|------|
| 后端 (.NET 9.0) | 80-90% | 12 Controller、14 Service、30+ 实体已就位。基础设施（JWT/Redis/RabbitMQ）已搭建 |
| 前端 (Vue 3) | 40-50% | 12 个页面中有部分实现，但存在 bug、缺失模块、无 TypeScript |
| Android (Kotlin) | 未评估 | API 路径与后端不一致 |

### 1.2 发现的关键 Bug

| Bug | 严重度 | 位置 | 说明 |
|-----|--------|------|------|
| FreezeAsync 双重扣减 | P0 | `InventoryService.cs` | 备料冻结错误扣减了 Quantity，上线出库再扣一次 → 库存数据损坏 |
| MySQL 乐观锁失效 | P0 | `DIPDbContext.cs` | `IsRowVersion()` 是 SQL Server 特性，MySQL 不工作 → 并发静默覆盖 |
| 缺显式事务包装 | P0 | `PrepService.cs` | SaveChanges 分两次调用，中间失败导致库存冻结但未记录 → 永久锁死 |
| Dashboard API 路径错误 | P0 | `Dashboard.vue:75` | 调 `/prep` 而非后端实际路径，仪表盘统计永远为 0 |
| 齐套检测调错 API | P0 | `PrepList.vue:144` | 调用 `cancelPrep()` 而非齐套检测 API |
| API 路径不一致 | P1 | 多文件 | 前端 `/preps` vs 移动端 `/prep`，Android `ApiResult` 用 `success: Boolean` 但后端返回 `code: number` |
| InventoryList 重复绑定 | P2 | `InventoryList.vue` | locationCode 搜索框绑了两次 |
| `material.js` 僵尸 Store | P2 | `stores/material.js` | 存了全量数据但无任何 view 从中读取 |

### 1.3 设计目标

1. **修数据完整性问题** — P0 bug 会导致静默数据损坏，必须在加功能前修复
2. **前端可维护** — 消除重复代码、补全缺失页面、类型安全
3. **多租户可部署** — 系统可部署到多个客户/工厂，数据隔离
4. **PC Web + Android 原生双端** — 保持现有双端架构，统一 API 契约

### 1.4 被否决的方案

以下方案在审查中被否决，不纳入本次设计：

- **OpenAPI 3.1 契约驱动** — .NET 生态无成熟的「OpenAPI → Controller」正向生成工具。改为 Swashbuckle 反向生成 + `openapi-typescript` 消费类型
- **前端逻辑/UI 框架无关分离** — Pinia、Vue Router、Element Plus 深度绑定 Vue，无法真正做到框架无关。改为 Vue 生态内的 Composable 复用

---

## 2. 架构总览

```
                        ┌──────────────────────────────────┐
                        │       Swagger / OpenAPI JSON      │
                        │   (Swashbuckle 反向生成)           │
                        │   CI 中用 openapi-typescript      │
                        │   生成前端 .d.ts 类型声明          │
                        └──────────────┬───────────────────┘
                                       │
        ┌──────────────────────────────┼──────────────────────────────┐
        │                              │                              │
        ▼                              ▼                              ▼
┌───────────────┐            ┌─────────────────┐           ┌─────────────────┐
│   .NET 后端    │            │   PC Web 前端    │           │  Android 前端    │
│               │            │                  │           │                 │
│ API 层        │   HTTP     │ api/             │   HTTP    │ data/remote/    │
│ Controllers   │◀────────▶│ ├─ types.d.ts    │◀────────▶│ ├─ ApiService   │
│ (ApiResponse) │            │ ├─ client.ts     │           │ ├─ DTO classes  │
│               │            │                  │           │                 │
│ 中间件层       │            │ composables/     │           │ domain/         │
│ Tenant        │            │ ├─ usePagination │           │ ├─ UseCases     │
│ Exception     │            │ ├─ useCrudDialog │           │ ├─ Repositories │
│ Idempotency   │            │ ├─ useWebSocket  │           │                 │
│               │            │                  │           │                 │
│ 应用/领域层    │            │ stores/          │           │ presentation/   │
│ Services      │            │ ├─ auth.ts       │           │ ├─ ViewModels   │
│ Entities      │            │ ├─ notification  │           │ ├─ Screens      │
│               │            │ └─ tenant.ts     │           │                 │
│ 基础设施       │            │                  │           │ utils/          │
│ EF Core/Redis │            │ views/           │           │ ├─ Scanner      │
│ /RabbitMQ     │            │ components/      │           │ ├─ SyncManager  │
│               │            │ utils/           │           │                 │
│               │            │ styles/          │           │                 │
└───────┬───────┘            └─────────────────┘           └─────────────────┘
        │
        ▼
┌───────────────┐
│   MySQL 8.0   │
│ 共享库 +       │
│ TenantId 隔离  │
└───────────────┘
```

### 核心设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| API 类型生成 | Swashbuckle → Swagger JSON → openapi-typescript | .NET 无正向契约工具，反向生成是唯一成熟路径 |
| 前端语言 | TypeScript（api/ 和 composables/ 层）+ Vue SFC `<script setup lang="ts">` | 与生成类型无缝衔接，api 和 composables 的泛型是核心收益点；views 渐进迁移 |
| UI 框架 | Vue 3 + Element Plus | 保持不变，不换框架 |
| 代码复用 | Vue Composable | 在 Vue 生态内复用，不追求框架无关 |
| 多租户策略 | 共享库 + TenantId 列 | 制造 MES 客户数少（数十个），共享库运维成本最低 |

---

## 3. API 层规范化

### 3.1 后端：ApiResponse&lt;T&gt; 统一封装

当前 Controller 返回匿名对象，Swashbuckle 无法推断 `data` 类型。所有 Controller 改为强类型返回：

```csharp
// 通用响应封装
public record ApiResponse<T>(int Code, T? Data, string Message);

// 静态工厂方法
public static class ApiResult
{
    public static ApiResponse<T> Success<T>(T data) => new(0, data, "ok");
    // 带泛型的错误（需指定类型，适合有返回结构的错误）
    public static ApiResponse<T> Fail<T>(int code, string message) => new(code, default, message);
    // 无泛型错误重载（常见场景：404/401/403，不需要泛型参数）
    public static ApiResponse<object?> Fail(int code, string message) => new(code, null, message);
}

// Controller 使用示例
[HttpGet("{id}")]
public async Task<ApiResponse<PartDto>> GetPart(long id)
{
    var part = await _partService.GetByIdAsync(id);
    if (part == null) return ApiResult.Fail(404, "部品不存在");  // 无泛型重载
    return ApiResult.Success(part);
}
```

### 3.2 前端类型生成流程

```
dotnet build → 启动后端 → Swagger JSON (localhost:8400/swagger/v1/swagger.json)
                     │
                     ▼
              openapi-typescript
                     │
                     ▼
         frontend-web/src/api/types.ts  (CI 中自动生成)
```

CI 脚本中必须加固容错逻辑：

```bash
#!/bin/bash
# 1. 启动后端
dotnet run --project backend/DIP.API &
BACKEND_PID=$!

# 2. 等待 Swagger 就绪（最多 30 秒）
for i in $(seq 1 30); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8400/swagger/v1/swagger.json)
  if [ "$STATUS" = "200" ]; then break; fi
  sleep 1
done

# 3. 下载 Swagger JSON
curl -s http://localhost:8400/swagger/v1/swagger.json -o /tmp/swagger.json

# 4. 容错检查：文件不为空
if [ ! -s /tmp/swagger.json ]; then
  echo "ERROR: Swagger JSON 为空，后端可能启动失败" >&2
  kill $BACKEND_PID 2>/dev/null
  exit 1
fi

# 5. 检查 JSON 有效性
if ! jq empty /tmp/swagger.json 2>/dev/null; then
  echo "ERROR: Swagger JSON 格式无效" >&2
  kill $BACKEND_PID 2>/dev/null
  exit 1
fi

# 6. 生成 TypeScript 类型
npx openapi-typescript /tmp/swagger.json -o frontend-web/src/api/types.ts
GEN_EXIT=$?

# 7. 清理
kill $BACKEND_PID 2>/dev/null

# 8. 检查生成结果
if [ $GEN_EXIT -ne 0 ] || [ ! -s frontend-web/src/api/types.ts ]; then
  echo "ERROR: openapi-typescript 生成失败" >&2
  exit 1
fi

echo "TypeScript 类型生成成功"
```

> ⚠️ **容错原则：** 如果后端启动失败或 Swagger JSON 为空，必须 `exit 1` 阻断流水线。空 `types.ts` 会导致 `import type` 全部失败，前端大面积编译报错，比不生成更危险。

### 3.3 API 路径统一

**规则：新增路径用单数名词，不加 `s`。已有复数路径（orders、parts）保持不变，避免破坏现有 API 兼容性。** 三端统一。

| 模块 | 路径 | 修正内容 | 备注 |
|------|------|----------|------|
| 备料 | `/api/v1/prep` | 前端从 `/preps` 改为 `/prep` | |
| 工单 | `/api/v1/orders` | 不变 | 已有复数路径，三端已一致 |
| 库存 | `/api/v1/inventory` | 不变 | |
| 部品 | `/api/v1/parts` | 不变 | 已有复数路径，三端已一致 |
| 上料 | `/api/v1/loading` | 不变 | |
| 上线 | `/api/v1/online` | 不变 | |
| 退料 | `/api/v1/return` | 不变 | `return` 是 C#/JS/Kotlin 保留字，但在 `[Route]` 属性中作为字符串使用不受影响；Swagger 代码生成时需注意命名（如 `materialReturn`） |
| 盘点 | `/api/v1/count` | 不变 | `count` 是 SQL 聚合函数名，作为 URL 路径段无冲突；代码生成时用 `stockCount` 命名 |
| 调拨 | `/api/v1/transfer` | 不变 | |

### 3.4 统一错误处理

在 `client.ts` 的 Axios 响应拦截器中统一处理，区分两个分支：

```typescript
// onFulfilled — HTTP 2xx 响应
client.interceptors.response.use(
  (response) => {
    const { code, message } = response.data
    if (code !== 0) {
      // 业务错误（code 非 0 但 HTTP 200），统一弹错误提示
      ElMessage.error(message || '操作失败')
      return Promise.reject(new Error(message))  // 让页面 catch 感知
    }
    return response  // code === 0，正常返回
  },
  // onRejected — HTTP 4xx/5xx 响应
  (error) => {
    if (error.response?.status === 401) {
      // 触发 Token 刷新（已有逻辑），不弹错误
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
```

各页面的变化：
- `catch` 块仅做状态重置（`loading = false`），不重复弹错误提示
- 如需自定义错误处理，可在 `catch` 中判断 error type，但不弹 toast

---

## 4. 后端 P0 Bug 修复

### 4.1 FreezeAsync 双重扣减

**问题：** 备料冻结时 `lot.Quantity -= qty`；上线出库时 `lot.Quantity -= qty`。库存被扣两次。

**修复：** `FreezeAsync` 只改 `Status = 2`（冻结），不改变 `Quantity`：

```csharp
// 修改前（错误）
lot.Quantity -= deductFromLot;
if (lot.Quantity <= 0) lot.Status = 3;   // 消耗完

// 修改后（正确）
lot.Status = 2;   // 仅冻结，数量不变
// Quantity 的扣减只发生在 DeductAsync（上线出库）
```

### 4.2 MySQL 乐观锁

**问题：** `IsRowVersion()` 是 SQL Server 特性。MySQL 需要手动版本号比对。

**修复：** 手动版本号模式：

```csharp
// 配置：移除 IsRowVersion()，Version 作为 concurrency token
builder.Entity<Inventory>()
    .Property(i => i.Version)
    .IsConcurrencyToken();   // EF Core 自动在 UPDATE 时生成 WHERE Version = @originalVersion
```

> ⚠️ **注意：** `IsConcurrencyToken()` 只负责并发检测（WHERE 子句），**不会自动递增 Version**。必须在每次 SaveChanges 前手动 `entity.Version++`。或者在 `SaveChangesInterceptor` 中统一为所有修改的实体递增 Version。

```csharp
// 每次保存前手动递增
entity.Version++;
await _db.SaveChangesAsync();

// 如果受影响行数 != 1 → DbUpdateConcurrencyException → 业务层捕获并重试
```

**全局异常中间件需专门捕获并发冲突：**

```csharp
// ExceptionHandlingMiddleware.cs
catch (DbUpdateConcurrencyException)
{
    // 乐观锁冲突 → 返回 409，而非 500
    ctx.Response.StatusCode = 409;
    var response = new ApiResponse<object?>(409, null, "数据已被其他用户修改，请刷新后重试");
    await ctx.Response.WriteAsJsonAsync(response);
}
```

> 不放到业务层重试循环中是因为：如果重试 3 次后仍然冲突，最终还是会抛到中间件。中间件这一层是兜底，确保不会返回 500。PDA 端的自动重试逻辑在服务层实现。

### 4.3 显式事务包装

**问题：** `SaveChanges` 分两次调用，中间失败导致库存状态不一致。

**修复：** 所有编排方法用 `IDbContextTransaction` 包裹：

```csharp
// 方案：将现有方法拆分为 Core（不保存）和 Facade（保存）两层
// 内部编排方法调用 Core 版本，外部 API 调用 Facade 版本

// 1. FreezeCoreAsync — 纯内存操作，不 SaveChanges
internal async Task FreezeCoreAsync(long partId, long locationId, decimal qty)
{
    // 只做 entity 状态变更：lot.Status = 2, lot.Version++
    // 不调用 _db.SaveChangesAsync()
}

// 2. FreezeAsync — 外部 API 使用的 Facade
public async Task FreezeAsync(long partId, long locationId, decimal qty)
{
    await FreezeCoreAsync(partId, locationId, qty);
    await _db.SaveChangesAsync();
}

// 3. ScanPrepAsync — 编排方法调用 Core 版本
public async Task ScanPrepAsync(PrepScanRequest request)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        await _inventory.FreezeCoreAsync(partId, locationId, qty);  // 不保存
        _db.PrepScanRecords.Add(record);
        _db.PrepDetails.Update(detail);
        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync();                                  // 一次保存
        await tx.CommitAsync();
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}
```

**影响范围：** 上料确认、扫码备料、上线确认、撤销上料、撤销备料、撤销上线、退料确认、盘点调整、调拨执行 — 共 9 个方法。

---

## 5. 多租户设计

### 5.1 策略：共享库 + TenantId 列

制造 MES 场景：客户数少（数十个），单客户数据量大。共享库 + TenantId 是最优选择。

### 5.2 实体层

```csharp
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

### 5.3 三层隔离防线

```
请求 → TenantMiddleware → ITenantProvider → EF Core HasQueryFilter
  │           │                │                    │
  │     X-Tenant-Id      AsyncLocal 存储         WHERE TenantId = @current
  │     校验 JWT 一致     线程安全              与 IsDeleted 过滤器链式组合
```

**TenantMiddleware：**
- 从 `X-Tenant-Id` Header 提取租户 ID
- 与 JWT `tenantId` claim 校验一致（防租户 A 用户冒充租户 B）
- 注入 `ITenantProvider` 的 `AsyncLocal` 存储

**HasQueryFilter：**
- 所有实体配置追加 `HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId)`
- 业务代码零感知

### 5.4 需要防护的泄露点

| 场景 | 风险 | 对策 |
|------|------|------|
| 原生 SQL / `FromSql` | 绕过过滤器 | 手动追加 `.Where(e => e.TenantId == _tenantId)` |
| `ExecuteUpdateAsync` | EF Core 不应用过滤器 | 显式追加 TenantId 条件 |
| Redis 缓存键 | 跨租户数据混淆 | 所有键加 `{tenantId}:` 前缀 |
| 种子数据 | 角色全局、业务数据按租户 | 拆分全局种子和租户种子 |
| RabbitMQ 消息 | 消息无 HTTP 上下文 | 消息体携带 tenantId，消费时手动设置 |

### 5.5 JWT 变更

登录时在 claims 中增加 `tenantId`：

```csharp
var claims = new Dictionary<string, ClaimValueOptions>
{
    { "sub", user.Id },
    { "username", user.Username },
    { "role", user.Role.RoleCode },
    { "tenantId", user.TenantId },    // ← 新增
    { "lineId", user.LineId?.ToString() ?? "" }
};
```

### 5.6 改动影响范围

| 层 | 改动内容 | 工作量 |
|----|----------|--------|
| `BaseEntity` | 加 1 个字段 | 1 行 |
| `DIPDbContext.OnModelCreating` | 所有实体加 HasQueryFilter | ~40 行 |
| 新增 `TenantMiddleware` | 提取校验租户 ID | ~40 行 |
| 新增 `ITenantProvider` | AsyncLocal 存储接口 | ~10 行 |
| 新增 `TenantSaveChangesInterceptor` | SavingChanges 事件中，对新增的 BaseEntity 自动设置 `TenantId = _tenantProvider.CurrentTenantId` | ~25 行 |
| `Program.cs` | 注册中间件 + 拦截器 | 3 行 |
| JWT 生成 | 加 tenantId claim | ~5 行 |

> ⚠️ **关键：** `HasQueryFilter` 只解决读取隔离。新增实体时 TenantId 默认为 0，如果不通过拦截器自动赋值，新数据全部落入错误租户。`TenantSaveChangesInterceptor` 是写入隔离的必要环节。

```csharp
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
            entry.Entity.TenantId = tenantId;
        }
        return result;
    }
}
```

---

## 6. 前端重架构

### 6.1 目录结构

```
frontend-web/src/
├── api/                        # API 客户端 + 类型（框架无关）
│   ├── types.d.ts              # ← 从 Swagger JSON 自动生成（CI）
│   ├── client.ts               # Axios 实例 + 统一错误拦截
│   ├── auth.ts
│   ├── prep.ts
│   ├── inventory.ts
│   ├── loading.ts
│   ├── order.ts
│   ├── part.ts
│   ├── location.ts
│   ├── online.ts
│   ├── abnormal.ts
│   ├── return.ts
│   ├── count.ts
│   └── transfer.ts
│
├── composables/                # Vue Composable（Vue 绑定）
│   ├── usePagination.ts        # 分页 + loading + fetch 通用模式
│   ├── useCrudDialog.ts        # 新增/编辑对话框通用逻辑
│   ├── useWebSocket.ts         # SignalR 连接管理
│   └── useChart.ts             # ECharts 图表管理
│
├── stores/                     # Pinia 状态管理
│   ├── auth.ts                 # 认证状态
│   ├── notification.ts         # 通知计数
│   └── tenant.ts               # 当前租户信息（新增）
│
├── router/
│   └── index.ts
│
├── views/
│   ├── Dashboard.vue
│   ├── Login.vue
│   ├── loading/                # ← 新增
│   │   └── LoadingList.vue
│   ├── prep/
│   │   ├── PrepList.vue
│   │   └── PrepDetail.vue
│   ├── orders/
│   │   ├── OrderList.vue
│   │   └── OrderDetail.vue
│   ├── inventory/
│   │   ├── InventoryList.vue
│   │   ├── InventoryDetail.vue
│   │   ├── CountList.vue       # ← 新增
│   │   └── TransferList.vue    # ← 新增
│   ├── parts/
│   │   └── PartList.vue
│   ├── locations/
│   │   └── LocationList.vue
│   ├── online/
│   │   └── OnlineList.vue
│   ├── abnormal/
│   │   └── AbnormalList.vue
│   ├── return/                 # ← 新增
│   │   └── ReturnList.vue
│   ├── reports/                # ← 新增
│   │   └── ReportList.vue
│   ├── system/                 # ← 新增
│   │   ├── UserList.vue
│   │   └── LineList.vue
│   └── settings/
│       └── Settings.vue
│
├── components/                 # 共享 UI 组件
│   ├── layout/
│   │   ├── AppSidebar.vue
│   │   ├── AppHeader.vue
│   │   └── MainLayout.vue      # Layout.vue 移入此目录
│   ├── StatusTag.vue
│   ├── PaginationBar.vue
│   └── TenantSelector.vue      # ← 新增
│
├── utils/                      # 工具函数（框架无关）
│   ├── statusMappers.ts        # 状态枚举 → 文本/颜色的映射（集中化）
│   ├── format.ts               # 日期/数字格式化
│   └── constants.ts            # API 路径 / 业务常量
│
└── styles/
    └── global.css              # 提取各页重复的 .page-container / .search-bar 等
```

### 6.2 Composable 模式

#### usePagination

消除每个列表页 ~60 行重复逻辑。覆盖约 10 个列表页，共消除 ~400 行重复代码。

```typescript
export function usePagination<T>(options: {
  fetchFn: (params: Record<string, any>) => Promise<PaginatedResponse<T>>
  transformParams?: (raw: Record<string, any>) => Record<string, any>
  immediate?: boolean          // 默认 true，设为 false 时需手动调 fetchData
  onError?: () => void
}) {
  const { immediate = true } = options
  const loading = ref(false)
  const data = ref<T[]>([])
  const total = ref(0)
  const queryParams = reactive({ page: 1, pageSize: 20 })

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

  // 防抖搜索：适合绑定到输入框 @input 事件，300ms 内连续输入只发一次请求
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

> **使用建议：** 搜索按钮用 `search()`（立即触发）；输入框 `@input` 用 `debouncedSearch()`（300ms 防抖）。

#### useCrudDialog

标准化新增/编辑对话框逻辑，兼容三种复杂度：

```typescript
export function useCrudDialog<T>(options: {
  createFn: (data: T) => Promise<void>
  updateFn: (id: number, data: T) => Promise<void>
  defaultForm: () => T
  onSuccess?: () => void
}) {
  const visible = ref(false)
  const editId = ref<number | null>(null)
  const title = computed(() => editId.value ? '编辑' : '新增')
  const submitting = ref(false)
  const formData = reactive<T>(options.defaultForm()) as T

  const open = (row?: T & { id?: number }) => {
    if (row?.id) {
      editId.value = row.id
      // 深拷贝避免编辑时污染源数据（行数据可能含嵌套对象）
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

#### useWebSocket

SignalR 连接管理，Dashboard 实时推送：

```typescript
interface AppNotification {
  id: string
  type: 'abnormal' | 'prep' | 'online'
  message: string
  createdAt: string
}

export function useWebSocket() {
  const connected = ref(false)
  const notifications = ref<AppNotification[]>([])

  // 连接延迟创建，避免在非浏览器环境报错
  let connection: HubConnection | null = null

  onMounted(async () => {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/notification')
      .withAutomaticReconnect([1000, 2000, 5000, 10000])
      .build()

    connection.on('AbnormalAlert', (msg) => {
      notifications.value.unshift(msg)
    })
    connection.on('PrepCompleted', (msg) => { /* 更新仪表盘 */ })
    connection.on('OnlineConfirmed', (msg) => { /* 更新仪表盘 */ })

    try {
      await connection.start()
      connected.value = true
    } catch { /* 重连由 withAutomaticReconnect 处理 */ }
  })

  onUnmounted(() => connection?.stop())

  return { connected, notifications }
}
```

### 6.3 状态枚举集中化

当前每个页面各自定义 priority/type/severity 的文本和颜色映射。集中到 `utils/statusMappers.ts`：

```typescript
export const STATUS_MAPS = {
  prepStatus: {
    1: { text: '待备料', tag: 'info' },
    2: { text: '备料中', tag: 'warning' },
    3: { text: '已完成', tag: 'success' },
    4: { text: '已撤销', tag: 'danger' },
    5: { text: '已暂停', tag: '' },
  },
  kitCheck: {
    1: { text: '齐套', type: 'success' },
    2: { text: '部分齐套', type: 'warning' },
    3: { text: '不齐套', type: 'danger' },
  },
  priority: {
    1: { text: '普通', type: '' },
    2: { text: '加急', type: 'warning' },
    3: { text: '特急', type: 'danger' },
  },
  abnormalType: {
    1: '库存不足',
    2: '品质异常',
    3: '批次过期',
    4: 'MSL超时',
    5: '其他',
  },
  severity: {
    1: { text: '低', type: 'info' },
    2: { text: '中', type: 'warning' },
    3: { text: '高', type: 'danger' },
  },
}
```

### 6.4 全局样式提取

各页重复的 scoped 样式提取到 `styles/global.css`：

```css
.page-container { min-width: 1200px; }
.search-bar { margin-bottom: 16px; }
.table-toolbar { margin-bottom: 12px; }
.detail-card { margin-bottom: 16px; }
.stat-cards { margin-bottom: 20px; }
```

### 6.5 删除僵尸代码

- **`stores/material.js`**：删除。全量数据存储但无任何 view 消费，是双数据源反模式

### 6.6 新增依赖

```bash
npm install @microsoft/signalr        # SignalR WebSocket 客户端
npm install -D openapi-typescript     # Swagger JSON → TS 类型生成
```

### 6.7 缺失页面补全清单

| 优先级 | 页面 | 路由 | 说明 |
|--------|------|------|------|
| P0 | Loading 上料记录 | `/loading` | 列表页：批次号、目标库位、状态、时间。与 Prep 是前后工序 |
| P0 | Return 退料管理 | `/return` | 列表页：退料单号、关联备料单、状态。班长审核按钮 |
| P0 | StockCount 盘点 | `/inventory/count` | 列表页：盘点单号、类型、状态。详情含差异明细 |
| P1 | Transfer 调拨 | `/inventory/transfer` | 列表页：调拨单号、源/目标库位、状态 |
| P1 | Users 用户管理 | `/system/users` | 列表 + CRUD 对话框 |
| P1 | Lines 产线管理 | `/system/lines` | 产线列表 + 工位子表 |
| P2 | Reports 报表 | `/reports` | 操作日志、扫码记录查询导出 |

### 6.8 已知 Bug 修复清单

| Bug | 文件 | 修复 |
|-----|------|------|
| Dashboard API 路径 | `Dashboard.vue:75` | 当前调用路径与后端不一致，统一改为 `/api/v1/prep`（单数），使用 `getPrepList()` |
| 齐套检测调错 API | `PrepList.vue:144` | `cancelPrep` → 实际齐套检测 API |
| 产线选项硬编码 | `PrepList.vue` | 改为从 `getLines()` 动态加载 |
| InventoryList 重复绑定 | `InventoryList.vue` | 删除重复的 locationCode 输入框 |
| 状态映射分散 | 多个文件 | 引用 `utils/statusMappers.ts` 统一常量 |

---

## 7. Android 端对齐

### 7.1 API 类型修正

当前 Android 端 `ApiResult<T>` 用 `success: Boolean`，但后端返回 `code: int`（code=0 为成功）。需要对齐：

```kotlin
// 修改前
data class ApiResult<T>(val success: Boolean, val data: T?, val message: String)

// 修改后
data class ApiResult<T>(val code: Int, val data: T?, val message: String) {
    val isSuccess: Boolean get() = code == 0
}
```

### 7.2 API 路径统一

Android 端 `ApiService.kt` 中的路径与后端 Controller 对齐（单数名词）。

### 7.3 多租户 Header

所有 Retrofit 请求通过 `AuthInterceptor` 自动追加 `X-Tenant-Id` Header。

### 7.4 类型生成

从 Swagger JSON 生成 Kotlin data class，替代手写 DTO。CI 流程与前端类似。

---

## 8. 实施路线

按依赖关系和风险排序：

### Phase 1: 地基修复（P0 Bug + API 规范化）— ~3 天

```
必须先做，不改架构，修复数据完整性问题
├── FreezeAsync 双重扣减修复           ← 数据损坏
├── MySQL 乐观锁配置修复               ← 静默覆盖
├── 显式事务包装（9 个编排方法）        ← 库存永久锁死
├── ApiResponse<T> 统一封装            ← 为 Phase 3 类型生成打地基
├── API 路径统一                       ← 三端对齐
└── 缺失端点实现（Recalculate 等）
```

### Phase 2: 多租户 — ~1.5 天

```
├── BaseEntity 加 TenantId
├── TenantMiddleware + ITenantProvider
├── HasQueryFilter 所有实体
├── JWT 加 tenantId claim
├── Redis 缓存键加前缀
└── 种子数据拆分
```

### Phase 3: 前端类型安全 + 消除重复 — ~3 天

```
├── CI 集成 openapi-typescript 生成 types.d.ts
├── utils/statusMappers.ts（状态枚举集中化）
├── styles/global.css（提取重复样式）
├── usePagination composable + 改造现有列表页
├── useCrudDialog composable + 改造 CRUD 页
├── useWebSocket composable
├── client.ts 统一错误拦截
├── 修复已知 Bug（Dashboard API、齐套检测、产线硬编码等）
└── 删除 stores/material.js
```

### Phase 4: 补全缺失页面 — ~5 天

```
├── P0 │ Loading 上料记录列表
├── P0 │ Return 退料管理
├── P0 │ StockCount 盘点管理
├── P1 │ Transfer 调拨管理
├── P1 │ Users 用户管理
├── P1 │ Lines 产线/工位管理
└── P2 │ Reports 报表
```

### Phase 5: Android 端对齐 — ~2 天

```
├── ApiResult 改为 code 模式
├── API 路径统一
├── 多租户 Header 支持
└── 类型对齐
```

---

## 附录 A：被否决的方案

| 方案 | 否决原因 |
|------|----------|
| OpenAPI 3.1 契约驱动 | .NET 无成熟的「契约→Controller」正向工具。Swashbuckle 反向生成是唯一成熟路径 |
| 前端逻辑/UI 框架无关分离 | Pinia、Vue Router 深度绑定 Vue，换框架时不可免重写。改为 Vue Composable 复用 |
| 前端换框架（React/Angular） | 当前团队熟悉 Vue，换框架成本 > 收益 |
| 后端 BFF 层 | 当前团队规模不适用，增加部署复杂度 |
| 每租户独立数据库 | 制造 MES 客户数少但数据量大，共享库 + TenantId 运维成本最低 |

## 附录 B：审查记录

| 审查者 | 视角 | 主要意见 |
|--------|------|----------|
| Agent 1 | 前端架构 | 否决框架无关分离；指出 PrepList bug、Dashboard API 路径错误、material.js 僵尸代码 |
| Agent 2 | 后端架构 | 否决 OpenAPI 契约驱动；发现 FreezeAsync 双重扣减、MySQL 乐观锁失效、缺显式事务 |
| Agent 3 | 前端实现 | 确认 Composable 方向但补充：需 utils/、styles/、types.d.ts 迁移路径、SignalR 依赖、错误处理统一 |

---

*文档结束 v1.0 — 基于三轮 agent 审查修订*
