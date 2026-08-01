---
kind: logging_system
name: DIP 物料管理系统日志系统
category: logging_system
scope:
    - '**'
source_files:
    - dip-system/api/Program.cs
    - dip-system/api/appsettings.json
    - dip-system/api/Controllers/AppExceptionFilter.cs
    - dip-system/api/Services/OnlineService.cs
    - dip-system/api/Services/PrepService.cs
---

## 日志系统概述

DIP 物料管理系统采用 ASP.NET Core 内置的 `Microsoft.Extensions.Logging` 框架，结合控制台输出和全局异常过滤器实现基础日志记录。系统未集成第三方日志库（如 Serilog、NLog），主要依赖 .NET 标准日志抽象。

## 核心架构与配置

### 日志框架初始化
- **Program.cs**: 使用 WebApplication.CreateBuilder 构建应用时自动注册默认日志提供者
- **appsettings.json**: 通过 `Logging.LogLevel` 配置日志级别过滤：
  - Default: Information
  - Microsoft.AspNetCore: Warning

### 日志注入模式
- 通过构造函数注入 `ILogger<T>` 实例到各个服务类
- 示例：`OrderService`、`AppExceptionFilter` 等类均通过依赖注入获取 ILogger

## 日志输出方式

### 结构化日志（推荐）
- 使用 `_logger.LogError(exception, "消息模板", 参数)` 格式记录异常和结构化信息
- 在 `AppExceptionFilter` 中统一记录数据库异常和未处理异常

### 控制台调试日志（现状）
- 大量使用 `Console.WriteLine()` 进行调试输出
- 常见于业务逻辑关键路径，如 `OnlineService`、`PrepService` 中的操作跟踪
- 格式：`[模块名] 描述性信息`，便于快速定位问题

## 异常处理与日志记录

### 全局异常过滤器
`AppExceptionFilter` 实现了统一的异常捕获和日志记录：
- 业务异常（AppException）：返回 HTTP 200 + 业务错误码
- 数据库异常（DbUpdateException）：转换为友好提示并记录错误日志
- 未处理异常：记录完整堆栈并返回通用错误消息

## 日志级别策略

| 级别 | 使用场景 | 示例 |
|------|----------|------|
| Error | 系统异常、数据库错误、未处理异常 | `_logger.LogError(ex, "数据库更新异常")` |
| Information | 重要业务流程状态变更 | Console.WriteLine 调试输出 |
| Warning | ASP.NET Core 框架警告 | 由框架自动记录 |
| Debug/Trace | 开发调试细节 | 未发现使用 |

## 约束与限制

1. **无集中式日志收集**：日志直接输出到控制台，未配置文件、数据库或远程日志收集器
2. **混合使用模式**：同时存在结构化的 ILogger 调用和原始的 Console.WriteLine，缺乏统一规范
3. **环境区分有限**：仅通过 appsettings.json 的 LogLevel 控制，未根据 Environment 动态调整
4. **缺少日志聚合**：各服务的日志分散输出，未实现请求追踪 ID 或上下文关联

## 改进建议

当前日志系统适合小型项目，但生产环境建议：
- 引入结构化日志库（Serilog）实现 JSON 格式输出
- 添加请求追踪 ID 以便跨服务关联日志
- 配置多目标输出（控制台、文件、事件查看器）
- 建立统一的日志级别规范和最佳实践