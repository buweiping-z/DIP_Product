# 替代料移库功能重做 — 设计文档

**日期**: 2026-07-13  
**状态**: 已确认

## 1. 问题与目标

### 现状问题
- 网页端每次只能创建一组替代移库，10个料需要操作10次
- 网页端选择部品没有筛选搜索，物料多时难以定位
- 手机端只有手动输入ID表单，没有扫码功能，无法在车间实际使用
- 移库记录（SubstituteRecord）是单表单记录模式，不支持一个订单包含多条明细

### 目标
- 网页端：支持一个订单多明细行，每行带搜索筛选
- 手机端：扫码 → 匹配 → 确认 → 提交的完整流程
- 手机端：支持中途退出再进入，已确认的明细不重扫
- 操作效率：明细按来源库位排序，一趟走完
- 后端：确认后自动执行移库 + 刷新订单冻结库存

## 2. 数据模型

### 新表

```
substitute_orders (订单头)
├── id (PK)
├── order_no           VARCHAR(50)  订单号, SUB-20260713-001
├── status             INT          1=待确认, 2=已完成, 3=已取消
├── operator_id        BIGINT       操作人
├── created_at, updated_at

substitute_details (订单明细)
├── id (PK)
├── order_id           BIGINT (FK → substitute_orders.id)
├── original_part_id / original_part_no      缺料部品
├── substitute_part_id / substitute_part_no  替代部品
├── source_location_id / source_location_code 替代料来源库位
├── target_location_id / target_location_code 缺料目标库位
├── quantity           DECIMAL      移库数量
├── status             INT          1=待确认, 2=已确认
├── created_at, updated_at
```

**不维护 `detail_count` / `confirmed_count` 冗余字段**，实时查询：
- 明细总数：`SELECT COUNT(*) FROM substitute_details WHERE order_id = ?`
- 已确认数：`SELECT COUNT(*) FROM substitute_details WHERE order_id = ? AND status = 2`

避免并发更新计数导致的数值错乱（两个操作员同时确认不同明细，各自读-改-写 confirmed_count 互相覆盖）。

### 旧表
- `substitute_records` 保留不动，历史数据可查询

## 3. 后端 API

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/substitute/orders` | 订单列表（page, page_size, search） |
| GET | `/substitute/orders/{id}` | 订单详情（含明细 + 实时统计） |
| POST | `/substitute/orders` | 创建订单（含多明细数组） |
| PUT | `/substitute/orders/{id}` | 编辑待确认订单（仅未确认明细可改） |
| POST | `/substitute/orders/{id}/cancel` | 取消订单（状态→3，已确认明细回退status→1） |
| GET | `/substitute/orders/{id}/details` | 获取明细列表（手机端用，按来源库位排序） |
| POST | `/substitute/orders/{id}/details/{detail_id}/confirm` | 确认单条明细（标记status=2） |
| POST | `/substitute/orders/{id}/confirm` | 全部确认后提交：执行移库 + 订单完成 + Refreeze |

### API 行为细节

**POST /details/{detail_id}/confirm**：
- 入参：空（detail_id 在 URL 中）
- 验证：detail 属于该订单、status=1（未确认）
- 标记 `detail.status = 2`
- 不执行实际库存变更
- 返回：`{ detail_id, confirmed: true }`

**POST /confirm**：
- 验证全部明细 status=2
- **数据库事务包裹**整个循环（EF Core `IDbContextTransaction`）
- 遍历明细：`TransferOutCoreAsync`（替代料出库）+ `AddCoreAsync`（缺料入库）
- 订单 status → 2（已完成）
- 调用 `RefreezeActiveOrdersAsync` 刷新冻结
- **任何一条失败 → 全部回滚**，订单保持待确认状态，返回具体失败原因

**POST /cancel**：
- 订单 status → 3（已取消）
- 如果有已确认明细（status=2），统一回退为 status=1
- 不物理删除，保留记录可追溯

**PUT /{id} 编辑规则**：
- 仅 status=1 的订单可编辑
- 已确认明细行（status=2）：灰色只读，不可修改，不可删除
- 未确认明细行（status=1）：可修改，可删除
- 可新增明细行
- 如果已确认数为 0，等同于完全重建明细列表

## 4. 网页端

### 订单列表页
- 搜索栏：料号输入框 + 日期范围
- 订单表格：订单号、明细数/已确认数（实时查询）、状态标签、创建时间、操作按钮
- 待确认订单（status=1）：展开查看明细、编辑（遵守编辑规则）、取消
- 已完成（status=2）/已取消（status=3）：仅查看

### 新建/编辑订单弹窗
- 顶部搜索筛选框（料号/名称模糊匹配）
- 多行明细表：
  - 每行：替代部品下拉（可搜索）→ 来源库位下拉 → 缺料部品下拉 → 目标库位下拉 → 数量输入
  - 编辑模式下：已确认行灰色只读 + 锁图标，不可删不可改
  - 未确认行：可删可改
  - "添加一行"按钮
- 提交时一次性创建/更新订单

## 5. 手机端

### 文件结构 (3文件)
- `SubstituteScreen.kt` — 订单列表 + 扫码确认（两个Composable）
- `SubstituteViewModel.kt` — 业务逻辑
- 复用现有 `QrCodeScanner.kt` / `BarcodeAnalyzer.kt` / `ScannerOverlay.kt`

### 界面A — 订单列表
- 待确认订单列表
- 点击进入扫码确认界面

### 界面B — 扫码确认

```
┌─────────────────────────────┐
│  替代料移库 #SUB-001         │
│  已完成: 2/5                 │
├─────────────────────────────┤
│                             │
│   📷 相机预览区域（圆角）     │
│                             │
├─────────────────────────────┤
│  提示：请扫描替代部品条码     │
│  ─────────────────────────  │
│  替代料: A12345678          │
│  来源库位: A-01-01          │
│  缺料: B87654321            │
│  目标库位: B-02-01          │
│  数量: 5                    │
├─────────────────────────────┤
│  [ 取消重扫 ]  [ 确认 ]     │  ← 匹配到多条时弹出选择列表
├─────────────────────────────┤
│  [ 提交并完成移库 ]          │  ← 全部确认后出现
└─────────────────────────────┘
```

### 扫码匹配规则（客户端执行）

1. 扫替代部品条码 → 解析料号（>14位取 length-4，≤14位取全部）
2. 在**未确认明细**（status=1）中按替代料号匹配（大小写不敏感 + 去空格）
3. **匹配到 0 条**：显示"无匹配明细，请检查条码"
4. **匹配到 1 条**：自动选中，显示明细详情（替代料/来源库位/缺料/目标库位/数量）
5. **匹配到多条**：弹出底部选择列表，每行显示"来源库位 + 缺料料号"做区分，点击选择
6. 显示匹配结果后 → 操作员核对 → 点"确认" 或 "取消重扫"
7. "取消重扫"：清除当前匹配结果，不改变已确认明细，回到等待扫码状态

### 整体流程
1. 进入 → 加载明细列表（按来源库位排序），从明细status恢复已确认状态
2. 按扫码按钮 → 扫替代部品条码 → 客户端匹配
3. 核对 → 确认 → POST `/details/{detail_id}/confirm` → 进度+1
4. 全部确认后 → "提交并完成移库" → POST `/confirm` → 完成
5. 中途退出：已确认明细 status=2 持久化，再进入恢复

### 数量处理
- 数量仅用于操作员核对，手机端不修改数量
- 实际移库数量由订单明细中的 quantity 决定

## 6. 关键决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 扫码模式 | 只扫替代料号（方案A） | 订单已指定缺料/库位/数量，扫一次即可防错 |
| 同料号多明细 | 弹出选择列表 | 显示来源库位 + 缺料料号区分 |
| 明细排序 | 按来源库位排序 | 操作员按库位顺序一趟走完，不来回跑 |
| 退出恢复 | confirm即标记，持久化状态 | 参考备料/上线现有模式 |
| 移库时机 | 全部确认后一次性 + 数据库事务 | 部分失败全部回滚 |
| 数量处理 | 手机端仅核对，不修改 | 库存处理在订单层面 |
| 计数方式 | 实时 COUNT，不维护冗余字段 | 避免并发计数错乱 |
| 扫码错误处理 | 取消重扫按钮 | 清除当前匹配，不改变已确认状态 |
| 取消订单 | 状态流转，不物理删除 | 可追溯 |
| 编辑已部分确认订单 | 已确认行只读，未确认行可改 | 灵活 + 安全 |
| API 命名 | `/details/{id}/confirm` | 语义准确，非误导的"scan" |