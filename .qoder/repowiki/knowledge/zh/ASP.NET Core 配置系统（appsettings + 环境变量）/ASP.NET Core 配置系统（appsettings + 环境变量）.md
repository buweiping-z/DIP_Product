---
kind: configuration_system
name: ASP.NET Core 配置系统（appsettings + 环境变量）
category: configuration_system
scope:
    - '**'
source_files:
    - dip-system/api/appsettings.json
    - dip-system/api/Program.cs
    - dip-system/api/Properties/launchSettings.json
    - dip-system-portable/appsettings.json
    - dip-system/docker-compose.yml
---

本项目采用 ASP.NET Core 内置的 `IConfiguration` 配置系统，通过 `appsettings.json` 文件与运行时环境变量进行配置管理，未引入第三方配置库。

**1. 配置文件来源与加载顺序**
- 启动入口 `Program.cs` 使用 `WebApplication.CreateBuilder(args)` 创建默认构建器，自动加载 `appsettings.json`、`appsettings.{Environment}.json`、环境变量、命令行参数等。
- 数据库连接字符串通过 `builder.Configuration.GetConnectionString("DefaultConnection")` 读取。
- JWT 相关配置通过 `builder.Configuration["Jwt:Secret"]`、`"Jwt:Issuer"]`、`"Jwt:Audience"]` 等键值对直接访问。
- 未在代码中显式调用 `AddJsonFile`、`AddEnvironmentVariables`、`AddCommandLine`，依赖 .NET 默认行为。

**2. 核心配置文件**
- `dip-system/api/appsettings.json`：开发环境主配置，包含数据库连接串、JWT 密钥、日志级别、AllowedHosts。
- `dip-system-portable/appsettings.json`：便携版/发布版配置，内容与开发版一致。
- `dip-system/Properties/launchSettings.json`：VS 调试环境配置，设置 `ASPNETCORE_ENVIRONMENT=Development` 和监听地址 `http://0.0.0.0:8800`。
- `dip-system/docker-compose.yml`：容器化部署时通过环境变量注入 MySQL、Redis、RabbitMQ 的连接信息。

**3. 配置项结构**
- `ConnectionStrings.DefaultConnection`：MySQL 连接串，格式为 `Server=localhost;Database=dip_material;User=root;Password=...`
- `Jwt.Secret` / `Jwt.Issuer` / `Jwt.Audience` / `Jwt.ExpiresMinutes` / `Jwt.RefreshExpireDays`：JWT 认证配置。
- `Logging.LogLevel`：日志级别，默认 Information，Microsoft.AspNetCore 为 Warning。
- `AllowedHosts`：设置为 `*`，允许所有主机访问。

**4. 运行环境与部署约定**
- 开发环境：通过 `launchSettings.json` 设置 `ASPNETCORE_ENVIRONMENT=Development`，启用 Swagger。
- 生产环境：通过环境变量覆盖配置（如数据库连接串、JWT 密钥），`docker-compose.yml` 中通过 `environment` 字段注入服务凭据。
- 固定监听地址：`builder.WebHost.UseUrls("http://0.0.0.0:8800")` 硬编码在 Program.cs 中。
- WebRootPath 设置为 `html`，前端静态资源从该目录提供。

**5. 安全约束**
- JWT Secret 直接写在 appsettings.json 中，注释标注 "ChangeInProduction"，表明生产环境应替换。
- 数据库密码明文存储在配置文件中，未使用用户机密（User Secrets）或外部密钥管理服务。
- CORS 策略仅允许 `localhost:3000` 和 `127.0.0.1:3000` 作为来源。

**6. 缺失的配置机制**
- 未发现 `.env` 文件或环境变量命名规范文档。
- 未发现配置验证（如 `IValidateOptions`）或配置热重载机制。
- 未发现多环境配置文件（如 `appsettings.Production.json`），环境差异完全依赖环境变量覆盖。