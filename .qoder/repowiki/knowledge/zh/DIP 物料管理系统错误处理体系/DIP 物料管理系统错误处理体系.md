---
kind: error_handling
name: DIP 物料管理系统错误处理体系
category: error_handling
scope:
    - '**'
source_files:
    - dip-system/api/Services/AppException.cs
    - dip-system/api/Controllers/AppExceptionFilter.cs
    - dip-system/api/Models/ApiResponse.cs
    - dip-system/api/Controllers/RequireManagerFilter.cs
    - dip-system/api/Program.cs
---

## 错误处理架构概述

DIP 物料管理系统采用 ASP.NET Core 全局异常过滤器 + 业务异常类 + 统一响应格式的组合模式，实现了从业务层到表现层的完整错误处理链路。

## 核心组件

### 1. 业务异常定义（AppException）
位于 `dip-system/api/Services/AppException.cs`，定义了统一的业务异常类型：
- 继承自 `System.Exception`
- 包含 `Code` 属性用于区分不同业务错误码
- 提供静态工厂方法：`NotFound()`、`Business()`、`Unauthorized()`
- 默认错误码为 400，支持自定义 HTTP 状态码映射

### 2. 全局异常过滤器（AppExceptionFilter）
位于 `dip-system/api/Controllers/AppExceptionFilter.cs`，实现 `IExceptionFilter` 接口：
- **业务异常处理**：将 `AppException` 转换为 HTTP 200 + 业务错误码的响应
- **数据库异常处理**：捕获 `DbUpdateException` 并转换为友好的中文错误消息
  - 重复数据 → "数据已存在，请检查是否重复"
  - 外键约束 → "关联数据不存在，请检查引用"
  - 非空约束 → "必填字段不能为空"
  - 其他 → "数据保存失败，请检查数据格式"
- **未处理异常**：记录日志并返回通用错误消息
- 所有异常响应均使用 HTTP 200 状态码，通过 `ApiResponse.Code` 区分成功与失败

### 3. 统一响应格式（ApiResponse）
位于 `dip-system/api/Models/ApiResponse.cs`：
- 泛型类 `ApiResponse<T>` 包含 `Code`、`Data`、`Message` 三个核心字段
- 提供静态工厂方法 `Ok()` 和 `Fail()` 简化响应构造
- 非泛型 `ApiResponse` 继承自 `ApiResponse<object?>` 便于无数据场景使用

### 4. 权限控制过滤器（RequireManagerFilter）
位于 `dip-system/api/Controllers/RequireManagerFilter.cs`：
- 实现 `IAsyncActionFilter` 对 POST/PUT/DELETE 操作进行登录验证
- 排除 AuthController 的登录/刷新接口
- 未登录时返回 `ApiResponse.Fail(401, "请先登录")`

## 架构设计决策

### 统一 HTTP 状态码策略
系统采用 HTTP 200 + 业务错误码的模式：
- 所有 API 响应都使用 HTTP 200 状态码
- 通过 `ApiResponse.Code` 字段区分业务状态（0=成功，其他=失败）
- 这种设计简化了前端错误处理逻辑

### 异常分类处理
- **业务异常**：由 Service 层抛出 `AppException`，携带明确的业务语义
- **数据异常**：由 EF Core 抛出的 `DbUpdateException` 被转换为用户友好的提示
- **系统异常**：未被捕获的异常记录日志后返回通用错误信息

### 中间件管道集成
在 `Program.cs` 中注册异常过滤器：
```csharp
builder.Services.AddControllers(options => {
    options.Filters.Add<AppExceptionFilter>();
    options.Filters.Add<RequireManagerFilter>();
})
```

## 使用模式

### Service 层错误处理
Service 方法中直接抛出业务异常：
```csharp
if (record == null) throw AppException.NotFound($"异常记录 {recordId} 不存在");
if (qty <= 0) throw AppException.Business("数量必须大于0");
```

### Controller 层响应构造
Controller 方法返回统一响应格式：
```csharp
return Ok(ApiResponse.Ok(await _svc.GetListAsync(...)));
// 或
return Ok(ApiResponse.Fail(401, "用户不存在"));
```

## 前端错误处理
前端通过检查 `ApiResponse.Code` 字段判断请求结果：
- Code = 0：请求成功
- Code ≠ 0：根据具体错误码显示相应提示信息

## 限流错误处理
登录接口使用固定窗口限流器，超过限制时返回：
```json
{"code":429,"data":null,"message":"请求过于频繁，请稍后再试"}
```

## 约束与约定
1. **禁止直接使用 System.Exception**：业务错误应使用 `AppException` 及其工厂方法
2. **统一响应格式**：所有 API 响应必须使用 `ApiResponse` 包装
3. **HTTP 200 原则**：业务错误不改变 HTTP 状态码，仅通过 Code 字段区分
4. **中文错误消息**：面向用户的错误消息使用中文，便于仓库操作员理解
5. **日志记录**：所有未处理异常都会记录详细日志，包含请求路径和异常堆栈