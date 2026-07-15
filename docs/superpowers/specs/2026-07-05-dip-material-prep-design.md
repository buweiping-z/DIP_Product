# DIP物料管理系统 - 线边仓备料流程设计

> **版本：** v3.0  
> **日期：** 2026-07-05  
> **状态：** 设计评审v3  
> **修订：** 基于两轮评审意见修复P0(6项)/P1(15项)/P2(20项)全部问题

---

## 目录

1. [概述](#1-概述)
   - [1.4 库存流转模型（冻结-出库模式）](#14-库存流转模型冻结-出库模式)
2. [技术架构](#2-技术架构)
3. [模块划分](#3-模块划分)
4. [数据模型](#4-数据模型)
   - [4.1 ER关系总览](#41-er关系总览)
   - [4.2 完整数据表](#42-完整数据表)
5. [核心业务流程](#5-核心业务流程)
6. [API接口设计](#6-api接口设计)
   - [6.1 统一规范（含幂等性设计）](#61-统一规范)
7. [Android端设计](#7-android端设计)
8. [PC端设计](#8-pc端设计)
9. [消息队列与实时通信](#9-消息队列与实时通信)
10. [权限与安全](#10-权限与安全)
11. [性能与可靠性](#11-性能与可靠性)
    - [11.1 并发控制（含跨备料单分布式锁）](#111-并发控制)
    - [11.2 事务边界（含Inventory汇总同步）](#112-事务边界)
    - [11.6 Inventory热点行更新优化](#116-inventory热点行更新优化)
    - [11.7 MySQL主从读写分离策略](#117-mysql主从读写分离策略)
12. [电子行业特性](#12-电子行业特性)
    - [12.4.1 条码规则说明](#1241-条码规则说明)
    - [12.7 尾料处理](#127-尾料处理)
13. [部署架构](#13-部署架构)
14. [非功能性需求](#14-非功能性需求)
15. [数据字典](#15-数据字典)

---

## 1. 概述

### 1.1 系统定位

DIP插件工程物料管理系统，覆盖从部品上料线边仓、工单驱动备料、到上线确认的核心业务流程。系统面向DIP插件生产线，支持批次追溯、FIFO先进先出、MSL湿敏管控等电子行业特性。

### 1.2 业务场景

| 环节 | 描述 | 操作终端 |
|---|---|---|
| 上料线边仓 | 部品从部管库取出后，扫码确认上料至线边仓指定库位 | Android PDA |
| 工单备料 | 生产工单下发后，系统根据BOM自动生成备料单，操作员逐项扫码备料 | Android PDA |
| 上线确认 | 备料完成后，确认部品上线到指定工位 | Android PDA |
| 逆向操作 | 撤销上料/备料/上线、退料、盘点、调拨等逆向流程 | Android + PC |
| 监控管理 | 部品/库位/工单主数据管理、库存监控、异常预警、报表统计 | PC Web |

### 1.3 核心原则

- **扫码驱动**：所有操作通过扫码完成，减少人工输入
- **批次追溯**：每次库存变动记录批次号，支持完整追溯链
- **FIFO管控**：备料优先取最早入库批次，过期批次自动拦截
- **事务一致**：多表操作同一事务，确保数据一致性
- **离线可用**：Android端断网可继续操作，恢复后自动同步

### 1.4 库存流转模型（冻结-出库模式）

系统采用**冻结-出库**两阶段库存扣减模型，确保备料与上线确认不重复扣减库存：

| 阶段 | 操作 | InventoryLot 变更 | Inventory 汇总变更 | StockMovement 记录 |
|---|---|---|---|---|
| **上料入库** | 部品上料至线边仓 | 目标库位新建/增加批次，Status=1(可用) | TotalQty += qty, AvailableQty += qty | 类型1:上料入库 |
| **备料冻结** | 扫码备料 | 批次Status=1→2(冻结)，Quantity不变 | AvailableQty -= qty, FrozenQty += qty | 类型2:备料冻结 |
| **上线出库** | 上线确认 | 批次Quantity -= qty | FrozenQty -= qty, TotalQty -= qty（**AvailableQty不变**）| 类型3:上线出库 |
| **撤销备料** | 撤销备料明细 | 批次Status=2→1(可用) | AvailableQty += qty, FrozenQty -= qty | 类型8:撤销 |
| **撤销上线** | 撤销上线确认 | 批次Quantity += qty，恢复冻结 | FrozenQty += qty, TotalQty += qty（**AvailableQty不变**）| 类型8:撤销 |
| **退料入库** | 退料确认 | 退回库位新建/增加批次 | TotalQty += qty, AvailableQty += qty | 类型4:退料入库 |

**关键约束：**
- Inventory.TotalQty = SUM(InventoryLot.Quantity) WHERE InventoryId = ?
- Inventory.AvailableQty + Inventory.FrozenQty + Inventory.InspectingQty = Inventory.TotalQty
- 所有库存变动必须在同一事务中同时更新 InventoryLot 和 Inventory 汇总表

---

## 2. 技术架构

### 2.1 技术栈

| 层 | 技术选型 | 说明 |
|---|---|---|
| 后端框架 | C# .NET 8.0 Web API | 高性能跨平台后端 |
| 数据库 | MySQL 8.0 | 关系型数据库，支持事务 |
| ORM | EF Core + Pomelo | 代码优先迁移 |
| 缓存 | Redis 7.x | 热点数据缓存、限流、Token存储 |
| 消息队列 | RabbitMQ 3.12 | 异步通知、事件驱动 |
| 认证 | JWT + Refresh Token | 无状态认证 |
| Android | Kotlin + Jetpack Compose | 声明式UI |
| Android DI | Koin | 轻量依赖注入 |
| Android 网络 | Retrofit + OkHttp | 类型安全HTTP客户端 |
| Android 本地 | Room | 离线缓存 |
| Android 扫码 | ML Kit Barcode Scanning | 多码型识别 |
| PC前端 | Vue 3 + TypeScript | 响应式框架 |
| PC UI | Element Plus | 企业级组件库 |
| PC 状态 | Pinia | 轻量状态管理 |
| 实时通信 | WebSocket / SignalR | 仪表盘实时推送 |
| 开发工具 | VS Code | 统一开发环境 |

### 2.2 架构图

```
┌──────────────────────────────────────────────────────────────────┐
│                        客户端层                                    │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │   Android PDA端      │    │   PC Web管理端                │    │
│  │  Kotlin + Compose    │    │  Vue 3 + Element Plus        │    │
│  │  Room(离线缓存)      │    │  Pinia(状态管理)              │    │
│  │  ML Kit(扫码)        │    │  WebSocket(实时推送)          │    │
│  └──────────┬──────────┘    └──────────────┬───────────────┘    │
│             │                               │                    │
│             ▼                               ▼                    │
│  ┌─────────────────────────────────────────────────────┐         │
│  │              API Gateway (Kestrel + YARP)            │         │
│  │         /api/v1/... 限流 · 认证 · 日志               │         │
│  └──────────────────────┬──────────────────────────────┘         │
└─────────────────────────┼────────────────────────────────────────┘
                          │
┌─────────────────────────┼────────────────────────────────────────┐
│                     后端服务层                                     │
│                          │                                       │
│  ┌───────────────────────┴──────────────────────────────────┐   │
│  │                   DIP.API (Web API)                       │   │
│  │  Controller → Application Service → Domain Service        │   │
│  │  (CQRS命令/查询通过DIP.Application.Commands/Queries路由)    │   │
│  │  统一异常处理 · 请求日志 · 全局错误码                       │   │
│  └─────┬────────────┬──────────────┬───────────────┬────────┘   │
│        │            │              │               │             │
│  ┌─────▼─────┐ ┌───▼────┐ ┌──────▼─────┐ ┌───────▼──────┐     │
│  │ DIP.Core  │ │DIP.App │ │DIP.Domain  │ │DIP.Infra     │     │
│  │ 业务服务   │ │DTO/映射 │ │实体/值对象  │ │仓储/上下文   │     │
│  └───────────┘ └────────┘ └────────────┘ └──────────────┘     │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────────────┐   │
│  │   Redis 7.x  │  │ RabbitMQ 3.12│  │   MySQL 8.0         │   │
│  │ 缓存/限流     │  │ 异步通知      │  │ 业务数据            │   │
│  │ Token存储    │  │ 死信队列      │  │ 库存流水            │   │
│  └──────────────┘  └──────────────┘  └─────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.3 项目结构

```
DIPMaterialManagement/
├── backend/
│   ├── DIP.API/                        # Web API入口
│   │   ├── Controllers/                # 控制器
│   │   │   ├── AuthController.cs
│   │   │   ├── LoadingController.cs
│   │   │   ├── PrepOrderController.cs
│   │   │   ├── OnlineConfirmController.cs
│   │   │   ├── InventoryController.cs
│   │   │   ├── PartController.cs
│   │   │   ├── ProductionOrderController.cs
│   │   │   └── SystemController.cs
│   │   ├── Middleware/                 # 中间件
│   │   │   ├── ExceptionMiddleware.cs  # 全局异常处理
│   │   │   ├── RequestLogMiddleware.cs # 请求日志
│   │   │   └── RateLimitMiddleware.cs  # 限流
│   │   ├── Filters/                    # 过滤器
│   │   ├── Hubs/                       # SignalR Hub
│   │   │   └── NotificationHub.cs
│   │   └── Program.cs
│   │
│   ├── DIP.Core/                       # 核心业务逻辑
│   │   ├── Services/                   # 业务服务
│   │   │   ├── ILoadingService.cs
│   │   │   ├── IPrepOrderService.cs
│   │   │   ├── IOnlineConfirmService.cs
│   │   │   ├── IInventoryService.cs
│   │   │   └── IStockCountService.cs
│   │   ├── Handlers/                   # CQRS命令处理
│   │   ├── Validators/                 # 业务校验
│   │   └── Events/                     # 领域事件
│   │
│   ├── DIP.Application/                # 应用服务层
│   │   ├── DTOs/                       # 数据传输对象
│   │   ├── Mappings/                   # AutoMapper映射
│   │   └── Commands/                   # 命令/查询
│   │
│   ├── DIP.Domain/                     # 领域模型
│   │   ├── Entities/                   # 实体
│   │   ├── ValueObjects/               # 值对象
│   │   ├── Enums/                      # 枚举
│   │   ├── Aggregates/                 # 聚合根
│   │   └── Events/                     # 领域事件定义
│   │
│   ├── DIP.Infrastructure/             # 数据访问层
│   │   ├── Data/                       # EF Core上下文
│   │   │   └── DIPDbContext.cs
│   │   ├── Repositories/               # 仓储实现
│   │   ├── Migrations/                 # 数据库迁移
│   │   └── Configurations/             # 实体配置
│   │
│   └── DIP.Shared/                     # 共享工具
│       ├── Constants/                  # 常量定义
│       ├── Errors/                     # 错误码
│       ├── Extensions/                 # 扩展方法
│       └── Helpers/                    # 工具类
│
├── frontend-web/                       # PC端Vue项目
│   ├── src/
│   │   ├── views/                      # 页面视图
│   │   │   ├── Dashboard/
│   │   │   ├── Inventory/
│   │   │   ├── Orders/
│   │   │   ├── PrepOrders/
│   │   │   ├── Parts/
│   │   │   ├── Locations/
│   │   │   ├── Abnormal/
│   │   │   ├── Returns/
│   │   │   ├── Reports/
│   │   │   └── System/
│   │   ├── components/                 # 公共组件
│   │   │   ├── Common/
│   │   │   ├── Table/
│   │   │   ├── Form/
│   │   │   └── Charts/
│   │   ├── api/                        # API接口
│   │   │   ├── auth.ts
│   │   │   ├── loading.ts
│   │   │   ├── prepOrder.ts
│   │   │   ├── online.ts
│   │   │   ├── inventory.ts
│   │   │   ├── part.ts
│   │   │   └── system.ts
│   │   ├── stores/                     # Pinia状态管理
│   │   │   ├── user.ts
│   │   │   ├── inventory.ts
│   │   │   └── notification.ts
│   │   ├── router/                     # 路由配置
│   │   ├── utils/                      # 工具类
│   │   │   ├── websocket.ts
│   │   │   ├── export.ts
│   │   │   └── request.ts
│   │   └── App.vue
│   ├── package.json
│   └── vite.config.ts
│
└── mobile-android/                     # Kotlin Android项目
    ├── app/
    │   └── src/main/java/com/dip/material/
    │       ├── data/                   # 数据层
    │       │   ├── local/              # Room本地数据库
    │       │   │   ├── dao/
    │       │   │   └── entity/
    │       │   ├── remote/             # 远程数据源
    │       │   └── repository/         # 仓库
    │       │       ├── LoadingRepository.kt
    │       │       ├── PrepOrderRepository.kt
    │       │       └── OnlineRepository.kt
    │       ├── domain/                 # 领域层
    │       │   ├── model/              # 领域模型
    │       │   └── usecase/            # 用例
    │       ├── presentation/           # UI层
    │       │   ├── ui/
    │       │   │   ├── screens/        # 页面
    │       │   │   └── components/     # 组件
    │       │   └── viewmodel/          # ViewModel
    │       ├── network/                # 网络层
    │       │   ├── RetrofitClient.kt
    │       │   ├── ApiService.kt
    │       │   └── interceptors/       # 拦截器
    │       ├── utils/                  # 工具类
    │       │   ├── BarcodeScanner.kt   # 扫码工具
    │       │   ├── SyncManager.kt      # 离线同步
    │       │   └── Feedback.kt         # 扫码反馈
    │       └── DIPApplication.kt       # Application入口
    │   └── build.gradle.kts
    ├── build.gradle.kts
    └── settings.gradle.kts
```

---

## 3. 模块划分

### 3.1 功能模块矩阵

| 模块 | 核心功能 | Android端 | PC端 | 说明 |
|---|---|---|---|---|
| 认证授权 | 登录/登出/Token刷新/权限校验 | ✅ | ✅ | JWT + Refresh Token |
| 上料管理 | 扫码累积/确认上料/撤销上料 | ✅ | 记录查询 | 支持连续扫码后统一确认 |
| 备料管理 | 自动生成/齐套检查/扫码备料/完成 | ✅ | 列表/详情 | 工单驱动，BOM自动生成 |
| 上线确认 | 扫码匹配/数量校验/分批上线 | ✅ | 记录查询 | 支持分批上线，累计≤备料量 |
| 退料管理 | 退料申请/审核/回退库存 | ✅ | 审核 | 班组长审核，审核后回退 |
| 库存管理 | 库存查询/批次明细/流水查询 | ✅ | ✅ | 支持库位维度查询 |
| 盘点管理 | 盘点单/扫码盘点/差异处理 | ✅ | ✅ | 循环/全盘/抽盘 |
| 调拨管理 | 库位间转移/执行调拨 | ❌ | ✅ | PC端操作 |
| 工单管理 | 工单查看/BOM查看/结案 | ❌ | ✅ | 结案自动处理尾料 |
| 部品管理 | 主数据CRUD/导入导出/替代料 | ❌ | ✅ | 支持Excel批量导入 |
| 库位管理 | 库位CRUD/锁定/容量监控 | ❌ | ✅ | 库位层级管理 |
| 异常预警 | 异常上报/处理/通知 | ✅上报 | ✅处理 | RabbitMQ实时推送 |
| 报表中心 | 操作日志/扫码记录/数据导出 | ❌ | ✅ | Excel/PDF导出 |
| 系统管理 | 用户/角色/产线/工位/参数 | ❌ | ✅ | RBAC权限控制 |

### 3.2 模块依赖关系

```
认证授权
    │
    ├──── 上料管理 ────┬──→ 库存管理
    │                  └──→ 扫码记录
    │
    ├──── 备料管理 ─────┬──→ 库存管理
    │                   ├──→ 异常预警
    │                   └──→ 扫码记录
    │
    ├──── 上线确认 ─────┬──→ 备料管理
    │                   └──→ 库存管理
    │
    ├──── 退料管理 ─────┬──→ 库存管理
    │                   └──→ 备料管理
    │
    ├──── 盘点管理 ────→ 库存管理
    │
    └──── 调拨管理 ────→ 库存管理
```

---

## 4. 数据模型

### 4.1 ER关系总览

```
┌──────────┐    1:N     ┌──────────┐
│   Role   │───────────▶│ Operator │  (P1-10:一个角色下有多个操作员)
└──────────┘            └──────────┘
                         │ 1
                         │ FK(LineId)
                         ▼ 1:N
┌──────────────┐    1:N     ┌──────────┐
│ProductionLine│───────────▶│ Station  │
└──────────────┘            └──────────┘
      │ 1
      │ FK(OrderId)
      ▼ 1:N
┌──────────────────┐ 1:N     ┌─────────────┐
│ProductionOrder   │────────▶│   BomItem    │
│(生产工单)         │         │(BOM明细)     │
└──────────────────┘         └─────────────┘
      │ 1                          │ 1
      │ FK(ProductionOrderId)      │ FK(PartId)
      ▼ 1:N                        ▼ 1:N
┌──────────────────┐              ┌─────────────┐
│   PrepOrder      │              │    Part     │
│   (备料单)        │── 1:N ────▶ │ (部品主数据) │
└──────────────────┘              └─────────────┘
      │ 1
      │ FK(PrepOrderId)
      ▼ 1:N
┌──────────────────┐    1:N     ┌──────────────────┐
│   PrepDetail     │───────────▶│ PrepScanRecord   │
│   (备料明细)       │           │(备料扫码记录)      │
└──────────────────┘            └──────────────────┘
      │ 1
      │ FK(PrepDetailId)         ┌──────────────────┐
      ▼ 1:N                      │ WarehouseLocation│
┌──────────────────┐             │   (库位)          │
│ OnlineConfirm    │             └──────────────────┘
│  (上线确认)       │                   │ 1
└──────────────────┘                   │ N
                                        │ 1
                                        │ FK(LocationId)
                                        ▼ 1:N
┌──────────────────┐    1:N     ┌──────────────────┐
│    Inventory     │───────────▶│ InventoryLot     │
│   (库存汇总)       │           │  (批次明细)       │
└──────────────────┘            └──────────────────┘
      │ 1
      │ N
      ▼ 1:N
┌──────────────────┐
│ StockMovement    │
│  (库存流水)       │
└──────────────────┘

┌──────────────────┐    1:N     ┌──────────────────┐
│  LoadingBatch    │───────────▶│LoadingBatchItem  │
│   (上料批次)       │           │  (上料明细)        │
└──────────────────┘            └──────────────────┘
      │ 1
      │ FK(BatchId)
      ▼ 1:N
┌──────────────────┐
│ MaterialLoading  │
│  (上料记录)       │
└──────────────────┘

┌──────────────────┐ 1:N     ┌──────────────────┐
│    Part          │────────▶│ PartSubstitute    │
│  (部品主数据)     │         │  (替代料关系)      │
└──────────────────┘         └──────────────────┘

┌──────────────────┐
│   AbnormalRecord │
│  (异常记录)       │
└──────────────────┘

┌──────────────────┐ 1:N     ┌──────────────────┐
│   ScanRecord     │         │   SystemLog       │
│  (扫码记录)       │         │  (操作日志)        │
└──────────────────┘         └──────────────────┘

┌──────────────────┐ 1:N     ┌──────────────────┐
│   ReturnOrder    │         │   OrderClosure    │
│  (退料单)         │         │  (工单结案)        │
└──────────────────┘         └──────────────────┘

┌──────────────────┐ 1:N     ┌──────────────────┐
│   StockCount     │────────▶│ StockCountItem    │
│  (盘点单)         │         │  (盘点明细)        │
└──────────────────┘         └──────────────────┘

┌──────────────────┐
│  TransferOrder   │
│  (调拨单)         │
└──────────────────┘
```

### 4.2 完整数据表

#### 4.2.1 基础数据表

**Operator（操作员/用户）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| Username | VARCHAR(50) | NOT NULL, UNIQUE | 用户名 |
| PasswordHash | VARCHAR(255) | NOT NULL | 密码哈希(BCrypt) |
| RealName | VARCHAR(50) | NOT NULL | 真实姓名 |
| EmployeeNo | VARCHAR(30) | NOT NULL | 员工编号 |
| RoleId | BIGINT | NOT NULL, FK→Role.Id | 角色ID |
| LineId | BIGINT | FK→ProductionLine.Id | 所属产线(数据权限) |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-启用 0-停用 |
| IsDeleted | TINYINT | NOT NULL, DEFAULT 0 | 软删除 |
| CreatedBy | BIGINT | | 创建人 |
| UpdatedBy | BIGINT | | 更新人 |
| CreatedAt | DATETIME | NOT NULL, DEFAULT NOW() | 创建时间 |
| UpdatedAt | DATETIME | ON UPDATE NOW() | 更新时间 |

**Role（角色）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| RoleName | VARCHAR(50) | NOT NULL, UNIQUE | 角色名称 |
| RoleCode | VARCHAR(30) | NOT NULL, UNIQUE | 角色编码 |
| Description | VARCHAR(200) | | 描述 |

预置角色：
| RoleCode | RoleName | 说明 |
|---|---|---|
| ADMIN | 系统管理员 | 全部权限 |
| WAREHOUSE | 仓管员 | 上料/备料/库存操作 |
| SUPERVISOR | 班组长 | 异常处理/退料审核 |
| OPERATOR | 操作员 | 只读查看 |

**ProductionLine（产线）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| LineCode | VARCHAR(30) | NOT NULL, UNIQUE | 产线编号 |
| LineName | VARCHAR(100) | NOT NULL | 产线名称 |
| Capacity | INT | | 日产能 |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-启用 0-停用 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

**Station（工位）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| StationNo | VARCHAR(30) | NOT NULL, UNIQUE | 工位编号 |
| LineId | BIGINT | NOT NULL, FK→ProductionLine.Id | 所属产线 |
| StationName | VARCHAR(100) | NOT NULL | 工位名称 |
| EquipmentId | BIGINT | | 绑定设备ID |
| SeqNo | INT | NOT NULL, DEFAULT 0 | 顺序号 |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-启用 0-停用 |

**Part（部品主数据）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PartNo | VARCHAR(50) | NOT NULL, UNIQUE | 部品编号 |
| PartName | VARCHAR(200) | NOT NULL | 部品名称 |
| Specification | VARCHAR(200) | | 规格型号 |
| PartType | TINYINT | NOT NULL | 1-电阻 2-电容 3-IC 4-连接器 5-其他 |
| Unit | VARCHAR(20) | NOT NULL | 单位(PCS/个/套) |
| SupplierId | BIGINT | FK | 供应商ID |
| SupplierPartNo | VARCHAR(50) | | 供应商料号 |
| BarcodeRule | VARCHAR(100) | | 条码解析规则，格式：模板字符串（如 `{PartNo}-B{BatchNo}-{LocationCode}`），详见P2-14 |
| ShelfLife | INT | | 保质期(天) |
| StorageCondition | VARCHAR(100) | | 存储条件 |
| MSLLevel | TINYINT | | 湿敏等级 1-6 |
| IsRoHS | TINYINT | DEFAULT 1 | 环保标识 |
| MinStock | INT | DEFAULT 0 | 安全库存 |
| MaxStock | INT | DEFAULT 0 | 最大库存 |
| ImageUrl | VARCHAR(500) | | 图片URL |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-启用 0-停用 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

#### 4.2.2 库存与库位

**WarehouseLocation（库位）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| LocationCode | VARCHAR(30) | NOT NULL, UNIQUE | 库位代码 |
| Warehouse | VARCHAR(50) | NOT NULL | 仓库名称 |
| Zone | VARCHAR(20) | NOT NULL | 区域 |
| Row | VARCHAR(10) | NOT NULL | 排 |
| Column | VARCHAR(10) | NOT NULL | 列 |
| Layer | VARCHAR(10) | NOT NULL | 层 |
| MaxCapacity | DECIMAL(18,2) | DEFAULT 0 | 容量上限 |
| CurrentQty | DECIMAL(18,2) | DEFAULT 0 | 当前占用量 |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-可用 2-锁定 0-停用 |
| IsDeleted / CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

**Inventory（库存汇总）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| LocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 库位ID |
| TotalQty | DECIMAL(18,2) | DEFAULT 0 | 总数量 |
| AvailableQty | DECIMAL(18,2) | DEFAULT 0 | 可用数量 |
| FrozenQty | DECIMAL(18,2) | DEFAULT 0 | 冻结数量 |
| InspectingQty | DECIMAL(18,2) | DEFAULT 0 | 待检数量 |
| Version | INT | NOT NULL, DEFAULT 0 | 乐观锁版本号 |
| UpdatedAt | DATETIME | ON UPDATE NOW() | 最后变动时间 |

唯一索引：`(PartId, LocationId)`

**InventoryLot（批次明细）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| InventoryId | BIGINT | NOT NULL, FK→Inventory.Id | 库存汇总行ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | **部品ID（冗余，避免FIFO查询JOIN Inventory表，P0-#1修复）** |
| LocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 库位ID |
| BatchNo | VARCHAR(50) | NOT NULL | 批次号 |
| Quantity | DECIMAL(18,2) | NOT NULL | 批次数量 |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-可用 2-冻结 3-待检 4-不合格 |
| ReceiptDate | DATE | NOT NULL | 入库日期(FIFO依据) |
| ExpiryDate | DATE | | 过期日期 |
| OriginType | TINYINT | NOT NULL | 1-上料 2-备料退回 3-盘点调整 4-其他 |
| MSLExposureTime | DATETIME | | MSL元件开袋时间 |
| Version | INT | NOT NULL, DEFAULT 0 | 乐观锁版本号 |
| IsDeleted / CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

索引：
- `(PartId, LocationId, Status)` — 查询可用批次
- `(InventoryId)` — 按汇总行查批次
- `(BatchNo)` — 批次追溯
- `(ReceiptDate)` — FIFO排序

**StockMovement（库存流水）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| LocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 库位ID |
| LocationCode | VARCHAR(30) | NOT NULL | 库位代码(冗余) |
| BatchNo | VARCHAR(50) | | 批次号 |
| MovementType | TINYINT | NOT NULL | 变动类型(见数据字典) |
| Quantity | DECIMAL(18,2) | NOT NULL | 变动数量(+入-出) |
| BalanceAfter | DECIMAL(18,2) | NOT NULL | 变动后结余 |
| ReferenceType | VARCHAR(20) | NOT NULL | 关联单据类型 |
| ReferenceId | BIGINT | | 关联单据ID |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| CreatedAt | DATETIME | NOT NULL, DEFAULT NOW() | 变动时间 |

索引：
- `(PartId, CreatedAt)` — 部品流水查询
- `(ReferenceType, ReferenceId)` — 单据关联查询
- `(BatchNo)` — 批次追溯

#### 4.2.3 工单与BOM

**ProductionOrder（生产工单）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| OrderNo | VARCHAR(50) | NOT NULL, UNIQUE | 工单号 |
| LineId | BIGINT | NOT NULL, FK→ProductionLine.Id | 产线ID |
| ProductId | BIGINT | NOT NULL | 产品ID |
| ProductName | VARCHAR(200) | NOT NULL | 产品名称 |
| PlanQty | DECIMAL(18,2) | NOT NULL | 计划数量 |
| PlanStartDate | DATE | | 计划开始日期 |
| PlanEndDate | DATE | | 计划结束日期 |
| ActualStartDate | DATETIME | | 实际开始时间 |
| ActualEndDate | DATETIME | | 实际结束时间 |
| Priority | TINYINT | DEFAULT 1 | 1-普通 2-加急 3-特急 |
| CustomerOrderNo | VARCHAR(50) | | 客户订单号 |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-待备料 2-备料中 3-生产中 4-已完成 5-已结案 |
| IsDeleted / CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

**BomItem（BOM明细）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| OrderId | BIGINT | NOT NULL, FK→ProductionOrder.Id | 工单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| ReferenceDesignator | VARCHAR(100) | NOT NULL | 位号(R1,C1,U3...) |
| RequiredQty | DECIMAL(18,2) | NOT NULL | 需求数量 |
| LossRate | DECIMAL(8,6) | DEFAULT 0 | 损耗率 |
| SubstitutePartId | BIGINT | FK→Part.Id | 替代料ID |
| PartType | TINYINT | | 物料类型(冗余) |
| SeqNo | INT | NOT NULL | 顺序号 |
| IsCritical | TINYINT | DEFAULT 0 | 1-关键物料 |

索引：`(OrderId, PartId)`, `(OrderId, ReferenceDesignator)`

**PartSubstitute（替代料关系）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| OriginalPartId | BIGINT | NOT NULL, FK→Part.Id | 原部品ID |
| SubstitutePartId | BIGINT | NOT NULL, FK→Part.Id | 替代部品ID |
| Status | TINYINT | DEFAULT 1 | 1-启用 0-停用 |
| ValidFrom | DATE | | 有效期起 |
| ValidTo | DATE | | 有效期止 |
| SubstituteReason | VARCHAR(200) | | **替代原因（P2-#4修复）：如"成本降低""供应商停产""交期更优"** |

#### 4.2.4 上料与备料

**LoadingBatch（上料批次）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| BatchNo | VARCHAR(50) | NOT NULL, UNIQUE | 批次编号 |
| TargetLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 目标库位 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Status | TINYINT | DEFAULT 1 | 1-暂存 2-已确认 3-已撤销 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt / ConfirmedAt | - | | 审计字段 |

**LoadingBatchItem（上料批次明细）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| BatchId | BIGINT | NOT NULL, FK→LoadingBatch.Id | 批次ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号 |
| SourceLocationId | BIGINT | FK→WarehouseLocation.Id | 源库位 |
| BatchNo | VARCHAR(50) | | 物料批次号 |
| Quantity | DECIMAL(18,2) | NOT NULL | 数量 |
| ScannedBarcode | VARCHAR(200) | NOT NULL | 扫描条码 |
| CreatedBy / CreatedAt | - | | 审计字段 |

**MaterialLoading（上料记录）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| SourceLocationId | BIGINT | FK→WarehouseLocation.Id | 源库位 |
| TargetLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 目标库位 |
| BatchNo | VARCHAR(50) | | 批次号 |
| Quantity | DECIMAL(18,2) | NOT NULL | 上料数量 |
| ScannedBarcode | VARCHAR(200) | NOT NULL | 扫描条码 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Status | TINYINT | DEFAULT 1 | 1-已上料 2-已撤销 |
| LoadedAt / CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

**PrepOrder（备料单）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| OrderNo | VARCHAR(50) | NOT NULL, UNIQUE | 备料单号 |
| ProductionOrderId | BIGINT | NOT NULL, FK→ProductionOrder.Id | 工单ID |
| LineId | BIGINT | NOT NULL | 产线ID |
| Status | TINYINT | NOT NULL, DEFAULT 1 | 1-待备料 2-备料中 3-已完成 4-已撤销 5-已暂停 |
| KitCheckResult | TINYINT | DEFAULT 1 | 1-未检查 2-齐套 3-不齐套 |
| KitCheckTime | DATETIME | | 齐套检查时间 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt / CompletedAt | - | | 审计字段(P1-8) |

**PrepDetail（备料明细）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PrepOrderId | BIGINT | NOT NULL, FK→PrepOrder.Id | 备料单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| ReferenceDesignator | VARCHAR(100) | NOT NULL | 位号 |
| RequiredQty | DECIMAL(18,2) | NOT NULL | 需求数量 |
| ActualQty | DECIMAL(18,2) | DEFAULT 0 | 已备数量 |
| LossQty | DECIMAL(18,2) | DEFAULT 0 | 损耗数量 |
| SubstituteFlag | TINYINT | DEFAULT 0 | 0-正料 1-替代料 |
| SubstitutePartId | BIGINT | FK→Part.Id | 实际替代料ID |
| Status | TINYINT | DEFAULT 1 | 1-待备料 2-已备料 3-异常 4-已撤销 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt | - | | 审计字段 |

索引：
- `(PrepOrderId, PartId)` — 备料明细查询
- `(PrepOrderId, ReferenceDesignator)` — 按位号查询
- `(PrepOrderId, Status)` — 按备料单查待备料项

约束（P1-#2修复）：
- `CHECK (ActualQty >= 0)` — 已备数量非负
- `CHECK (ActualQty <= RequiredQty)` — 已备数量不超过需求数量
- 业务层校验：`if (detail.ActualQty + quantity > detail.RequiredQty) throw BusinessException("备料超量")`

**PrepScanRecord（备料扫码记录）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PrepDetailId | BIGINT | NOT NULL, FK→PrepDetail.Id | 备料明细ID |
| SourceLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 源库位 |
| SourceLocationCode | VARCHAR(30) | NOT NULL | 源库位代码(冗余) |
| BatchNo | VARCHAR(50) | | 批次号 |
| Quantity | DECIMAL(18,2) | NOT NULL | 取料数量 |
| ScannedBarcode | VARCHAR(200) | NOT NULL | 扫描条码 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| CreatedAt | DATETIME | DEFAULT NOW() | 扫码时间 |

#### 4.2.5 上线确认

**OnlineConfirm（上线确认）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PrepOrderId | BIGINT | NOT NULL, FK→PrepOrder.Id | 备料单ID |
| PrepDetailId | BIGINT | NOT NULL, FK→PrepDetail.Id | 备料明细ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| BatchNo | VARCHAR(50) | | 批次号 |
| LoadedQty | DECIMAL(18,2) | NOT NULL | 上线数量 |
| StationId | BIGINT | FK→Station.Id | 工位ID |
| StationNo | VARCHAR(30) | NOT NULL | 工位编号(冗余) |
| SourceLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | **上线前所在库位（P1-#3/P2-18修复）** |
| SourceLocationCode | VARCHAR(30) | | 库位代码(冗余) |
| EquipmentId | BIGINT | | 设备ID |
| Barcode | VARCHAR(200) | NOT NULL | 扫描条码 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Status | TINYINT | DEFAULT 1 | 1-已确认 2-已撤销 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / ConfirmedAt / CreatedAt / UpdatedAt | - | | 审计字段 |

索引：`(PrepDetailId, Status)` — **按备料明细累计上线量，支持SUM(LoadedQty) WHERE PrepDetailId=? AND Status=1（P1修复）**

#### 4.2.6 逆向流程

**ReturnOrder（退料单头）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| ReturnNo | VARCHAR(50) | NOT NULL, UNIQUE | 退料单号 |
| PrepOrderId | BIGINT | FK→PrepOrder.Id | 原备料单ID |
| Reason | VARCHAR(500) | NOT NULL | 退料原因 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Status | TINYINT | DEFAULT 1 | 1-待确认 2-已确认 3-已拒绝 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt / ConfirmedAt | - | | 审计字段 |

**ReturnOrderItem（退料单身）（P1-6修复）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| ReturnOrderId | BIGINT | NOT NULL, FK→ReturnOrder.Id | 退料单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| ReturnQty | DECIMAL(18,2) | NOT NULL | 退料数量 |
| BatchNo | VARCHAR(50) | | 批次号 |
| ReturnLocationId | BIGINT | FK→WarehouseLocation.Id | 退回库位 |

索引：`(ReturnOrderId, PartId)`

**StockCount（盘点单）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| CountNo | VARCHAR(50) | NOT NULL, UNIQUE | 盘点单号 |
| LocationId | BIGINT | FK→WarehouseLocation.Id | 库位ID(NULL=全盘) |
| CountType | TINYINT | NOT NULL | 1-循环 2-全盘 3-抽盘 |
| Status | TINYINT | DEFAULT 1 | 1-进行中 2-已完成 3-已结案 |
| OperatorId | BIGINT | NOT NULL | 盘点人 |
| CreatedAt / CompletedAt | DATETIME | | 审计字段 |

**StockCountItem（盘点明细）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| CountId | BIGINT | NOT NULL, FK→StockCount.Id | 盘点单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| BatchNo | VARCHAR(50) | | 批次号 |
| SystemQty | DECIMAL(18,2) | NOT NULL | 系统数量 |
| ActualQty | DECIMAL(18,2) | DEFAULT 0 | 实盘数量 |
| DiffQty | DECIMAL(18,2) | DEFAULT 0 | 差异数量 |
| Status | TINYINT | DEFAULT 1 | 1-未盘 2-已盘 3-有差异 |

**TransferOrder（调拨单头）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| TransferNo | VARCHAR(50) | NOT NULL, UNIQUE | 调拨单号 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Status | TINYINT | DEFAULT 1 | 1-进行中 2-已完成 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt / CompletedAt | - | | 审计字段 |

**TransferOrderItem（调拨单身）（P1-7修复）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| TransferOrderId | BIGINT | NOT NULL, FK→TransferOrder.Id | 调拨单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| BatchNo | VARCHAR(50) | | 批次号 |
| FromLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 源库位 |
| ToLocationId | BIGINT | NOT NULL, FK→WarehouseLocation.Id | 目标库位 |
| Quantity | DECIMAL(18,2) | NOT NULL | 调拨数量 |

索引：`(TransferOrderId, PartId)`

#### 4.2.7 异常与追溯

**AbnormalRecord（异常记录）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| PrepOrderId | BIGINT | FK→PrepOrder.Id | 备料单ID |
| PartId | BIGINT | NOT NULL, FK→Part.Id | 部品ID |
| PartNo | VARCHAR(50) | NOT NULL | 部品编号(冗余) |
| Type | TINYINT | NOT NULL | 1-数量不足 2-品质异常 3-批次过期 4-MSL超时 5-其他 |
| Reason | VARCHAR(500) | NOT NULL | 异常原因 |
| Severity | TINYINT | DEFAULT 2 | 1-低 2-中 3-高 |
| HandlerId | BIGINT | FK→Operator.Id | 处理人 |
| HandleResult | VARCHAR(500) | | 处理结果 |
| Status | TINYINT | DEFAULT 1 | 1-待处理 2-已处理 |
| CreatedAt / HandledAt / UpdatedAt | DATETIME | | 审计字段 |

**ScanRecord（扫码记录）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| Barcode | VARCHAR(200) | NOT NULL | 扫描条码 |
| ScanType | TINYINT | NOT NULL | 1-上料 2-备料 3-上线 4-退料 5-盘点 6-调拨 |
| ReferenceType | VARCHAR(20) | | 关联单据类型 |
| ReferenceId | BIGINT | | 关联单据ID |
| Result | TINYINT | NOT NULL | 1-成功 2-失败 3-异常 |
| ErrorMessage | VARCHAR(500) | | 错误信息 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| DeviceId | VARCHAR(50) | | 设备ID |
| CreatedAt | DATETIME | DEFAULT NOW() | 扫码时间 |

索引：`(Barcode, CreatedAt)`, `(OperatorId, CreatedAt)`, `(ScanType)`

**ScanRecord定位说明（P1-11修复）：**
- ScanRecord 定位为**只读审计日志**，不参与任何业务逻辑判断
- 各业务模块的扫码记录表（PrepScanRecord等）是业务操作的唯一数据源
- ScanRecord 仅在以下场景写入：每次扫码操作完成后异步追加一条审计记录
- 撤销业务操作时，仅修改业务表（PrepScanRecord），ScanRecord 保留原始记录（用于审计追踪）

**SystemLog（系统操作日志）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| OperatorId | BIGINT | NOT NULL | 操作人 |
| Action | VARCHAR(50) | NOT NULL | 操作类型 |
| Module | VARCHAR(50) | NOT NULL | 模块 |
| TargetType | VARCHAR(50) | | 目标类型 |
| TargetId | BIGINT | | 目标ID |
| OldValue | JSON | | 旧值 |
| NewValue | JSON | | 新值 |
| IPAddress | VARCHAR(50) | | IP地址 |
| CreatedAt | DATETIME | DEFAULT NOW() | 操作时间 |

**OrderClosure（工单结案）**

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | BIGINT | PK, AUTO_INCREMENT | 主键 |
| ProductionOrderId | BIGINT | NOT NULL, UNIQUE, FK | 工单ID |
| ActualQty | DECIMAL(18,2) | NOT NULL | 实际产出 |
| TotalLossQty | DECIMAL(18,2) | DEFAULT 0 | 总损耗 |
| ScrapQty | DECIMAL(18,2) | DEFAULT 0 | 报废数量 |
| PartRemainDetails | JSON | | **尾料明细：[{partId, partNo, remainQty}]（P2-19修复）** |
| ClosureResult | VARCHAR(500) | | 结案说明 |
| OperatorId | BIGINT | NOT NULL | 结案人 |
| Status | TINYINT | DEFAULT 1 | 1-待结案 2-已结案 |
| IsDeleted | TINYINT | DEFAULT 0 | 软删除 |
| CreatedBy / UpdatedBy / CreatedAt / UpdatedAt / CompletedAt | - | | 审计字段 |

---

## 5. 核心业务流程

### 5.1 上料线边仓

**场景：** 仓管员从部管库取出部品，使用PDA扫码将部品上料到线边仓指定库位。

**时序图：**

```
PDA操作员          API网关          LoadingController     LoadingService      Inventory
    │                │                    │                    │               │
    │──扫码库位──────▶│                    │                    │               │
    │                │──POST /loading/batch──▶│                    │               │
    │                │                    │──创建LoadingBatch(暂存)──▶│               │
    │                │◀──batchId──────────│                    │               │
    │◀───────────────│                    │                    │               │
    │                │                    │                    │               │
    │──连续扫码部品──▶│                    │                    │               │
    │                │──POST /loading/batch/{id}/item──▶│                    │
    │                │                    │──追加LoadingBatchItem──▶│               │
    │                │◀──success──────────│                    │               │
    │                │                    │                    │               │
    │                │                    │                    │               │
    │──确认上料──────▶│                    │                    │               │
    │                │──POST /loading/batch/{id}/confirm──▶│                    │
    │                │                    │──逐条校验:部品/库存/容量──▶│               │
    │                │                    │                    │──BEGIN TRANSACTION──│
    │                │                    │                    │──写入MaterialLoading──│
    │                │                    │                    │──更新InventoryLot(扣源/加目标)──│
    │                │                    │                    │──更新Inventory汇总(扣源/加目标)──│
    │                │                    │                    │──写入StockMovement──│
    │                │                    │                    │──COMMIT────────│
    │                │                    │──写入ScanRecord────▶│               │
    │                │                    │──写入SystemLog────▶│               │
    │                │◀──success──────────│                    │               │
    │◀──上料成功──────│                    │                    │               │
```

**上料流程说明：**
- 上料是物理转移（部管库→线边仓），不涉及FIFO选择
- 批次号从扫码条码解析获得，按批次号匹配源库位InventoryLot或直接新建
- 上料时同步更新 Inventory 汇总表：源库位 TotalQty -= qty / AvailableQty -= qty，目标库位 TotalQty += qty / AvailableQty += qty
- **目标库位容量校验（P1-#5修复）**：上料确认前，计算 `当前目标库位已用库存 + 本次上料总量`，若超过 `WarehouseLocation.MaxCapacity` 则拒绝上料，返回错误码 20002

```csharp
// LoadingService.ConfirmAsync() - 容量校验
var targetLocation = await _locationRepo.GetByIdAsync(request.TargetLocationId);
var currentUsage = await _inventoryRepo
    .Where(i => i.LocationId == targetLocation.Id)
    .SumAsync(i => i.TotalQty);

if (currentUsage + totalQty > targetLocation.MaxCapacity)
{
    throw new BusinessException($"库位 {targetLocation.LocationCode} 容量不足", 20002);
}
```

**撤销上料：**

```
PDA/PC操作员      API网关          LoadingController     LoadingService      Inventory
    │                │                    │                    │               │
    │──撤销请求──────▶│                    │                    │               │
    │                │──POST /loading/{id}/cancel──▶│                    │
    │                │                    │──校验状态=已上料─────▶│               │
    │                │                    │                    │──BEGIN TRANSACTION──│
    │                │                    │                    │──反向操作InventoryLot──│
    │                │                    │                    │──更新Inventory汇总(反向:源+=,目标-=)──│
    │                │                    │                    │──写入StockMovement(类型8:撤销)──│
    │                │                    │                    │──状态→已撤销──│
    │                │                    │                    │──COMMIT────────│
    │                │◀──success──────────│                    │               │
    │◀──撤销成功──────│                    │                    │               │
```

**撤销上料说明：** InventoryLot 目标库位回扣，源库位恢复；Inventory 汇总表同步反向更新。

### 5.2 工单驱动备料

**场景：** 生产工单创建后系统自动生成备料单，仓管员进行齐套检查后逐项扫码备料。

**备料单生成时机（P1-12修复）：**

系统采用**自动生成为主、手动触发为辅**的策略：
- **默认**：工单创建（POST /api/v1/orders）时自动根据BOM生成备料单，状态=待备料
- **手动**：提供 POST /api/v1/prep/generate 接口，允许管理员对已存在的工单补生备料单
- **幂等保护**：同一工单自动生成的备料单已存在时，手动接口返回已有备料单信息，不重复创建

**多备料单场景（P1-13修复）：**
- 一个工单默认生成**一个**备料单（1:1关系）
- 特殊场景允许分批备料（1:N）：
  - 触发条件：工单量极大或分多批次生产时，班组长可手动拆分备料单
  - 拆分规则：每个备料单包含部分BOM明细，需求数量按比例分配
  - 约束：同一备料明细的多个备料单的需求数量之和 ≤ BOM总需求量

**备料单生成（自动）：**

```
MES系统/人工      API网关          OrderController     PrepOrderService      DB
    │                │                    │                    │           │
    │──创建工单──────▶│                    │                    │           │
    │                │──POST /orders──────▶│                    │           │
    │                │                    │──写入ProductionOrder────────▶│
    │                │                    │──根据BOM生成PrepOrder+PrepDetail──▶│
    │                │                    │──状态:待备料─────────▶│           │
    │                │◀──success──────────│                    │           │
    │◀──工单创建成功──│                    │                    │           │
```

**替代料自动匹配（P1-#6修复）：**
备料单生成时，系统自动检查正料库存，不足时推荐可用替代料：

```csharp
// PrepOrderService.GenerateFromBOM()
foreach (var bomItem in bomItems)
{
    var primaryPart = bomItem.Part;
    var availableQty = await GetAvailableQty(primaryPart.Id);

    if (availableQty >= bomItem.RequiredQty)
    {
        // 正料充足，直接使用
        prepDetails.Add(CreatePrepDetail(bomItem, substituteFlag: 0));
    }
    else
    {
        // 正料不足，查询替代料
        var substitutes = await _substituteRepo
            .Where(s => s.OriginalPartId == primaryPart.Id
                      && s.Status == 1
                      && (s.ValidFrom == null || s.ValidFrom <= DateTime.Today)
                      && (s.ValidTo == null || s.ValidTo >= DateTime.Today))
            .ToListAsync();

        SubstitutePartInfo matchedSub = null;
        foreach (var sub in substitutes)
        {
            var subQty = await GetAvailableQty(sub.SubstitutePartId);
            if (subQty >= bomItem.RequiredQty)
            {
                matchedSub = new SubstitutePartInfo(sub.SubstitutePartId, sub.SubstituteReason);
                break;
            }
        }

        if (matchedSub != null)
        {
            // 推荐替代料，标记 SubstituteFlag=1，备料单状态为"待确认"需人工审批
            prepDetails.Add(CreatePrepDetail(bomItem, substituteFlag: 1,
                substitutePartId: matchedSub.PartId, note: matchedSub.Reason));
        }
        else
        {
            // 无可用替代料，标记异常，通知班组长
            prepDetails.Add(CreatePrepDetail(bomItem, substituteFlag: 0, status: 3 /*异常*/));
            await _abnormalService.ReportAsync(new AbnormalRequest
            {
                Type = 1, // 库存不足
                Message = $"部品 {primaryPart.PartNo} 库存不足，且无可用替代料"
            });
        }
    }
}
```

**齐套检查 → 扫码备料：**

```
PDA操作员          API网关          PrepController      PrepService        Inventory       AbnormalService
    │                │                    │                    │               │               │
    │──齐套检查──────▶│                    │                    │               │               │
    │                │──POST /prep/{id}/kit-check──▶│                    │               │
    │                │                    │──遍历PrepDetail─────▶│               │               │
    │                │                    │──按ReceiptDate排序(FIFO)──│               │               │
    │                │                    │──汇总可用量 vs 需求量──│               │               │
    │                │                    │──更新KitCheckResult──│               │               │
    │                │                    │──不齐套→推送预警─────▶│               │────▶RabbitMQ
    │                │◀──检查结果──────────│                    │               │               │
    │◀──齐套/不齐套──│                    │                    │               │               │
    │                │                    │                    │               │               │
    │──扫码备料──────▶│                    │                    │               │               │
    │                │──POST /prep/{id}/scan──▶│                    │               │               │
    │                │                    │──校验:部品匹配BOM   │               │               │
    │                │                    │──校验:批次未过期    │               │               │
    │                │                    │──校验:MSL未超时     │               │               │
    │                │                    │                    │──BEGIN TRANSACTION──│               │
    │                │                    │                    │──冻结InventoryLot(Status→2)──│               │
    │                │                    │                    │──更新Inventory汇总(Available-=,Frozen+=)──│               │
    │                │                    │                    │──写入PrepScanRecord──│               │
    │                │                    │                    │──更新PrepDetail.ActualQty──│               │
    │                │                    │                    │──写入StockMovement(类型2:备料冻结)──│               │
    │                │                    │                    │──COMMIT────────│               │
    │                │                    │──不足→写入异常──────▶│               │────▶AbnormalRecord
    │                │                    │                    │               │               │
    │                │◀──success──────────│                    │               │               │
    │◀──备料成功──────│                    │                    │               │               │
    │                │                    │                    │               │               │
    │──完成备料──────▶│                    │                    │               │               │
    │                │──POST /prep/{id}/complete──▶│                    │               │               │
    │                │                    │──全部PrepDetail已备料?──▶│               │               │
    │                │                    │──是→状态=已完成────▶│               │               │
    │                │                    │──推送prep.completed──│               │────▶RabbitMQ
    │                │◀──success──────────│                    │               │               │
    │◀──完成备料──────│                    │                    │               │               │
```

**暂停/恢复：**

```
POST /prep/{id}/pause    → PrepOrder.Status = 5(已暂停)
POST /prep/{id}/resume   → PrepOrder.Status = 2(备料中)
```

**撤销备料明细：**

```
POST /prep/detail/{detailId}/cancel
    → 校验PrepDetail.Status = 2(已备料)
    → 遍历PrepScanRecord，解除InventoryLot冻结(Status 2→1)
    → 更新Inventory汇总(Available+=, Frozen-=)
    → PrepDetail.Status = 4(已撤销), ActualQty = 0
    → 写入StockMovement(类型8:撤销)
```

### 5.3 上线确认

**场景：** 备料完成后，操作员确认部品上线到指定工位。支持分批上线。

```
PDA操作员          API网关          OnlineController    OnlineService      Inventory
    │                │                    │                    │               │
    │──扫码备料单───▶│                    │                    │               │
    │──扫码部品──────▶│                    │                    │               │
    │                │──POST /online/confirm──▶│                    │               │
    │                │  {                   │                    │               │
    │                │    prepOrderId: 0,   │                    │               │
    │                │    partNo: "XXX",    │                    │               │
    │                │    barcode: "...",   │                    │               │
    │                │    loadedQty: 0,     │                    │               │
    │                │    stationNo: "S01"  │                    │               │
    │                │  }                   │                    │               │
    │                │                    │──根据partNo查PrepDetail──▶│               │
    │                │                    │──校验:工位匹配       │               │
    │                │                    │──校验:累计上线量≤备料量──│               │
    │                │                    │──校验:批次Status=2(已冻结)──│               │
    │                │                    │                    │──BEGIN TRANSACTION──│
    │                │                    │                    │──写入OnlineConfirm──│
    │                │                    │                    │──批次拆分(如LoadedQty<批次数量)──│
    │                │                    │                    │──出库InventoryLot(实际出库数量)──│
    │                │                    │                    │──更新Inventory汇总(Frozen-=,Total-=)──│
    │                │                    │                    │──写入StockMovement(类型3:上线出库)──│
    │                │                    │                    │──写入ScanRecord──│
    │                │                    │                    │──COMMIT────────│
    │                │◀──success──────────│                    │               │
    │◀──上线成功──────│                    │                    │               │
```

**上线确认流程说明（P0-1修复）：**
- 备料阶段已完成冻结（AvailableQty -= qty, FrozenQty += qty），库存总量未变
- 上线确认是**实际出库**：FrozenQty -= qty, TotalQty -= qty
- **不重复扣减**：上线确认不会再次扣减 AvailableQty（已在备料时冻结扣减）
- 校验：批次必须处于 Status=2(已冻结) 状态才能上线

**批次拆分逻辑（P1-#8修复）：**
上线确认数量小于批次数量时，系统自动拆分批次，保留剩余数量在原库位：

```csharp
// OnlineService.ConfirmAsync() - 批次拆分
if (loadedQty < lot.Quantity)
{
    // 创建剩余批次（保持原库位，状态恢复为可用）
    var remainingLot = new InventoryLot
    {
        InventoryId = lot.InventoryId,
        PartId = lot.PartId,
        LocationId = lot.LocationId,
        BatchNo = lot.BatchNo,
        Quantity = lot.Quantity - loadedQty,
        Status = 1, // 可用（剩余部分恢复可用）
        ReceiptDate = lot.ReceiptDate,
        ExpiryDate = lot.ExpiryDate,
        OriginType = lot.OriginType,
        MSLExposureTime = lot.MSLExposureTime,
        Version = 0
    };
    await _inventoryLotRepo.AddAsync(remainingLot);

    // 当前批次数量 = loadedQty，标记为已出库
    lot.Quantity = loadedQty;
    lot.Status = 5; // 5-已出库
}
else
{
    // 整批上线
    lot.Status = 5; // 5-已出库
}
```

**撤销上线：**

```
POST /online/{id}/cancel
    → 校验OnlineConfirm.Status = 1(已确认)
    → 恢复InventoryLot(Quantity+=,恢复冻结)
    → 更新Inventory汇总(Frozen+=, Total+=)
    → OnlineConfirm.Status = 2(已撤销)
    → 写入StockMovement(类型8:撤销)
```

### 5.4 退料流程

```
PDA操作员          API网关          ReturnController    ReturnService     PC班组长
    │                │                    │                    │               │
    │──扫码退料──────▶│                    │                    │               │
    │                │──POST /return/create──▶│                    │               │
    │                │                    │──写入ReturnOrder(待确认)──▶│               │
    │                │◀──success──────────│                    │               │
    │                │                    │                    │──WebSocket推送──▶│
    │                │                    │                    │               │──审核──────▶│
    │                │                    │                    │               │           │
    │                │                    │                    │               │──POST /return/{id}/confirm──▶│
    │                │                    │                    │──更新InventoryLot(回退)──▶│               │
    │                │                    │                    │──更新Inventory汇总(Total+=,Available+=)──▶│               │
    │                │                    │                    │──写入StockMovement(类型4:退料入库)──▶│               │
    │                │                    │                    │──更新PrepDetail──▶│               │
    │                │                    │                    │◀─────────────────│               │
    │                │                    │                    │──WebSocket推送──▶│
    │                │                    │                    │               │◀──已确认────│
```

### 5.5 盘点流程

```
PC管理员          API网关          CountController     CountService      PDA盘点员
    │                │                    │                    │               │
    │──创建盘点单───▶│                    │                    │               │
    │                │──POST /count/create──▶│                    │               │
    │                │                    │──写入StockCount+StockCountItem──▶│               │
    │                │◀──success──────────│                    │               │
    │                │                    │                    │               │──扫码盘点──▶│
    │                │                    │                    │               │           │
    │                │                    │                    │               │──POST /count/{id}/scan──▶│
    │                │                    │                    │──更新StockCountItem.ActualQty──▶│               │
    │                │                    │                    │               │◀──success──│
    │                │                    │                    │               │──确认盘点──▶│
    │                │                    │                    │               │           │
    │                │                    │                    │               │──POST /count/{id}/confirm──▶│
    │                │                    │                    │──调整InventoryLot(差异部分)──▶│               │
    │                │                    │                    │──更新Inventory汇总(差异)──▶│               │
    │                │                    │                    │──写入StockMovement(类型5:盘点调整)──▶│               │
    │                │                    │                    │◀─────────────────│               │
    │                │◀──success──────────│                    │               │◀──已完成──│
    │◀──盘点完成──────│                    │                    │               │
```

### 5.6 库存调拨

```
PC管理员          API网关          TransferController  TransferService   Inventory
    │                │                    │                    │               │
    │──创建调拨单───▶│                    │                    │               │
    │                │──POST /transfer/create──▶│                    │               │
    │                │                    │──写入TransferOrder──▶│               │
    │                │                    │                    │               │
    │──执行调拨──────▶│                    │                    │               │
    │                │──POST /transfer/{id}/execute──▶│                    │               │
    │                │                    │                    │──BEGIN TRANSACTION──│
    │                │                    │                    │──扣减源库位InventoryLot──│
    │                │                    │                    │──增加目标库位InventoryLot──│
    │                │                    │                    │──更新Inventory汇总(源-=,目标+=)──│
    │                │                    │                    │──写入StockMovement(调拨出/入)──│
    │                │                    │                    │──COMMIT────────│
    │                │◀──success──────────│                    │               │
    │◀──调拨完成──────│                    │                    │               │
```

### 5.7 工单结案

```
PC班组长          API网关          OrderController     OrderService      ReturnService
    │                │                    │                    │               │
    │──工单结案──────▶│                    │                    │               │
    │                │──POST /orders/{id}/close──▶│                    │               │
    │                │                    │──统计实际产出/损耗/报废──▶│               │
    │                │                    │──计算尾料→PartRemainDetails(JSON)──▶│               │
    │                │                    │──写入OrderClosure────▶│               │
    │                │                    │──尾料>0→自动创建ReturnOrder+ReturnOrderItem──▶│────▶ReturnOrder
    │                │                    │──工单状态=已结案────▶│               │
    │                │◀──success──────────│                    │               │
    │◀──结案完成──────│                    │                    │               │
```

---

## 6. API接口设计

### 6.1 统一规范

**API版本控制：** 所有接口前缀 `/api/v1/`

**统一返回格式：**

```json
{
  "code": 200,
  "message": "success",
  "data": {},
  "timestamp": "2026-07-05T16:00:00Z",
  "requestId": "req-a1b2c3d4-e5f6-7890"
}
```

**失败返回：**

```json
{
  "code": 30001,
  "message": "库存不足",
  "data": null,
  "timestamp": "2026-07-05T16:00:00Z",
  "requestId": "req-a1b2c3d4-e5f6-7890"
}
```

**分页请求参数：** `pageIndex`（从1开始）, `pageSize`（默认20，最大100）

**分页响应：**

```json
{
  "code": 200,
  "data": {
    "items": [],
    "total": 100,
    "pageIndex": 1,
    "pageSize": 20,
    "totalPages": 5
  }
}
```

**请求头：**
- `Authorization: Bearer {token}` — JWT认证
- `X-Request-Id` — 请求追踪ID（自动生成）
- `X-Device-Id` — Android端设备标识
- `X-Idempotency-Key` — **幂等键（写操作必填）**

**幂等性设计（P0-4修复）：**
- 所有写操作接口（上料确认、扫码备料、上线确认、撤销等）必须携带 `X-Idempotency-Key`
- 幂等键格式：`{DeviceId}:{Timestamp}:{BarcodeHash}` （如 `PDA001:1720166400:a1b2c3d4`）
- 服务端使用Redis进行去重：`SET idem:{key} {response} EX 300 NX`（5分钟窗口）
- 相同幂等键在窗口期内再次请求 → 直接返回首次响应的结果（HTTP 200），不执行写操作
- 业务层补充：`(ReferenceType + ReferenceId + Barcode + OperatorId)` 唯一约束作为兜底

### 6.2 错误码定义

| 错误码 | 模块 | 说明 | HTTP状态 |
|---|---|---|---|
| 200 | 通用 | 成功 | 200 |
| 400 | 通用 | 请求参数错误 | 400 |
| 401 | 通用 | 未授权/Token过期 | 401 |
| 403 | 通用 | 无权限 | 403 |
| 404 | 通用 | 资源不存在 | 404 |
| 409 | 通用 | 资源冲突 | 409 |
| 429 | 通用 | 请求过于频繁 | 429 |
| 10001 | 部品 | 部品不存在 | 404 |
| 10002 | 部品 | 部品已停用 | 400 |
| 20001 | 库位 | 库位不存在 | 404 |
| 20002 | 库位 | 库位容量超限 | 409 |
| 20003 | 库位 | 库位已锁定 | 409 |
| 30001 | 库存 | 库存不足 | 409 |
| 30002 | 库存 | 批次已过期 | 400 |
| 30003 | 库存 | 库存状态不允许操作 | 400 |
| 30004 | 库存 | MSL元件超时 | 400 |
| 30005 | 库存 | 乐观锁冲突，请重试 | 409 |
| 40001 | 上料 | 上料记录不存在 | 404 |
| 40002 | 上料 | 上料批次已确认，不可修改 | 409 |
| 40003 | 上料/备料 | 条码格式不识别 | 400 |
| 50001 | 备料 | 备料单不存在 | 404 |
| 50002 | 备料 | 部品与BOM不匹配 | 400 |
| 50003 | 备料 | 备料单状态不允许操作 | 409 |
| 50004 | 备料 | 齐套检查未通过 | 409 |
| 60001 | 上线 | 上线数量超过备料数量 | 400 |
| 60002 | 上线 | 工位不匹配 | 400 |
| 60003 | 上线 | 部品未备料 | 400 |
| 70001 | 异常 | 异常记录不存在 | 404 |
| 80001 | 退料 | 退料单不存在 | 404 |
| 80002 | 退料 | 退料数量超过备料数量 | 400 |
| 90001 | 盘点 | 盘点单不存在 | 404 |
| 99999 | 通用 | 服务器内部错误 | 500 |

### 6.3 接口清单

#### 认证接口

| 方法 | 端点 | 说明 | 认证 |
|---|---|---|---|
| POST | `/api/v1/auth/login` | 登录 | 否 |
| POST | `/api/v1/auth/refresh` | 刷新Token | Refresh Token |
| POST | `/api/v1/auth/logout` | 登出 | 是 |

**登录请求：**
```json
{ "username": "admin", "password": "Admin123" }
```

**登录响应：**
```json
{
  "code": 200,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "rt-xxxxxxxx",
    "expiresIn": 1800,
    "user": {
      "id": 1,
      "username": "admin",
      "realName": "张三",
      "roleCode": "ADMIN",
      "lineId": null
    }
  }
}
```

#### 上料管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/loading/batch` | 创建上料批次 | 仓管 |
| POST | `/api/v1/loading/batch/{batchId}/item` | 添加上料明细 | 仓管 |
| POST | `/api/v1/loading/batch/{batchId}/confirm` | 确认上料批次 | 仓管 |
| GET | `/api/v1/loading/batch/{batchId}` | 查询上料批次 | 仓管/班长 |
| GET | `/api/v1/loading/list` | 上料记录列表 | 仓管/班长 |
| POST | `/api/v1/loading/{id}/cancel` | 撤销上料 | 仓管/班长 |

**创建上料批次请求：**
```json
{ "targetLocationCode": "A-01-02-03" }
```

**确认上料响应：**
```json
{
  "code": 200,
  "data": {
    "batchId": 1001,
    "batchNo": "LB202607050001",
    "itemCount": 5,
    "totalQty": 500,
    "confirmedAt": "2026-07-05T16:30:00Z"
  }
}
```

#### 备料管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/prep/generate` | 工单驱动生成备料单 | 系统 |
| POST | `/api/v1/prep/{prepOrderId}/recalculate` | **重新计算备料单（P2-#10修复）** | 管理员 |
| GET | `/api/v1/prep/list` | 备料单列表 | 全部角色 |
| GET | `/api/v1/prep/{orderId}` | 备料单详情(含明细) | 全部角色 |
| GET | `/api/v1/prep/{orderId}/details` | 备料明细列表 | 全部角色 |
| POST | `/api/v1/prep/{orderId}/kit-check` | 齐套性检查 | 仓管 |
| POST | `/api/v1/prep/{orderId}/scan` | 扫码备料 | 仓管 |
| POST | `/api/v1/prep/{orderId}/batch-scan` | 批量扫码备料 | 仓管 |
| POST | `/api/v1/prep/{orderId}/complete` | 完成备料 | 仓管 |
| POST | `/api/v1/prep/{orderId}/cancel` | 取消备料单 | 仓管/班长 |
| POST | `/api/v1/prep/{orderId}/pause` | 暂停备料 | 仓管/班长 |
| POST | `/api/v1/prep/{orderId}/resume` | 恢复备料 | 仓管/班长 |
| POST | `/api/v1/prep/detail/{detailId}/cancel` | 撤销备料明细 | 仓管/班长 |
| POST | `/api/v1/prep/{orderId}/abnormal` | 上报异常 | 仓管 |

**备料单生成请求（P2-#9修复）：**
```json
{
  "productionOrderId": 1001,
  "forceRegenerate": false
}
```
- `forceRegenerate: true` — 删除已有备料单，重新生成并覆盖（需管理员权限）
- `forceRegenerate: false`（默认）— 幂等模式，已有备料单时直接返回已有信息

**备料单重新计算（P2-#10修复）：**
当 BOM 变更后，重新计算备料单需求数量：
```
POST /api/v1/prep/{prepOrderId}/recalculate
```
- 校验：备料单状态必须为"待备料"或"备料中"
- 行为：根据最新BOM重新计算 PrepDetail.RequiredQty，已备数量(ActualQty)不变
- 如果 BOM 新增明细 → 追加 PrepDetail
- 如果 BOM 删除明细 → 标记原 PrepDetail 为"已撤销"
- 如果数量变更 → 更新 RequiredQty，触发重新齐套检查

**扫码备料请求：**
```json
{
  "partNo": "R-10K-0402",
  "barcode": "R10K0402-B20260701-A01-02",
  "sourceLocationCode": "A-01-02",
  "quantity": 100,
  "batchNo": "B20260701"
}
```

**批次确定逻辑（P0-3修复）：**
系统提供两种批次选择策略，通过 `batchNo` 字段区分：

| 策略 | 请求 | 行为 | 适用场景 |
|---|---|---|---|
| **操作员自选批次** | `batchNo` 非空 | 从条码解析批次号，直接从指定批次冻结 | 操作员已确认物理批次 |
| **系统FIFO自动选批** | `batchNo` 为空 | 按FIFO SQL选取最早可用批次 | 操作员不关心批次，系统自动分配 |

条码解析规则（参考 Part.BarcodeRule）：
- 模板格式：`{PartNo}-B{BatchNo}-{LocationCode}`
- 示例：`R10K0402-B20260701-A01-02` → PartNo=R10K0402, BatchNo=B20260701, Location=A01-02
- 解析失败 → 返回错误码 40003(条码格式不识别)，PDA弹窗提示操作员手动输入
```

**扫码备料响应：**
```json
{
  "code": 200,
  "data": {
    "prepDetailId": 5001,
    "requiredQty": 100,
    "actualQty": 100,
    "remainingQty": 0,
    "isComplete": true
  }
}
```

**齐套检查结果：**
```json
{
  "code": 200,
  "data": {
    "kitCheckResult": 3,
    "isComplete": false,
    "shortageItems": [
      {
        "partNo": "C-100NF-0603",
        "requiredQty": 500,
        "availableQty": 300,
        "shortageQty": 200,
        "suggestedLocation": "A-02-01"
      }
    ]
  }
}
```

#### 上线确认接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/online/confirm` | 上线确认 | 仓管 |
| GET | `/api/v1/online/list` | 上线记录列表 | 全部角色 |
| GET | `/api/v1/online/{prepOrderId}` | 备料单上线记录 | 全部角色 |
| POST | `/api/v1/online/{id}/cancel` | 撤销上线确认 | 仓管/班长 |

**上线确认请求：**
```json
{
  "prepOrderId": 1001,
  "partNo": "R-10K-0402",
  "barcode": "R10K0402-B20260701-A01-02",
  "loadedQty": 100,
  "stationNo": "S01",
  "sourceLocationCode": "A-01-02"
}
```

**上线确认响应：**
```json
{
  "code": 200,
  "data": {
    "confirmId": 3001,
    "partNo": "R-10K-0402",
    "loadedQty": 100,
    "totalLoaded": 100,
    "requiredQty": 100,
    "stationNo": "S01",
    "sourceLocationCode": "A-01-02",
    "confirmedAt": "2026-07-05T17:00:00Z"
  }
}
```

#### 退料管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/return/create` | 创建退料单(含明细) | 仓管 |
| POST | `/api/v1/return/{id}/items` | 追加退料明细 | 仓管 |
| GET | `/api/v1/return/list` | 退料单列表 | 全部角色 |
| GET | `/api/v1/return/{id}` | 退料单详情(含明细) | 全部角色 |
| POST | `/api/v1/return/{id}/confirm` | 确认退料(整单) | 班长/管理员 |
| POST | `/api/v1/return/{id}/reject` | 拒绝退料 | 班长/管理员 |

#### 库存管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/inventory` | 查询库存 | 全部角色 |
| GET | `/api/v1/inventory/lot` | 查询批次明细 | 全部角色 |
| GET | `/api/v1/inventory/check` | 校验库存是否充足 | 系统 |
| GET | `/api/v1/inventory/movement` | 库存流水查询 | 全部角色 |
| POST | `/api/v1/inventory/adjust` | 库存调整(盘点) | 管理员 |

**库存查询请求：** `GET /api/v1/inventory?partId=1&locationId=2`

**库存查询响应：**
```json
{
  "code": 200,
  "data": {
    "partId": 1,
    "partNo": "R-10K-0402",
    "partName": "电阻 10K 0402",
    "locationId": 2,
    "locationCode": "A-01-02",
    "totalQty": 1000,
    "availableQty": 800,
    "frozenQty": 200,
    "lots": [
      {
        "lotId": 101,
        "batchNo": "B20260701",
        "quantity": 500,
        "status": 1,
        "receiptDate": "2026-07-01",
        "expiryDate": "2027-07-01"
      },
      {
        "lotId": 102,
        "batchNo": "B20260702",
        "quantity": 300,
        "status": 1,
        "receiptDate": "2026-07-02",
        "expiryDate": "2027-07-02"
      }
    ]
  }
}
```

#### 盘点管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/count/create` | 创建盘点单 | 管理员/仓管 |
| GET | `/api/v1/count/list` | 盘点单列表 | 全部角色 |
| GET | `/api/v1/count/{id}` | 盘点单详情 | 全部角色 |
| POST | `/api/v1/count/{id}/scan` | 盘点扫码 | 仓管 |
| POST | `/api/v1/count/{id}/confirm` | 确认盘点结果 | 管理员/班长 |

#### 调拨管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| POST | `/api/v1/transfer/create` | 创建调拨单(含明细) | 管理员/仓管 |
| POST | `/api/v1/transfer/{id}/items` | 追加调拨明细 | 管理员/仓管 |
| GET | `/api/v1/transfer/list` | 调拨单列表 | 全部角色 |
| GET | `/api/v1/transfer/{id}` | 调拨单详情(含明细) | 全部角色 |
| POST | `/api/v1/transfer/{id}/execute` | 执行调拨(整单) | 管理员/仓管 |

#### 库位管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/locations` | 库位列表 | 全部角色 |
| POST | `/api/v1/locations` | 新增库位 | 管理员 |
| PUT | `/api/v1/locations/{id}` | 更新库位 | 管理员 |
| POST | `/api/v1/locations/{id}/lock` | 锁定库位 | 管理员 |
| POST | `/api/v1/locations/{id}/unlock` | 解锁库位 | 管理员 |
| GET | `/api/v1/locations/export` | 导出库位Excel | 全部角色 |

#### 部品管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/parts` | 部品列表 | 全部角色 |
| GET | `/api/v1/parts/{id}` | 部品详情 | 全部角色 |
| POST | `/api/v1/parts` | 新增部品 | 管理员 |
| PUT | `/api/v1/parts/{id}` | 更新部品 | 管理员 |
| DELETE | `/api/v1/parts/{id}` | 停用部品 | 管理员 |
| POST | `/api/v1/parts/import` | 批量导入Excel | 管理员 |
| GET | `/api/v1/parts/export` | 导出部品Excel | 全部角色 |
| GET | `/api/v1/parts/substitute/{partId}` | 查询替代料 | 全部角色 |

#### 工单管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/orders` | 工单列表 | 全部角色 |
| GET | `/api/v1/orders/{id}` | 工单详情(含BOM) | 全部角色 |
| POST | `/api/v1/orders` | **创建工单（P2-20修复）** | 管理员/班长 |
| POST | `/api/v1/orders/{id}/status` | 工单状态变更 | 管理员/班长 |
| POST | `/api/v1/orders/{id}/close` | 工单结案 | 班长/管理员 |

#### 异常管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/abnormal/list` | 异常记录列表 | 全部角色 |
| GET | `/api/v1/abnormal/{id}` | 异常详情 | 全部角色 |
| POST | `/api/v1/abnormal/{id}/handle` | 处理异常 | 管理员/班长 |

#### 系统管理接口

| 方法 | 端点 | 说明 | 权限 |
|---|---|---|---|
| GET | `/api/v1/users` | 用户列表 | 管理员 |
| POST | `/api/v1/users` | 新增用户 | 管理员 |
| PUT | `/api/v1/users/{id}` | 更新用户 | 管理员 |
| GET | `/api/v1/roles` | 角色列表 | 管理员 |
| GET | `/api/v1/lines` | 产线列表 | 全部角色 |
| GET | `/api/v1/stations` | 工位列表 | 全部角色 |
| GET | `/api/v1/logs` | 操作日志 | 管理员 |
| GET | `/api/v1/scan-records` | 扫码记录 | 管理员/班长 |

---

## 7. Android端设计

### 7.1 页面路由

| 路由 | 页面 | 说明 |
|---|---|---|
| `/login` | 登录页 | 账号密码登录 |
| `/home` | 工作台 | 功能入口 + 待办统计 |
| `/loading` | 上料页面 | 扫码→累积→确认/暂存 |
| `/prep/list` | 备料列表 | 待备料/备料中/已完成/异常 |
| `/prep/:id` | 备料作业 | 逐项扫码，进度展示，异常上报 |
| `/online` | 上线确认 | 扫码→匹配→确认 |
| `/return` | 退料页面 | 扫码→数量→目标库位 |
| `/count` | 盘点页面 | 扫码库位→扫码盘点 |
| `/scan` | 扫码页 | 摄像头扫码(ML Kit) |
| `/profile` | 个人中心 | 退出登录、切换产线、设备信息 |

### 7.2 页面设计

**工作台页面**

```
┌─────────────────────────────────┐
│  🟢 在线 | DIP物料管理 [设置][用户]│  (P2-11:网络状态指示)
├─────────────────────────────────┤
│                                 │
│  ┌──────────┐  ┌──────────┐    │
│  │  📦 上料  │  │  📋 备料  │    │
│  │          │  │   (12)   │    │
│  │ 线边仓上料│  │  12单待备 │    │
│  └──────────┘  └──────────┘    │
│                                 │
│  ┌──────────┐  ┌──────────┐    │
│  │  ✅ 上线  │  │  ↩ 退料  │    │
│  │          │  │          │    │
│  │ 上线确认  │  │ 退料申请  │    │
│  └──────────┘  └──────────┘    │
│                                 │
│  ┌───────────────────────────┐  │
│  │  ⚠️  3条异常待处理         │  │
│  │  [查看详情]                │  │
│  └───────────────────────────┘  │
│                                 │
│  ┌───────────────────────────┐  │
│  │         🔍 快捷扫码        │  │
│  └───────────────────────────┘  │
└─────────────────────────────────┘
```

**备料作业页面**

```
┌─────────────────────────────────┐
│  ◀ 备料作业          [异常上报]   │
├─────────────────────────────────┤
│  备料单: PO20260705001           │
│  工单: WO20260705001 产线:L01   │
├─────────────────────────────────┤
│  进度: ████████░░ 80% (4/5)     │
├─────────────────────────────────┤
│  ┌───────────────────────────┐  │
│  │ R-10K-0402  位号:R1~R20   │  │
│  │ 需求:200 已备:200 ✅ 已备料│  │
│  └───────────────────────────┘  │
│  ┌───────────────────────────┐  │
│  │ C-100NF-0603 位号:C1~C50  │  │
│  │ 需求:500 已备:500 ✅ 已备料│  │
│  └───────────────────────────┘  │
│  ┌───────────────────────────┐  │
│  │ IC-STM32 位号:U1~U5       │  │
│  │ 需求:50  已备:50  ✅ 已备料│  │
│  └───────────────────────────┘  │
│  ┌───────────────────────────┐  │
│  │ CONN-4P 位号:J1~J10       │  │
│  │ 需求:100 已备:100 ✅ 已备料│  │
│  └───────────────────────────┘  │
│  ┌───────────────────────────┐  │
│  │ RES-1K-0805 位号:R21~R30  │  │
│  │ 需求:100 已备:0  ⏳ 待备料 │  │
│  └───────────────────────────┘  │
├─────────────────────────────────┤
│  [📷 扫码备料]  [完成备料]        │
└─────────────────────────────────┘
```

### 7.3 关键特性

1. **离线模式**
    - Room本地缓存备料单数据、扫码记录
    - **部品主数据缓存（P1-#11修复）：** 开机联网后，拉取当前工单相关部品主数据到本地Room
    - 断网时扫码数据暂存本地，恢复后自动同步（SyncManager）
    - **同步冲突策略（P1-4修复）：**
      - 查询类冲突（库存查询、备料单详情）：以服务器为准
      - 写操作冲突（扫码备料、上料确认）：**不得静默丢弃**，必须提示操作员"同步失败，请核对实物与账面"
      - PDA显示冲突详情：本地记录 vs 服务器记录，由操作员确认最终状态

2. **扫码反馈**
   - 成功：短振动 + 绿色提示音 + 屏幕边缘绿色闪烁
   - 失败：长振动 + 红色提示音 + 屏幕边缘红色闪烁
   - 异常：双短振动 + 橙色提示音

3. **多码型支持**
   - QR Code / Code 128 / Code 39 / EAN-13 / EAN-8 / Code 93

4. **重试机制**
   - 指数退避：3次重试，间隔 1s / 2s / 4s
   - 重试后仍失败 → 提示用户手动重试或暂存

5. **连续扫码**
   - 上料支持连续扫码累积，统一确认
   - 备料扫码自动识别部品，触发**数量校验**（P1-8修复：原"过量确认"用词易引发歧义）

---

## 8. PC端设计

### 8.1 路由结构

| 路由 | 页面 | 权限 | 说明 |
|---|---|---|---|
| `/login` | 登录 | 公开 | 账号密码登录 |
| `/dashboard` | 仪表盘 | 全部 | KPI数据 + 实时推送 |
| `/inventory` | 库存管理 | 仓管+ | 库存查询、批次明细、库存流水 |
| `/inventory/count` | 库存盘点 | 仓管+ | 盘点单管理 |
| `/inventory/transfer` | 库存调拨 | 仓管+ | 调拨单管理 |
| `/orders` | 工单管理 | 全部 | 工单列表、BOM查看、结案 |
| `/prep-orders` | 备料单管理 | 全部 | 备料单列表、详情、操作 |
| `/parts` | 部品管理 | 管理员 | 主数据CRUD、导入导出 |
| `/locations` | 库位管理 | 管理员 | 库位CRUD、容量监控 |
| `/abnormal` | 异常预警 | 班长+ | 异常列表、处理、统计 |
| `/return` | 退料管理 | 班长+ | 退料单审核 |
| `/reports` | 报表中心 | 全部 | 操作日志、扫码记录、导出 |
| `/system/users` | 用户管理 | 管理员 | 用户CRUD、角色分配 |
| `/system/lines` | 产线管理 | 管理员 | 产线/工位/设备维护 |
| `/system/settings` | 系统设置 | 管理员 | 参数配置、条码规则 |

### 8.2 仪表盘KPI

| 指标 | 说明 | 数据来源 |
|---|---|---|
| 今日上料量 | 今日上料总数量 | MaterialLoading |
| 备料完成率 | 已完成备料单 / 总备料单 | PrepOrder |
| 上线达成率 | 已上线部品数 / 应上线部品数 | OnlineConfirm |
| 库存周转率 | 月出库量 / 月平均库存 | StockMovement |
| 呆滞物料预警 | 超过90天无变动的库存 | Inventory + StockMovement |
| 异常处理及时率 | **在规定时限内处理的异常 / 总异常**（按Severity对应时限判断，P1-5修复） | AbnormalRecord |

**实时推送：** WebSocket连接SignalR Hub，RabbitMQ消费消息后推送前端。

---

## 9. 消息队列与实时通信

### 9.1 RabbitMQ 队列

| 队列 | 用途 | 消费者 | 幂等性 |
|---|---|---|---|
| `abnormal.alert` | 备料异常预警 | SignalR→PC端 | 消息ID去重 |
| `prep.completed` | 备料完成通知 | SignalR→PC端 | 备料单ID去重 |
| `prep.kit-alert` | 齐套检查结果 | SignalR→PC端 | 备料单ID去重 |
| `online.confirmed` | 上线确认通知 | SignalR→PC端 | 记录ID去重 |
| `dead-letter` | 死信队列 | **DeadLetterHandler（落库+WebSocket推送PC端）（P2-12修复）** | - |

### 9.2 消息重试策略

```
消息发送 → 消费者处理失败
              │
              ├── 第1次重试: 1秒后
              ├── 第2次重试: 5秒后
              ├── 第3次重试: 30秒后
              └── 超过3次 → 进入死信队列 → 人工处理
```

### 9.3 WebSocket 实时推送

```
RabbitMQ        .NET后端          SignalR Hub         PC前端
    │               │                    │                │
    │──消息──▶│               │                    │                │
    │               │──消费消息──▶│                    │                │
    │               │                    │──推送事件──▶│                │
    │               │                    │                    │──更新UI──│
    │               │                    │                    │──显示通知──│
```

---

## 10. 权限与安全

### 10.1 角色权限矩阵

| 功能 | 管理员 | 仓管员 | 班组长 | 操作员 |
|---|---|---|---|---|
| 上料操作 | ✅ | ✅ | ❌ | ❌ |
| 备料操作 | ✅ | ✅ | ❌ | ❌ |
| 上线确认 | ✅ | ✅ | ❌ | ❌ |
| 撤销操作 | ✅ | ✅ | ✅ | ❌ |
| 异常处理 | ✅ | ❌ | ✅ | ❌ |
| 库存管理 | ✅ | ✅ | ✅ | ❌ |
| 盘点操作 | ✅ | ✅ | ✅ | ❌ |
| 退料审核 | ✅ | ❌ | ✅ | ❌ |
| 部品管理 | ✅ | ❌ | ❌ | ❌ |
| 用户管理 | ✅ | ❌ | ❌ | ❌ |
| 系统设置 | ✅ | ❌ | ❌ | ❌ |
| 报表查看 | ✅ | ✅ | ✅ | ✅ |
| 数据范围 | 全部 | 全部 | 所管产线 | 所管产线 |

### 10.2 安全策略

| 项目 | 策略 |
|---|---|
| 密码存储 | BCrypt哈希，salt rounds = 12 |
| 密码强度 | 最小8位，含大小写字母+数字 |
| Access Token | JWT，有效期30分钟，Redis存储黑名单 |
| Refresh Token | 随机字符串，有效期7天，Redis存储，轮换更新 |
| 接口限流 | Redis滑动窗口，扫码接口10次/秒/设备 |
| 敏感操作 | 撤销/取消需二次确认 |
| 数据权限 | Operator.LineId隔离，NULL=全部数据 |
| SQL注入 | EF Core参数化查询 |
| XSS | 前端模板自动转义 |
| **敏感字段脱敏（P2-#15修复）** | **SystemLog 的 OldValue/NewValue JSON 中，自动脱敏 Password、Token、PasswordHash 等敏感字段** |

**敏感字段脱敏实现（P2-#15）：**
```csharp
// 标记敏感属性
[AttributeUsage(AttributeTargets.Property)]
public class SensitiveAttribute : Attribute { }

public class UserDto {
    public string Username { get; set; }
    [Sensitive]
    public string PasswordHash { get; set; }
    [Sensitive]
    public string RefreshToken { get; set; }
}

// JSON序列化时自动脱敏
var sanitized = JsonSerializer.Serialize(entity, new JsonSerializerOptions {
    Converters = { new SensitiveDataConverter() }
});
// 输出: { "Username": "admin", "PasswordHash": "***", "RefreshToken": "***" }
```

---

## 11. 性能与可靠性

### 11.1 并发控制

| 场景 | 策略 | 说明 |
|---|---|---|
| 库存扣减 | 乐观锁 | InventoryLot.Version字段，冲突返回30005重试 |
| **PDA乐观锁体验（P2-17）** | **自动重试3次 + 中文提示** | **首次冲突：PDA显示"库存数据变动中，正在重试(1/3)..."；3次后失败：显示"操作冲突，请刷新后重试"** |
| 高并发扫码 | Redis限流 + 消息队列 | 扫码接口限流，库存操作异步化 |
| 备料并发 | 备料单级别锁 | 同一备料单同时只允许一人操作 |
| **跨备料单抢同一批次** | **Redis分布式锁 + 乐观锁** | **对 InventoryLot 加分布式锁(lock:inv:{PartId}:{LocationId})，防止两备料单同时选中同一批次（P0-6修复）** |

**跨备料单并发解决方案：**
1. FIFO选取批次时，对 (PartId, LocationId) 加Redis分布式锁 `lock:inv:{PartId}:{LocationId}`，TTL=5s
2. 获取锁后执行FIFO查询 + 冻结操作（`UPDATE InventoryLot SET Status=2, Version=Version+1 WHERE Id={lotId} AND Status=1 AND Version={version}`）
3. 乐观锁更新失败 → 释放锁 → 重新FIFO查询 → 重试（最多3次）
4. 释放锁后提交事务

### 11.2 事务边界

| 操作 | 涉及表 | 事务范围 |
|---|---|---|
| 上料确认 | MaterialLoading, InventoryLot(xN), **Inventory(x2)**, StockMovement(xN), ScanRecord, SystemLog | 整体事务 |
| 扫码备料 | InventoryLot, **Inventory**, PrepDetail, PrepScanRecord, StockMovement, ScanRecord | 整体事务 |
| 上线确认 | OnlineConfirm, InventoryLot, **Inventory**, StockMovement, ScanRecord | 整体事务 |
| 撤销上料 | MaterialLoading, InventoryLot(xN), **Inventory(x2)**, StockMovement(xN) | 整体事务 |
| 撤销备料 | InventoryLot, **Inventory**, PrepDetail, PrepScanRecord, StockMovement | 整体事务 |
| 撤销上线 | OnlineConfirm, InventoryLot, **Inventory**, StockMovement | 整体事务 |
| 退料确认 | ReturnOrder, InventoryLot, **Inventory**, StockMovement | 整体事务 |
| 盘点调整 | StockCountItem, InventoryLot, **Inventory**, StockMovement | 整体事务 |
| 调拨执行 | TransferOrder, InventoryLot(x2), **Inventory(x2)**, StockMovement(x2) | 整体事务 |

**约束：所有涉及 InventoryLot 的操作，必须在同一事务中同步更新 Inventory 汇总表。如果只更新 InventoryLot 而遗漏 Inventory，将导致汇总数据与明细数据不一致，仪表盘和齐套检查全部报错。**

### 11.3 缓存策略

| 缓存项 | Redis Key | 过期时间 | 更新策略 |
|---|---|---|---|
| 部品主数据 | `part:{id}` | 10分钟 | 写入时清除 |
| 库位信息 | `location:{id}` | 10分钟 | 写入时清除 |
| Token黑名单 | `token:black:{jti}` | Token有效期 | 登出时写入 |
| Refresh Token | `token:refresh:{rt}` | 7天 | 轮换更新 |
| 限流计数器 | `ratelimit:{device}:{endpoint}` | 1秒 | 滑动窗口 |
| 备料单操作锁 | `lock:prep:{orderId}` | 30分钟 | 分布式锁 |
| **库存批次操作锁** | `lock:inv:{PartId}:{LocationId}` | 5秒 | **分布式锁，FIFO选取+冻结时使用（P0-6）** |

### 11.4 索引设计

| 表 | 索引 | 类型 | 说明 |
|---|---|---|---|
| Part | PartNo | UNIQUE | 部品查询 |
| Part | (SupplierId, PartType) | 复合 | 供应商查询 |
| WarehouseLocation | LocationCode | UNIQUE | 库位查询 |
| WarehouseLocation | (Warehouse, Zone, Status) | 复合 | 区域查询 |
| InventoryLot | (PartId, LocationId, Status) | 复合 | 可用批次查询 |
| InventoryLot | BatchNo | 普通 | 批次追溯 |
| InventoryLot | ReceiptDate | 普通 | FIFO排序 |
| StockMovement | (PartId, CreatedAt) | 复合 | 部品流水 |
| StockMovement | (ReferenceType, ReferenceId) | 复合 | 单据关联 |
| StockMovement | BatchNo | 普通 | 批次追溯 |
| PrepOrder | (ProductionOrderId) | 普通 | 工单关联 |
| PrepOrder | (LineId, Status, CreatedAt) | 复合 | 产线备料查询 |
| PrepDetail | (PrepOrderId, PartId) | 复合 | 备料明细查询 |
| PrepDetail | (PrepOrderId, ReferenceDesignator) | 复合 | 按位号查询 |
| OnlineConfirm | (PrepOrderId, ConfirmedAt) | 复合 | 上线记录查询 |
| ScanRecord | (Barcode, CreatedAt) | 复合 | 扫码追溯 |
| ScanRecord | (OperatorId, CreatedAt) | 复合 | 操作人查询 |
| OnlineConfirm | (PrepDetailId, Status) | 复合 | **上线累计量校验，支持SUM(LoadedQty) WHERE PrepDetailId=? AND Status=1（P2优化）** |
| PrepScanRecord | ScannedBarcode | 普通 | **条码去重/追溯（P1-6修复）** |
| StockMovement | (PartId, LocationId, CreatedAt) | 复合 | **库存流水查询（P1-6修复）** |

### 11.5 数据归档

> **注：本系统不使用物理外键，仅靠应用层保证一致性（P2-13）**

| 表 | 归档条件 | 归档频率 | 保留策略 |
|---|---|---|---|
| StockMovement | CreatedAt > 1年前 | 每月1日凌晨 | 归档至StockMovement_Archive |
| ScanRecord | CreatedAt > 1年前 | 每月1日凌晨 | 归档至ScanRecord_Archive |
| SystemLog | CreatedAt > 1年前 | 每月1日凌晨 | 归档至SystemLog_Archive |
| AbnormalRecord | Status=2 且 HandledAt > 2年前 | 每季度 | 归档至AbnormalRecord_Archive |

归档执行：EF Core批量迁移至 Archive 表 + 原数据软删除（IsDeleted=1）+ 保留统计汇总表
- **归档保留期限（P2-#14补充）：** Archive表保留3年，超过3年的数据导出为Parquet格式后删除

### 11.6 Inventory热点行更新优化（P2-#13修复）

高并发场景下，同一 `(PartId, LocationId)` 的 Inventory 行会成为热点（大量并发 UPDATE）。

**优化方案：**
1. **批量合并更新：** 将 N 笔备料操作的 Inventory 汇总更新合并为 1 次（使用内存队列聚合，每100ms或攒够10笔触发一次）
2. **异步更新：** 核心备料事务成功后（InventoryLot + PrepDetail + StockMovement），将 Inventory 汇总更新放入 RabbitMQ 异步执行
3. **Redis 预热：** 高频查询的库存数据（仪表盘、齐套检查）预热到 Redis，降低 MySQL 读压力
   - Redis Key: `inv:summary:{PartId}:{LocationId}` → `{TotalQty, AvailableQty, FrozenQty}`
   - 更新策略：写Inventory后立即删除对应Redis key（Cache-Aside模式）
4. **乐观锁降级：** 热点行更新冲突率 > 10% 时，自动降级为批次级别锁（Redis分布式锁），避免反复重试

---

## 12. 电子行业特性

### 12.1 批次追溯

- 每次库存变动（InventoryLot + StockMovement）记录BatchNo
- 查询路径：批次号 → StockMovement → 关联工单/备料单/上线记录
- 反向查询：工单 → OnlineConfirm → StockMovement → InventoryLot → 批次号

### 12.2 FIFO先进先出

```sql
-- 备料时按入库日期排序，优先取最早可用批次（排除冻结/过期）
-- 单库位FIFO（操作员指定了sourceLocationCode时使用）
SELECT Id, BatchNo, Quantity
FROM InventoryLot
WHERE PartId = @partId
  AND LocationId = @locationId
  AND Status = 1          -- 仅可用批次
  AND (ExpiryDate IS NULL OR ExpiryDate > NOW())  -- 未过期
ORDER BY ReceiptDate ASC
LIMIT 1
```

**跨库位FIFO（P1-#7修复）：** 当操作员未指定库位（`batchNo` 为空且无 `sourceLocationCode`），
系统跨所有线边仓库位按 ReceiptDate 全局排序，自动选取最早可用批次：

```sql
-- 跨库位FIFO：同一部品分布多库位时，全局按入库日期排序
SELECT Id, BatchNo, Quantity, LocationId, LocationCode
FROM InventoryLot
WHERE PartId = @partId
  AND Status = 1
  AND (ExpiryDate IS NULL OR ExpiryDate > NOW())
ORDER BY ReceiptDate ASC
LIMIT 1
```

**策略选择：**
| 场景 | 策略 | SQL |
|---|---|---|
| 操作员指定了sourceLocationCode | 单库位FIFO | 限定 `LocationId = @locationId` |
| 操作员仅扫码部品条码 | 跨库位FIFO | 不限Location，全局按ReceiptDate排序 |
| 操作员扫码包含批次号 | 精确匹配 | 按 PartId + BatchNo 定位批次 |

**并发防护：** 执行FIFO查询前，先获取Redis分布式锁。
- 单库位FIFO：`lock:inv:{PartId}:{LocationId}`（TTL=5s）
- 跨库位FIFO：`lock:inv:{PartId}:global`（TTL=5s），锁定该部品的全局批次选取
执行 `UPDATE InventoryLot SET Status=2, Version=Version+1 WHERE Id={lotId} AND Status=1 AND Version={version}`，
乐观锁失败则释放锁后重新查询重试（最多3次）。

### 12.3 MSL湿敏管控

| MSL等级 | 开袋暴露时间 | 处理方式 |
|---|---|---|
| MSL 1 | 无限 | 无需管控 |
| MSL 2 | 无限 | 无需管控 |
| MSL 3 | 168小时 | 记录开袋时间，超时预警 |
| MSL 4 | 72小时 | 记录开袋时间，超时拦截 |
| MSL 5 | 48小时 | 记录开袋时间，超时拦截 |
| MSL 6 | 24小时 | 记录开袋时间，超时拦截 |

**MSLExposureTime 写入时机（P1-3修复）：**
- MSLExposureTime 在**备料扫码时**由系统自动写入（= NOW()），表示物料从线边仓被取出开袋的时刻
- 上料环节**不**写入 MSLExposureTime（物料在线边仓仍处于密封状态）
- 如果物料已被开袋但未及时扫码，MSLExposureTime 会晚于实际开袋时间，因此要求操作员开袋后立即扫码
- 撤销备料时，MSLExposureTime 清空（物料恢复未开袋状态）

校验逻辑：
```
IF Part.MSLLevel >= 3 THEN
    elapsed = NOW() - InventoryLot.MSLExposureTime
    IF elapsed > MSL_LIMIT[MSLLevel] THEN
        创建AbnormalRecord(Type=4, Severity=3)
        阻止备料
    END IF
END IF
```

### 12.4 替代料管理

```
备料扫码 → 校验部品匹配BOM
            │
            ├── 匹配正料 → 正常备料
            └── 不匹配 → 检查PartSubstitute
                         ├── 存在替代关系 → 标记SubstituteFlag=1，正常备料
                         └── 无替代关系 → 报错50002
```

### 12.4.1 条码规则说明（P2-14修复）

**Part.BarcodeRule 格式：** 模板字符串，使用 `{FieldName}` 占位符

| 示例规则 | 条码示例 | 解析结果 |
|---|---|---|
| `{PartNo}-B{BatchNo}-{LocationCode}` | `R10K0402-B20260701-A01-02` | PartNo=R10K0402, BatchNo=B20260701, Loc=A01-02 |
| `{PartNo}{BatchNo}` | `R10K0402B20260701` | PartNo=R10K0402, BatchNo=B20260701（需配置定长） |
| `{PartNo}/{BatchNo}/{Qty}` | `R10K0402/B20260701/1000` | PartNo=R10K0402, BatchNo=B20260701, Qty=1000 |

**解析失败处理：** 返回错误码 40003(条码格式不识别)，PDA提示"条码格式不匹配，请检查后重试"

### 12.5 位号管理

- BomItem.ReferenceDesignator 记录DIP位号（如 R1~R20, C1~C50）
- PrepDetail 按位号分组，支持按位号顺序备料
- 备料列表按位号排序显示

### 12.7 尾料处理

```
工单结案
  ├── 遍历每个 PrepDetail，计算 RemainQty = RequiredQty - ActualOnlineQty
  ├── 汇总尾料到 OrderClosure.PartRemainDetails (JSON数组)
  ├── 尾料 > 0 → 自动创建 ReturnOrder + ReturnOrderItem(s)
  └── ReturnOrder → 回退线边仓
```

---

### 11.7 MySQL主从读写分离策略

**问题：** 仓管员扫码备料（写主库）后立即查看备料进度（读从库），主从延迟导致数据不一致。

**解决方案：**
- 扫码相关的核心写接口（上料、备料、上线、撤销）：写操作后**强制读主库**，在EF Core中使用 `FromSql` 或 `AsQuery()` 指定主库
- 写操作后的N秒内（默认3秒），同一会话的查询强制路由到主库
- 实现方式：使用 `IDbContextTransaction` + 自定义 `IQuerySplittingBehavior`，或在应用层通过 `HttpContext.Items` 标记"刚写过，强制主读"

---

## 13. 部署架构

### 13.1 生产环境

```
                    ┌──────────────────────┐
                    │    Nginx (反向代理)    │
                    │  /api/* → Kestrel     │
                    │  /*   → SPA静态文件    │
                    └──────────┬───────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │                │
    ┌─────────▼─────────┐ ┌───▼──────┐ ┌───────▼────────┐
    │   .NET Web API    │ │  Redis   │ │   RabbitMQ     │
    │   (Kestrel)       │ │  Cluster │ │   Cluster      │
    │   x2 (负载均衡)    │ │          │ │                │
    └─────────┬─────────┘ └──────────┘ └────────────────┘
              │
              ▼
    ┌───────────────────┐
    │   MySQL 8.0       │
    │   主从复制         │
    │   读写分离         │
    └───────────────────┘
```

### 13.2 开发环境

```
单机部署：
  Docker Compose:
    - MySQL 8.0
    - Redis 7
    - RabbitMQ 3.12
  .NET Web API (本地运行)
  Vue DevServer (本地运行)
  Android Emulator / 真机调试
```

**Docker Compose 配置示例：**
```yaml
version: '3.8'
services:
  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: root123
      MYSQL_DATABASE: dip_material
    ports: ["3306:3306"]
    volumes: [mysql_data:/var/lib/mysql]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  rabbitmq:
    image: rabbitmq:3.12-management
    ports: ["5672:5672", "15672:15672"]
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest

volumes:
  mysql_data:
```

### 13.3 环境变量配置

```
# .env (后端)
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=mysql:server=db;port=3306;database=dip_material;user=admin;password=***
Redis__ConnectionString=redis:6379
RabbitMQ__ConnectionString=amqp://rabbitmq:5672
JWT__Secret=***
JWT__ExpiresMinutes=30
```

### 13.4 数据迁移版本管理（P2-#14修复）

使用 EF Core Migration 管理数据库变更，确保多环境数据库结构一致：

```bash
# 创建初始迁移
dotnet ef migrations add InitialCreate --project DIP.Infrastructure

# 创建增量迁移
dotnet ef migrations add AddPartIdToInventoryLot --project DIP.Infrastructure
dotnet ef migrations add AddSubstituteReason --project DIP.Infrastructure
dotnet ef migrations add AddPrepDetailCheck --project DIP.Infrastructure

# 应用迁移
dotnet ef database update --project DIP.Infrastructure

# 生成SQL脚本（用于生产环境DBA执行）
dotnet ef migrations script --project DIP.Infrastructure --idempotent
```

**迁移规范：**
- 每个迁移文件命名：`{日期}_{功能描述}`（如 `20260705_AddPartIdToInventoryLot`）
- 生产环境迁移由DBA审核SQL脚本后执行
- 迁移回滚策略：每个迁移必须可逆（Up/Down双向）
- CI/CD 集成：部署前自动检查 `__EFMigrationsHistory` 表，确保迁移版本一致

---

## 14. 非功能性需求

### 14.1 性能指标

| 指标 | 目标 | 说明 |
|---|---|---|
| API响应时间 | P95 < 200ms | 扫码接口P95 < 100ms |
| 扫码识别时间 | < 300ms | ML Kit识别+API请求 |
| 并发用户 | 50+ | 车间同时在线PDA |
| 库存查询 | < 500ms | 含批次明细 |
| 页面加载 | < 2s | PC端首屏 |

### 14.2 可用性

| 指标 | 目标 |
|---|---|
| 系统可用性 | 99.9%（月停机<43分钟） |
| 数据备份 | 每日全量 + 每小时增量 |
| 故障恢复 | RTO < 30分钟, RPO < 1小时 |
| 监控告警 | 应用健康检查 + 数据库监控 + 磁盘监控 |

### 14.3 可维护性

| 指标 | 目标 |
|---|---|
| 代码覆盖率 | 单元测试 > 80% |
| 日志规范 | 结构化JSON日志，统一日志级别 |
| 错误追踪 | 全局异常中间件，错误码分类 |
| 文档 | API Swagger文档，数据库DDL文档 |

### 14.4 安全

| 项目 | 要求 |
|---|---|
| 传输加密 | HTTPS（生产环境） |
| CSRF | SameSite Cookie + JWT |
| 敏感日志 | 密码/Token不记录日志 |

> **注：** 密码存储（BCrypt）、JWT/Token机制、SQL注入防护、XSS防护详见第10.2节"安全策略"（P2-9去重修复）

---

## 15. 数据字典

### 15.1 备料单状态

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 待备料 | 工单下发后自动创建 |
| 2 | 备料中 | 开始扫码备料 |
| 3 | 已完成 | 所有明细已备料 |
| 4 | 已撤销 | 手动撤销（P1-9统一术语） |
| 5 | 已暂停 | 临时暂停 |

### 15.2 备料明细状态

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 待备料 | 等待扫码 |
| 2 | 已备料 | ActualQty == RequiredQty（P2-10修复：不允许超量备料，如需损耗余量需配置"允许超量比例"参数） |
| 3 | 异常 | 备料过程中发生异常 |
| 4 | 已撤销 | 备料被撤销 |

### 15.3 库存变动类型

| 值 | 名称 | 方向 | InventoryLot 变更 | Inventory 汇总变更 |
|---|---|---|---|---|
| 1 | 上料入库 | + | Quantity += | TotalQty +=, AvailableQty += |
| 2 | 备料冻结 | 0 | Status→2, 数量不变 | AvailableQty -=, FrozenQty += |
| 3 | 上线出库 | - | Quantity -= | FrozenQty -=, TotalQty -=（**AvailableQty不变**）|
| 4 | 退料入库 | + | Quantity += | TotalQty +=, AvailableQty += |
| 5 | 盘点调整 | ± | Quantity ±= | TotalQty ±=, AvailableQty ±= |
| 6 | 调拨出 | - | Quantity -= | TotalQty -=, AvailableQty -= |
| 7 | 调拨入 | + | Quantity += | TotalQty +=, AvailableQty += |
| 8 | 撤销 | ± | 反向操作 | 反向操作 |
| 9 | 报废 | - | Quantity -= | TotalQty -=, AvailableQty -= |

### 15.4 库存批次状态

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 可用 | 正常库存，可被备料选中 |
| 2 | 冻结 | 已被备料单冻结，等待上线确认（冻结-出库模型） |
| 3 | 待检 | 等待质检 |
| 4 | 不合格 | 质检不合格 |

### 15.5 库存变动来源

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 上料 | 从部管上料到线边仓 |
| 2 | 备料退回 | 备料剩余退回 |
| 3 | 盘点调整 | 盘点差异调整 |
| 4 | 其他 | 其他来源 |

### 15.6 异常类型

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 数量不足 | 库存不满足备料需求 |
| 2 | 品质异常 | 物料品质问题 |
| 3 | 批次过期 | 批次已过有效期 |
| 4 | MSL超时 | 湿敏元件暴露超时 |
| 5 | 其他 | 其他异常 |

### 15.7 异常严重等级

| 值 | 名称 | 处理时限 |
|---|---|---|
| 1 | 低 | 24小时内 |
| 2 | 中 | 4小时内 |
| 3 | 高 | 2小时内 |

### 15.8 盘点类型

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 循环盘点 | 按计划定期盘点部分库位 |
| 2 | 全盘 | 所有库位盘点 |
| 3 | 抽盘 | 随机抽查部分库位 |

### 15.9 扫码类型

| 值 | 名称 | 说明 |
|---|---|---|
| 1 | 上料 | 上料线边仓 |
| 2 | 备料 | 工单备料 |
| 3 | 上线 | 上线确认 |
| 4 | 退料 | 退料操作 |
| 5 | 盘点 | 盘点扫码 |
| 6 | 调拨 | 库存调拨 |

### 15.10 MSL等级

| 值 | 等级 | 开袋暴露时间 |
|---|---|---|
| 1 | MSL 1 | 无限 |
| 2 | MSL 2 | 无限 |
| 3 | MSL 3 | 168小时 |
| 4 | MSL 4 | 72小时 |
| 5 | MSL 5 | 48小时 |
| 6 | MSL 6 | 24小时 |

### 15.11 部品类型

| 值 | 名称 |
|---|---|
| 1 | 电阻 |
| 2 | 电容 |
| 3 | IC |
| 4 | 连接器 |
| 5 | 其他 |

### 15.12 工单优先级

| 值 | 名称 | 颜色标识 |
|---|---|---|
| 1 | 普通 | 绿色 |
| 2 | 加急 | 橙色 |
| 3 | 特急 | 红色 |

### 15.13 条码规则格式（P2-14）

**Part.BarcodeRule 使用模板字符串格式：**

| 占位符 | 含义 | 示例值 |
|---|---|---|
| `{PartNo}` | 部品编号 | R10K0402 |
| `{BatchNo}` | 批次号 | B20260701 |
| `{LocationCode}` | 库位代码 | A0102 |
| `{Qty}` | 数量 | 1000 |
| `{SupplierCode}` | 供应商代码 | SUP001 |

**分隔符：** 支持 `-`、`/`、`_` 等任意字符作为段分隔符

**解析失败处理：** 返回错误码 40003(条码格式不识别)，PDA提示操作员手动输入部品编号和批次号

### 15.14 幂等键格式（P0-4）

| 字段 | 格式 | 示例 |
|---|---|---|
| DeviceId | PDA设备序列号 | `PDA-001` |
| Timestamp | Unix时间戳(秒) | `1720166400` |
| BarcodeHash | 条码前8位字符的MD5前8位 | `a1b2c3d4` |
| **组合** | `{DeviceId}:{Timestamp}:{BarcodeHash}` | `PDA-001:1720166400:a1b2c3d4` |

---

*文档结束 v3.1 — 基于三轮评审修复P0(1)/P1(9)/P2(5)全部问题：PartId冗余注解、PrepDetail CHECK约束、OnlineConfirm SourceLocationId标注、PartSubstitute SubstituteReason、上料容量校验、替代料自动匹配、FIFO跨库位、批次拆分、离线部品缓存、forceRegenerate参数、备料单重算接口、手动输入兜底增强、Inventory热点优化、数据迁移版本管理、敏感字段脱敏*
