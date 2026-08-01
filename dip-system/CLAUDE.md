# DIP 物料管理系统 v2.1

## 项目概述

SMT 电子制造物料管理系统。2026-07-09 后端从 Python FastAPI 迁移到 C# ASP.NET Core 8.0。

## 技术栈

| 层 | 技术 |
|---|------|
| 后端 | C# ASP.NET Core 8.0 + EF Core 8 + Pomelo MySQL |
| 前端 | React 18 + TypeScript + Vite + Tailwind CSS |
| 数据库 | MySQL 8.0 (dip_material) |
| Excel | ClosedXML |

## 项目结构

```
dip-system/
├── api/                            # C# ASP.NET Core 8.0 后端
│   ├── Program.cs                  # 入口：CORS, JWT, Swagger, DI
│   ├── appsettings.json            # MySQL 连接 + JWT 密钥
│   ├── Controllers/                # 13 个控制器
│   ├── Models/                     # 33 张表实体
│   ├── Services/                   # 14 个服务（含库存 Core/Facade 引擎）
│   ├── Data/AppDbContext.cs        # EF Core 上下文
│   └── Migrations/
├── frontend-web/                   # React 前端
│   ├── src/
│   │   ├── App.tsx
│   │   ├── lib/api.ts              # Axios + JWT 自动刷新
│   │   └── pages/                  # 13 个页面
│   ├── package.json
│   └── vite.config.ts              # 代理 /api → localhost:8800
├── docker-compose.yml
└── CLAUDE.md
```

## 启动方式

```bash
# 终端 1：后端 (端口 8800)
cd dip-system/api
dotnet run

# 终端 2：前端 (端口 3000)
cd dip-system/frontend-web
npm run dev
```

访问：`http://localhost:3000` | API 文档：`http://localhost:8800/swagger` | 登录：admin / admin123

## 数据库

- 连接：`Server=localhost;Database=dip_material;User=root;Password=172308687;`
- 启动时自动 `EnsureCreated()`（跳过已存在的表）
- 所有 BaseEntity 子类有软删除（`IsDeleted`）

## 已实现的 API 模块 (13/13)

| 模块 | 路由 | 状态 |
|------|------|------|
| Dashboard | /api/v1/dashboard/stats | ✅ |
| Auth | /api/v1/auth/* | ✅ |
| Parts | /api/v1/parts/* | ✅ |
| Locations | /api/v1/locations/* | ✅ |
| Lines | /api/v1/lines | ✅ |
| Inventory | /api/v1/inventory/* | ✅ |
| Orders | /api/v1/orders/* | ✅ |
| Prep | /api/v1/prep/* | ✅ |
| Shelving | /api/v1/shelving/* | ✅ |
| Online | /api/v1/online/* | ✅ |
| Return | /api/v1/return/* | ✅ |
| Transfer | /api/v1/transfer/* | ✅ |
| StockCount | /api/v1/stockcount/* | ✅ |
| Abnormal | /api/v1/abnormal/* | ✅ |

## 统一响应格式

```json
{ "code": 0, "data": {...}, "message": "ok" }
```

## 关键设计决策

1. JSON 序列化：全局 `SnakeCaseLower`，与前端完全对齐
2. 库存引擎：`InventoryService` Core/Facade 双层模式
3. 异常处理：`AppExceptionFilter` 全局过滤器，HTTP 200 + 业务 code
4. 免安装部署：`dotnet publish --self-contained -r win-x64`

## 避坑经验

1. **NoTracking 陷阱**：EF Core 全局 NoTracking 导致 SaveChanges 不生成 UPDATE
2. **JsonElement 陷阱**：`[FromBody] Dictionary<string,object?>` 的 value 是 JsonElement
3. **路由对齐**：所有路由必须与 Python 原版完全一致
4. **响应格式对齐**：字段名、嵌套结构、分页格式必须与原版一致

## 修改履历

### 2026-07-17 — 多产品合并订单 + 库存排序分页导出

**多产品合并订单：**
- 新增 `order_products` 表，解除 1订单=1产品 限制
- 新建订单支持多产品选择（模糊搜索 + 批量添加表格 + 各自计划数量）
- 按 BOM 料号集合自动分组：同组合并为一个订单，不同组拆分
- 编辑时 BOM 分组一致性校验（前后端双重），`/` 分隔符
- 冻结：先创建再冻结，缺料标记待补货，不阻塞订单创建
- 新建弹窗实时合并 BOM 清单预览

**库存管理页面：**
- 表头点击排序（料号/库位/总数量/可用/冻结）
- 每页 50 条 + 翻页控件 + 总记录数
- 数据导出 Excel（带当前筛选条件）

**修复的 Bug：**
| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 新建订单报错后订单残留 | CreateSingleOrder 在 Refreeze 前 SaveChanges | FreezeCoreAsync 加 try/catch 兜底 |
| 2 | 所有订单冻结量为 0 | FreezeCoreAsync 无容错，一条失败中断循环 | 逐条 try/catch |
| 3 | order_products 表不存在 | EnsureCreated 不建新表 | CREATE TABLE IF NOT EXISTS |
| 4 | 编辑弹窗 BOM 有时无数据 | GetBomStatusAsync 用拼接 ProductName 匹配 | 查 order_products + 合并 BOM |
| 5 | 库存排序是数字序非字母序 | QueryAsync 按 PartId 排序 | JOIN 后按字符串排序 |

### 2026-07-20 — 手机端界面自适应 + 补料多源搜索 + 便携发布优化

**手机端界面自适应（Compose Column 溢出修复）：**
- 主界面 HomeScreen、上架 ShelvingScreen、退料 ReturnScreen 外层 Column 加 `verticalScroll(rememberScrollState())`
- 备料 PrepScreen、上线 OnlineScreen 进度计数器从 LazyColumn 内移出到固定顶部 + Surface 凸显，LazyColumn 改 `weight(1f)`
- BarcodeTextField 手动输入标签简化："手动输入（未配广播时使用）" → "手动输入"

**补料多源搜索（RefillService.GetPartsByProductAsync）：**
- 多产品订单改造后 product_name 分散在三张表：`product_boms`（BOM 目录）、`order_products`（新订单产品）、`production_orders.product_name`（旧字段）
- 修复：三层搜索 `product_boms`（模糊匹配获取精确名）→ `order_products`（精确名+模糊搜索）→ `production_orders`（兼容旧数据）
- 产品名>10位时截取前10位（`RefillViewModel.scanProduct`）

**便携发布优化：**
- 移除 Program.cs 启动自动建表/种子数据/僵尸清理/冻结重建代码块（目标环境 DB 已就绪）
- 启动.cmd 用纯英文避免 CMD 的 GBK/UTF-8 编码乱码

### 2026-07-22 — WiFi自动恢复 + Token自动刷新 + 途中切替 + 多项修复

**WiFi 断网自动重连：**
- `DIPApplication` 注册 `ConnectivityManager.NetworkCallback` 监听 WiFi `onAvailable` → `RetrofitClient.reset()`
- `RetrofitClient.getApiService()` 加 `networkHandle` 兜底对比，Doze/待机漏了 callback 时自动检测并重建

**Token 自动刷新（401）：**
- 新建 `TokenHolder` 内存单例：启动从 DataStore 加载 + 登录/刷新后同步写内存+DataStore
- OkHttp `TokenAuthenticator`：拦截 401 → 读 `TokenHolder.refreshToken` → POST /auth/refresh → 保存新 Token → 重试
- AuthInterceptor 去掉 `runBlocking` + `Context` 参数，直接读 `TokenHolder.accessToken`

**扫描模块 \r\n 处理修复：**
- `ScanBroadcastReceiver.extractBarcode` 全局 `replace("\r\n","")` → `trim()` 仅去首尾空白
- 取全部候选 Extra Key 中最长的值（避免无空格的短值被选中）

**连接预热（onResume）：**
- `MainActivity.onResume` 发 `getCurrentUser()` 预热 TCP 连接，解决待机后首次请求 5 秒延迟

**途中切替（新模块）：**
- 后端：`ChangeoverBatch` 批次表 + `InlineChangeover` 明细表，8 个 API 端点
- 手机端：`ChangeoverScreen` + `ChangeoverViewModel` 批次管理模式（扫产品→存 BOM→逐袋扫→退出可续）
- 网页端：`ChangeoverList` 记录页 + Dashboard 统计卡片

**其他修复：**
- 上架管理扫库位去除 `searchLocations` 兜底，只匹配已有库存库位
- 补料核对 `doVerify` 不清除 `boxPartNo`，允许连续扫多袋
- 备料单列表加产品名：后端 `PrepService.GetListAsync` JOIN `order_products`，`ToDict` 加 `product_name`
- 订单打印：产品名右侧加 Code 128 条形码（jsbarcode）+ 表格加"生产数量"列
- 库位管理加搜索自动匹配下拉
- 补料/切替 `scanProduct` 10位截断改为9位

### 2026-07-23/24 — 生连（生产月份 BOM 版本管理）+ 多项修复

**生连功能（重大架构变更）：**
- 数据模型：`product_boms` 和 `production_orders` 新增 `production_month VARCHAR(7) NULL`
- 自然键：产品名 + 生产月份（`YYYY_MM` 格式）唯一确定一套 BOM
- BOM 查询 fallback：优先精确匹配月份，未命中 → NULL 通用版本
- BOM 导入：4 列模板（产品名称 / 生连 / 料号 / 用量），相同产品+月份全量替换
- 新建订单：先选生连月份（默认当月，← → 翻页），再选该月份有 BOM 的产品
- 打印 PDF：订单号追加 Code 128 条码 + 产品明细表追加生连列
- 导出产品BOM：`GET /orders/export-product-bom` 下载全量产品 BOM Excel
- `GET /orders/by-no`：通过订单号反查订单信息供手机端使用
- 前端面板标签统一从"生产月份"改为"生连"

**手机端补料/切替改造：**
- 入口从"扫产品名（9位截断）"改为"扫订单号条码"
- BOM 不再绕 `product_boms` 查询，直接从订单的 `bom_items` / `prep_details` 取
- 切替批次的产品名从后端实时返回（`order_products` 拼接），不再用"订单:WO2026..."

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 替代移库提交后 App 崩溃 | Screen 中 `state.selectedOrder!!` 在重组时 NPE | 判空后提局部 val，去掉全部 `!!` |
| 2 | 选择 8 月生连但 BOM 预览显示 7 月 | `/orders/product-bom` 漏传 `month` 参数 | useEffect 加 `form.production_month` 传参和 deps |
| 3 | 补料扫 8 月订单返回 7+8 月料号 | `GetPartsByOrderNoAsync` 未传 `productionMonth` | 改为直接从订单 `prep_orders→prep_details` 查 |
| 4 | 切替扫订单号找不到 BOM | `Order.ProductName` 是拼接名（"主板/电源板"），不匹配单个 BOM | 改为直接从 `bom_items` 表查 |
| 5 | 新建弹窗产品列表为空 | `openCreate` 先调 `loadMeta()` 读旧月份再 `setForm` | 提前计算月份 → 传参给 `loadMeta(thisMonth)` |
| 6 | 前端偶尔卡死 F5 无反应 | 防抖 useEffect 闭包过期 + BOM 预览无 `showDialog` 守卫 | 定时器直调 api；预览加 `!showDialog` 前置守卫 |
| 7 | 产品大小写不一致导致 BOM 匹配失败 | `b.ProductName == name` 大小写敏感 | 统一用 `.ToLower().Trim()` |

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `efcore-case-insensitive-string-match` — EF Core MySQL 字符串匹配必须加 `.ToLower().Trim()`
- `react-useeffect-closure-stale-guard` — useEffect 闭包过期 + 缺少守卫导致请求风暴
- `compose-state-snapshot-npe` — Compose 重组中 !! 导致的 NPE 崩溃
- `inline_changeovers` 表补建 `operator_id`、`batch_no` 列

### 2026-07-25 — 安全加固 + 性能优化 + 代码质量

**JWT Claim 字面量统一（全局）：**
- 17 个 Controller + JwtTokenService + RequireManagerFilter：`ClaimTypes.NameIdentifier` → `"nameid"`，`ClaimTypes.Name` → `"unique_name"`
- 根因：`MapInboundClaims = false` 禁用了 URI→短名映射，写入用长 URI 读取用短名导致 null

**清空业务数据重写（SystemController）：**
- EF Core 25+ DbSet RemoveRange → Raw SQL `DELETE FROM` + `SET FOREIGN_KEY_CHECKS=0`
- 扩展 leader 角色权限；补充 inventory_lots / inline_changeovers / changeover_batches / refresh_tokens

**BOM 导入/导出去重（OrderService）：**
- 导入：同批次 GroupBy(productName, productionMonth, partNo) 保留最后一条
- 导出：HashSet 去重 + 删除 DB 重复记录

**前端 Blob 下载修复（OrderList.tsx）：**
- Axios 拦截器已 unwrap response.data，`res.data` → 直接用返回值

**修复的 Bug：**
| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 67 | 部分接口 401 / userId null | ClaimTypes URI 与字面量不匹配 | 统一 "nameid"/"unique_name" |
| 68 | 清空数据超时/外键报错 | RemoveRange 全表加载+外键顺序 | Raw SQL + FOREIGN_KEY_CHECKS=0 |
| 69 | BOM 导入重复料号 | Excel 重复行未去重 | GroupBy 保留最后 |

### 2026-07-27 — 订单页卡死修复 + 冻结归零修复 + 状态过滤

**订单管理页面卡死（再次修复）：**
- 根因1：组件卸载后未取消的 API 请求仍在更新 state，StrictMode 双挂载+快速导航时竞争状态更新导致浏览器卡死
- 根因2：Enter 键搜索与防抖定时器竞态——Enter 立即发请求，300ms 后定时器又发一次，两次竞争更新同一 state
- 修复1：新增 `mountedRef` + `abortRef`，所有 `setData/setTotal/setLoading` 前检查 `mountedRef.current`；请求发起时取消上一个未完成的 AbortController
- 修复2：抽取 `refreshList(p)` 统一入口——先清防抖定时器再发请求；所有按钮（页码/清除/Enter/新建/编辑/删除/导入）统一走此入口
- 文件：`frontend-web/src/pages/OrderList.tsx`

**冻结数量归零（EF Core ChangeTracker.Clear()）：**
- 根因：`RefreezeActiveOrdersAsync` 中 Phase 1 解冻后 `SaveChangesAsync` 提交，但 `_db.ChangeTracker.Clear()` 缺失。Phase 2 查询返回 ChangeTracker 缓存的过期实体快照，`FreezeCoreAsync` 对 `FrozenQty` 的修改在最终 `SaveChangesAsync` 时被 EF Core 跳过
- 注意：此修复在 2026-07-25 已写入全局 CLAUDE.md 和 memory，但**代码中只加了注释未加实际调用**
- 修复：Phase 1 `SaveChangesAsync` 后加 `_db.ChangeTracker.Clear()`，同时在 `UpdatePlanQtyAsync` 同样模式处也补上
- `CancelAsync` 增加防御：单个 prep 取消失败不中断整批（加 try/catch）
- 文件：`api/Services/OrderService.cs`

**已撤销/已取消状态默认隐藏：**
- 前端 + 后端双重过滤：`PrepService.GetListAsync` 默认排除 status=3；`OrderService.GetListAsync` 默认排除 status=4
- 前端 `PrepList.tsx` / `OrderList.tsx` 表格渲染加客户端兜底过滤
- 文件：`api/Services/PrepService.cs`、`api/Services/OrderService.cs`、`frontend-web/src/pages/PrepList.tsx`、`frontend-web/src/pages/OrderList.tsx`

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 71 | 订单管理页面卡死 | 未取消的 API 请求 + 防抖竞态竞争 state | AbortController + mountedRef + refreshList 统一入口 |
| 72 | 增删订单后冻结数量归零 | ChangeTracker 跨阶段实体快照污染（Clear 只写注释未写代码） | SaveChanges 后加 `_db.ChangeTracker.Clear()` |
| 73 | 备料管理显示已撤销记录 | 前后端均未过滤 status=3 | 后端默认排除 + 前端客户端过滤 |
| 74 | 订单管理显示已取消记录 | 前后端均未过滤 status=4 | 后端默认排除 + 前端客户端过滤 |
| 75 | 上线完成扣光其他订单冻结 | ConfirmAsync 按 PartId 扣减全部 FrozenQty | 按 RequiredQty 扣减 + Refreeze |
| 76 | BOM 导入结果不显示 | fetchData 的 setMsg('') 清掉导入成功消息 | 去掉 fetchData 的 setMsg('') |

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `react-abortcontroller-mountedref-cleanup` — React 组件卸载后 AbortController + mountedRef 防止过期状态更新
- `changetracker-clear-must-be-code-not-comment` — ChangeTracker.Clear() 必须实际调用，只写注释无效
- `online-deduct-by-requiredqty-not-all-frozen` — 上线完成扣减应按 RequiredQty 不按全部 FrozenQty

### 2026-07-28 — Axios 401 死循环修复 + 发布便携包

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 77 | 订单管理页面卡死后 F5/其他操作均无法恢复，DevTools 显示 401→refresh 200→retry→401 无限循环 | API 拦截器中 `doRefresh()` 返回 true 但新 token 无效，重试仍 401，`_retry=true` 跳过整个 if 块永不登出 | 将 `localStorage.clear()` + 跳转登录移到 `!_retry` 判断外部；新增 `axios.isCancel()` 防 abort 误杀 |

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `axios-retry-401-dead-loop` — Axios 401 拦截器 refresh 成功但重试仍 401 时死循环永不登出

### 2026-07-29 — 叫料功能（新模块）+ 补料扫码放宽 + 订单重复创建修复 + 权限控制

**叫料功能（新模块 — 三端完整实现）：**
- 数据库：`material_requests` 表（id, part_no, part_id, location_code, status, operator_id, created_at）
- 后端：`MaterialRequest` 实体 + `MaterialRequestService`（批量创建/分页搜索/改状态/删除/导出）+ `MaterialRequestController`（5 端点）
- API 端点：`POST/GET/PUT/DELETE/GET export` → `/api/v1/call-material`
- 手机端：`CallMaterialScreen` + `CallMaterialViewModel`，流程：扫料号（>14去尾4）→ API 匹配部品→查库存库位→加入列表（去重）→上传→自动回首页
- 网页端：`CallMaterialList.tsx` 管理页面（搜索/分页/改状态/删除/导出 Excel）+ 侧边栏菜单 + 仪表盘计数
- 文件：新增 7 个，修改 10 个

**补料扫码放开位数限制：**
- `RefillViewModel.togglePart` 移除 `>14位` 限制，改为 >14 去尾 4、≤14 取全部后匹配
- `RefillScreen` 步骤 1 提示文字 `"扫部品条码(>14位)"` → `"扫部品条码"`
- 文件：`RefillViewModel.kt`、`RefillScreen.kt`

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 78 | 新建订单双击出现两份 | `handleSubmit` 无防重复点击，两次点击并发 POST | `useRef` 同步锁 + `useState` UI 反馈 + button disabled |
| 79 | 叫料管理页面报"服务器内部错误" | LINQ `Select` 中 `CreatedAt.ToString()` EF Core 无法翻译为 SQL | 直接传 `DateTime`，由 JsonConverter 格式化 |
| 80 | 叫料管理页面报 Table 'materialrequests' doesn't exist | `MaterialRequest` 实体缺 `[Table]`，EF Core 推导表名无下划线 | 加 `[Table("material_requests")]` |
| 81 | 手机端编译失败 `'return' is prohibited here` | 嵌套 `fold` 中裸 `return` Kotlin 不允许 | 改为 `return@fold` |

**网页端权限控制：**
- 订单管理：编辑/删除按钮仅 admin/leader 可见（详情/打印不受限）
- 物料管理：编辑/删除仅 admin/leader 可见
- 库位管理：编辑/删除仅 admin/leader 可见
- 叫料管理：撤销/恢复/删除仅 admin/leader 可见（标记已处理/取消不受限）
- 文件：`OrderList.tsx`、`PartList.tsx`、`LocationList.tsx`、`CallMaterialList.tsx`

**新增避坑经验（全局 CLAUDE.md + memory）：**
- `react-submit-double-click-guard` — React `useRef` 防重复提交
- `efcore-table-attribute-missing` — 实体类必须加 `[Table]` 显式指定表名
- `efcore-linq-select-tostring-cannot-translate` — LINQ Select 中不能调 C# 方法
- `kotlin-nested-fold-return-label` — 嵌套 fold 必须用带标签 return

### 2026-07-29 — BOM 清单向前查找规则 + 已选产品表加 BOM 月份列

**BOM 向前查找规则：**
- 将 `GetBomWithFallbackAsync` 从"精确匹配→NULL兜底"改为"精确匹配→向前≤查找→NULL兜底"
- 提取 `GetEffectiveBomMonthAsync` 辅助方法，三步确定有效月份
- `GetProductNamesAsync` 同步改为内存中按同样规则筛选，确保产品下拉列表一致
- 所有调用方（新建/编辑订单、BOM预览、产品下拉）自动生效
- 文件：`api/Services/OrderService.cs`

**已选产品表格加 BOM 月份列：**
- 后端 `GetProductNamesAsync` 返回新增 `bom_month` 字段
- 前端 `SelectedProduct` 接口 + `addProduct` + `openEdit` 同步补上该字段
- "已选产品"表新增"BOM表月份"列（产品名和BOM料号数之间），显示具体月份或"通用"
- 文件：`OrderService.cs`、`OrderList.tsx`
