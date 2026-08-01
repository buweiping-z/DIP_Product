# BOM 清单向前查找规则

**日期**: 2026-07-29
**状态**: 已确认

## 背景

当前创建订单时，BOM 匹配规则为：精确匹配当月 BOM → 无则兜底 NULL（通用版本）。
需求：当月无 BOM 时，应使用当月之前最新月份的 BOM，而非直接退到通用版本。

## 设计

### 修改方法

`OrderService.GetBomWithFallbackAsync` — 仅此一个方法。

### 当前逻辑

```
1. WHERE ProductName == name AND ProductionMonth == targetMonth → 找到返回
2. WHERE ProductName == name AND ProductionMonth IS NULL → 返回（可能空）
```

### 新逻辑

```
1. WHERE ProductName == name AND ProductionMonth == targetMonth → 找到返回
2. WHERE ProductName == name AND ProductionMonth <= targetMonth
   ORDER BY ProductionMonth DESC → 取第一条 → 找到返回
3. WHERE ProductName == name AND ProductionMonth IS NULL → 返回（可能空）
```

### 示例

产品 X2477DS55，BOM 分布在 2026_05、2026_06、2026_08：

| 订单生连 | 步骤 1 | 步骤 2 | 步骤 3 | 最终结果 |
|----------|--------|--------|--------|---------|
| 2026_07 | 未命中 | ≤07 中最近=06 | — | 2026_06 |
| 2026_06 | 命中 | — | — | 2026_06 |
| 2026_08 | 命中 | — | — | 2026_08 |
| 2026_04 | 未命中 | ≤04 无结果 | NULL | NULL（通用） |

### 步骤 2 实现要点

`ProductionMonth` 是 `VARCHAR(7)`，格式 `YYYY_MM`。因为格式固定（4 位年 + 下划线 + 2 位月），字符串排序等价于时间排序：`"2026_05" < "2026_06" < "2026_07"`。直接用 MySQL 的字符串比较即可，无需转换为日期类型。

```csharp
// 步骤 2: 向前查找 ≤ targetMonth 的最近 BOM
if (!string.IsNullOrEmpty(productionMonth))
{
    var recentBoms = await _db.ProductBoms
        .Where(b => b.ProductName.ToLower().Trim() == nameLower 
            && b.ProductionMonth != null 
            && b.ProductionMonth.CompareTo(productionMonth) <= 0)
        .OrderByDescending(b => b.ProductionMonth)
        .ToListAsync();
    if (recentBoms.Any())
    {
        var latestMonth = recentBoms.First().ProductionMonth;
        return recentBoms.Where(b => b.ProductionMonth == latestMonth).ToList();
    }
}
```

### 影响范围

`GetBomWithFallbackAsync` 被以下位置调用，全部自动生效：

| 调用方 | 场景 |
|--------|------|
| `CreateAsync` | 新建订单 → 分组前校验 BOM 存在性 |
| `CreateSingleOrder` | 新建订单 → 实际取 BOM 写入明细 |
| `UpdateAsync` | 编辑订单 → 变更产品时重取 BOM |
| `UpdatePlanQtyAsync` | 调整计划数量 → 重取 BOM |
| `GetProductBomAsync` | `GET /orders/product-bom` → 前端弹窗 BOM 预览 |
| `GetProductNamesAsync` | `GET /orders/products` → 前端产品下拉 |
| `GetBomStatusAsync` | `GET /orders/{id}/bom-status` → 编辑弹窗 BOM 状态 |
| `GetDetailAsync` | `GET /orders/{id}/details` → 订单详情 |

### 改动量

- 文件：1 个（`api/Services/OrderService.cs`）
- 方法：2 个（`GetBomWithFallbackAsync` + `GetProductNamesAsync`）
- 新增代码：约 20 行
- 无需数据库变更、无需前端变更

### GetProductNamesAsync 同步适配

前端新建订单弹窗的产品下拉列表通过 `GET /orders/products?production_month=2026_07` 获取可选产品。当前逻辑也是精确月份 + NULL fallback。需改为同样的向前查找规则：选 2026_07 时，只有 2026_06 BOM 的产品也应出现在列表中。

改为：对每个有 BOM 的产品，取其 ≤ targetMonth 的最新非 NULL 月份，若无则取 NULL。等效于用 `GetBomWithFallbackAsync` 的规则判断产品在指定月份是否有可用 BOM。
