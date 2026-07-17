# 多产品合并订单 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 订单管理支持多产品批量选择 + BOM料号集合相同自动合并为一个订单 + 产品模糊搜索

**Architecture:** 新增 `order_products` 关联表解除 1订单=1产品 的限制；后端 `CreateAsync` 按 BOM 料号集合自动分组生成订单；前端新建/编辑弹窗改为表格批量添加模式。手机端零改动。

**Tech Stack:** C# ASP.NET Core 8.0 + EF Core 8 + React 18 + TypeScript + Tailwind CSS

## Global Constraints

- 产品名分隔符统一用 `/`（非 `,`）
- API 返回格式统一：`{ code: 0, data: { orders: [...], total: N }, message: "..." }`
- 编辑时必须校验所有产品属于同一个 BOM 分组，不一致则阻断
- 数量更新直接覆盖 required_qty，不校验已备料量，Refreeze 兜底
- 空 BOM 产品在服务端提前拦截，整个创建中止
- `order_products` 必须存储 `product_id` 以备追溯
- 旧单产品格式完全兼容，`total` 始终为 1

---

### Task 1: 数据库 — 新增 `order_products` 表 + EF Core 实体注册

**Files:**
- Modify: `dip-system/api/Models/Order.cs`（新增 `OrderProduct` 实体）
- Modify: `dip-system/api/Data/AppDbContext.cs`（注册 DbSet + QueryFilter + 表名映射）

**Interfaces:**
- Consumes: `BaseEntity` from `Models/BaseEntity.cs`
- Produces: `OrderProduct` 实体类；`AppDbContext.OrderProducts` DbSet

- [ ] **Step 1: 在 `Order.cs` 末尾新增 `OrderProduct` 实体**

```csharp
/// <summary>
/// 订单关联产品（多产品订单支持）
/// </summary>
public class OrderProduct : BaseEntity
{
    [Column("order_id")]
    public long OrderId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [Column("plan_qty")]
    public decimal PlanQty { get; set; }

    [ForeignKey(nameof(OrderId))]
    public ProductionOrder? Order { get; set; }
}
```

- [ ] **Step 2: 在 `ProductionOrder` 类中添加导航属性**

在 `ProductionOrder` 类的 `BomItems` 属性后面添加：

```csharp
public List<OrderProduct> OrderProducts { get; set; } = new();
```

- [ ] **Step 3: 在 `AppDbContext.cs` 中注册 `OrderProduct`**

找到其他 DbSet 声明区域（约 line 33-37），添加：

```csharp
public DbSet<OrderProduct> OrderProducts { get; set; }
```

- [ ] **Step 4: 添加全局查询过滤器**

找到 `HasQueryFilter` 配置区域（约 line 95-100），在 `ProductionOrder` 的过滤器之后添加：

```csharp
modelBuilder.Entity<OrderProduct>().HasQueryFilter(e => !e.IsDeleted);
```

- [ ] **Step 5: 配置表名映射**

找到 `ToTable` 配置区域（约 line 130-136），添加：

```csharp
modelBuilder.Entity<OrderProduct>(e => e.ToTable("order_products"));
```

- [ ] **Step 6: 配置外键关系**

找到外键配置区域末尾，添加：

```csharp
modelBuilder.Entity<OrderProduct>()
    .HasOne(e => e.Order)
    .WithMany(o => o.OrderProducts)
    .HasForeignKey(e => e.OrderId)
    .HasConstraintName("fk_order_products_order");
```

- [ ] **Step 7: Commit**

```bash
git add dip-system/api/Models/Order.cs dip-system/api/Data/AppDbContext.cs
git commit -m "feat: 新增 OrderProduct 实体 + EF Core 注册"
```

---

### Task 2: 后端 — 产品列表接口升级（返回 product_id + bom_count）

**Files:**
- Modify: `dip-system/api/Services/OrderService.cs` — `GetProductNamesAsync` 方法
- Modify: `dip-system/api/Controllers/OrdersController.cs` — 路由不变

**Interfaces:**
- Consumes: `AppDbContext.ProductBoms`
- Produces: `List<object>` 每项含 `product_id`, `product_name`, `bom_count`

- [ ] **Step 1: 重写 `GetProductNamesAsync`**

```csharp
public async Task<List<object>> GetProductNamesAsync()
{
    var boms = await _db.ProductBoms.ToListAsync();
    return boms
        .GroupBy(b => new { b.ProductName, b.PartId })  // 先按 product+part 去重
        .GroupBy(g => g.Key.ProductName)
        .Select(g => (object)new
        {
            product_name = g.Key,
            product_id = g.First().First().PartId,  // 取第一个 BOM 行的 part_id 作为 product_id 近似值
            bom_count = g.Count()
        })
        .ToList();
}
```

> **注意：** 当前系统没有独立的 `products` 主数据表，产品以 `product_boms` 中的 `product_name` 标识。`product_id` 此处取该产品第一个 BOM 行的 `part_id` 作为近似值。如果后续系统有了 `products` 表，改查 products 表即可，不影响前端。

- [ ] **Step 2: 控制器无需改动**（路由和返回格式不变，`ApiResponse.Ok(data)` 自动包装）

- [ ] **Step 3: Commit**

```bash
git add dip-system/api/Services/OrderService.cs
git commit -m "feat: GetProductNamesAsync 返回 product_id + bom_count"
```

---

### Task 3: 后端 — 新建订单重写（多产品分组合并）

**Files:**
- Modify: `dip-system/api/Services/OrderService.cs` — `CreateAsync` 方法
- Modify: `dip-system/api/Controllers/OrdersController.cs` — 不需要改（请求体仍是 Dictionary）

**Interfaces:**
- Consumes: `DictHelper`（GetStr/GetDecimal/GetLong/GetInt），`ProductBoms`，`ProductionOrders`，`OrderProducts`，`BomItems`，`PrepOrders`，`PrepDetails`
- Produces: 统一返回 `{ orders: [...], total: N }`

- [ ] **Step 1: 重写 `CreateAsync` 方法**

```csharp
/// <summary>
/// 【新建订单 → 多产品分组 + BOM合并 + 立即冻结】
/// 1. 如果传了 products 数组 → 按 BOM 料号集合分组，每组生成一个订单
/// 2. 如果没传 → 走旧单产品逻辑（兼容）
/// 3. 返回 { orders: [...], total }
/// </summary>
public async Task<object> CreateAsync(Dictionary<string, object?> data, long operatorId)
{
    var lineId = data.GetLong("line_id") ?? 0;
    var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == lineId);
    if (line == null) throw AppException.NotFound("产线不存在");

    var priority = data.GetInt("priority") ?? 2;

    // 判断新旧格式：是否有 products 数组
    var productsRaw = data.TryGetValue("products", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array
        ? je.EnumerateArray().ToList()
        : null;

    if (productsRaw == null || productsRaw.Count == 0)
    {
        // === 旧格式：单产品（兼容） ===
        var productName = data.GetStr("product_name") ?? "";
        var planQty = data.GetDecimal("plan_qty");
        if (planQty == 0) planQty = 1;
        if (string.IsNullOrEmpty(productName))
            throw AppException.Business("产品名称不能为空");

        var order = await CreateSingleOrder(lineId, priority,
            new List<(long productId, string name, decimal qty)> { (0, productName, planQty) });
        return new { orders = new[] { ToDict(order) }, total = 1 };
    }

    // === 新格式：多产品 → 分组 → 批量创建 ===
    var products = new List<(long productId, string name, decimal qty)>();
    foreach (var item in productsRaw)
    {
        var name = item.TryGetProperty("product_name", out var np) ? np.GetString() ?? "" : "";
        var qty = item.TryGetProperty("plan_qty", out var qp) && qp.ValueKind == JsonValueKind.Number ? qp.GetDecimal() : 1m;
        var pid = item.TryGetProperty("product_id", out var ip) && ip.ValueKind == JsonValueKind.Number ? ip.GetInt64() : 0L;
        if (string.IsNullOrEmpty(name)) continue;
        products.Add((pid, name, qty));
    }

    if (products.Count == 0)
        throw AppException.Business("请至少选择一个产品");

    // 空 BOM 拦截：逐产品检查
    foreach (var p in products)
    {
        var hasBom = await _db.ProductBoms.AnyAsync(b => b.ProductName == p.name);
        if (!hasBom)
            throw AppException.Business($"产品 {p.name} 没有 BOM 数据，请先导入 BOM");
    }

    // 分组：按 BOM 料号集合（part_no 去重排序后拼接为签名）
    var groups = new Dictionary<string, List<(long productId, string name, decimal qty)>>();
    foreach (var p in products)
    {
        var partNos = await _db.ProductBoms
            .Where(b => b.ProductName == p.name)
            .Select(b => b.PartNo)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        var signature = string.Join(",", partNos);  // 料号集合签名
        if (!groups.ContainsKey(signature))
            groups[signature] = new List<(long, string, decimal)>();
        groups[signature].Add(p);
    }

    // 每组生成一个订单
    var createdOrders = new List<object>();
    foreach (var kv in groups)
    {
        var order = await CreateSingleOrder(lineId, priority, kv.Value);
        createdOrders.Add(ToDict(order));
    }

    // 统一冻结
    await RefreezeActiveOrdersAsync(operatorId);

    return new { orders = createdOrders, total = createdOrders.Count };
}

/// <summary>
/// 创建一个订单（含 BOM 合并 + PrepOrder + order_products 写入）
/// products 列表中的产品必须属于同一 BOM 分组，调用方保证
/// </summary>
private async Task<ProductionOrder> CreateSingleOrder(long lineId, int priority,
    List<(long productId, string name, decimal planQty)> products)
{
    var orderNo = $"WO{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
    var totalPlanQty = products.Sum(p => p.planQty);
    var productNames = products.Select(p => p.name).Distinct().ToList();
    var displayName = string.Join(" / ", productNames);

    var order = new ProductionOrder
    {
        OrderNo = orderNo,
        LineId = lineId,
        ProductName = displayName,
        PlanQty = totalPlanQty,
        Priority = priority,
        Status = 1
    };
    _db.ProductionOrders.Add(order);
    await _db.SaveChangesAsync();

    // 写入 order_products
    foreach (var p in products)
    {
        _db.OrderProducts.Add(new OrderProduct
        {
            OrderId = order.Id,
            ProductId = p.productId,
            ProductName = p.name,
            PlanQty = p.planQty
        });
    }

    // 合并 BOM：按 part_no 汇总，跨所有产品
    var allProductNames = products.Select(p => p.name).Distinct().ToList();
    var allBoms = await _db.ProductBoms
        .Where(b => allProductNames.Contains(b.ProductName))
        .ToListAsync();

    // part_id → (part_no, total_required_qty)
    var merged = new Dictionary<long, (string partNo, decimal totalQty)>();
    foreach (var bom in allBoms)
    {
        var productPlanQty = products.First(p => p.name == bom.ProductName).planQty;
        var qty = bom.Quantity * productPlanQty;
        if (merged.ContainsKey(bom.PartId))
            merged[bom.PartId] = (bom.PartNo, merged[bom.PartId].totalQty + qty);
        else
            merged[bom.PartId] = (bom.PartNo, qty);
    }

    int seq = 0;
    foreach (var kv in merged)
    {
        seq++;
        _db.BomItems.Add(new BomItem
        {
            OrderId = order.Id,
            PartId = kv.Key,
            PartNo = kv.Value.partNo,
            RequiredQty = kv.Value.totalQty,
            SeqNo = seq,
            ReferenceDesignator = "",
            LossRate = 0,
            IsCritical = 0
        });
    }
    await _db.SaveChangesAsync();

    // 创建 PrepOrder + PrepDetails
    var prepOrderNo = $"PO-{order.OrderNo}";
    var prep = new PrepOrder
    {
        OrderNo = prepOrderNo,
        ProductionOrderId = order.Id,
        LineId = order.LineId,
        Status = 1
    };
    _db.PrepOrders.Add(prep);
    await _db.SaveChangesAsync();

    foreach (var kv in merged)
    {
        _db.PrepDetails.Add(new PrepDetail
        {
            PrepOrderId = prep.Id,
            PartId = kv.Key,
            PartNo = kv.Value.partNo,
            RequiredQty = kv.Value.totalQty,
            ActualQty = 0,
            LossQty = 0,
            SubstituteFlag = 0,
            Status = 1,
            ReferenceDesignator = ""
        });
    }
    await _db.SaveChangesAsync();

    return order;
}
```

- [ ] **Step 2: Commit**

```bash
git add dip-system/api/Services/OrderService.cs
git commit -m "feat: CreateAsync 支持多产品分组 + BOM合并 + 空BOM拦截"
```

---

### Task 4: 后端 — 订单详情接口升级（返回 order_products）

**Files:**
- Modify: `dip-system/api/Services/OrderService.cs` — `GetDetailAsync` 方法

**Interfaces:**
- Consumes: `OrderProducts` DbSet
- Produces: 响应新增 `order_products` 数组

- [ ] **Step 1: 在 `GetDetailAsync` 中添加 order_products 查询**

在 `GetDetailAsync` 方法中，找到 `var prepOrders = ...` 行之前，添加：

```csharp
var orderProducts = await _db.OrderProducts
    .Where(op => op.OrderId == order.Id)
    .ToListAsync();
```

- [ ] **Step 2: 在返回对象中添加 `order_products` 字段**

在返回匿名对象的 `prep_orders` 之后，添加：

```csharp
order_products = orderProducts.Select(op => (object)new
{
    product_id = op.ProductId,
    product_name = op.ProductName,
    plan_qty = op.PlanQty
}).ToList()
```

- [ ] **Step 3: Commit**

```bash
git add dip-system/api/Services/OrderService.cs
git commit -m "feat: GetDetailAsync 返回 order_products 字段"
```

---

### Task 5: 后端 — 编辑订单重写（BOM一致性校验 + 数量简化更新）

**Files:**
- Modify: `dip-system/api/Services/OrderService.cs` — `UpdateAsync` 方法

**Interfaces:**
- Consumes: `OrderProducts` DbSet, `ProductBoms`, `PrepDetails`, `BomItems`
- Produces: 更新后的订单，`{ orders: [{...}], total: 1 }`

- [ ] **Step 1: 重写 `UpdateAsync`**

```csharp
/// <summary>
/// 【编辑订单 → BOM一致性校验 + 数量简化更新 + 重新冻结】
/// 1. 如果传了 products 数组 → 校验BOM分组一致 → 更新order_products → 重建BOM/PrepDetails
/// 2. 如果没传 → 走旧逻辑（兼容）
/// </summary>
public async Task<object> UpdateAsync(long orderId, Dictionary<string, object?> data)
{
    var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == orderId);
    if (order == null) throw AppException.NotFound($"订单 {orderId} 不存在");

    // 判断新旧格式
    var productsRaw = data.TryGetValue("products", out var pv) && pv is JsonElement je && je.ValueKind == JsonValueKind.Array
        ? je.EnumerateArray().ToList()
        : null;

    if (productsRaw != null && productsRaw.Count > 0)
    {
        // === 新格式：多产品编辑 ===
        var products = new List<(long productId, string name, decimal qty)>();
        foreach (var item in productsRaw)
        {
            var name = item.TryGetProperty("product_name", out var np) ? np.GetString() ?? "" : "";
            var qty = item.TryGetProperty("plan_qty", out var qp) && qp.ValueKind == JsonValueKind.Number ? qp.GetDecimal() : 1m;
            var pid = item.TryGetProperty("product_id", out var ip) && ip.ValueKind == JsonValueKind.Number ? ip.GetInt64() : 0L;
            if (string.IsNullOrEmpty(name)) continue;
            products.Add((pid, name, qty));
        }

        if (products.Count == 0)
            throw AppException.Business("请至少保留一个产品");

        // BOM 分组一致性校验：所有产品必须属于同一分组
        string? groupSignature = null;
        foreach (var p in products)
        {
            var partNos = await _db.ProductBoms
                .Where(b => b.ProductName == p.name)
                .Select(b => b.PartNo)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
            if (partNos.Count == 0)
                throw AppException.Business($"产品 {p.name} 没有 BOM 数据，请先导入 BOM");
            var sig = string.Join(",", partNos);
            if (groupSignature == null)
                groupSignature = sig;
            else if (groupSignature != sig)
                throw AppException.Business("编辑后的产品 BOM 不一致，请删除当前订单并重新创建");
        }

        var totalPlanQty = products.Sum(p => p.qty);
        var displayName = string.Join(" / ", products.Select(p => p.name).Distinct());

        order.ProductName = displayName;
        order.PlanQty = totalPlanQty;
        data.ApplyTo(order, new[] { "priority", "status" });

        // 更新 order_products：删旧 + 写新
        var oldProducts = await _db.OrderProducts.Where(op => op.OrderId == orderId).ToListAsync();
        foreach (var op in oldProducts) op.IsDeleted = true;
        foreach (var p in products)
        {
            _db.OrderProducts.Add(new OrderProduct
            {
                OrderId = order.Id,
                ProductId = p.productId,
                ProductName = p.name,
                PlanQty = p.qty
            });
        }

        // 重建 BOM：先删旧 BomItems
        var oldBomItems = await _db.BomItems.Where(b => b.OrderId == orderId).ToListAsync();
        foreach (var bi in oldBomItems) bi.IsDeleted = true;

        // 合并新 BOM
        var allProductNames = products.Select(p => p.name).Distinct().ToList();
        var allBoms = await _db.ProductBoms
            .Where(b => allProductNames.Contains(b.ProductName))
            .ToListAsync();
        var merged = new Dictionary<long, (string partNo, decimal totalQty)>();
        foreach (var bom in allBoms)
        {
            var productPlanQty = products.First(p => p.name == bom.ProductName).qty;
            var qty = bom.Quantity * productPlanQty;
            if (merged.ContainsKey(bom.PartId))
                merged[bom.PartId] = (bom.PartNo, merged[bom.PartId].totalQty + qty);
            else
                merged[bom.PartId] = (bom.PartNo, qty);
        }
        int seq = 0;
        foreach (var kv in merged)
        {
            seq++;
            _db.BomItems.Add(new BomItem
            {
                OrderId = order.Id,
                PartId = kv.Key,
                PartNo = kv.Value.partNo,
                RequiredQty = kv.Value.totalQty,
                SeqNo = seq,
                ReferenceDesignator = "",
                LossRate = 0,
                IsCritical = 0
            });
        }

        // 更新 PrepDetails：删旧 + 建新
        var preps = await _db.PrepOrders
            .Where(p => p.ProductionOrderId == orderId && p.Status != 3)
            .ToListAsync();
        foreach (var prep in preps)
        {
            var oldDetails = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
            foreach (var d in oldDetails) d.IsDeleted = true;

            // 解冻旧 PrepOrder 的库存
            var prepSvc = new PrepService(_db);
            try { await prepSvc.CancelAsync(prep.Id, 0); } catch { /* 如果之前没有冻结则忽略 */ }

            // 建新的 PrepDetails（简化：直接覆盖 required_qty，不校验已备料量）
            foreach (var kv in merged)
            {
                _db.PrepDetails.Add(new PrepDetail
                {
                    PrepOrderId = prep.Id,
                    PartId = kv.Key,
                    PartNo = kv.Value.partNo,
                    RequiredQty = kv.Value.totalQty,
                    ActualQty = 0,
                    LossQty = 0,
                    SubstituteFlag = 0,
                    Status = 1,
                    ReferenceDesignator = ""
                });
            }
            prep.Status = 1;
            prep.KitCheckResult = 0;
        }
        order.Status = 1;

        await _db.SaveChangesAsync();
        await RefreezeActiveOrdersAsync(0);

        return new { orders = new[] { ToDict(order) }, total = 1 };
    }

    // === 旧格式：兼容原编辑逻辑 ===
    var oldPlanQty = order.PlanQty;
    var newPlanQty = data.ContainsKey("plan_qty") ? data.GetDecimal("plan_qty") : oldPlanQty;
    if (newPlanQty <= 0) newPlanQty = oldPlanQty;

    if (newPlanQty != oldPlanQty)
    {
        var ratio = newPlanQty / oldPlanQty;
        var preps = await _db.PrepOrders.Where(p => p.ProductionOrderId == orderId && p.Status != 3).ToListAsync();
        foreach (var prep in preps)
        {
            var details = await _db.PrepDetails.Where(d => d.PrepOrderId == prep.Id).ToListAsync();
            foreach (var d in details)
                d.RequiredQty = Math.Round(d.RequiredQty * ratio, 2);
            prep.Status = 1;
            prep.KitCheckResult = 0;
        }
        order.Status = 1;
    }

    data.ApplyTo(order, new[] { "product_name", "plan_qty", "priority", "status" });
    await _db.SaveChangesAsync();
    await RefreezeActiveOrdersAsync(0);

    return new { orders = new[] { ToDict(order) }, total = 1 };
}
```

- [ ] **Step 2: Commit**

```bash
git add dip-system/api/Services/OrderService.cs
git commit -m "feat: UpdateAsync 支持多产品编辑 + BOM一致性校验 + 简化数量更新"
```

---

### Task 6: 后端 — 删除订单级联 order_products

**Files:**
- Modify: `dip-system/api/Services/OrderService.cs` — `DeleteAsync` 方法

- [ ] **Step 1: 在 `DeleteAsync` 中添加 order_products 级联删除**

在 `DeleteAsync` 方法中，找到 `order.IsDeleted = true;` 之后、`var preps = ...` 之前，添加：

```csharp
var orderProducts = await _db.OrderProducts.Where(op => op.OrderId == orderId).ToListAsync();
foreach (var op in orderProducts) op.IsDeleted = true;
```

- [ ] **Step 2: Commit**

```bash
git add dip-system/api/Services/OrderService.cs
git commit -m "fix: DeleteAsync 级联软删除 order_products"
```

---

### Task 7: 前端 — 产品列表接口适配 + 新建/编辑弹窗重写

**Files:**
- Modify: `dip-system/frontend-web/src/pages/OrderList.tsx`

**Interfaces:**
- Consumes: `GET /orders/products`（升级后返回 `{ product_name, product_id, bom_count }`）
- Produces: 多产品表格批量选择 UI

- [ ] **Step 1: 更新 TypeScript 类型和 state**

在 `OrderList` 组件顶部，将 `products` state 类型从 `string[]` 改为结构化类型：

```tsx
interface ProductInfo {
  product_id: number;
  product_name: string;
  bom_count: number;
}
```

替换：
```tsx
// 旧：
const [products, setProducts] = useState<string[]>([]);

// 新：
const [products, setProducts] = useState<ProductInfo[]>([]);
```

新增已选产品表格的 state：

```tsx
interface SelectedProduct {
  product_id: number;
  product_name: string;
  bom_count: number;
  plan_qty: number;
}

const [selectedProducts, setSelectedProducts] = useState<SelectedProduct[]>([]);
const [productSearch, setProductSearch] = useState('');
const [showDropdown, setShowDropdown] = useState(false);
```

- [ ] **Step 2: 更新 `loadMeta` 中的产品加载逻辑**

```tsx
const loadMeta = async () => {
  try {
    const [pRes, lRes] = await Promise.all([api.get('/orders/products'), api.get('/lines')]);
    setProducts((pRes.data || []) as ProductInfo[]);
    setLines(lRes.data || []);
    return lRes.data || [];
  } catch { return []; }
};
```

- [ ] **Step 3: 重写 `openCreate`**

```tsx
const openCreate = async () => {
  setEditId(null);
  setBomItems([]);
  setSelectedProducts([]);
  setProductSearch('');
  const loadedLines = await loadMeta();
  setForm({ line_id: loadedLines[0]?.id || 1, product_name: '', plan_qty: 1, priority: 2 });
  setShowDialog(true);
};
```

- [ ] **Step 4: 重写 `openEdit`（并行加载，避免闭包陷阱）**

```tsx
const openEdit = async (order: any) => {
  setEditId(order.id);
  setSelectedProducts([]);
  setProductSearch('');
  setForm({ line_id: order.line_id, product_name: order.product_name, plan_qty: order.plan_qty, priority: order.priority });

  // 并行加载所有数据（用局部变量，避免 React state 闭包陷阱）
  try {
    const [pRes, lRes, bomRes, detailRes] = await Promise.all([
      api.get('/orders/products'),
      api.get('/lines'),
      api.get(`/orders/${order.id}/bom-status`),
      api.get(`/orders/${order.id}/details`)
    ]);
    setProducts((pRes.data || []) as ProductInfo[]);
    setLines(lRes.data || []);
    setBomItems((bomRes.data || []).map((item: any) => ({ ...item, stock: item.net })));

    // 用局部变量 prods 而非 state，确保读到最新值
    const prods = (pRes.data || []) as ProductInfo[];
    const ops = detailRes.data?.order_products || [];
    const enriched = ops.map((op: any) => {
      const prod = prods.find((p: ProductInfo) => p.product_name === op.product_name);
      return { ...op, bom_count: prod?.bom_count || 0 };
    });
    setSelectedProducts(enriched);
  } catch { setBomItems([]); }
  setShowDialog(true);
};
```

- [ ] **Step 5: 新增产品搜索和添加逻辑**

```tsx
// 模糊搜索过滤
const filteredProducts = products.filter(p =>
  p.product_name.toLowerCase().includes(productSearch.toLowerCase()) ||
  (p.bom_count !== undefined && p.product_name.includes(productSearch))
);

// 添加产品到表格
const addProduct = (prod: ProductInfo) => {
  if (selectedProducts.some(sp => sp.product_name === prod.product_name)) return; // 已存在
  setSelectedProducts([...selectedProducts, {
    product_id: prod.product_id,
    product_name: prod.product_name,
    bom_count: prod.bom_count,
    plan_qty: 1
  }]);
  setProductSearch('');
  setShowDropdown(false);
};

// 删除已选产品
const removeProduct = (idx: number) => {
  setSelectedProducts(selectedProducts.filter((_, i) => i !== idx));
};

// 修改计划数量
const updatePlanQty = (idx: number, qty: number) => {
  const updated = [...selectedProducts];
  updated[idx] = { ...updated[idx], plan_qty: qty };
  setSelectedProducts(updated);
};
```

- [ ] **Step 6: 计算分组预览**

```tsx
// 按 bom_count 分组预览（同 bom_count 的为一组）
const groupPreview = (() => {
  const groups = new Map<number, SelectedProduct[]>();
  selectedProducts.forEach(sp => {
    const key = sp.bom_count;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(sp);
  });
  return Array.from(groups.entries());
})();

// 编辑模式下的 BOM 一致性检测
const allSameBomCount = selectedProducts.length <= 1
  || new Set(selectedProducts.map(p => p.bom_count)).size === 1;
```

- [ ] **Step 7: 重写 `handleSubmit`**

```tsx
const handleSubmit = async () => {
  if (selectedProducts.length === 0) return alert('请至少选择一个产品');
  if (editId && !allSameBomCount) return alert('编辑后的产品 BOM 不一致，请删除当前订单并重新创建');

  try {
    const payload = {
      line_id: form.line_id,
      priority: form.priority,
      products: selectedProducts.map(sp => ({
        product_id: sp.product_id,
        product_name: sp.product_name,
        plan_qty: sp.plan_qty
      }))
    };
    if (editId) {
      await api.put(`/orders/${editId}`, payload);
      showToast('订单更新成功', 'success');
    } else {
      const res = await api.post('/orders', payload);
      const total = res.data?.total || 1;
      showToast(`订单创建成功！已生成 ${total} 个订单`, 'success');
    }
    setShowDialog(false);
    fetchData();
  } catch {}
};
```

- [ ] **Step 8: 重写弹窗 JSX — 产品搜索 + 表格**

在弹窗 JSX 中，替换原有的"产品名称下拉"和"计划数量"区域：

```tsx
{/* 产品搜索栏 */}
<div className="mb-4">
  <label className="block text-sm font-medium mb-1">搜索产品</label>
  <div className="relative">
    <input
      type="text"
      className="w-full border p-2 rounded"
      placeholder="输入产品名称模糊搜索..."
      value={productSearch}
      onChange={e => { setProductSearch(e.target.value); setShowDropdown(true); }}
      onFocus={() => setShowDropdown(true)}
      onBlur={() => setTimeout(() => setShowDropdown(false), 200)}
    />
    {showDropdown && productSearch && (
      <div className="absolute z-10 w-full bg-white border rounded-b shadow-lg max-h-48 overflow-auto">
        {filteredProducts.length === 0 ? (
          <div className="p-2 text-gray-400 text-sm">无匹配产品</div>
        ) : (
          filteredProducts.map(p => (
            <div
              key={p.product_name}
              className={`px-3 py-2 cursor-pointer hover:bg-blue-50 flex justify-between ${
                p.bom_count === 0 ? 'text-gray-300 cursor-not-allowed' : ''
              } ${selectedProducts.some(sp => sp.product_name === p.product_name) ? 'bg-green-50' : ''}`}
              onMouseDown={() => { if (p.bom_count > 0) addProduct(p); }}
            >
              <span>{p.product_name}</span>
              <span className="text-xs text-gray-400">
                {p.bom_count === 0 ? '无BOM' : `${p.bom_count} 种料号`}
                {selectedProducts.some(sp => sp.product_name === p.product_name) && ' ✓'}
              </span>
            </div>
          ))
        )}
      </div>
    )}
  </div>
</div>
```

- [ ] **Step 9: 已选产品表格 JSX**

```tsx
{/* 已选产品表格 */}
{selectedProducts.length > 0 && (
  <div className="mb-4">
    <h3 className="font-medium mb-2">已选产品</h3>
    <table className="w-full border text-sm">
      <thead><tr className="bg-gray-100">
        <th className="p-2 text-left">产品</th>
        <th className="p-2 text-center">BOM料号数</th>
        <th className="p-2 text-right">计划数量</th>
        <th className="p-2 text-center">操作</th>
      </tr></thead>
      <tbody>
        {selectedProducts.map((sp, idx) => (
          <tr key={idx} className={`border-t ${editId && !allSameBomCount && sp.bom_count !== selectedProducts[0]?.bom_count ? 'bg-red-50' : ''}`}>
            <td className="p-2">{sp.product_name}</td>
            <td className="p-2 text-center">{sp.bom_count}</td>
            <td className="p-2 text-right">
              <input
                type="number"
                className="w-20 border p-1 rounded text-right"
                min={1}
                value={sp.plan_qty}
                onChange={e => updatePlanQty(idx, Number(e.target.value) || 1)}
              />
            </td>
            <td className="p-2 text-center">
              <button onClick={() => removeProduct(idx)}
                className="text-red-500 hover:text-red-700 text-lg leading-none">&times;</button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  </div>
)}
```

- [ ] **Step 10: 分组预览 + 一致性警告 JSX**

```tsx
{/* 分组预览 */}
{selectedProducts.length > 0 && (
  <div className="mb-4 p-3 bg-gray-50 rounded text-sm">
    {editId && !allSameBomCount ? (
      <p className="text-red-600 font-medium">⚠ 编辑后的产品 BOM 不一致，请删除当前订单并重新创建</p>
    ) : (
      <p className="text-gray-600">
        将生成 <strong>{groupPreview.length}</strong> 个订单：
        {groupPreview.map(([bomCount, prods], gi) => (
          <span key={gi}>
            {gi > 0 && '；'}
            订单{gi + 1}: {prods.map(p => p.product_name).join(' / ')}
            {groupPreview.length > 1 && `（${bomCount}种料号）`}
          </span>
        ))}
      </p>
    )}
  </div>
)}
```

- [ ] **Step 11: 替换弹窗中原有的产品下拉和计划数量行**

删除原有 grid-cols-2 中的产品下拉 + 计划数量行，替换为顶部一行（产线 + 优先级）：

```tsx
<div className="grid grid-cols-2 gap-4 mb-4">
  <div>
    <label className="block text-sm font-medium mb-1">产线</label>
    <select className="w-full border p-2 rounded" value={form.line_id}
      onChange={e => setForm({ ...form, line_id: Number(e.target.value) })}>
      {lines.map((l: any) => <option key={l.id} value={l.id}>{l.line_name}</option>)}
    </select>
  </div>
  <div>
    <label className="block text-sm font-medium mb-1">优先级</label>
    <select className="w-full border p-2 rounded" value={form.priority}
      onChange={e => setForm({ ...form, priority: Number(e.target.value) })}>
      <option value={3}>高</option><option value={2}>中</option><option value={1}>低</option>
    </select>
  </div>
</div>
```

- [ ] **Step 12: 删除原有的 BOM 预览表格**（因为新产品选择模式下，创建时不展示合并 BOM，只在提交时后端处理）

保留 `editId` 模式下的 `bomItems` 表格（编辑时展示已有 BOM 状态），但创建模式不再显示。

- [ ] **Step 13: Commit**

```bash
git add dip-system/frontend-web/src/pages/OrderList.tsx
git commit -m "feat: 订单新建/编辑弹窗改为多产品表格选择 + 模糊搜索 + 分组预览"
```

---

### Task 8: 前端 — 详情弹窗补充产品明细表格

**Files:**
- Modify: `dip-system/frontend-web/src/pages/OrderList.tsx` — 详情弹窗 JSX

- [ ] **Step 1: 在详情弹窗的基本信息区下方，新增产品明细小表格**

在 `{/* Basic Info */}` 的 grid 之后、`{/* BOM Items */}` 之前，添加：

```tsx
{/* Order Products */}
{detailData.order_products && detailData.order_products.length > 0 && (
  <>
    <h3 className="font-medium mb-2">产品明细</h3>
    <table className="w-full border text-sm mb-4">
      <thead><tr className="bg-gray-100">
        <th className="p-2 text-left">产品名称</th>
        <th className="p-2 text-right">计划数量</th>
      </tr></thead>
      <tbody>{detailData.order_products.map((op: any, idx: number) => (
        <tr key={idx} className="border-t">
          <td className="p-2">{op.product_name}</td>
          <td className="p-2 text-right">{op.plan_qty}</td>
        </tr>
      ))}</tbody>
    </table>
  </>
)}
```

- [ ] **Step 2: Commit**

```bash
git add dip-system/frontend-web/src/pages/OrderList.tsx
git commit -m "feat: 订单详情弹窗新增产品明细表格"
```

---

### Task 9: 端到端验证

- [ ] **Step 1: 启动后端**

```bash
cd dip-system/api && dotnet run
```

确认启动无报错，`order_products` 表自动创建。

- [ ] **Step 2: 启动前端**

```bash
cd dip-system/frontend-web && npm run dev
```

- [ ] **Step 3: 测试用例**

| # | 操作 | 预期 |
|---|------|------|
| 1 | 新建订单 → 搜索"主板" → 选中产品A（BOM 8种料号） → plan_qty=100 → 确认创建 | 生成1个订单，订单号正常，备料单自动生成 |
| 2 | 新建订单 → 选中产品A（BOM 8种）+ 产品B（BOM 5种，不同组） → 各设 plan_qty → 创建 | 生成2个订单，各带自己的 BOM |
| 3 | 新建订单 → 选中产品A + 产品C（同 BOM 料号集合） → 创建 | 生成1个订单，product_name 用 `/` 拼接，plan_qty 为两者之和 |
| 4 | 搜索一个无 BOM 的产品 → 选中创建 | 前端搜索置灰不可选；若绕过，后端返回"没有 BOM 数据" |
| 5 | 编辑订单 → 新增不同 BOM 组的产品 | 预览区变红警告，保存时阻断提示 |
| 6 | 编辑订单 → 修改 plan_qty | 保存成功，BOM required_qty 更新 |
| 7 | 删除订单 | order_products 级联软删除 |
| 8 | 详情弹窗 | 产品明细表格正确显示 |

- [ ] **Step 4: Commit 任何验证中发现的修复**

---
