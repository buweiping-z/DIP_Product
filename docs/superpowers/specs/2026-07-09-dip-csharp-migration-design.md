# DIP 物料管理系统 — Python → C# ASP.NET Core 迁移设计

## 决策

将 `dip-system/backend` 从 Python FastAPI + SQLAlchemy 全量迁移至 C# ASP.NET Core 8.0 + EF Core，对齐参考项目 `webapi_smt_ver1.0` 架构。

### 动机

1. **便携部署**：`dotnet publish --self-contained` 生成独立文件夹，复制即可运行，无需安装运行时
2. **类型安全**：编译期检查，重构 IDE 支持，降低维护成本
3. **大数据处理**：原生 async/await，无 GIL 限制，ClosedXML 流式 Excel

## 项目结构

```
dip-system/api/                        # 新建 C# 项目
├── DIP.Api.csproj
├── Program.cs
├── appsettings.json
├── Controllers/                       # 12 个模块
│   ├── AuthController.cs
│   ├── PartsController.cs
│   ├── LocationsController.cs
│   ├── LinesController.cs
│   ├── InventoryController.cs
│   ├── OrdersController.cs
│   ├── PrepController.cs
│   ├── ShelvingController.cs
│   ├── OnlineController.cs
│   ├── ReturnController.cs
│   ├── StockCountController.cs
│   ├── AbnormalController.cs
│   └── DashboardController.cs
├── Models/                            # 32 张表实体
├── Services/                          # 业务逻辑
├── Data/
│   └── AppDbContext.cs
├── Migrations/
└── html/                              # 前端静态文件
```

## 技术栈

- ASP.NET Core 8.0 Web API
- Entity Framework Core 8 + Pomelo MySQL
- ClosedXML (Excel)
- JWT Bearer Authentication
- Swashbuckle (Swagger)

## 核心设计

### 统一响应

```json
{ "code": 0, "data": {...}, "message": "ok" }
```

### 数据层

- 实体基类 `BaseEntity` (Id, TenantId, IsDeleted, CreatedAt, UpdatedAt)
- 全局 `NoTracking`
- 启动自动 Migrate

### 服务层

- 构造函数注入 `AppDbContext`
- 库存 Core/Facade 双层模式平移

### 认证

- JWT Bearer (access 30min + refresh 7d)
- 自定义过滤器注入 tenant_id 上下文

### 部署

```bash
dotnet publish -c Release --self-contained -r win-x64
```
输出 `publish/` 文件夹，复制即运行。
