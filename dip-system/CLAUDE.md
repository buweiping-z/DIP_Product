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
