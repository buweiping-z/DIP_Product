# 多产品合并订单 — 设计文档

> 日期: 2026-07-17 | 状态: 待审批

## 背景

当前订单管理存在两个效率瓶颈：

1. **新建订单只能单选产品**，逐个创建，跟不上生产现场效率需求
2. **BOM 相同的产品无法合并**，备料和上线时需重复扫描同一料号，浪费工时

## 目标

- 新建订单支持多产品批量选择，各自独立计划数量
- 同 BOM 料号集合的产品自动合并为一个订单，备料上线一次扫描完成
- 产品搜索支持模糊匹配，替代下拉翻找

---

## 数据模型

### 新增表：`order_products`

| 列 | 类型 | 说明 |
|----|------|------|
| id | long (PK) | |
| order_id | long (FK → production_orders) | 所属订单 |
| product_name | string | 产品名称 |
| plan_qty | decimal | 该产品计划数量 |
| created_at / updated_at / is_deleted | (BaseEntity) | |

### `production_orders` 变更

| 字段 | 变更 |
|------|------|
| `product_name` | 改为 `/` 拼接所有产品名（如 `主板V2.2 / 电源板V1`），列表展示用 |
| `plan_qty` | 改为存储所有产品 `plan_qty` 的总和，列表展示用 |

BomItems / PrepOrder / PrepDetails 关系不变。核心变化：BOM 来源从 1 个产品变为 N 个产品的合并。

---

## 合并算法

### 分组规则

按 BOM 料号集合（part_no 去重排序后）完全相同为同一组，忽略每种料号的用量差异。

```
选中产品:
├── 产品A: {LH_A, LH_B}  ← 组α
├── 产品B: {LH_A, LH_C}  ← 组β
├── 产品C: {LH_A, LH_B}  ← 组α ✓
└── 产品D: {LH_A, LH_C}  ← 组β ✓

结果:
├── 订单1 (组α): 产品A + 产品C
└── 订单2 (组β): 产品B + 产品D
```

### 数量合并

```
订单1 (产品A×100台 + 产品C×80台):
  LH_A: 1×100 + 1×80 = 180
  LH_B: 1×100 + 2×80 = 260
```

BomItem.required_qty = Σ(该产品BOM用量 × 该产品plan_qty)，跨同组所有产品。

---

## API 变更

### `POST /api/v1/orders` （新建）

```json
// 请求 — 新增 products 数组
{
  "line_id": 1,
  "priority": 2,
  "products": [
    { "product_name": "产品A", "plan_qty": 100 },
    { "product_name": "产品B", "plan_qty": 50 },
    { "product_name": "产品C", "plan_qty": 80 }
  ]
}
```

**处理流程：**

1. 逐产品加载 BOM，提取料号集合（part_no 去重排序）
2. 按料号集合分组
3. 每组生成一个 ProductionOrder：
   - order_no 自动生成
   - product_name = 组内产品名用 `/` 拼接
   - plan_qty = 组内所有产品 plan_qty 之和
   - 写入 order_products 表（每产品一行）
   - 合并 BOM → BomItems
   - 生成 PrepOrder + 合并后的 PrepDetails
4. RefreezeActiveOrdersAsync 统一冻结
5. 返回 `{ orders: [{ id, order_no, ... }], total: N }`

**兼容性：** 未传 `products` 时走旧逻辑（单产品模式），保证手机端等已有调用方不受影响。

### `PUT /api/v1/orders/{id}` （编辑）

加载 `order_products` 回填 → 用户增删改产品 → 确认时：

1. 更新 `order_products`（增删改）
2. 重新计算 `product_name` 和 `plan_qty`
3. 按新分组重新计算合并 BOM
4. BomItems / PrepDetails 同步增减：
   - 新增料号 → 创建 PrepDetail
   - 减少料号 → 软删除对应 PrepDetail（先解冻其冻结量）
   - 料号不变 → 只更新 required_qty
5. RefreezeActiveOrdersAsync 重新冻结

### `GET /api/v1/orders/{id}/details` （详情）

响应新增 `order_products` 字段：
```json
{
  "order_no": "WO...",
  "product_name": "主板V2.2 / 电源板V1",
  "plan_qty": 230,
  "order_products": [
    { "product_name": "主板V2.2", "plan_qty": 100 },
    { "product_name": "电源板V1", "plan_qty": 130 }
  ],
  "bom_items": [...],
  "prep_orders": [...]
}
```

### 删除订单

`DELETE /api/v1/orders/{id}` — 级联软删除 `order_products` 行。

---

## 前端

### 新建/编辑弹窗

- 弹窗宽度：900px
- 顶部：产线下拉 + 优先级下拉
- 产品搜索栏：输入时实时模糊匹配产品列表（`GET /orders/products`）
- 候选下拉框：悬停高亮，点击选中加入下方表格
- 已选产品表格：

| 产品 | BOM料号数 | 计划数量 | 操作 |
|------|-----------|----------|------|
| 主板V2 | 8 | 100 | 🗑 |
| 电源V1 | 8 | 50 | 🗑 |
| 接口V3 | 5 | 80 | 🗑 |

- 表格下方实时预览："将生成 N 个订单"，按 BOM 料号集合自动分组显示
- 编辑模式：打开时从 `order_products` 加载已有产品回填表格
- 校验：BOM 为空的产品阻止创建并提示；至少保留一个产品

### 订单列表

| 列 | 展示 |
|----|------|
| 订单号 | 不变 |
| 产品名称 | `/` 拼接，超长截断+省略号 |
| 计划数量 | 各产品 plan_qty 总和 |
| 优先级 | 不变 |
| 状态 | 不变 |
| 创建时间 | 不变 |
| 操作 | 详情 / 编辑 / 删除 |

### 详情弹窗

基本信息区新增"产品明细"小表格（产品名 / 计划数量），展示 `order_products` 内容。

---

## 异常处理

| 场景 | 处理 |
|------|------|
| 所选产品的 BOM 为空 | 警告"产品 X 没有 BOM 数据，请先导入 BOM"，阻止创建 |
| 全部产品分到同一组 | 正常创建 1 个订单 |
| 前端编辑时清空所有产品 | 阻止保存，"请至少保留一个产品" |

---

## 手机端影响

无改动。订单仍然是 1 个 ProductionOrder = 1 个 PrepOrder，备料/上线扫描流程不变，只是数量是合并后的。用户感知：扫一个料号覆盖了多个产品。

---

## 实施范围

| 层 | 文件 | 改动 |
|----|------|------|
| 数据库 | migration | 新增 `order_products` 表 |
| 后端 | `Models/Order.cs` | 新增 `OrderProduct` 实体；`ProductionOrder` 注释更新 |
| 后端 | `Services/OrderService.cs` | `CreateAsync` / `UpdateAsync` / `GetDetailAsync` / `DeleteAsync` 改造；新增 BOM 分组算法 |
| 后端 | `Controllers/OrdersController.cs` | 请求体兼容新旧格式 |
| 前端 | `OrderList.tsx` | 新建/编辑弹窗重写；列表列调整；详情弹窗补充；模糊搜索 |
