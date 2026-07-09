# 上架管理重构 — 直接上架模式

**日期:** 2026-07-09
**状态:** 确认

## 概述

将上架管理从批次模式改为直接上架模式，取消批次号管理。前端新增期间查询功能，手机端改为单步直接上架流程。

## 核心变更

| 层 | 变更 |
|------|------|
| 数据库 | `MaterialShelving` 增加 `part_name` 冗余字段 |
| 后端 | 新增 `POST /shelving/direct` + `GET /shelving/records`，废弃旧 batch 系列端点 |
| 前端 | `LoadingList.tsx` 重写，增加搜索栏 + 期间查询 |
| 手机端 | `ShelvingScreen` 重写，4 步直接上架流程 |

## 1. 手机端操作流程

```
1. 扫部品条码 → 显示部品信息（料号、名称、当前库存库位/数量）
                        ↓
2. 扫目标库位条码 → 确认放置位置
                        ↓
3. 输入数量
                        ↓
4. 确认 → 目标库位库存增加 + 写上架记录
```

### UI 状态设计

| 步骤 | 界面 | 用户操作 |
|------|------|---------|
| Step1 | 条码输入框 + 搜索按钮 | 扫部品条码 |
| Step1 结果 | 显示：料号、部品名称、当前库存分布列表 | 确认部品正确，点击"下一步" |
| Step2 | 库位条码输入框 | 扫目标库位条码 |
| Step2 结果 | 显示：库位编码、库位描述 | 确认库位正确，点击"下一步" |
| Step3 | 数量输入框（数字键盘） | 输入数量 |
| Step4 | 摘要确认页：部品 + 库位 + 数量 | 点击"确认上架" |
| 完成 | 成功提示 | 自动返回 Step1 准备下一次扫描 |

## 2. 后端 API

### POST /api/v1/shelving/direct

**请求：**
```json
{
  "barcode": "PART001",
  "target_location_code": "LOC-A-01",
  "quantity": 10
}
```

**响应：**
```json
{
  "code": 0,
  "data": {
    "id": 123,
    "part_no": "PART001",
    "part_name": "电阻 10K",
    "target_location_code": "LOC-A-01",
    "quantity": 10,
    "operator_id": 1,
    "loaded_at": "2026-07-09T10:30:00Z"
  },
  "message": "上架成功"
}
```

**服务端逻辑：**
1. `barcode` 匹配 `PartNo` → 获取 `part_id`, `part_name`
2. `target_location_code` → 获取库位 ID
3. `InventoryService.AddCore()` → 目标库位库存增加
4. 写入 `MaterialShelving` 记录

### GET /api/v1/shelving/records

**查询参数：**

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `part_name` | string | 否 | 部品名称，模糊匹配 (LIKE %x%) |
| `location_code` | string | 否 | 目标库位编码 |
| `start_date` | string | 否 | 上架开始时间，格式 yyyy-MM-dd |
| `end_date` | string | 否 | 上架结束时间，格式 yyyy-MM-dd |
| `page` | int | 否 | 页码，默认 1 |
| `page_size` | int | 否 | 每页条数，默认 20 |

**响应：**
```json
{
  "code": 0,
  "data": {
    "total": 100,
    "page": 1,
    "page_size": 20,
    "items": [
      {
        "id": 123,
        "part_no": "PART001",
        "part_name": "电阻 10K",
        "target_location_code": "LOC-A-01",
        "quantity": 10,
        "operator_name": "张三",
        "loaded_at": "2026-07-09T10:30:00Z"
      }
    ]
  }
}
```

## 3. 前端页面 (LoadingList.tsx)

### 搜索栏

```
┌────────────────────────────────────────────────────┐
│ 部品名称: [____________]  库位: [____________]     │
│ 上架时间: [2026-07-01] 至 [2026-07-09]             │
│ [查询]  [清除]                                      │
└────────────────────────────────────────────────────┘
```

- 输入即搜，300ms 防抖（参考 InventoryList 的模式）
- 清除按钮重置所有筛选条件
- `start_date` 只传日期部分 "yyyy-MM-dd"
- `end_date` 只传日期部分 "yyyy-MM-dd"

### 表格

| 部品编号 | 部品名称 | 库位 | 数量 | 上架时间 | 担当者 |
|----------|---------|------|------|---------|--------|
| PART001 | 电阻 10K | LOC-A-01 | 10 | 2026-07-09 10:30 | 张三 |

## 4. 手机端 (ShelvingScreen.kt)

### UI 状态

```kotlin
data class ShelvingUiState(
    // Step 1
    val partBarcode: String = "",
    val scannedPart: PartItem? = null,          // 扫到的部品
    val partLocations: List<InventoryAvailable> = emptyList(),  // 部品当前库存分布
    val partLookupMsg: String? = null,
    // Step 2
    val locationBarcode: String = "",
    val scannedLocation: LocationItem? = null,   // 扫到的目标库位
    val locationLookupMsg: String? = null,
    // Step 3
    val quantity: String = "",
    // Flow
    val step: Int = 1,                            // 1-4
    val isLoading: Boolean = false,
    val resultMsg: String? = null
)
```

### 新增 API 调用

```kotlin
// 根据条码查部品
@GET("api/v1/parts")
suspend fun getParts(@Query("part_no") partNo: String, ...): ApiResponse<PageResult<PartItem>>

// 查部品库存分布
@GET("api/v1/inventory/available/{partId}")
suspend fun getAvailableInventory(@Path("partId") partId: Int): ApiResponse<List<InventoryAvailable>>

// 根据编码查库位
@GET("api/v1/locations")
suspend fun getLocations(@Query("location_code") locationCode: String, ...): ApiResponse<PageResult<LocationItem>>

// 直接上架
@POST("api/v1/shelving/direct")
suspend fun directShelving(@Body request: DirectShelvingRequest): ApiResponse<ShelvingRecord>
```

### 新增 DTO

```kotlin
data class DirectShelvingRequest(
    val barcode: String,
    @SerializedName("target_location_code") val targetLocationCode: String,
    val quantity: Double
)

data class ShelvingRecord(
    val id: Int,
    @SerializedName("part_no") val partNo: String,
    @SerializedName("part_name") val partName: String,
    @SerializedName("target_location_code") val targetLocationCode: String,
    val quantity: Double,
    @SerializedName("operator_id") val operatorId: Int,
    @SerializedName("loaded_at") val loadedAt: String?
)
```

## 5. 数据库变更

### MaterialShelving 表新增字段

```sql
ALTER TABLE material_shelvings ADD COLUMN part_name VARCHAR(200) DEFAULT '' AFTER part_no;
```

### Entity 模型更新

```csharp
[Column("part_name")]
public string PartName { get; set; } = string.Empty;
```

### 索引建议

```sql
CREATE INDEX idx_material_shelvings_loaded_at ON material_shelvings(loaded_at);
CREATE INDEX idx_material_shelvings_part_name ON material_shelvings(part_name);
CREATE INDEX idx_material_shelvings_target_location_id ON material_shelvings(target_location_id);
```

## 6. 废弃内容（保留不动）

以下端点/代码不做删除，仅新代码不再使用：

- `POST /api/v1/shelving/batch` — 创建批次
- `GET /api/v1/shelving/batch` — 批次列表
- `GET /api/v1/shelving/batch/{id}` — 批次详情
- `POST /api/v1/shelving/batch/{id}/scan` — 批次扫码
- `POST /api/v1/shelving/batch/{id}/confirm` — 批次确认
- `POST /api/v1/shelving/batch/{id}/cancel` — 批次撤销
- `ShelvingBatch` / `ShelvingBatchItem` 表 — 保留历史数据
