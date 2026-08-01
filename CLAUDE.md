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

### 2026-07-12 — 手机端扫码重构 + 上架/备料/上线流程重写

**扫码模块重构：**
- 去重逻辑删除，改为按钮触发模式：`BarcodeAnalyzer.isActive` 默认 `false`
- 按扫码按钮 → `isActive = true` → 识别到条码 → 自动 `isActive = false` 停止
- `QrCodeScanner` 布局重构：顶部提示文字 + 中部圆角相机面板 + 底部大扫码按钮（单手操作）
- `ScannerOverlay` 底部提示文字移除

**料号解析规则（全局统一）：** ≤14位取全部，>14位取 length-4（去末尾4位）

**上架流程重写：**
- 新四步：①扫部品→解析→查API→显示库存 ②扫库位→匹配 ③逐袋扫码确认→"完成扫描" ④输入总数量→上传

**备料流程重写：**
- 扫描阶段：只核对料号匹配+本地累加，不调后端写操作
- 退出时调 `POST /prep/{id}/finish` 传入已扫明细ID，后端标记 Status=2
- 手机端自维护 `scannedCounts` 计数器，不依赖后端 `ActualQty`（该字段被冻结系统占用）
- 条码必须>14位，用解析后的料号匹配

**上线流程重写：**
- 与备料相同模式：扫描核对+本地累加，退出时逐条调 `confirmOnline`

**后端修复：**
- `RefreezeActiveOrdersAsync`：`p.Status != 3` → `p.Status == 1`，避免已完成备料单明细被重置
- 新增 `POST /prep/{id}/finish` 端点，接收 `{ detail_ids: [...] }`
- `ScanPrepAsync` 不再修改 `ActualQty`，只验证匹配

### 2026-07-12 — 上线确认流程修复

**上线确认去数量化（重大设计变更）：**
- 上线目的：防错（确认用对料），不管理数量
- `ConfirmAsync` 单次确认不再扣减冻结库存，仅创建 `OnlineConfirm` 记录
- 订单完成条件：`已确认明细数 >= 总明细数`（每个料号至少确认一次），不再用 `totalConsumed >= totalRequired`
- 订单完成时一次性将全部 `FrozenQty` 扣减至 0（`TotalQty` 同步减少）

**手机端修复：**
- 上线退出再进：`scannedCounts` 从后端 `online_consumed_qty` 恢复，已确认料号不重扫
- 增量提交：退出时只提交 `scannedCounts - initialScannedCounts`，避免重复计数
- 列表刷新时序：`loadOrders()` 改为 suspend，等 `confirmOnline`/`finishPrep` 完成后刷新
- 上线/备料扫描界面添加料号完成进度计数器 `已完成: 2/7`

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 36 | 上线退出再进需重扫 | selectOrder 重置 scannedCounts=emptyMap，忽略 onlineConsumedQty | 从 onlineConsumedQty 恢复，跟踪 initialScannedCounts，退出只提交增量 |
| 37 | 全部确认后订单不完成 | totalConsumed(扫描次数) >= totalRequired(BOM总量) 永远达不到 | 改为已确认明细数 >= 总明细数 |
| 38 | 上线完成冻结未清零 | 单次 confirmOnline 只扣扫描数量 | 单次不扣，订单完成时一次性扣减全部 FrozenQty |
| 39 | 退出扫描界面列表不刷新 | loadOrders() 和提交接口并发跑 | loadOrders 改 suspend，等提交完成后再刷新 |

**新增避坑经验（memory）：**
- `online-completion-by-detail-count.md` — 上线完成用明细确认数判断
- `online-deduct-on-complete.md` — 订单完成时一次性扣减全部冻结库存
- `suspend-await-submit-then-refresh.md` — 提交流程等完成后再刷新列表

### 2026-07-13 — 替代料移库功能重做 + 手机端多项修复

**替代料移库重做：**
- 新数据模型：`substitute_orders` + `substitute_details`（替代旧的 `substitute_records` 单表）
- 网页端：订单列表 + 多明细行新建/编辑弹窗（替代/缺料部品各自独立搜索筛选 + 默认选第一个库位 + 数量上限提示 + 空行提醒）
- 手机端：订单列表 → 扫码确认（仅扫替代料号，>14位解析，多匹配候选列表弹出选择，按来源库位排序）
- 后端：8 端点 API（CRUD + 逐条确认 + 全部提交事务移库 + Refreeze）
- 旧 `substitute_records` 表保留不动

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 40 | 新建移库订弹窗替代/缺料共用筛选框 | 全局 searchText 同时过滤两个下拉框 | RowEditor 内各自独立搜索（subSearch/origSearch） |
| 41 | 新建移库选库位列宽不够/数量无上限/空行无声 | 列宽固定/没读可用量/未检查不完整行 | 加宽列+选库位读取max+提交时提示第几行不完整 |
| 42 | 选部品后库位不自动填 | 未做默认选择 | 加载库存后自动选第一个，多库位可下拉换 |
| 43 | 手机端替代提交完成不退出 | confirmAll 只刷新列表未清选择 | confirmAll 成功后调用 clearSelection 回到订单列表 |
| 44 | 备料界面缺需求数量 | 仅显示料号，操作员看不到需要多少 | 后端列表加 total_required_qty + 手机端卡片显示"需求: X" |
| 45 | 登录页扫码不自动填充用户名 | QrCodeScanner 的 isActive 参数未生效，内部 analyzer.isActive 为 false | LaunchedEffect 监听 isActive 自动启动扫描 |
| 46 | 首页统计卡片无用 | 显示"今日上架/退料/备料扫描"无实际操作价值 | 改用黄色未完成任务栏（待备料/待上线/待出库/补料中/待移库）+ 刷新按钮 |
| 47 | 退出登录后重新进网络不通 | 手机 WiFi+移动数据双开，请求走移动数据通道 | OkHttp 强制绑定 WiFi 网卡（WifiSocketFactory + network.bindSocket） |
| 48 | 登录页默认 admin 用户 | 硬编码默认值 | 用户名密码默认空，从 DataStore 加载上次保存值 |
| 49 | 登录失败密码未清 | 旧密码一直保存，改密码后反复用 | 登录失败时 savePassword("") 清除 |
| 50 | 补料完成不退扫码窗口 | goDone 后 step=0 但未关扫码 | LaunchedEffect(state.step) 监听 step=0 时 showScanner=false |
| 51 | 补料完成/退回不刷新批次列表 | goDone/clearAll 后未 checkActiveBatches | 添加 loadBatches() 调用 |
| 52 | 补料 NG 匹配显示绿色 | "未匹配"不含"不匹配"，漏过红色判断 | 关键词加"未匹配"和"无效" |

**新增避坑经验（memory）：**
- `android-wifi-mobile-data-dual-channel.md` — Android WiFi+移动数据双开导致请求走错通道
- `ensure-created-missing-tables.md` — EnsureCreated() 不给已有数据库建新表
- `qrcode-scanner-isactive-not-working.md` — QrCodeScanner isActive 参数未生效
- `datastore-credential-persistence.md` — DataStore 持久化登录凭据模式

### 2026-07-14 — 相机扫码→PDA键盘输入 + 出库主明细改造 + 退料流程重做 + 发布部署

**相机扫码→PDA键盘输入：**
- 删除 CameraX + ML Kit，改为 PDA 键盘楔模式
- 新建 `BarcodeTextField` 组件：自动聚焦 + onKeyEvent 硬按键兜底 + key 强制重建 + view.post 抑键盘
- 8 个 Screen 全面改造为 PDA 文本输入
- Manifest 删除 CAMERA 权限，新增 ACCESS_NETWORK_STATE

**出库管理主明细改造：**
- 数据模型：`outbound_orders`（主表）+ `outbound_details`（明细表），参照替代料移库模式
- 前端弹窗多行添加，手机端逐种扫码核销
- 后端新增 `POST /{id}/details/{detailId}/confirm` + `POST /{id}/confirm`

**退料流程重做：**
- 4 步向导：扫料号找库位 → 扫库位 → 逐件扫退料 → 退料完成
- 后端新增 `POST /return/batch-finish` 批量提交接口
- 前端退料记录去掉了退料单号和退料原因列，新增 location_code

**发布部署：**
- 统一端口 8800，前端 build 产物放入 `api/html/`
- `dotnet publish --self-contained -r win-x64` 自包含部署包

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 53 | 登录失败报 ACCESS_NETWORK_STATE | WiFi 绑定代码需要此权限但 Manifest 缺失 | 添加权限声明 |
| 54 | 相同料号连续扫不累加 | PDA Enter 键走原始事件不走 IME，onDone 不触发 | onKeyEvent 兜底 + key(scanKey) 强制重建 |
| 55 | 扫码后软键盘总是弹出 | requestFocus()→IME show 与 hide 竞态 | window.setSoftInputMode + view.post 延迟隐藏 |
| 56 | 出库保存报 part_id 无默认值 | EF Core 模型移除列但旧表仍有 NOT NULL | ALTER TABLE 补 DEFAULT 0 |
| 57 | 提交按钮被挤出屏幕 | LazyColumn fillMaxSize() 占满空间 | 改用 weight(1f) + verticalScroll 兜底 |
| 58 | 退回库位显示数字 ID | 后端只返回 target_location_id | 查 WarehouseLocations 补 location_code |

**新增避坑经验（memory）：**
- `efcore-model-change-orphan-notnull.md` — EF Core 模型变更遗留 NOT NULL 列
- `pda-barcode-raw-keyevent.md` — PDA 扫码 Enter 键不走 IME
- `compose-keyboard-suppression.md` — Compose TextField 强制抑软键盘
- `compose-lazylist-weight-layout.md` — LazyColumn fillMaxSize 挤出尾随组件

### 2026-07-15 — 扫码广播模式逻辑修复（键盘楔 → Intent 广播迁移收尾）

> 背景：手机端扫码从 PDA 键盘楔模式改为 SEUIC 广播模式（`ScanBroadcastReceiver` + `ScanBus` + 重写 `BarcodeTextField`，MainActivity 注册接收器并下发 `service_settings` 配置广播）。本次修复迁移后的扫描逻辑问题。

**BarcodeTextField 组件（广播版）改造：**
- 接收框显示**解析后料号**（全局规则：≤14位取全部，>14位去末尾4位），回调仍传原始条码——长度判断留在各 ViewModel（料盒/库位是短码，组件层不能拦截）
- 新增 `clearKey: Any? = null` 参数，变化时清空接收框和手动输入框；上架/补料/退料三个步骤向导界面传 `clearKey = state.step`，切步骤自动清空
- 扫码收集协程改用 `rememberUpdatedState` 持有回调，消除长驻 `LaunchedEffect` 捕获过期闭包的隐患

**消息颜色判断根治（不再猜关键词）：**
- `RefillUiState` 新增 `msgOk: Boolean` 字段，全部 19 处 `scanMsg` 赋值点逐一标注成败
- 补料/备料/替代三个界面（原排除法关键词判断，NG 会漏成绿色）改用显式标志；退料/上线/出库/上架为白名单式（默认红色）本就安全，保持不动

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 59 | 补料核对不扫料盒直接扫>14位部品也处理OK | scanVerify 留了"无料盒直接匹配"兜底分支，绕过料盒验证 | 删除兜底分支，无料盒时报"无效扫描：请先扫料盒(≤14位)" |
| 60 | 接收文本框处理后不清空、显示原始条码 | 广播版 BarcodeTextField 的 lastCode 只赋值从不清空 | 显示解析后料号 + clearKey=state.step 切步骤清空 |
| 61 | 补料扫描比对 NG 显示绿色背景 | 界面关键词排除法判断颜色，"该料号未勾选"/e.message 等不含关键词漏成绿色 | VM 显式携带 msgOk/lastScanOk 标志，界面不再猜文本 |

**新增避坑经验（memory）：**
- `scan-msg-color-explicit-flag.md` — 消息颜色用显式标志不用关键词匹配
- `broadcast-scan-textfield-pattern.md` — 广播模式扫码组件模式
- `wizard-no-fallback-bypass.md` — 步骤向导校验不留兜底分支

**扫码 OK/NG 音效升级（醒目化）：**
- ok.wav 改为升调两音 ding-ding（1318→1760Hz，正弦+二次谐波，明亮悦耳=对）；ng.wav 改为三连降调（660→494→330Hz，E5→B4→E4，陡方波含7次谐波，最狠警报=错）。
- 设计："升=对 / 降=错"音频语法 + 陡方波高 RMS 使 NG 比 OK 更刺、更狠、更醒目；5ms 软起音+指数衰减防爆音。纯标准库生成（`gen_scan_sounds.py`），旧音效备份于 scan-sound-backup/。
- ScanSoundManager 注释同步更新；播放仍走 USAGE_ALARM 闹钟通道最大音量。
- 移除扫码调试页 `DebugScanScreen`（首页入口与 debug_scan 路由一并撤销，诊断已完成）。

### 2026-07-25 — 订单删除改为硬删除 + 扫码去重修复 + 首页并行加载 + ChangeTracker 跨阶段清理

**订单删除改为硬删除：**
- `DeleteAsync` 从软删除（`IsDeleted=1`）改为 `_db.Remove()` 物理删除
- 级联清理：OrderProducts → BomItems → OrderClosures → PrepScanRecords → PrepDetails → OnlineConfirms → PrepOrders → ProductionOrder
- 启动时自动清理历史残留：`IsDeleted=1` 的软删除订单 + `Status=4` 的已取消订单

**RefreezeActiveOrdersAsync ChangeTracker 清理：**
- Phase 1 解冻 `SaveChangesAsync` 后加 `_db.ChangeTracker.Clear()`，再进入 Phase 2 重新冻结
- 避免跨阶段 entity snapshot 污染导致 Inventory.FrozenQty 修改未持久化

**扫码广播去重修复（根因）：**
- 原去重逻辑放在 `ScanBroadcastReceiver` 实例字段中，但 Android 每次广播都新建 BroadcastReceiver 实例，字段从未保留
- 改为在 `ScanBus` 单例中维护 `lastCode`/`lastTime`，所有 BroadcastReceiver 实例共享，500ms 去重真正生效

**首页加载优化：**
- `HomeViewModel.loadPendingTasks()` 6 个 API 从串行改为 `async`/`awaitAll` 并行
- 移除 `init{}` 中的 `loadPendingTasks()` 避免和 `LaunchedEffect` 重复加载
- `OnlineViewModel.selectOrder` 备料明细 + `clearSelection` 提交确认并行化

**途中切替界面增强：**
- 网页端：产品名称 + 料号双输入框模糊搜索 + 删除按钮；页面改为展示批次列表
- 后端：新增 `GET /batches/list` 分页搜索 + `DELETE /batches/{batchNo}` 硬删除

**Vite 代理修复：**
- `localhost` → `127.0.0.1` 解决 Windows IPv6 优先导致 ECONNREFUSED

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 62 | 切替扫描后出现多个重复批次 | Android BroadcastReceiver 每次新建实例，去重字段不保留 | 去重逻辑移到 ScanBus 单例 |
| 63 | 订单删除后库存冻结为0，其他订单未重新冻结 | ChangeTracker 跨 Phase 实体快照不一致 | Phase1 SaveChanges 后 ChangeTracker.Clear() |
| 64 | 手机首页加载3~6秒 | 6个API串行调用，总耗时累加 | async/awaitAll 并行 |
| 65 | 前端网页 ECONNREFUSED | localhost 解析为 IPv6 ::1 但后端只绑 IPv4 | 改为 127.0.0.1 |
| 66 | 删除订单仪表盘统计不正确 | 软删除数据仍占统计 | 改为硬删除 + 启动清理历史残留 |

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `efcore-changetracker-clear-between-phases` — ChangeTracker.Clear() 跨 SaveChanges 清空快照
- `android-broadcastreceiver-instance-lifecycle-dedup` — BroadcastReceiver 实例字段不保留
- `homeviewmodel-parallel-api-loading` — Kotlin async/awaitAll 并行加载模式

---

### 2026-07-25 — JWT Claim 字面量统一 + 清空数据重写 + BOM 去重 + Blob 下载修复

**JWT ClaimTypes 字面量统一（17 个 Controller + JwtTokenService + RequireManagerFilter）：**
- 问题：`MapInboundClaims = false` 时，.NET 不做 URI→短名映射，`ClaimTypes.NameIdentifier`（长 URI）写入 Token 后读取 `FindFirstValue("nameid")` 返回 null
- 修复：Token 生成统一用字面量 `"nameid"` / `"unique_name"`，所有 Controller 读取也统一用字面量
- 教训：**一旦设置 `MapInboundClaims = false`，Claim 的写入和读取必须用完全相同的字面量字符串**

**SystemController.ClearData 重写（EF Core RemoveRange → Raw SQL）：**
- 问题：25+ 个 DbSet `RemoveRange` 加载全部实体到内存，外键约束顺序出错，新增表容易遗漏
- 修复：改为 `SET FOREIGN_KEY_CHECKS=0` + 逐表 `DELETE FROM` + `SET FOREIGN_KEY_CHECKS=1`
- 同时扩展 leader 角色权限 + 补充 inventory_lots / inline_changeovers / changeover_batches / refresh_tokens 表
- 教训：**批量清空大表用 Raw SQL 比 EF Core RemoveRange 快 10x+ 且不受外键顺序约束**

**BOM 导入/导出去重：**
- 问题：同一 Excel 内或数据库中存在 (产品名, 生产月份, 料号) 重复记录，导入叠加、导出重复
- 修复：导入时 `GroupBy` 同 key 保留最后一条；导出时 HashSet 去重保留第一条 + 删除 DB 多余记录
- 教训：**Excel 导入必须做幂等去重，用户可能重复粘贴或文件本身有重复行**

**前端 Blob 下载 res.data 修复：**
- 问题：Axios 响应拦截器已 unwrap `response.data`，返回的直接是 Blob，再取 `.data` 是 undefined
- 修复：`const blob = await api.get(...)` 直接使用，不再 `res.data`
- 教训：**如果 Axios 拦截器 return response.data，调用方拿到的就是 data 本身，不能再 .data**

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 67 | 部分接口 401 / userId 为 null | MapInboundClaims=false 后 ClaimTypes URI 与字面量不匹配 | 全部改为 "nameid"/"unique_name" 字面量 |
| 68 | 清空业务数据超时/外键报错 | EF Core RemoveRange 加载全表+外键顺序 | Raw SQL DELETE + FOREIGN_KEY_CHECKS=0 |
| 69 | BOM 导入后出现重复料号 | Excel 内有重复行，导入未去重 | GroupBy(product,month,partNo) 保留最后 |
| 70 | 导出产品 BOM 下载空文件 | Axios 拦截器已 unwrap，res.data 是 undefined | 直接用返回值作为 Blob |

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `dotnet-claim-literal-vs-claimtypes-uri` — MapInboundClaims=false 时必须用字面量，不能用 ClaimTypes 常量
- `efcore-bulk-delete-raw-sql` — 批量清空大表用 Raw SQL + FOREIGN_KEY_CHECKS，不用 RemoveRange
- `excel-import-idempotent-dedup` — Excel 导入必须 GroupBy 去重，保证幂等
- `axios-interceptor-unwrap-blob` — 拦截器 return response.data 后调用方不能再 .data

### 2026-07-27 — 订单卡死再修复 + 冻结归零 + 状态过滤

**订单管理页面卡死（第二次修复）：**
- 上次修复（2026-07-25）解决了 token 竞态和双重请求，但组件卸载后过期状态更新仍导致卡死
- 新增 `mountedRef` + `abortRef`：每次请求前取消上一个未完成的 AbortController；所有 setState 前检查 mountedRef
- 新增 `refreshList(p)` 统一入口：先清防抖定时器再发请求，消除 Enter/定时器竞态
- 文件：`dip-system/frontend-web/src/pages/OrderList.tsx`

**冻结数量归零（ChangeTracker.Clear 未实际执行）：**
- 2026-07-25 修复中**只加了注释未加实际代码调用**
- `RefreezeActiveOrdersAsync` Phase 1 后注释写"ChangeTracker 已清空"，但 `_db.ChangeTracker.Clear()` 不存在
- 加上 Clear() 调用；同时 `UpdatePlanQtyAsync` 同样模式也补上
- `CancelAsync` 中单个 prep 取消失败不再中断整批
- 文件：`dip-system/api/Services/OrderService.cs`

**已撤销/已取消状态默认隐藏：**
- 后端 `GetListAsync` + 前端表格渲染双重过滤 status=3/4
- 文件：`PrepService.cs`、`OrderService.cs`、`PrepList.tsx`、`OrderList.tsx`

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 71 | 订单管理页面卡死 | 未取消 API 请求 + 防抖竞态 | AbortController + mountedRef + refreshList |
| 72 | 增删订单后冻结归零 | ChangeTracker.Clear 只写注释未写代码 | 实际加上 Clear() 调用 |
| 73 | 备料管理显示已撤销 | 未过滤 status=3 | 后端默认排除 + 前端兜底 |
| 74 | 订单管理显示已取消 | 未过滤 status=4 | 后端默认排除 + 前端兜底 |
| 75 | 上线完成扣光其他订单冻结 | ConfirmAsync 按 PartId 扣减全部 FrozenQty | 按 RequiredQty 扣减 + Refreeze |
| 76 | BOM 导入结果不显示 | fetchData 的 setMsg('') 清掉消息 | 去掉 fetchData 的 setMsg('') |

**新增避坑经验（memory）：**
- `react-abortcontroller-mountedref-cleanup` — AbortController + mountedRef 防过期 setState
- `changetracker-clear-must-be-code-not-comment` — 经验文档化 ≠ 代码已修复
- `online-deduct-by-requiredqty-not-all-frozen` — 上线完成扣减应按 RequiredQty 不按全部 FrozenQty

---

### 2026-07-30 性能优化 + 日报功能

**新建订单弹框优化：**
- 前端 `openCreate` 改为非阻塞：点击立刻弹框，产品搜索框显示"加载中"并禁用，产线/月份/优先级可先操作
- 后端 `GetProductNamesAsync` 加 `IMemoryCache` 按月份 key 缓存（5 分钟过期）
- BOM 导入/去重操作后主动 `InvalidateProductCache()` 清除缓存
- 文件：`OrderList.tsx`、`OrderService.cs`、`Program.cs`

**生产作业日报 PDF：**
- 新增 `ReportService.cs`：聚合当日订单/备料/上线/异常/换线/库存数据，QuestPDF 排版生成 A4 PDF
- 新增 `ReportController.cs`：`GET /api/v1/report/daily?date=YYYY-MM-DD` 返回 PDF 文件流
- 前端订单页顶部新增"生成日报"按钮 → 日期选择弹窗 → 下载 PDF
- 新增依赖：QuestPDF 2025.7.0 + 嵌入中文字体 `api/Fonts/simhei.ttf`
- 报告结构：总览指标 → 订单明细 → 异常明细 → 换线明细 → 汇总统计

**技术栈变更：**

| 层 | 新增 |
|---|------|
| 后端 | QuestPDF 2025.7.0（PDF 生成）、IMemoryCache（产品列表缓存） |
| 字体 | `api/Fonts/simhei.ttf`（黑体，CopyToOutput） |
