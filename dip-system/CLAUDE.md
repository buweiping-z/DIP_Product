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
