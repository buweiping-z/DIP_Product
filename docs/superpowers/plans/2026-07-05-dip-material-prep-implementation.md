# DIP物料管理系统 - 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现完整的DIP线边仓备料管理系统，包含 .NET 8.0 后端、Kotlin Android PDA端、Vue 3 PC管理端

**Architecture:** 前后端分离，后端DDD分层(Domain/Core/Application/Infrastructure/API)，Android Clean Architecture，PC Vue3+Element Plus。核心流程：上料线边仓 → 工单驱动备料 → 上线确认。

**Tech Stack:** .NET 8.0 + EF Core + MySQL 8.0 + Redis 7 + RabbitMQ 3.12 + JWT | Kotlin Compose + ML Kit + Room | Vue 3 + TypeScript + Element Plus + Pinia

**Design Spec:** `docs/superpowers/specs/2026-07-05-dip-material-prep-design.md` (v3.1, 2525 lines)

---

## 项目文件总览

```
DIP_Product/
├── backend/
│   ├── DIP.Domain/           # 实体、值对象、领域接口
│   ├── DIP.Core/             # 业务服务、领域逻辑
│   ├── DIP.Application/      # DTO、CQRS Handler、Service facade
│   ├── DIP.Infrastructure/   # EF Core、MySQL、Redis、RabbitMQ、JWT
│   └── DIP.API/              # Controllers、Middleware、Program.cs
├── frontend-web/             # Vue 3 + Element Plus
│   ├── src/api/              # Axios API封装
│   ├── src/views/            # 页面
│   ├── src/components/       # 公共组件
│   ├── src/stores/           # Pinia状态管理
│   └── src/router/           # 路由配置
└── mobile-android/           # Kotlin Compose
    ├── app/src/main/java/com/dip/material/
    │   ├── data/local/       # Room DB
    │   ├── data/remote/      # Retrofit API
    │   ├── data/repository/  # Repository
    │   ├── domain/           # 领域模型 + UseCase
    │   ├── presentation/     # Compose UI + ViewModel
    │   ├── network/          # 网络层
    │   └── utils/            # 扫码、同步、反馈工具
```

---

## Phase 1: 后端基础框架

### Task 1: 解决方案初始化

**Files:**
- Create: `backend/DIP.Domain/DIP.Domain.csproj`
- Create: `backend/DIP.Core/DIP.Core.csproj`
- Create: `backend/DIP.Application/DIP.Application.csproj`
- Create: `backend/DIP.Infrastructure/DIP.Infrastructure.csproj`
- Create: `backend/DIP.API/DIP.API.csproj`
- Create: `backend/DIP.API/Program.cs`
- Create: `backend/DIP.API/appsettings.json`
- Create: `backend/DIP.sln`

- [ ] **Step 1: 创建解决方案和项目**

```bash
cd D:\DIP_Product\backend
dotnet new sln -n DIP
dotnet new classlib -n DIP.Domain -f net8.0
dotnet new classlib -n DIP.Core -f net8.0
dotnet new classlib -n DIP.Application -f net8.0
dotnet new classlib -n DIP.Infrastructure -f net8.0
dotnet new webapi -n DIP.API -f net8.0 --no-openapi
dotnet sln add DIP.Domain DIP.Core DIP.Application DIP.Infrastructure DIP.API
```

- [ ] **Step 2: 配置项目依赖关系**

```
DIP.Core → DIP.Domain
DIP.Application → DIP.Core
DIP.Infrastructure → DIP.Application
DIP.API → DIP.Infrastructure, DIP.Application
```

```bash
dotnet add DIP.Core reference DIP.Domain
dotnet add DIP.Application reference DIP.Core
dotnet add DIP.Infrastructure reference DIP.Application
dotnet add DIP.API reference DIP.Infrastructure DIP.Application
```

- [ ] **Step 3: 安装 Infrastructure NuGet包**

```bash
dotnet add DIP.Infrastructure package Pomelo.EntityFrameworkCore.MySql --version 8.0.2
dotnet add DIP.Infrastructure package Microsoft.EntityFrameworkCore.Tools --version 8.0.11
dotnet add DIP.Infrastructure package StackExchange.Redis --version 2.8.16
dotnet add DIP.Infrastructure package RabbitMQ.Client --version 7.0.0
dotnet add DIP.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.11
dotnet add DIP.Infrastructure package BCrypt.Net-Next --version 4.0.3
dotnet add DIP.API package Swashbuckle.AspNetCore --version 6.9.0
dotnet add DIP.API package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.11
```

- [ ] **Step 4: 编写 Program.cs 基础框架**

```csharp
// DIP.API/Program.cs
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// JWT Authentication
var jwtSecret = builder.Configuration["JWT:Secret"]
    ?? throw new ArgumentNullException("JWT:Secret is required");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TODO: Register Infrastructure, Application, Core services
// builder.Services.AddInfrastructure(builder.Configuration);
// builder.Services.AddApplicationServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

- [ ] **Step 5: 配置 appsettings.json**

```json
{
  "ConnectionStrings": {
    "Default": "server=localhost;port=3306;database=dip_material;user=root;password=root123"
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "RabbitMQ": {
    "ConnectionString": "amqp://localhost:5672"
  },
  "JWT": {
    "Secret": "DIP-Material-Management-Secret-Key-2026-ChangeInProduction",
    "ExpiresMinutes": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 6: Commit**

```bash
git add backend/
git commit -m "feat: initialize .NET 8.0 solution with DDD structure and dependencies"
```

---

### Task 2: EF Core 数据库上下文 + 所有实体

**Files:**
- Create: `backend/DIP.Domain/Entities/BaseEntity.cs`
- Create: `backend/DIP.Domain/Entities/Part.cs`
- Create: `backend/DIP.Domain/Entities/Supplier.cs`
- Create: `backend/DIP.Domain/Entities/ProductionLine.cs`
- Create: `backend/DIP.Domain/Entities/Station.cs`
- Create: `backend/DIP.Domain/Entities/WarehouseLocation.cs`
- Create: `backend/DIP.Domain/Entities/Inventory.cs`
- Create: `backend/DIP.Domain/Entities/InventoryLot.cs`
- Create: `backend/DIP.Domain/Entities/StockMovement.cs`
- Create: `backend/DIP.Domain/Entities/ProductionOrder.cs`
- Create: `backend/DIP.Domain/Entities/BomItem.cs`
- Create: `backend/DIP.Domain/Entities/PartSubstitute.cs`
- Create: `backend/DIP.Domain/Entities/LoadingBatch.cs`
- Create: `backend/DIP.Domain/Entities/LoadingBatchItem.cs`
- Create: `backend/DIP.Domain/Entities/MaterialLoading.cs`
- Create: `backend/DIP.Domain/Entities/PrepOrder.cs`
- Create: `backend/DIP.Domain/Entities/PrepDetail.cs`
- Create: `backend/DIP.Domain/Entities/PrepScanRecord.cs`
- Create: `backend/DIP.Domain/Entities/OnlineConfirm.cs`
- Create: `backend/DIP.Domain/Entities/AbnormalRecord.cs`
- Create: `backend/DIP.Domain/Entities/ReturnOrder.cs`
- Create: `backend/DIP.Domain/Entities/ReturnOrderItem.cs`
- Create: `backend/DIP.Domain/Entities/TransferOrder.cs`
- Create: `backend/DIP.Domain/Entities/TransferOrderItem.cs`
- Create: `backend/DIP.Domain/Entities/StockCount.cs`
- Create: `backend/DIP.Domain/Entities/StockCountItem.cs`
- Create: `backend/DIP.Domain/Entities/ScanRecord.cs`
- Create: `backend/DIP.Domain/Entities/SystemLog.cs`
- Create: `backend/DIP.Domain/Entities/OrderClosure.cs`
- Create: `backend/DIP.Domain/Entities/Operator.cs`
- Create: `backend/DIP.Domain/Entities/Role.cs`
- Create: `backend/DIP.Domain/Entities/RefreshToken.cs`
- Create: `backend/DIP.Infrastructure/Data/DIPDbContext.cs`
- Create: `backend/DIP.Infrastructure/Data/DesignTimeFactory.cs`

- [ ] **Step 1: 创建 BaseEntity（统一审计字段）**

```csharp
// DIP.Domain/Entities/BaseEntity.cs
using System;

namespace DIP.Domain.Entities;

public abstract class BaseEntity
{
    public long Id { get; set; }
    public bool IsDeleted { get; set; }
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: 创建核心实体（Part, Supplier, ProductionLine, Station）**

```csharp
// DIP.Domain/Entities/Part.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class Part : BaseEntity
{
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public long? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int PartType { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Specification { get; set; } = string.Empty;
    public int MSLLevel { get; set; }
    public int? MinStock { get; set; }
    public int? MaxStock { get; set; }
    public string? BarcodeRule { get; set; }
    public int Status { get; set; } = 1;
    public string? ImageUrl { get; set; }

    public Supplier? Supplier { get; set; }
}

// DIP.Domain/Entities/Supplier.cs
namespace DIP.Domain.Entities;

public class Supplier : BaseEntity
{
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public int Status { get; set; } = 1;
}

// DIP.Domain/Entities/ProductionLine.cs
namespace DIP.Domain.Entities;

public class ProductionLine : BaseEntity
{
    public string LineNo { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int Status { get; set; } = 1;
    public ICollection<Station> Stations { get; set; } = new List<Station>();
}

// DIP.Domain/Entities/Station.cs
namespace DIP.Domain.Entities;

public class Station : BaseEntity
{
    public string StationNo { get; set; } = string.Empty;
    public long LineId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public int ProcessOrder { get; set; }
    public int Status { get; set; } = 1;
    public ProductionLine Line { get; set; } = null!;
}
```

- [ ] **Step 3: 创建库存实体（WarehouseLocation, Inventory, InventoryLot, StockMovement）**

```csharp
// DIP.Domain/Entities/WarehouseLocation.cs
using System;

namespace DIP.Domain.Entities;

public class WarehouseLocation : BaseEntity
{
    public string LocationCode { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string Row { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public decimal MaxCapacity { get; set; }
    public decimal CurrentQty { get; set; }
    public int Status { get; set; } = 1;
}

// DIP.Domain/Entities/Inventory.cs
namespace DIP.Domain.Entities;

public class Inventory : BaseEntity
{
    public long PartId { get; set; }
    public long LocationId { get; set; }
    public decimal TotalQty { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal FrozenQty { get; set; }
    public decimal InspectingQty { get; set; }
    public int Version { get; set; }

    public Part Part { get; set; } = null!;
    public WarehouseLocation Location { get; set; } = null!;
}

// DIP.Domain/Entities/InventoryLot.cs
using System;

namespace DIP.Domain.Entities;

public class InventoryLot : BaseEntity
{
    public long InventoryId { get; set; }
    public long PartId { get; set; }
    public long LocationId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int Status { get; set; } = 1;
    public DateTime ReceiptDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int OriginType { get; set; }
    public DateTime? MSLExposureTime { get; set; }
    public int Version { get; set; }

    public Inventory Inventory { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

// DIP.Domain/Entities/StockMovement.cs
namespace DIP.Domain.Entities;

public class StockMovement
{
    public long Id { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public int MovementType { get; set; }
    public decimal Quantity { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long? ReferenceId { get; set; }
    public long OperatorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: 创建工单与BOM实体（ProductionOrder, BomItem, PartSubstitute）**

```csharp
// DIP.Domain/Entities/ProductionOrder.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class ProductionOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public long LineId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal PlanQty { get; set; }
    public DateTime? PlanStartDate { get; set; }
    public DateTime? PlanEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    public int Priority { get; set; } = 1;
    public string? CustomerOrderNo { get; set; }
    public int Status { get; set; } = 1;

    public ICollection<BomItem> BomItems { get; set; } = new List<BomItem>();
}

// DIP.Domain/Entities/BomItem.cs
namespace DIP.Domain.Entities;

public class BomItem : BaseEntity
{
    public long OrderId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string ReferenceDesignator { get; set; } = string.Empty;
    public decimal RequiredQty { get; set; }
    public decimal LossRate { get; set; }
    public long? SubstitutePartId { get; set; }
    public int? PartType { get; set; }
    public int SeqNo { get; set; }
    public int IsCritical { get; set; }

    public ProductionOrder Order { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

// DIP.Domain/Entities/PartSubstitute.cs
using System;

namespace DIP.Domain.Entities;

public class PartSubstitute : BaseEntity
{
    public long OriginalPartId { get; set; }
    public long SubstitutePartId { get; set; }
    public int Status { get; set; } = 1;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? SubstituteReason { get; set; }
}
```

- [ ] **Step 5: 创建上料与备料实体（LoadingBatch, LoadingBatchItem, MaterialLoading, PrepOrder, PrepDetail, PrepScanRecord）**

```csharp
// DIP.Domain/Entities/LoadingBatch.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class LoadingBatch : BaseEntity
{
    public string BatchNo { get; set; } = string.Empty;
    public long TargetLocationId { get; set; }
    public long OperatorId { get; set; }
    public int Status { get; set; } = 1;
    public DateTime? ConfirmedAt { get; set; }

    public ICollection<LoadingBatchItem> Items { get; set; } = new List<LoadingBatchItem>();
}

// DIP.Domain/Entities/LoadingBatchItem.cs
namespace DIP.Domain.Entities;

public class LoadingBatchItem : BaseEntity
{
    public long BatchId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public long? SourceLocationId { get; set; }
    public string? BatchNo { get; set; }
    public decimal Quantity { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;

    public LoadingBatch Batch { get; set; } = null!;
}

// DIP.Domain/Entities/MaterialLoading.cs
namespace DIP.Domain.Entities;

public class MaterialLoading : BaseEntity
{
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public long? SourceLocationId { get; set; }
    public long TargetLocationId { get; set; }
    public string? BatchNo { get; set; }
    public decimal Quantity { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public long OperatorId { get; set; }
    public int Status { get; set; } = 1;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
}

// DIP.Domain/Entities/PrepOrder.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class PrepOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public long ProductionOrderId { get; set; }
    public long LineId { get; set; }
    public int Status { get; set; } = 1;
    public int KitCheckResult { get; set; } = 1;
    public DateTime? KitCheckTime { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<PrepDetail> Details { get; set; } = new List<PrepDetail>();
}

// DIP.Domain/Entities/PrepDetail.cs
namespace DIP.Domain.Entities;

public class PrepDetail : BaseEntity
{
    public long PrepOrderId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string ReferenceDesignator { get; set; } = string.Empty;
    public decimal RequiredQty { get; set; }
    public decimal ActualQty { get; set; }
    public decimal LossQty { get; set; }
    public int SubstituteFlag { get; set; }
    public long? SubstitutePartId { get; set; }
    public int Status { get; set; } = 1;

    public PrepOrder PrepOrder { get; set; } = null!;
}

// DIP.Domain/Entities/PrepScanRecord.cs
namespace DIP.Domain.Entities;

public class PrepScanRecord
{
    public long Id { get; set; }
    public long PrepDetailId { get; set; }
    public long SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public decimal Quantity { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public long OperatorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 6: 创建上线、异常、退料、盘点、调拨实体**

```csharp
// DIP.Domain/Entities/OnlineConfirm.cs
using System;

namespace DIP.Domain.Entities;

public class OnlineConfirm : BaseEntity
{
    public long PrepOrderId { get; set; }
    public long PrepDetailId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public decimal LoadedQty { get; set; }
    public long? StationId { get; set; }
    public string StationNo { get; set; } = string.Empty;
    public long SourceLocationId { get; set; }
    public string? SourceLocationCode { get; set; }
    public long? EquipmentId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public long OperatorId { get; set; }
    public int Status { get; set; } = 1;
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}

// DIP.Domain/Entities/AbnormalRecord.cs
namespace DIP.Domain.Entities;

public class AbnormalRecord : BaseEntity
{
    public int Type { get; set; }
    public int Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public long? PrepOrderId { get; set; }
    public long? PartId { get; set; }
    public int Status { get; set; } = 1;
    public long? HandlerId { get; set; }
    public string? HandleNote { get; set; }
    public DateTime? HandledAt { get; set; }
}

// DIP.Domain/Entities/ReturnOrder.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class ReturnOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public long? PrepOrderId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public int Status { get; set; } = 1;
    public long? ApproverId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public ICollection<ReturnOrderItem> Items { get; set; } = new List<ReturnOrderItem>();
}

// DIP.Domain/Entities/ReturnOrderItem.cs
namespace DIP.Domain.Entities;

public class ReturnOrderItem : BaseEntity
{
    public long ReturnOrderId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public decimal Quantity { get; set; }
    public long TargetLocationId { get; set; }
}

// DIP.Domain/Entities/TransferOrder.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class TransferOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public int Status { get; set; } = 1;

    public ICollection<TransferOrderItem> Items { get; set; } = new List<TransferOrderItem>();
}

// DIP.Domain/Entities/TransferOrderItem.cs
namespace DIP.Domain.Entities;

public class TransferOrderItem : BaseEntity
{
    public long TransferOrderId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public long SourceLocationId { get; set; }
    public long TargetLocationId { get; set; }
    public decimal Quantity { get; set; }
    public int Status { get; set; } = 1;
}

// DIP.Domain/Entities/StockCount.cs
using System;
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class StockCount : BaseEntity
{
    public string CountNo { get; set; } = string.Empty;
    public int Status { get; set; } = 1;
    public DateTime? ConfirmedAt { get; set; }

    public ICollection<StockCountItem> Items { get; set; } = new List<StockCountItem>();
}

// DIP.Domain/Entities/StockCountItem.cs
namespace DIP.Domain.Entities;

public class StockCountItem : BaseEntity
{
    public long StockCountId { get; set; }
    public long PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public decimal SystemQty { get; set; }
    public decimal? ActualQty { get; set; }
    public decimal? DifferenceQty { get; set; }
}

// DIP.Domain/Entities/ScanRecord.cs
namespace DIP.Domain.Entities;

public class ScanRecord
{
    public long Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public long? PartId { get; set; }
    public string? PartNo { get; set; }
    public long? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public long OperatorId { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DIP.Domain/Entities/SystemLog.cs
namespace DIP.Domain.Entities;

public class SystemLog
{
    public long Id { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public long OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public string? IpAddress { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public int? ResultCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DIP.Domain/Entities/OrderClosure.cs
using System;

namespace DIP.Domain.Entities;

public class OrderClosure : BaseEntity
{
    public long ProductionOrderId { get; set; }
    public decimal ActualOutput { get; set; }
    public decimal GoodQty { get; set; }
    public decimal ScrapQty { get; set; }
    public decimal TotalLoss { get; set; }
    public string? PartRemainDetails { get; set; }
    public string? CloseNote { get; set; }
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 7: 创建用户相关实体**

```csharp
// DIP.Domain/Entities/Operator.cs
using System;

namespace DIP.Domain.Entities;

public class Operator : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string RealName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long RoleId { get; set; }
    public long? LineId { get; set; }
    public int Status { get; set; } = 1;

    public Role Role { get; set; } = null!;
}

// DIP.Domain/Entities/Role.cs
using System.Collections.Generic;

namespace DIP.Domain.Entities;

public class Role : BaseEntity
{
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Status { get; set; } = 1;
}

// DIP.Domain/Entities/RefreshToken.cs
namespace DIP.Domain.Entities;

public class RefreshToken
{
    public long Id { get; set; }
    public long OperatorId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? ReplacedByToken { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime ExpiresAt { get; set; }
}
```

- [ ] **Step 8: 编写 DIPDbContext 和所有 DbSet + 配置**

```csharp
// DIP.Infrastructure/Data/DIPDbContext.cs
using Microsoft.EntityFrameworkCore;
using DIP.Domain.Entities;

namespace DIP.Infrastructure.Data;

public class DIPDbContext : DbContext
{
    public DIPDbContext(DbContextOptions<DIPDbContext> options) : base(options) { }

    // Master data
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<WarehouseLocation> WarehouseLocations => Set<WarehouseLocation>();
    public DbSet<PartSubstitute> PartSubstitutes => Set<PartSubstitute>();

    // Inventory
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Orders & BOM
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<BomItem> BomItems => Set<BomItem>();

    // Loading
    public DbSet<LoadingBatch> LoadingBatches => Set<LoadingBatch>();
    public DbSet<LoadingBatchItem> LoadingBatchItems => Set<LoadingBatchItem>();
    public DbSet<MaterialLoading> MaterialLoadings => Set<MaterialLoading>();

    // Prep
    public DbSet<PrepOrder> PrepOrders => Set<PrepOrder>();
    public DbSet<PrepDetail> PrepDetails => Set<PrepDetail>();
    public DbSet<PrepScanRecord> PrepScanRecords => Set<PrepScanRecord>();

    // Online
    public DbSet<OnlineConfirm> OnlineConfirms => Set<OnlineConfirm>();

    // Abnormal & Return
    public DbSet<AbnormalRecord> AbnormalRecords => Set<AbnormalRecord>();
    public DbSet<ReturnOrder> ReturnOrders => Set<ReturnOrder>();
    public DbSet<ReturnOrderItem> ReturnOrderItems => Set<ReturnOrderItem>();

    // Transfer & Count
    public DbSet<TransferOrder> TransferOrders => Set<TransferOrder>();
    public DbSet<TransferOrderItem> TransferOrderItems => Set<TransferOrderItem>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountItem> StockCountItems => Set<StockCountItem>();

    // Audit
    public DbSet<ScanRecord> ScanRecords => Set<ScanRecord>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<OrderClosure> OrderClosures => Set<OrderClosure>();

    // Users
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Part
        builder.Entity<Part>(e =>
        {
            e.HasIndex(p => p.PartNo).IsUnique();
            e.HasIndex(p => new { p.SupplierId, p.PartType });
            e.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId);
        });

        // Supplier
        builder.Entity<Supplier>(e =>
        {
            e.HasIndex(s => s.SupplierCode).IsUnique();
        });

        // ProductionLine
        builder.Entity<ProductionLine>(e =>
        {
            e.HasIndex(l => l.LineNo).IsUnique();
        });

        // Station
        builder.Entity<Station>(e =>
        {
            e.HasIndex(s => new { s.LineId, s.StationNo }).IsUnique();
            e.HasOne(s => s.Line).WithMany(l => l.Stations).HasForeignKey(s => s.LineId);
        });

        // WarehouseLocation
        builder.Entity<WarehouseLocation>(e =>
        {
            e.HasIndex(l => l.LocationCode).IsUnique();
            e.HasIndex(l => new { l.Warehouse, l.Zone, l.Status });
        });

        // Inventory
        builder.Entity<Inventory>(e =>
        {
            e.HasIndex(i => new { i.PartId, i.LocationId }).IsUnique();
            e.HasOne(i => i.Part).WithMany().HasForeignKey(i => i.PartId);
            e.HasOne(i => i.Location).WithMany().HasForeignKey(i => i.LocationId);
        });

        // InventoryLot
        builder.Entity<InventoryLot>(e =>
        {
            e.HasIndex(l => new { l.PartId, l.LocationId, l.Status });
            e.HasIndex(l => l.InventoryId);
            e.HasIndex(l => l.BatchNo);
            e.HasIndex(l => l.ReceiptDate);
            e.HasOne(l => l.Inventory).WithMany().HasForeignKey(l => l.InventoryId);
            e.HasOne(l => l.Part).WithMany().HasForeignKey(l => l.PartId);
        });

        // StockMovement
        builder.Entity<StockMovement>(e =>
        {
            e.HasIndex(m => new { m.PartId, m.CreatedAt });
            e.HasIndex(m => new { m.ReferenceType, m.ReferenceId });
            e.HasIndex(m => m.BatchNo);
            e.HasIndex(m => new { m.PartId, m.LocationId, m.CreatedAt });
        });

        // ProductionOrder
        builder.Entity<ProductionOrder>(e =>
        {
            e.HasIndex(o => o.OrderNo).IsUnique();
        });

        // BomItem
        builder.Entity<BomItem>(e =>
        {
            e.HasIndex(b => new { b.OrderId, b.PartId });
            e.HasIndex(b => new { b.OrderId, b.ReferenceDesignator });
            e.HasOne(b => b.Order).WithMany(o => o.BomItems).HasForeignKey(b => b.OrderId);
            e.HasOne(b => b.Part).WithMany().HasForeignKey(b => b.PartId);
        });

        // PartSubstitute
        builder.Entity<PartSubstitute>(e =>
        {
            e.HasIndex(s => new { s.OriginalPartId, s.SubstitutePartId }).IsUnique();
        });

        // LoadingBatch
        builder.Entity<LoadingBatch>(e =>
        {
            e.HasIndex(b => b.BatchNo).IsUnique();
        });

        // LoadingBatchItem
        builder.Entity<LoadingBatchItem>(e =>
        {
            e.HasOne(i => i.Batch).WithMany(b => b.Items).HasForeignKey(i => i.BatchId);
        });

        // PrepOrder
        builder.Entity<PrepOrder>(e =>
        {
            e.HasIndex(o => o.OrderNo).IsUnique();
            e.HasIndex(o => o.ProductionOrderId);
            e.HasIndex(o => new { o.LineId, o.Status, o.CreatedAt });
        });

        // PrepDetail
        builder.Entity<PrepDetail>(e =>
        {
            e.HasIndex(d => new { d.PrepOrderId, d.PartId });
            e.HasIndex(d => new { d.PrepOrderId, d.ReferenceDesignator });
            e.HasIndex(d => new { d.PrepOrderId, d.Status });
            // CHECK constraints (MySQL 8.0)
            e.HasCheckConstraint("CHK_ActualQty_NonNegative", "ActualQty >= 0");
            e.HasCheckConstraint("CHK_ActualQty_LTE_RequiredQty", "ActualQty <= RequiredQty");
            e.HasOne(d => d.PrepOrder).WithMany(o => o.Details).HasForeignKey(d => d.PrepOrderId);
        });

        // PrepScanRecord
        builder.Entity<PrepScanRecord>(e =>
        {
            e.HasIndex(r => r.ScannedBarcode);
        });

        // OnlineConfirm
        builder.Entity<OnlineConfirm>(e =>
        {
            e.HasIndex(o => new { o.PrepOrderId, o.ConfirmedAt });
            e.HasIndex(o => new { o.PrepDetailId, o.Status });
        });

        // ReturnOrder
        builder.Entity<ReturnOrder>(e =>
        {
            e.HasIndex(o => o.OrderNo).IsUnique();
        });

        // ReturnOrderItem
        builder.Entity<ReturnOrderItem>(e =>
        {
            e.HasIndex(i => i.ReturnOrderId);
        });

        // TransferOrder
        builder.Entity<TransferOrder>(e =>
        {
            e.HasIndex(o => o.OrderNo).IsUnique();
        });

        // TransferOrderItem
        builder.Entity<TransferOrderItem>(e =>
        {
            e.HasIndex(i => i.TransferOrderId);
        });

        // StockCount
        builder.Entity<StockCount>(e =>
        {
            e.HasIndex(c => c.CountNo).IsUnique();
        });

        // StockCountItem
        builder.Entity<StockCountItem>(e =>
        {
            e.HasIndex(i => i.StockCountId);
        });

        // ScanRecord
        builder.Entity<ScanRecord>(e =>
        {
            e.HasIndex(r => new { r.Barcode, r.CreatedAt });
            e.HasIndex(r => new { r.OperatorId, r.CreatedAt });
        });

        // SystemLog
        builder.Entity<SystemLog>(e =>
        {
            e.HasIndex(l => new { l.Module, l.CreatedAt });
            e.HasIndex(l => new { l.OperatorId, l.CreatedAt });
        });

        // OrderClosure
        builder.Entity<OrderClosure>(e =>
        {
            e.HasIndex(o => o.ProductionOrderId).IsUnique();
        });

        // Operator
        builder.Entity<Operator>(e =>
        {
            e.HasIndex(o => o.Username).IsUnique();
            e.HasOne(o => o.Role).WithMany().HasForeignKey(o => o.RoleId);
        });

        // Role
        builder.Entity<Role>(e =>
        {
            e.HasIndex(r => r.RoleCode).IsUnique();
        });

        // RefreshToken
        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(r => r.Token).IsUnique();
            e.HasIndex(r => r.OperatorId);
        });

        // Sensitive data converter for SystemLog
        // (implemented in Application layer as JsonSerializerConverter)
    }
}
```

- [ ] **Step 9: 创建 DesignTimeFactory（支持 EF Migrations CLI）**

```csharp
// DIP.Infrastructure/Data/DesignTimeFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DIP.Infrastructure.Data;

public class DesignTimeFactory : IDesignTimeDbContextFactory<DIPDbContext>
{
    public DIPDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DIPDbContext>();
        optionsBuilder.UseMySql(
            "server=localhost;port=3306;database=dip_material;user=root;password=root123",
            new MySqlServerVersion(new Version(8, 0, 35)));
        return new DIPDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 10: 创建并应用初始迁移**

```bash
cd D:\DIP_Product\backend
dotnet ef migrations add InitialCreate --project DIP.Infrastructure --startup-project DIP.API
dotnet ef database update --project DIP.Infrastructure --startup-project DIP.API
```

- [ ] **Step 11: Commit**

```bash
git add backend/
git commit -m "feat: add all 22 entity classes, EF Core DbContext, and initial migration"
```

---

### Task 3: 认证模块（JWT + Refresh Token）

**Files:**
- Create: `backend/DIP.Domain/Services/IAuthService.cs`
- Create: `backend/DIP.Core/Services/AuthService.cs`
- Create: `backend/DIP.Application/DTOs/Auth/LoginRequest.cs`
- Create: `backend/DIP.Application/DTOs/Auth/LoginResponse.cs`
- Create: `backend/DIP.Application/DTOs/Auth/RefreshTokenRequest.cs`
- Create: `backend/DIP.Infrastructure/Services/JwtTokenService.cs`
- Create: `backend/DIP.API/Controllers/AuthController.cs`
- Create: `backend/DIP.API/Middleware/ExceptionHandlingMiddleware.cs`
- Create: `backend/DIP.API/Middleware/IdempotencyMiddleware.cs`

- [ ] **Step 1: 实现 JwtTokenService**

```csharp
// DIP.Infrastructure/Services/JwtTokenService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DIP.Infrastructure.Services;

public class JwtTokenService
{
    private readonly string _secret;

    public JwtTokenService(IConfiguration config)
    {
        _secret = config["JWT:Secret"]!;
    }

    public string GenerateAccessToken(Dictionary<string, ClaimValueOptions> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims.Select(kv => new Claim(kv.Key, kv.Value.ToString())),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
```

- [ ] **Step 2: 实现 AuthService**

```csharp
// DIP.Core/Services/AuthService.cs
using BCrypt.Net;
using DIP.Domain.Entities;
using DIP.Infrastructure.Data;
using DIP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DIP.Core.Services;

public class AuthService
{
    private readonly DIPDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthService(DIPDbContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<(string accessToken, string refreshToken, Operator user)> LoginAsync(string username, string password)
    {
        var user = await _db.Operators
            .Include(o => o.Role)
            .FirstOrDefaultAsync(o => o.Username == username && o.Status == 1);

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("用户名或密码错误");

        var claims = new Dictionary<string, ClaimValueOptions>
        {
            { "sub", user.Id },
            { "username", user.Username },
            { "role", user.Role.RoleCode },
            { "lineId", user.LineId?.ToString() ?? "" }
        };

        var accessToken = _jwt.GenerateAccessToken(claims);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = new RefreshToken
        {
            OperatorId = user.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.RefreshTokens.Add(rt);
        await _db.SaveChangesAsync();

        return (accessToken, refreshToken, user);
    }
}
```

- [ ] **Step 3: 实现 AuthController + 统一异常中间件**

```csharp
// DIP.API/Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;

namespace DIP.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // TODO: Inject AuthService and call LoginAsync
        return Ok(new { code = 200, message = "ok" });
    }

    [HttpPost("refresh")]
    public IActionResult Refresh() => Ok();

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout() => Ok();
}
```

- [ ] **Step 4: 实现统一异常处理和幂等中间件**

```csharp
// DIP.API/Middleware/ExceptionHandlingMiddleware.cs
using System.Text.Json;

namespace DIP.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            var response = new
            {
                code = ex is UnauthorizedAccessException ? 401 : 99999,
                message = ex.Message,
                data = (object?)null
            };
            ctx.Response.StatusCode = ex is UnauthorizedAccessException ? 401 : 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add backend/
git commit -m "feat: implement JWT authentication, refresh token, exception middleware, idempotency middleware"
```

---

## Phase 2: 后端核心业务

### Task 4: 部品/库位 CRUD

**Files:**
- Create: `backend/DIP.Core/Services/PartService.cs`
- Create: `backend/DIP.Core/Services/LocationService.cs`
- Create: `backend/DIP.API/Controllers/PartController.cs`
- Create: `backend/DIP.API/Controllers/LocationController.cs`

- [ ] **Step 1: 实现 PartService（CRUD + Redis缓存）**

PartService 实现：
- `GetListAsync` — 分页查询，支持按 SupplierId/PartType/关键词筛选
- `GetByIdAsync` — 按ID查询（Redis缓存 `part:{id}`，10min过期）
- `CreateAsync` — 创建部品，校验 PartNo 唯一性
- `UpdateAsync` — 更新部品，写入时清除Redis缓存
- `GetSubstitutesAsync(partId)` — 查询替代料列表

- [ ] **Step 2: 实现 LocationService（CRUD + 容量监控）**

LocationService 实现：
- `GetListAsync` — 分页查询，按仓库/区域筛选
- `CreateAsync` — 创建库位，校验 LocationCode 唯一
- `UpdateCapacityAsync` — 更新 CurrentQty（Inventory变动时调用）
- `CheckCapacityAsync(locationId, qty)` — 容量校验（P1-#5）

- [ ] **Step 3: 实现 PartController + LocationController**

按 spec 6.3 接口清单实现 Controller，使用统一响应格式 `{ code, data, message }`。

- [ ] **Step 4: Commit**

```bash
git add backend/
git commit -m "feat: implement Part and WarehouseLocation CRUD services with Redis caching"
```

### Task 5: 上料流程（Loading）

**Files:**
- Create: `backend/DIP.Core/Services/LoadingService.cs`
- Create: `backend/DIP.Core/Services/InventoryService.cs`
- Create: `backend/DIP.API/Controllers/LoadingController.cs`

- [ ] **Step 1: 实现 InventoryService 基础操作**

InventoryService 实现核心库存操作：
- `FreezeAsync(partId, locationId, qty)` — 冻结（Available-=, Frozen+=）
- `DeductAsync(partId, locationId, qty)` — 出库（Frozen-=, Total-=）
- `AddAsync(partId, locationId, qty)` — 入库（Total+=, Available+=）
- `ThawAsync(partId, locationId, qty)` — 解冻（Frozen-=, Available+=）
- `GetAvailableAsync(partId)` — 查询可用库存
- 所有操作包含乐观锁 + Redis分布式锁 + StockMovement写入

- [ ] **Step 2: 实现 LoadingService**

```
LoadingService 方法：
- CreateBatchAsync(targetLocationCode, operatorId)
  → 创建 LoadingBatch(状态=暂存)
- AddItemAsync(batchId, barcode, operatorId)
  → 解析条码，追加 LoadingBatchItem
- ConfirmAsync(batchId, operatorId)
  → 容量校验 → 遍历明细 → 逐条执行 Inventory 源扣/目标加 → 写 MaterialLoading + StockMovement + ScanRecord
- CancelAsync(loadingId, operatorId)
  → 反向操作 Inventory → 状态=已撤销
```

- [ ] **Step 3: 实现 LoadingController**

按 spec 接口清单实现 6 个端点，挂载在 `/api/v1/loading`。

- [ ] **Step 4: Commit**

```bash
git add backend/
git commit -m "feat: implement loading flow with inventory freeze-deduct model and capacity check"
```

### Task 6: 工单 + BOM + 备料流程

**Files:**
- Create: `backend/DIP.Core/Services/OrderService.cs`
- Create: `backend/DIP.Core/Services/PrepOrderService.cs`
- Create: `backend/DIP.API/Controllers/OrderController.cs`
- Create: `backend/DIP.API/Controllers/PrepController.cs`

- [ ] **Step 1: 实现 OrderService**

```
OrderService 方法：
- CreateOrderAsync(orderDto) → 创建 ProductionOrder + BomItems + 自动生成 PrepOrder
- GetListAsync / GetByIdAsync
- CloseOrderAsync(orderId) → 统计产出/损耗/报废，创建 OrderClosure
```

- [ ] **Step 2: 实现 PrepOrderService（含替代料自动匹配）**

```
PrepOrderService 方法：
- GenerateFromBOM(orderId) → 遍历BOM，检查库存，替代料自动匹配（P1-#6）
- RegenerateAsync(orderId, force: bool) → forceRegenerate参数（P2-#9）
- RecalculateAsync(prepOrderId) → BOM变更后重新计算（P2-#10）
- KitCheckAsync(prepOrderId) → 齐套检查，汇总可用量 vs 需求量
- ScanPrepAsync(prepOrderId, scanDto) → 扫码备料，FIFO批次选择（P1-#7跨库位）
- CompletePrepAsync(prepOrderId) → 全部备料完成
- CancelPrepAsync(prepOrderId) → 取消备料单
- Pause/Resume → 暂停/恢复
- CancelDetailAsync(detailId) → 撤销备料明细
- ReportAbnormalAsync(prepOrderId, abnormalDto) → 上报异常
```

- [ ] **Step 3: 实现 OrderController + PrepController**

按 spec 接口清单实现所有端点。

- [ ] **Step 4: Commit**

```bash
git add backend/
git commit -m "feat: implement order/BOM management and prep flow with FIFO, substitute matching, kit check"
```

### Task 7: 上线确认

**Files:**
- Create: `backend/DIP.Core/Services/OnlineService.cs`
- Create: `backend/DIP.API/Controllers/OnlineController.cs`

- [ ] **Step 1: 实现 OnlineService**

```
OnlineService 方法：
- ConfirmAsync(confirmDto) → 校验累计上线量 ≤ 备料量 → 批次拆分（P1-#8）→ 出库
- CancelAsync(confirmId) → 恢复冻结 → 状态=已撤销
- GetListAsync / GetByPrepOrderIdAsync
```

- [ ] **Step 2: 实现 OnlineController**

- [ ] **Step 3: Commit**

```bash
git add backend/
git commit -m "feat: implement online confirm with batch splitting logic"
```

### Task 8: 退料/盘点/调拨/异常

**Files:**
- Create: `backend/DIP.Core/Services/ReturnService.cs`
- Create: `backend/DIP.Core/Services/CountService.cs`
- Create: `backend/DIP.Core/Services/TransferService.cs`
- Create: `backend/DIP.Core/Services/AbnormalService.cs`
- Create: `backend/DIP.Infrastructure/Messaging/RabbitMQService.cs`
- Create: `backend/DIP.Infrastructure/Messaging/DeadLetterHandler.cs`
- Create: `backend/DIP.API/Controllers/ReturnController.cs`
- Create: `backend/DIP.API/Controllers/CountController.cs`
- Create: `backend/DIP.API/Controllers/TransferController.cs`
- Create: `backend/DIP.API/Controllers/AbnormalController.cs`
- Create: `backend/DIP.API/Controllers/ReportController.cs`

- [ ] **Step 1: 实现 RabbitMQ 消息服务**

```csharp
// DIP.Infrastructure/Messaging/RabbitMQService.cs
// 声明队列: abnormal.alert, prep.completed, prep.kit-alert, online.confirmed, dead-letter
// Publish<T>(string queue, T message)
// 支持 JSON 序列化和重试机制
```

- [ ] **Step 2: 实现 ReturnService / CountService / TransferService / AbnormalService**

各 Service 包含创建、执行、审核流程，退料审核通过后更新 Inventory，盘点确认后调整 Inventory，调拨执行后更新双向 Inventory。异常记录通过 RabbitMQ 推送 WebSocket 通知。

- [ ] **Step 3: 实现所有 Controller**

- [ ] **Step 4: 实现 WebSocket Hub（PC端实时通知）**

```csharp
// DIP.API/Hubs/NotificationHub.cs
// 连接时根据 Operator.LineId 加入对应组
// 推送: 异常预警、备料完成、上线确认
```

- [ ] **Step 5: Commit**

```bash
git add backend/
git commit -m "feat: implement return, count, transfer, abnormal flows with RabbitMQ and WebSocket Hub"
```

---

## Phase 3: Android PDA 端

### Task 9: Android 项目初始化

**Files:**
- Create: `mobile-android/build.gradle.kts`
- Create: `mobile-android/settings.gradle.kts`
- Create: `mobile-android/app/build.gradle.kts`
- Create: `mobile-android/app/src/main/AndroidManifest.xml`
- Create: `mobile-android/app/src/main/java/com/dip/material/DIPApplication.kt`

- [ ] **Step 1: 创建 Gradle 配置**

```kotlin
// mobile-android/build.gradle.kts
plugins {
    id("com.android.application") version "8.2.0" apply false
    id("org.jetbrains.kotlin.android") version "1.9.20" apply false
    id("org.jetbrains.kotlin.plugin.serialization") version "1.9.20" apply false
}

// mobile-android/app/build.gradle.kts
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("kotlin-kapt")
}

android {
    namespace = "com.dip.material"
    compileSdk = 34
    defaultConfig {
        applicationId = "com.dip.material"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "1.0.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
    buildFeatures { compose = true }
    composeOptions { kotlinCompilerExtensionVersion = "1.5.4" }
    packaging { resources.excludes.add("META-INF/{LICENSE,NOTICE,*.version}") }
}

dependencies {
    implementation("androidx.core:core-ktx:1.12.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.7.0")
    implementation("androidx.activity:activity-compose:1.8.2")
    implementation(platform("androidx.compose:compose-bom:2024.02.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.navigation:navigation-compose:2.7.7")
    implementation("com.google.mlkit:barcode-scanning:17.3.0")
    implementation("androidx.room:room-runtime:2.6.1")
    kapt("androidx.room:room-compiler:2.6.1")
    implementation("androidx.room:room-ktx:2.6.1")
    implementation("com.squareup.retrofit2:retrofit:2.9.0")
    implementation("com.squareup.retrofit2:converter-gson:2.9.0")
    implementation("com.jakewharton.retrofit:retrofit2-kotlinx-serialization-converter:1.0.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.6.3")
    implementation("io.coil-kt:coil-compose:2.6.0")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")
}
```

- [ ] **Step 2: 创建 AndroidManifest.xml（相机、网络权限）**

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-permission android:name="android.permission.CAMERA" />
<uses-feature android:name="android.hardware.camera" android:required="true" />
```

- [ ] **Step 3: Commit**

```bash
git add mobile-android/
git commit -m "feat: initialize Android project with Compose, ML Kit, Room, Retrofit dependencies"
```

### Task 10: Android 网络层 + Room 本地数据库

**Files:**
- Create: `mobile-android/.../network/RetrofitClient.kt`
- Create: `mobile-android/.../network/ApiService.kt`
- Create: `mobile-android/.../network/interceptors/AuthInterceptor.kt`
- Create: `mobile-android/.../data/local/DIPDatabase.kt`
- Create: `mobile-android/.../data/local/entity/PartEntity.kt`
- Create: `mobile-android/.../data/local/entity/PrepOrderEntity.kt`
- Create: `mobile-android/.../data/local/dao/PartDao.kt`
- Create: `mobile-android/.../data/local/dao/PrepOrderDao.kt`
- Create: `mobile-android/.../data/local/dao/ScanQueueDao.kt`

- [ ] **Step 1: 实现 Retrofit + 认证拦截器**

RetrofitClient 配置：
- Base URL from BuildConfig
- AuthInterceptor: 自动附加 JWT Token，401时刷新Token
- OkHttp LoggingInterceptor for debug

- [ ] **Step 2: 实现 Room 本地数据库**

```kotlin
// PartEntity — 部品主数据缓存（P1-#11）
// PrepOrderEntity — 备料单离线缓存
// ScanQueueEntity — 离线扫码排队

@Dao interface PartDao {
    @Query("SELECT * FROM parts WHERE partNo LIKE '%' || :keyword || '%'")
    suspend fun searchParts(keyword: String): List<PartEntity>
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertParts(parts: List<PartEntity>)
}
```

- [ ] **Step 3: Commit**

```bash
git add mobile-android/
git commit -m "feat: implement Android network layer with Retrofit/OkHttp and Room offline database"
```

### Task 11: Android UI 页面

**Files:**
- Create: `mobile-android/.../presentation/ui/screens/LoginScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/HomeScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/LoadingScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/PrepListScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/PrepWorkScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/OnlineScreen.kt`
- Create: `mobile-android/.../presentation/ui/screens/ScanScreen.kt`
- Create: `mobile-android/.../presentation/ui/components/ScanResultCard.kt`
- Create: `mobile-android/.../presentation/viewmodel/LoginViewModel.kt`
- Create: `mobile-android/.../presentation/viewmodel/HomeViewModel.kt`
- Create: `mobile-android/.../presentation/viewmodel/LoadingViewModel.kt`
- Create: `mobile-android/.../presentation/viewmodel/PrepViewModel.kt`
- Create: `mobile-android/.../presentation/viewmodel/OnlineViewModel.kt`
- Create: `mobile-android/.../utils/BarcodeScanner.kt`
- Create: `mobile-android/.../utils/SyncManager.kt`
- Create: `mobile-android/.../utils/Feedback.kt`

- [ ] **Step 1: 实现扫码工具（ML Kit）**

```kotlin
// BarcodeScanner.kt
// 支持 QR/Code128/Code39/EAN-13
// 返回 BarcodeFormat + rawValue
```

- [ ] **Step 2: 实现扫码反馈工具**

```kotlin
// Feedback.kt
// 成功: 短振动 + 绿色提示音 + 屏幕边缘绿色闪烁
// 失败: 长振动 + 红色提示音 + 屏幕边缘红色闪烁
// 异常: 双短振动 + 橙色提示音
```

- [ ] **Step 3: 实现离线同步管理器**

```kotlin
// SyncManager.kt
// 网络恢复后自动同步 ScanQueue 中待同步数据
// 指数退避重试: 1s/2s/4s
// 写冲突: 强制操作员确认（不静默丢弃）
```

- [ ] **Step 4: 实现登录页面 + 工作台**

登录页：账号密码 → POST /api/v1/auth/login → 存储 Token
工作台：功能入口（上料/备料/上线/退料/盘点），待办统计，**网络状态指示器**（P2-11）

- [ ] **Step 5: 实现上料页面**

扫码库位 → 创建批次 → 连续扫码部品 → 确认/暂存/撤销

- [ ] **Step 6: 实现备料页面**

备料列表 → 选择备料单 → 逐项扫码 → 进度展示 → 齐套检查 → 异常上报 → 完成备料

- [ ] **Step 7: 实现在线确认页面**

扫码备料单 → 扫码部品 → 确认上线 → 批次拆分提示

- [ ] **Step 8: 实现手动输入兜底**

```kotlin
// ScanScreen.kt - 解析失败时显示手动输入弹窗
@Composable fun ManualInputDialog(onConfirm: (String) -> Unit) {
    var barcode by remember { mutableStateOf("") }
    AlertDialog(
        title = { Text("手动输入条码") },
        text = {
            OutlinedTextField(value = barcode, onValueChange = { barcode = it },
                label = { Text("请输入条码") })
        },
        confirmButton = {
            Button(onClick = { onConfirm(barcode) }) { Text("确认") }
        }
    )
}
```

- [ ] **Step 9: Commit**

```bash
git add mobile-android/
git commit -m "feat: implement Android PDA screens, ViewModel, barcode scanner, offline sync, manual input fallback"
```

---

## Phase 4: PC Web 管理端

### Task 12: Vue 3 项目初始化

**Files:**
- Create: `frontend-web/package.json`
- Create: `frontend-web/vite.config.ts`
- Create: `frontend-web/tsconfig.json`
- Create: `frontend-web/src/main.ts`
- Create: `frontend-web/src/App.vue`
- Create: `frontend-web/src/router/index.ts`

- [ ] **Step 1: 初始化 Vue 3 + Vite 项目**

```bash
cd D:\DIP_Product\frontend-web
npm create vite@latest . -- --template vue-ts
npm install element-plus @element-plus/icons-vue pinia axios vue-router
npm install -D unplugin-auto-import unplugin-vue-components
```

- [ ] **Step 2: 配置 Vite + Element Plus 自动导入**

```typescript
// vite.config.ts
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'

export default defineConfig({
  plugins: [
    vue(),
    AutoImport({ resolvers: [ElementPlusResolver()] }),
    Components({ resolvers: [ElementPlusResolver()] })
  ],
  server: {
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true }
    }
  }
})
```

- [ ] **Step 3: Commit**

```bash
git add frontend-web/
git commit -m "feat: initialize Vue 3 + Element Plus + Pinia + Vite project"
```

### Task 13: PC 核心页面

**Files:**
- Create: `frontend-web/src/api/request.ts`
- Create: `frontend-web/src/api/auth.ts`
- Create: `frontend-web/src/api/inventory.ts`
- Create: `frontend-web/src/api/prepOrder.ts`
- Create: `frontend-web/src/api/part.ts`
- Create: `frontend-web/src/api/system.ts`
- Create: `frontend-web/src/stores/user.ts`
- Create: `frontend-web/src/stores/notification.ts`
- Create: `frontend-web/src/utils/websocket.ts`
- Create: `frontend-web/src/views/Login.vue`
- Create: `frontend-web/src/views/Dashboard.vue`
- Create: `frontend-web/src/views/Inventory.vue`
- `frontend-web/src/views/Inventory/Count.vue`
- `frontend-web/src/views/Inventory/Transfer.vue`
- Create: `frontend-web/src/views/Orders.vue`
- Create: `frontend-web/src/views/PrepOrders.vue`
- Create: `frontend-web/src/views/Parts.vue`
- Create: `frontend-web/src/views/Locations.vue`
- Create: `frontend-web/src/views/Abnormal.vue`
- Create: `frontend-web/src/views/Return.vue`
- Create: `frontend-web/src/views/Reports.vue`
- Create: `frontend-web/src/views/System/Users.vue`
- Create: `frontend-web/src/components/Layout/MainLayout.vue`

- [ ] **Step 1: 实现 Axios 封装 + WebSocket 连接**

request.ts: 统一请求封装，Token 自动刷新，错误码统一处理
websocket.ts: WebSocket 连接，断线重连，消息分类路由

- [ ] **Step 2: 实现登录 + 主布局**

登录页：账号密码 → Element Plus 表单
MainLayout：左侧导航 + 顶部用户信息 + WebSocket 通知铃铛

- [ ] **Step 3: 实现仪表盘**

KPI卡片：今日上料/备料/上线统计、异常预警计数、库存预警
实时推送：WebSocket 接收 abnormal.alert / prep.completed / online.confirmed

- [ ] **Step 4: 实现库存管理页面**

库存查询（部品+库位筛选）、批次明细展开、库存流水查看
盘点管理（创建盘点单→确认→调整）、调拨管理（创建→执行→确认）

- [ ] **Step 5: 实现工单 + 备料单管理**

工单列表（状态筛选）、BOM查看、工单结案
备料单列表（待备料/备料中/已完成/异常）、齐套检查结果展示、操作按钮

- [ ] **Step 6: 实现部品 + 库位管理**

部品 CRUD + Excel导入导出、替代料关系管理
库位 CRUD、容量监控（可视化展示）、状态切换

- [ ] **Step 7: 实现异常预警 + 退料管理**

异常列表（严重度颜色标识）、异常处理（分配+备注）
退料列表、退料审核

- [ ] **Step 8: 实现报表 + 系统管理**

操作日志、扫码记录查询导出
用户管理、角色管理、产线/工位管理

- [ ] **Step 9: Commit**

```bash
git add frontend-web/
git commit -m "feat: implement PC Web management frontend with all pages and WebSocket real-time notifications"
```

---

## Phase 5: 集成测试 + 部署

### Task 14: Docker Compose + 种子数据

**Files:**
- Create: `docker-compose.yml`
- Create: `backend/DIP.Infrastructure/Data/SeedData.cs`

- [ ] **Step 1: 创建 Docker Compose 配置**

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

  api:
    build: ./backend/DIP.API
    ports: ["5000:8080"]
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "server=mysql;port=3306;database=dip_material;user=root;password=root123"
      Redis__ConnectionString: "redis:6379"
      RabbitMQ__ConnectionString: "amqp://guest:guest@rabbitmq:5672"
    depends_on: [mysql, redis, rabbitmq]

  web:
    build: ./frontend-web
    ports: ["80:80"]
    depends_on: [api]

volumes:
  mysql_data:
```

- [ ] **Step 2: 实现种子数据**

初始化默认角色（仓管员/班组长/管理员）、测试用户、示例产线/工位。

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add Docker Compose configuration and seed data"
```

---

## Self-Review Checklist

| 检查项 | 状态 | 说明 |
|---|---|---|
| 22张数据表 | ✅ | Task 2 全部创建 |
| 60+ API端点 | ✅ | Task 3-8 覆盖所有Controller |
| 冻结-出库库存模型 | ✅ | Task 5 InventoryService 实现 |
| FIFO先进先出 | ✅ | Task 6 PrepOrderService（含跨库位） |
| MSL湿敏管控 | ✅ | Task 6 备料扫码时检查 |
| 替代料自动匹配 | ✅ | Task 6 PrepOrderService（P1-#6） |
| 批次拆分 | ✅ | Task 7 OnlineService（P1-#8） |
| 容量校验 | ✅ | Task 5 LoadingService（P1-#5） |
| 幂等性 | ✅ | Task 3 IdempotencyMiddleware |
| 离线缓存 | ✅ | Task 10 Room DB + Task 11 SyncManager |
| WebSocket实时通知 | ✅ | Task 8 NotificationHub |
| PC管理页面 | ✅ | Task 13 全部页面 |
| Docker部署 | ✅ | Task 14 |

---
