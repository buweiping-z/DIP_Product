# 上架管理重构 — 直接上架模式 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将上架管理从批次模式改为直接上架模式 — 扫部品 → 扫库位 → 输数量 → 确认，前端新增期间查询，手机端改为四步向导流。

**Architecture:** 后端新增 `POST /shelving/direct` 和 `GET /shelving/records` 两个端点替换旧 batch 端点，`MaterialShelving` 增加 `part_name` 冗余字段。`InventoryService.AddCoreAsync` 处理库存入库，无需源库位扣减。

**Tech Stack:** C# ASP.NET Core 8.0 + EF Core + React 18 TypeScript + Kotlin Jetpack Compose

## Global Constraints

- JSON 序列化全局 `SnakeCaseLower`，所有 DTO 属性返回 snake_case
- API 统一响应格式 `{ "code": 0, "data": {...}, "message": "..." }`
- 旧 batch 端点保留不动，仅不调用
- 数据库使用 `EnsureCreated()`，不生成 Migration
- 前端搜索 300ms 防抖，参考 InventoryList 模式
- 手机端 Kotlin 2.0.21 + Jetpack Compose BOM 2024.06.00
- 手机端使用 Gson + `@SerializedName` 映射 snake_case

---

### Task 1: 数据库 + Model — MaterialShelving 增加 PartName 字段

**Files:**
- Modify: `dip-system/api/Models/Shelving.cs:61-92`（MaterialShelving 类）
- Execute: SQL（手工执行 ALTER TABLE + CREATE INDEX）

**Interfaces:**
- Produces: `MaterialShelving.PartName` 属性（string），后续 Task 2/3 写入和读取

- [ ] **Step 1: 更新 MaterialShelving Entity**

在 `dip-system/api/Models/Shelving.cs` 的 `MaterialShelving` 类中，`PartNo` 属性后添加 `PartName`：

```csharp
public class MaterialShelving : BaseEntity
{
    [Column("part_id")]
    public long PartId { get; set; }

    [Column("part_no")]
    public string PartNo { get; set; } = string.Empty;

    // === 新增 ===
    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [Column("source_location_id")]
    public long? SourceLocationId { get; set; }

    [Column("target_location_id")]
    public long TargetLocationId { get; set; }

    // ... 其余字段不变
}
```

- [ ] **Step 2: 执行数据库 DDL**

在 MySQL 中执行以下 SQL（连接字符串见 `appsettings.json`）：

```sql
ALTER TABLE material_shelvings ADD COLUMN IF NOT EXISTS part_name VARCHAR(200) DEFAULT '' AFTER part_no;

CREATE INDEX IF NOT EXISTS idx_material_shelvings_loaded_at ON material_shelvings(loaded_at);
CREATE INDEX IF NOT EXISTS idx_material_shelvings_part_name ON material_shelvings(part_name);
CREATE INDEX IF NOT EXISTS idx_material_shelvings_target_location_id ON material_shelvings(target_location_id);
```

> 注意：MySQL 8.0 不支持 `ADD COLUMN IF NOT EXISTS` 和 `CREATE INDEX IF NOT EXISTS`。如果列/索引已存在会报错，报错可忽略。或者用以下安全写法：
>
> ```sql
> -- 忽略已存在错误
> SET @sql = (SELECT IF(COUNT(*) = 0, 'ALTER TABLE material_shelvings ADD COLUMN part_name VARCHAR(200) DEFAULT '''' AFTER part_no', 'SELECT 1') FROM information_schema.COLUMNS WHERE TABLE_NAME='material_shelvings' AND COLUMN_NAME='part_name');
> PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
> ```
>
> 简单做法：直接执行 `ALTER TABLE`，报错忽略。

- [ ] **Step 3: 验证**

```bash
cd dip-system/api && dotnet build
```

预期：编译成功。

- [ ] **Step 4: Commit**

```bash
git add dip-system/api/Models/Shelving.cs
git commit -m "feat: MaterialShelving 增加 PartName 字段"
```

---

### Task 2: 后端 — POST /api/v1/shelving/direct 直接上架

**Files:**
- Create: `dip-system/api/Controllers/ShelvingController.cs`（追加方法 + DTO）
- Modify: `dip-system/api/Services/ShelvingService.cs`（追加方法）

**Interfaces:**
- Produces: `POST /api/v1/shelving/direct` 端点
- Consumes: `InventoryService.AddCoreAsync(partId, locationId, qty, batchNo, operatorId, referenceType, referenceId)` from Task 1
- Consumes: `MaterialShelving.PartName` (Task 1)

- [ ] **Step 1: 在 ShelvingService.cs 添加 DirectShelvingAsync 方法**

在 `dip-system/api/Services/ShelvingService.cs` 末尾（类闭合 `}` 前）添加：

```csharp
public async Task<object> DirectShelvingAsync(string barcode, string targetLocationCode,
    decimal quantity, long operatorId)
{
    // 1. 条码匹配部品
    var part = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == barcode);
    if (part == null) throw AppException.NotFound($"未找到部品: {barcode}");

    // 2. 编码匹配库位
    var loc = await _db.WarehouseLocations.FirstOrDefaultAsync(l => l.LocationCode == targetLocationCode);
    if (loc == null) throw AppException.NotFound($"未找到库位: {targetLocationCode}");

    // 3. 库存入库
    var invSvc = new InventoryService(_db);
    await invSvc.AddCoreAsync(part.Id, loc.Id, quantity, "", operatorId, "ShelvingDirect");

    // 4. 写上架记录
    var record = new MaterialShelving
    {
        PartId = part.Id,
        PartNo = part.PartNo,
        PartName = part.PartName,
        TargetLocationId = loc.Id,
        Quantity = quantity,
        OperatorId = operatorId,
        Status = 1,
        LoadedAt = DateTime.UtcNow
    };
    _db.MaterialShelvings.Add(record);
    await _db.SaveChangesAsync();

    return new
    {
        id = record.Id,
        part_no = record.PartNo,
        part_name = record.PartName,
        target_location_id = record.TargetLocationId,
        target_location_code = loc.LocationCode,
        quantity = record.Quantity,
        operator_id = record.OperatorId,
        loaded_at = record.LoadedAt
    };
}
```

- [ ] **Step 2: 在 ShelvingController.cs 添加 DirectShelving 端点**

在 `dip-system/api/Controllers/ShelvingController.cs` 的类末尾（`}` 前）和文件末尾（`}` 后）添加：

```csharp
// 类内部 — 放在现有的 CancelBatch 方法之后
[HttpPost("direct")]
[Authorize]
public async Task<IActionResult> DirectShelving([FromBody] DirectShelvingRequest req)
{
    var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return Ok(ApiResponse.Ok(await _svc.DirectShelvingAsync(req.Barcode, req.TargetLocationCode, req.Quantity, userId), "上架成功"));
}
```

在文件末尾的 DTO class 区域添加：

```csharp
public class DirectShelvingRequest
{
    public string Barcode { get; set; } = "";
    public string TargetLocationCode { get; set; } = "";
    public decimal Quantity { get; set; }
}
```

- [ ] **Step 3: 编译验证**

```bash
cd dip-system/api && dotnet build
```

预期：编译成功，无错误。

- [ ] **Step 4: Commit**

```bash
git add dip-system/api/Controllers/ShelvingController.cs dip-system/api/Services/ShelvingService.cs
git commit -m "feat: 新增 POST /shelving/direct 直接上架端点"
```

---

### Task 3: 后端 — GET /api/v1/shelving/records 上架记录查询

**Files:**
- Modify: `dip-system/api/Controllers/ShelvingController.cs`（追加方法）
- Modify: `dip-system/api/Services/ShelvingService.cs`（追加方法）

**Interfaces:**
- Produces: `GET /api/v1/shelving/records?part_name=&location_code=&start_date=&end_date=&page=&page_size=` 端点
- Consumes: `MaterialShelving.PartName` (Task 1)

- [ ] **Step 1: 在 ShelvingService.cs 添加 GetRecordsAsync 方法**

在 `ShelvingService.cs` 末尾添加：

```csharp
public async Task<object> GetRecordsAsync(string? partName, string? locationCode,
    DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 20)
{
    var query = _db.MaterialShelvings.AsQueryable();

    if (!string.IsNullOrEmpty(partName))
        query = query.Where(m => m.PartName.Contains(partName));

    if (!string.IsNullOrEmpty(locationCode))
    {
        var locIds = await _db.WarehouseLocations
            .Where(l => l.LocationCode.Contains(locationCode))
            .Select(l => l.Id)
            .ToListAsync();
        query = query.Where(m => locIds.Contains(m.TargetLocationId));
    }

    if (startDate.HasValue)
        query = query.Where(m => m.LoadedAt >= startDate.Value);

    if (endDate.HasValue)
        query = query.Where(m => m.LoadedAt < endDate.Value.AddDays(1));

    var total = await query.CountAsync();
    var items = await query.OrderByDescending(m => m.Id)
        .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

    // 批量取库位编码和操作者姓名
    var locIds = items.Select(i => i.TargetLocationId).Distinct().ToList();
    var userIds = items.Select(i => i.OperatorId).Distinct().ToList();
    var locsMap = (await _db.WarehouseLocations.Where(l => locIds.Contains(l.Id)).ToListAsync())
        .ToDictionary(l => l.Id);
    var usersMap = (await _db.Operators.Where(u => userIds.Contains(u.Id)).ToListAsync())
        .ToDictionary(u => u.Id);

    return new
    {
        total, page, page_size = pageSize,
        items = items.Select(m => (object)new
        {
            m.Id,
            part_no = m.PartNo,
            part_name = m.PartName,
            target_location_id = m.TargetLocationId,
            target_location_code = locsMap.TryGetValue(m.TargetLocationId, out var l) ? l.LocationCode : "",
            quantity = m.Quantity,
            operator_id = m.OperatorId,
            operator_name = usersMap.TryGetValue(m.OperatorId, out var u) ? u.RealName : "",
            loaded_at = m.LoadedAt
        })
    };
}
```

- [ ] **Step 2: 在 ShelvingController.cs 添加 GetRecords 端点**

在 `ShelvingController.cs` 类的 `DirectShelving` 方法之后添加：

```csharp
[HttpGet("records")]
public async Task<IActionResult> GetRecords(
    [FromQuery] string? part_name,
    [FromQuery] string? location_code,
    [FromQuery] DateTime? start_date,
    [FromQuery] DateTime? end_date,
    [FromQuery] int page = 1,
    [FromQuery] int page_size = 20)
    => Ok(ApiResponse.Ok(await _svc.GetRecordsAsync(part_name, location_code, start_date, end_date, page, page_size)));
```

- [ ] **Step 3: 编译验证**

```bash
cd dip-system/api && dotnet build
```

预期：编译成功。

- [ ] **Step 4: Commit**

```bash
git add dip-system/api/Controllers/ShelvingController.cs dip-system/api/Services/ShelvingService.cs
git commit -m "feat: 新增 GET /shelving/records 上架记录查询端点"
```

---

### Task 4: 前端 — LoadingList.tsx 重写 + 期间查询

**Files:**
- Modify: `dip-system/frontend-web/src/pages/LoadingList.tsx`（全量重写）

**Interfaces:**
- Consumes: `GET /api/v1/shelving/records?part_name=&location_code=&start_date=&end_date=&page=&page_size=` (Task 3)

- [ ] **Step 1: 重写 LoadingList.tsx**

完整替换 `dip-system/frontend-web/src/pages/LoadingList.tsx`：

```tsx
import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';

export default function ShelvingList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [partName, setPartName] = useState('');
  const [locationCode, setLocationCode] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [msg, setMsg] = useState('');

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (partName) params.part_name = partName;
      if (locationCode) params.location_code = locationCode;
      if (startDate) params.start_date = startDate;
      if (endDate) params.end_date = endDate;
      setData((await api.get('/shelving/records', { params })).data?.items || []);
    } catch (err: any) {
      setMsg('查询失败: ' + (err.response?.data?.message || err.message));
    } finally { setLoading(false); }
  }, [partName, locationCode, startDate, endDate]);

  useEffect(() => { fetchData(); }, []);

  const handleSearch = () => fetchData();

  const handleClear = () => {
    setPartName('');
    setLocationCode('');
    setStartDate('');
    setEndDate('');
  };

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">上架管理</h1>

      {/* Search bar */}
      <div className="bg-white rounded-lg shadow p-4 mb-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-sm text-gray-600 mb-1">部品名称</label>
            <input className="border rounded px-3 py-1.5 w-40" placeholder="输入部品名称"
              value={partName} onChange={e => setPartName(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">库位</label>
            <input className="border rounded px-3 py-1.5 w-36" placeholder="输入库位编码"
              value={locationCode} onChange={e => setLocationCode(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSearch()} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">开始时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-40"
              value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm text-gray-600 mb-1">结束时间</label>
            <input type="date" className="border rounded px-3 py-1.5 w-40"
              value={endDate} onChange={e => setEndDate(e.target.value)} />
          </div>
          <button onClick={handleSearch}
            className="bg-blue-600 text-white px-4 py-1.5 rounded hover:bg-blue-700">查询</button>
          <button onClick={handleClear}
            className="text-gray-500 px-3 py-1.5 hover:text-gray-700">清除</button>
        </div>
      </div>

      {msg && <div className="bg-red-50 text-red-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">部品编号</th>
            <th className="p-3">部品名称</th>
            <th className="p-3">库位</th>
            <th className="p-3 text-right">数量</th>
            <th className="p-3">上架时间</th>
            <th className="p-3">担当者</th>
          </tr></thead>
          <tbody>{data.map((r: any) => (
            <tr key={r.id} className="border-t hover:bg-gray-50">
              <td className="p-3 font-mono text-sm">{r.part_no}</td>
              <td className="p-3">{r.part_name}</td>
              <td className="p-3 font-mono text-sm">{r.target_location_code}</td>
              <td className="p-3 text-right">{r.quantity}</td>
              <td className="p-3 text-sm text-gray-500">{r.loaded_at?.slice(0, 19) || '-'}</td>
              <td className="p-3">{r.operator_name || '-'}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}
```

- [ ] **Step 2: 提交**

```bash
git add dip-system/frontend-web/src/pages/LoadingList.tsx
git commit -m "feat: 上架管理页面重写 — 部品/库位/期间查询 + 明细表格"
```

---

### Task 5: 手机端 — DTO + API + Repository

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/models/Models.kt`（追加 DTO）
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/network/ApiService.kt`（追加/修改端点）
- Modify: `mobile-android/app/src/main/java/com/dip/material/data/repository/AppRepository.kt`（追加/修改方法）

**Interfaces:**
- Produces: `DirectShelvingRequest`, `ShelvingRecord` DTO
- Produces: `ApiService.directShelving()`, `ApiService.getParts(partNo)`, `ApiService.getLocations(locationCode)`, `ApiService.getAvailableInventory(partId)`
- Produces: `AppRepository.directShelving()`, `AppRepository.searchParts()`, `AppRepository.searchLocations()`, `AppRepository.getAvailableInventory()`

- [ ] **Step 1: 在 Models.kt 末尾追加 DTO**

在 `mobile-android/app/src/main/java/com/dip/material/data/models/Models.kt` 末尾追加：

```kotlin
// ===== Direct Shelving =====
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

- [ ] **Step 2: 在 ApiService.kt 追加/修改端点**

在 `mobile-android/app/src/main/java/com/dip/material/data/network/ApiService.kt` 中：

**修改 getParts（添加 part_no 查询参数）：**

```kotlin
@GET("api/v1/parts")
suspend fun getParts(
    @Query("part_no") partNo: String? = null,
    @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 5
): ApiResponse<PageResult<PartItem>>
```

**修改 getLocations（添加 location_code 查询参数）：**

```kotlin
@GET("api/v1/locations")
suspend fun getLocations(
    @Query("location_code") locationCode: String? = null,
    @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 5
): ApiResponse<PageResult<LocationItem>>
```

**在 Shelving 区域，替换旧的 batch 端点，追加 directShelving：**

```kotlin
// ===== Shelving =====
@POST("api/v1/shelving/direct")
suspend fun directShelving(@Body request: DirectShelvingRequest): ApiResponse<ShelvingRecord>
```

旧的 `getShelvingBatches`、`getShelvingDetail`、`confirmShelving`、`scanShelvingItem` 保留不动，不再调用。

- [ ] **Step 3: 在 AppRepository.kt 追加方法**

在 `AppRepository.kt` 的 Shelving 区域，替换为：

```kotlin
// Shelving
suspend fun directShelving(barcode: String, targetLocationCode: String, quantity: Double) =
    api.directShelving(DirectShelvingRequest(barcode, targetLocationCode, quantity))

// Parts & Locations — 追加按条件搜索的方法
suspend fun searchParts(partNo: String) = api.getParts(partNo = partNo)
suspend fun searchLocations(locationCode: String) = api.getLocations(locationCode = locationCode)
```

旧的 `getShelvingBatches`、`getShelvingDetail`、`confirmShelving`、`scanShelvingItem` 保留不动。

- [ ] **Step 4: 提交**

```bash
git add mobile-android/app/src/main/java/com/dip/material/data/models/Models.kt \
        mobile-android/app/src/main/java/com/dip/material/data/network/ApiService.kt \
        mobile-android/app/src/main/java/com/dip/material/data/repository/AppRepository.kt
git commit -m "feat: 手机端新增 DirectShelving DTO + API + Repository"
```

---

### Task 6: 手机端 — ShelvingViewModel 重写

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/ui/viewmodels/ViewModels.kt`（替换 Shelving 区域）

**Interfaces:**
- Produces: `ShelvingUiState`（新）、`ShelvingViewModel`（新）
- Consumes: `AppRepository.directShelving()`, `searchParts()`, `searchLocations()`, `getAvailableInventory()` (Task 5)

- [ ] **Step 1: 替换 Shelving 区域的 UI State 和 ViewModel**

在 `mobile-android/app/src/main/java/com/dip/material/ui/viewmodels/ViewModels.kt` 中，替换 `ShelvingUiState` 和 `ShelvingViewModel` 类（行 233-279）为：

```kotlin
// ===== Shelving =====
data class ShelvingUiState(
    // Step 1: 扫部品
    val partBarcode: String = "",
    val scannedPart: PartItem? = null,
    val partLocations: List<InventoryAvailable> = emptyList(),
    val partLookupMsg: String? = null,
    // Step 2: 扫库位
    val locationBarcode: String = "",
    val scannedLocation: LocationItem? = null,
    val locationLookupMsg: String? = null,
    // Step 3: 输数量
    val quantity: String = "",
    // Flow
    val step: Int = 1,          // 1=扫部品, 2=扫库位, 3=输数量, 4=确认
    val isLoading: Boolean = false,
    val resultMsg: String? = null
)

class ShelvingViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(ShelvingUiState())
    val state: StateFlow<ShelvingUiState> = _state.asStateFlow()

    fun lookupPart(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(partBarcode = barcode, isLoading = true, partLookupMsg = null) }
            try {
                val res = repo.searchParts(barcode)
                if (res.isSuccess) {
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        _state.update { it.copy(isLoading = false, partLookupMsg = "未找到部品: $barcode") }
                    } else {
                        val part = items.first()
                        // 同时查询库存分布
                        val invRes = repo.getAvailableInventory(part.id)
                        val locs = if (invRes.isSuccess) invRes.data ?: emptyList() else emptyList()
                        _state.update { it.copy(
                            scannedPart = part,
                            partLocations = locs,
                            isLoading = false,
                            step = 2
                        )}
                    }
                } else {
                    _state.update { it.copy(isLoading = false, partLookupMsg = res.message ?: "查询失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, partLookupMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun lookupLocation(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(locationBarcode = barcode, isLoading = true, locationLookupMsg = null) }
            try {
                val res = repo.searchLocations(barcode)
                if (res.isSuccess) {
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        _state.update { it.copy(isLoading = false, locationLookupMsg = "未找到库位: $barcode") }
                    } else {
                        _state.update { it.copy(
                            scannedLocation = items.first(),
                            isLoading = false,
                            step = 3
                        )}
                    }
                } else {
                    _state.update { it.copy(isLoading = false, locationLookupMsg = res.message ?: "查询失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, locationLookupMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun onQuantityInput(qty: String) {
        _state.update { it.copy(quantity = qty) }
    }

    fun gotoConfirm() {
        val qty = _state.value.quantity.toDoubleOrNull()
        if (qty == null || qty <= 0) {
            _state.update { it.copy(resultMsg = "请输入有效数量") }
            return
        }
        _state.update { it.copy(step = 4) }
    }

    fun confirmShelving() {
        val s = _state.value
        val part = s.scannedPart ?: return
        val loc = s.scannedLocation ?: return
        val qty = s.quantity.toDoubleOrNull() ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            try {
                val res = repo.directShelving(part.partNo, loc.locationCode, qty)
                if (res.isSuccess) {
                    _state.update { it.copy(
                        isLoading = false,
                        resultMsg = "上架成功！${part.partNo} → ${loc.locationCode} × ${qty}",
                        // 重置回 Step 1
                        step = 1,
                        partBarcode = "", scannedPart = null, partLocations = emptyList(),
                        locationBarcode = "", scannedLocation = null,
                        quantity = ""
                    )}
                } else {
                    _state.update { it.copy(isLoading = false, resultMsg = res.message ?: "上架失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, resultMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun resetToStep1() {
        _state.update { it.copy(
            step = 1, partBarcode = "", scannedPart = null, partLocations = emptyList(),
            partLookupMsg = null, locationBarcode = "", scannedLocation = null,
            locationLookupMsg = null, quantity = "", resultMsg = null
        )}
    }

    fun goToStep2() { _state.update { it.copy(step = 2) } }
    fun goToStep3() { _state.update { it.copy(step = 3) } }
    fun clearMsg() { _state.update { it.copy(resultMsg = null, partLookupMsg = null, locationLookupMsg = null) } }
}
```

- [ ] **Step 2: 提交**

```bash
git add mobile-android/app/src/main/java/com/dip/material/ui/viewmodels/ViewModels.kt
git commit -m "feat: 手机端 ShelvingViewModel 重写为四步直接上架流程"
```

---

### Task 7: 手机端 — ShelvingScreen.kt 重写

**Files:**
- Modify: `mobile-android/app/src/main/java/com/dip/material/ui/screens/ShelvingScreen.kt`（全量重写）

**Interfaces:**
- Consumes: `ShelvingViewModel` (Task 6) — 所有方法和 `ShelvingUiState`

- [ ] **Step 1: 重写 ShelvingScreen.kt**

完整替换 `mobile-android/app/src/main/java/com/dip/material/ui/screens/ShelvingScreen.kt`：

```kotlin
@file:OptIn(ExperimentalMaterial3Api::class)

package com.dip.material.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.dip.material.ui.viewmodels.ShelvingViewModel
import org.koin.androidx.compose.koinViewModel

@Composable
fun ShelvingScreen(viewModel: ShelvingViewModel = koinViewModel(), onBack: () -> Unit = {}) {
    val state by viewModel.state.collectAsState()
    var inputValue by remember { mutableStateOf("") }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("上架管理") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, "返回") } }
            )
        }
    ) { padding ->
        Column(Modifier.padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {

            // Step 指示器
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center) {
                (1..4).forEach { s ->
                    Surface(
                        shape = MaterialTheme.shapes.small,
                        color = if (s <= state.step) MaterialTheme.colorScheme.primary
                                else MaterialTheme.colorScheme.surfaceVariant,
                        modifier = Modifier.padding(horizontal = 4.dp)
                    ) {
                        Text("$s",
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp),
                            color = if (s <= state.step) MaterialTheme.colorScheme.onPrimary
                                    else MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }

            // 加载指示器
            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            // Step 1: 扫部品条码
            if (state.step == 1) {
                Text("第1步：扫部品条码", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = inputValue,
                    onValueChange = { inputValue = it },
                    label = { Text("部品条码") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    trailingIcon = {
                        Button(onClick = {
                            if (inputValue.isNotBlank()) {
                                viewModel.lookupPart(inputValue.trim())
                                inputValue = ""
                            }
                        }, enabled = inputValue.isNotBlank()) { Text("搜索") }
                    }
                )
                state.partLookupMsg?.let {
                    Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                }
            }

            // Step 2: 扫目标库位条码
            if (state.step == 2) {
                // 显示已扫到的部品信息
                state.scannedPart?.let { part ->
                    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("已选部品", style = MaterialTheme.typography.labelMedium)
                            Text("料号: ${part.partNo}", style = MaterialTheme.typography.bodyMedium)
                            Text("名称: ${part.partName}", style = MaterialTheme.typography.bodyMedium)
                            if (state.partLocations.isNotEmpty()) {
                                Text("当前库存:", style = MaterialTheme.typography.labelSmall)
                                state.partLocations.forEach { loc ->
                                    Text("  ${loc.locationCode}: 可用 ${loc.availableQty}",
                                        style = MaterialTheme.typography.bodySmall)
                                }
                            }
                        }
                    }
                }
                Spacer(Modifier.height(8.dp))
                Text("第2步：扫目标库位条码", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = inputValue,
                    onValueChange = { inputValue = it },
                    label = { Text("库位条码") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    trailingIcon = {
                        Button(onClick = {
                            if (inputValue.isNotBlank()) {
                                viewModel.lookupLocation(inputValue.trim())
                                inputValue = ""
                            }
                        }, enabled = inputValue.isNotBlank()) { Text("搜索") }
                    }
                )
                state.locationLookupMsg?.let {
                    Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                }
                TextButton(onClick = { viewModel.resetToStep1() }) { Text("← 重新扫部品") }
            }

            // Step 3: 输入数量
            if (state.step == 3) {
                state.scannedPart?.let { part ->
                    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("部品: ${part.partNo} / ${part.partName}", style = MaterialTheme.typography.bodyMedium)
                            state.scannedLocation?.let { loc ->
                                Text("目标库位: ${loc.locationCode}", style = MaterialTheme.typography.bodyMedium)
                            }
                        }
                    }
                }
                Spacer(Modifier.height(8.dp))
                Text("第3步：输入数量", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = state.quantity,
                    onValueChange = { viewModel.onQuantityInput(it) },
                    label = { Text("上架数量") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal)
                )
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    TextButton(onClick = { viewModel.goToStep2() }) { Text("← 返回") }
                    Button(onClick = { viewModel.gotoConfirm() },
                        enabled = state.quantity.isNotBlank()) { Text("下一步 →") }
                }
            }

            // Step 4: 确认
            if (state.step == 4) {
                Text("确认上架", style = MaterialTheme.typography.titleMedium)
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        state.scannedPart?.let { part ->
                            Text("部品: ${part.partNo}", style = MaterialTheme.typography.bodyLarge)
                            Text("名称: ${part.partName}", style = MaterialTheme.typography.bodyMedium)
                        }
                        state.scannedLocation?.let { loc ->
                            Text("目标库位: ${loc.locationCode}", style = MaterialTheme.typography.bodyLarge)
                        }
                        Text("数量: ${state.quantity}", style = MaterialTheme.typography.titleMedium)
                    }
                }
                Spacer(Modifier.height(8.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    TextButton(onClick = { viewModel.goToStep3() }) { Text("← 修改") }
                    Button(onClick = { viewModel.confirmShelving() }) { Text("确认上架") }
                }
            }

            // 结果消息
            state.resultMsg?.let { msg ->
                Card(
                    Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = if (msg.contains("成功")) MaterialTheme.colorScheme.primaryContainer
                                         else MaterialTheme.colorScheme.errorContainer
                    )
                ) {
                    Text(msg, modifier = Modifier.padding(12.dp))
                }
            }
        }
    }
}
```

**注意：** Step 3 中的 `viewModel._state.value.let { viewModel.update { ... } }` 写法依赖 MutableStateFlow 的扩展方法。如果编译报错，改为在 ViewModel 中添加 `goToStep2()` 和 `goToStep3()` 方法：

在 ShelvingViewModel 中添加：

```kotlin
fun goToStep2() { _state.update { it.copy(step = 2) } }
fun goToStep3() { _state.update { it.copy(step = 3) } }
```

然后在 Screen 中调用 `viewModel.goToStep2()` / `viewModel.goToStep3()`。

- [ ] **Step 2: 提交**

```bash
git add mobile-android/app/src/main/java/com/dip/material/ui/screens/ShelvingScreen.kt
git commit -m "feat: 手机端 ShelvingScreen 重写为四步直接上架向导"
```

---

## 验证清单

实现全部 Task 后，执行以下验证：

- [ ] `cd dip-system/api && dotnet build` 编译通过
- [ ] `cd dip-system/api && dotnet run` 启动后 `/swagger` 可见两个新端点
- [ ] POST `/api/v1/shelving/direct` 能成功上架并返回记录
- [ ] GET `/api/v1/shelving/records?part_name=X&start_date=Y&end_date=Z` 返回正确筛选结果
- [ ] 前端 `npm run dev` 启动，上架管理页显示新搜索栏和表格
- [ ] 手机端编译 `gradlew.bat :app:assembleDebug` 成功
