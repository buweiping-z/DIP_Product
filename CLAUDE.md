# CLAUDE.md

> **注意：2026-07-09 后端已从 Python FastAPI 迁移到 C# ASP.NET Core 8.0。**
> 新后端：`dip-system/api/` (C#)，前端：`dip-system/frontend-web/` (React + TypeScript)

本仓库根目录下的 `backend/`、`frontend-web/`、`mobile-android/` 为旧版系统代码，已删除。

## 当前系统

| 层 | 技术 |
|---|------|
| 后端 | C# ASP.NET Core 8.0 + EF Core 8 + Pomelo MySQL |
| 前端 | React 18 + TypeScript + Vite + Tailwind CSS |
| 数据库 | MySQL 8.0 (dip_material) |
| Excel | ClosedXML |

### 启动方式

```bash
# 后端 (端口 8800)
cd dip-system/api && dotnet run

# 前端 (端口 3000)
cd dip-system/frontend-web && npm run dev
```

访问：`http://localhost:3000` | Swagger：`http://localhost:8800/swagger`

### 免安装部署

```bash
cd dip-system/api
dotnet publish -c Release --self-contained -r win-x64
# 复制 publish/ 文件夹到目标电脑，双击 DIP.Api.exe
```

## 避坑经验

### EF Core NoTracking → 写操作静默失败
`AppDbContext` 中 **不能** 设置全局 `QueryTrackingBehavior.NoTracking`，否则所有实体不被追踪，`SaveChangesAsync()` 不生成 UPDATE。写操作若需要可用 `.AsTracking()` 单独指定。

### JSON 序列化必须 snake_case
全局配置 `JsonNamingPolicy.SnakeCaseLower`，因为前端全用 snake_case。

### [FromBody] Dictionary 中的 JsonElement
`Dictionary<string, object?>` 的 value 是 `JsonElement`，不是原生类型。用 `DictHelper` 中的扩展方法提取值。

## 修改履历

### 2026-07-09 — Python FastAPI → C# ASP.NET Core 8.0 迁移

**架构变更：**
- `dip-system/backend/` (Python) → 删除，替换为 `dip-system/api/` (C#)
- 根目录 `backend/` (旧 C# 代码) → 删除
- 前端不变，仅 Vite 代理端口 8400→8800

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 登录后菜单栏一闪白屏 | JSON camelCase/snake_case 不匹配，token 刷新失败 | Program.cs 全局 SnakeCaseLower |
| 2 | 编辑保存数据库不变 | EF Core 全局 NoTracking，ChangeTracker 不追踪 | 移除 AppDbContext 的 NoTracking |
| 3 | 编辑保存提示成功但数据未改 | `[FromBody] Dictionary` 的 value 是 JsonElement | 创建 DictHelper，先用 `is JsonElement` 判断再提取 |
| 4 | 启动报 Table already exists | Migrate() 试图重建旧表 | 改用 EnsureCreated() |

**新增文件：**
- `dip-system/api/` — 48 个源文件
- `Services/DictHelper.cs` — JsonElement 安全提取
- `memory/*.md` × 3 — EF Core / JsonElement / JSON 序列化 经验记忆
- `~/.claude/skills/bug-collect/` — bug 经验收集 Skill

### 2026-07-09 — 上架管理重构 + 补料/上线查询 + 用户管理

**上架管理（直接上架模式）：**
- 取消批次号管理，改为直接上架：扫部品→扫库位→输数量→确认
- 新增 `POST /shelving/direct`、`GET /shelving/records` 端点
- `MaterialShelving` 增加 `part_name` 字段
- 前端 LoadingList.tsx 重写：搜索栏（部品/库位/日期范围）
- 手机端 ShelvingScreen 重写：四步向导

**补料管理：**
- 去批次号列、去数量列
- 新增搜索栏（料号/库位/日期范围）
- 修复 `source_location_code` 始终为空

**上线确认：**
- 新增搜索栏（料号/工位/日期范围）

**用户管理（新增模块）：**
- `UserService` + `UserController`：CRUD + 密码重置 + 修改密码
- 前端 UserList 页面：搜索/新建/编辑/删除
- 侧边栏新增用户管理菜单项，显示当前用户姓名和角色
- Program.cs 启动时种子角色和 admin 账号

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 5 | 新建用户提示成功但未写入 | API 业务错误 HTTP 200，前端不抛异常 | 所有 API 调用检查 `res.code === 0` |
| 6 | 新建用户 role_id 始终为 0 | React setState 异步，闭包读到旧值 | loadRoles 返回数据而非依赖 state |
| 7 | admin 报"仅管理员可操作" | 数据库 role_code 是 ADMIN(大写)，代码比小写 | `StringComparison.OrdinalIgnoreCase` |
| 8 | admin JWT 中 role 为空 | 旧 admin 的 RoleId 指向不存在的角色 | 种子数据幂等，修正已有 RoleId |

**新增避坑经验（memory）：**
- `api-response-code-check.md` — 前端必须检查业务 code
- `react-setstate-closure-trap.md` — React setState 异步闭包
- `seed-data-idempotency.md` — 种子数据幂等修正

### 2026-07-10 — 手机端完整重写 + 订单库存修复 + 备料扫描优化

**手机端重写：**
- 参考 machine_check 架构，MVVM + Navigation Compose + CameraX + ML Kit
- 30+ 新文件，Material 3 蓝灰主题，7 路由 NavHost
- 半屏相机扫码组件（QrCodeScanner + BarcodeAnalyzer）
- 上架 4 步向导 + 备料/补料/退料/上线/替代页面
- 默认服务器 `192.168.5.11:8800`

**备料扫描优化：**
- 扫码/输入料号自动匹配备料明细，一次性冻结全部剩余数量
- 全部完成自动退出，手动退出按钮保留
- 大小写不敏感 + 去空格匹配
- 显示总需求量（total_required_qty）和库存库位

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 9 | 订单取消库存未释放 | CancelAsync 只设 status=4，不调 ThawCoreAsync | 取消时遍历关联备料单解冻库存 |
| 10 | 库存冻结数量不准 | 历史取消订单留下的僵尸 FrozenQty | 启动时根据活跃备料扫描重建 FrozenQty |
| 11 | 物料软删除后无法重建同号 | PartNo 唯一索引 + IsDeleted 仍占位 | 创建时 IgnoreQueryFilters 查软删除记录→恢复 |
| 12 | 库存编辑迁移库位数量不同步 | 未更新 WarehouseLocations.CurrentQty | 换库位时旧减新加，改数量时调整 |
| 13 | 库存导入同一库位不同料号静默覆盖 | 导入未检查已有库存 | 预加载库存，同库位不同料号跳过+报告 |
| 14 | 前端 alert 弹窗标题 localhost:3000 | 浏览器原生 alert() | 自定义 Toast 组件 + axios 拦截器全局错误处理 |
| 15 | 手机端备料扫描 Gson 泛型擦除 | ApiResponse<PrepScanResult> data 字段无法反序列化 | 改用 Map<String, Any?> 手动解析 |

**新增避坑经验（memory）：**
- `soft-delete-restore-on-create.md` — 软删除+唯一索引的创建恢复模式
- `android-retrofit-gson-generic-erasure.md` — Android Gson 泛型擦除陷阱

### 2026-07-11 — 订单状态机改造 + 库存批次拆分 + 手机端完善

**订单状态机自动流转：**
- 状态定义：1=待备料、2=待上线、3=已完成、4=已取消
- 备料全部完成 → 自动 `订单.Status=2`(待上线) + `KitCheckResult=1`
- 上线全部消耗 → 自动 `订单.Status=3`(已完成)
- 前端去掉手动改状态下拉框，状态只读

**编辑订单 plan_qty 联动：**
- plan 增加 → 全部解冻释放库存，`RequiredQty` 按比例更新，备料从头开始
- plan 减少 → `RequiredQty` 按比例缩小，多余冻结量解冻，明细状态同步

**InventoryLot 批次拆分：**
- `FreezeCoreAsync` / `ThawCoreAsync` 改为按需拆分而非整批改状态
- `AddCoreAsync` 空 batchNo 自动生成，确保所有入库都创建 InventoryLot
- 启动修复：补建缺失批次用 `AvailableQty`(非 TotalQty)，修正数量不一致的批次

**权限修复：**
- `JwtBearerOptions.MapInboundClaims = false` 解决 JWT role claim 映射导致的管理员权限失效

**替代料移库：**
- 添加删除功能（后端 `DeleteSubstituteAsync` + `[HttpDelete]` 端点）
- 操作列宽度 `w-32` → `w-48`

**手机端修复：**
- 备料全部完成自动关闭扫码窗口 + 回到列表时刷新
- 退出登录清 token 防自动跳回
- 上架步骤2库位精确匹配（去空格+不区分大小写），与步骤1已加载的库存列表比对
- 步骤切换时输入框清空
- 扫码/输入统一 trim 去空格

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 16 | 管理员导入BOM报无权限 | JWT `role` claim 被 MapInboundClaims 映射为 URI | `MapInboundClaims = false` |
| 17 | 删除物料/库位后库存表格仍有数据 | 未级联软删除 Inventory/InventoryLot | 删除时同步清理+扣减 CurrentQty |
| 18 | 备料扫描报"可用库存不足"但库存充足 | 上架入库batchNo为空→未创建InventoryLot | AddCoreAsync 空batchNo自动生成 |
| 19 | 备料需求数量不对 | RequiredQty 存单台用量未乘 planQty | RequiredQty = BOM用量 × planQty |
| 20 | 冻结后剩余可用批次丢失 | FreezeCoreAsync 整批改状态未拆分 | 按需拆分：可用部分保留 status=1 |
| 21 | 编辑订单后状态不更新 | 备完未同步 order.Status；RequiredQty==ActualQty 时未更新明细状态 | 按完成情况同步状态 |
| 22 | 备料已完成但齐套结果显示未检查 | 备齐时漏设 KitCheckResult | 备齐时设 KitCheckResult=1 |
| 23 | 产品下拉显示已删除的产品 | ProductBom 的 Part 已删除但自身未删 | GetProductNamesAsync 加 Part 存在性检查 |
| 24 | 退出登录闪回主画面 | token 未清除，登录页自动验证通过 | onLogout 先 clear tokens 再跳转 |
| 25 | 上架扫库位无校验 | 未检查同库位不同料号冲突 | 后端 DirectShelvingAsync 加冲突检查 |
| 26 | 库存编辑移库报"数据已存在" | 同料号移到已有同料号库位→重复冲突 | 同料号合并数量，旧记录清零 |

### 2026-07-11 — 库存冻结前移 + 手机扫码优化 + 出库管理

**库存冻结前移（重大架构变更）：**
- 订单创建时立即冻结库存（不再等到备料扫描）
- PrepDetail 新增 Status=3（待补货）：库存不足时标记
- PrepService.ScanPrepAsync 去冻结逻辑，仅核实条码
- PrepScanRecords 不再创建，所有解冻/扣减改为直接查 `Inventories.FrozenQty`
- `FreezeCoreAsync` 内部自动补建缺失批次
- 编辑订单 plan_qty 变更：解冻→SaveChanges→重新冻结
- 上架入库自动补冻结（ShelvingService.DirectShelvingAsync 新增 auto-replenish）
- 仪表盘新增待补货清单表格 + Excel 导出

**出库管理（新模块）：**
- 后端：OutboundOrder 模型 + OutboundService + OutboundController 完整 CRUD
- 前端：出库管理页面（搜索/新增/编辑/删除）+ 侧边栏菜单
- 手机端：出库扫描核销（直接扣减可用库存，不经过冻结）
- 出库扣减：直接操作 AvailableQty/TotalQty/InventoryLots/WarehouseLocations

**手机端扫码模块优化：**
- 清除 ZXing 僵尸依赖，纯 ML Kit
- BarcodeAnalyzer 同码去重改为换码触发（不再依赖时间冷却）
- QrCodeScanner 相机曝光补偿 +3 级
- ScannerOverlay 取景框 EvenOdd 挖洞（框内透明）
- 音效方案：SoundPool + res/raw/ok.wav + ng.wav，闹钟通道最大音量
- 上架扫描窗口保持打开直到库位匹配成功
- 上线功能重写：订单列表→料号清单→扫码核对，基于 online_consumed_qty
- 备料界面去除数量显示，保留库位按库位排序
- 全部界面接入扫码音效 + 去重

**权限：**
- 全局过滤器改为仅验证登录（不再限制 admin/leader 角色）
- AuthController 加入过滤器白名单

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 27 | 订单删除后冻结未解冻 | CancelAsync 依赖 PrepScanRecords，新流程无记录 | 改为查 Inventories.FrozenQty 直接解冻 |
| 28 | 编辑计划数量冻结不更新 | 解冻后未 SaveChanges 就查库冻结 | 先解冻→SaveChanges→再冻结 |
| 29 | 上线扫描全部失败 | ConfirmAsync 靠 PrepScanRecords 扣减 | 改为查 Inventories.FrozenQty 扣减 |
| 30 | 每次启动释放全部冻结 | 启动脚本用 PrepScanRecords 计算应冻量→0 | 改为用 PrepDetails.ActualQty 计算 |
| 31 | 出库记录每次重启丢失 | 启动脚本 DROP TABLE outbound_orders | 改为 IF NOT EXISTS 建表 |
| 32 | 操作员无法操作 | RequireManagerFilter 限制 admin/leader | 改为仅验证登录，不限角色 |
| 33 | 手机登录被拦截 | 过滤器未跳过 AuthController | 添加 AuthController 白名单 |
| 34 | 上架库存 0 不显示库位 | GetAvailableAsync `>0` 过滤 | 改为 `>=0` |
| 35 | 上架扫库位找不到 | 后端 Locations 接口缺 `location_code` 参数 | 新增参数支持按编码模糊匹配 |

**新增避坑经验（memory）：**
- `freeze-on-order-creation.md` — 库存冻结前移完整方案
- `prep-scan-records-dependency-hell.md` — 废弃数据结构的连锁依赖
- `startup-data-safety.md` — 启动脚本数据安全
- `freezecore-auto-create-lot.md` — FreezeCoreAsync 自动补批次
