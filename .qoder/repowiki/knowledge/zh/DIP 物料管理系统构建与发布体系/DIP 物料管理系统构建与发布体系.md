---
kind: build_system
name: DIP 物料管理系统构建与发布体系
category: build_system
scope:
    - '**'
source_files:
    - dip-system/api/DIP.Api.csproj
    - dip-system/frontend-web/package.json
    - dip-system/frontend-web/vite.config.ts
    - mobile-android/build.gradle.kts
    - mobile-android/app/build.gradle.kts
    - mobile-android/gradle.properties
    - dip-system/docker-compose.yml
    - scripts/init-db.sql
---

## 构建系统概览

该项目采用多语言、多平台的混合构建体系，包含 C# ASP.NET Core 后端、React 前端和 Android PDA 客户端三个独立构建单元，通过 Docker Compose 统一管理依赖服务。

## 后端构建（C# ASP.NET Core 8.0）

**构建工具链：**
- 项目文件：`dip-system/api/DIP.Api.csproj`，使用 .NET SDK Web 模板
- 目标框架：net8.0，启用 Nullable 和 ImplicitUsings
- 包管理：NuGet PackageReference 方式管理依赖
- 关键依赖：EF Core 8.0 + Pomelo MySQL、JWT Bearer、Swashbuckle、ClosedXML、QuestPDF

**构建命令：**
```bash
cd dip-system/api
dotnet build          # 编译
dotnet run            # 开发运行（端口 8800）
dotnet publish --self-contained -r win-x64  # 自包含发布
```

**发布产物：**
- 可执行文件：`DIP.Api.exe` 及依赖 DLL
- 配置文件：`appsettings.json`、`web.config`
- 静态资源：`html/` 目录下的前端页面
- 启动脚本：`启动.cmd`（纯英文避免编码问题）

## 前端构建（React + Vite）

**构建配置：**
- 包管理：`package.json` 定义依赖和脚本
- 构建工具：Vite 5.4.0 + React 插件
- TypeScript 编译：`tsc -b` 增量编译
- 样式处理：PostCSS + Tailwind CSS 3.4.7

**构建脚本：**
```bash
cd dip-system/frontend-web
npm run dev           # 开发模式（端口 3000，代理 /api → 127.0.0.1:8800）
npm run build         # 生产构建（tsc -b && vite build）
npm run preview       # 预览构建产物
```

**开发环境：**
- 本地开发服务器：`http://localhost:3000`
- API 代理：通过 Vite proxy 转发 `/api` 到 `http://127.0.0.1:8800`
- 超时配置：120秒长请求支持

## Android 构建（Kotlin + Gradle）

**构建系统：**
- 构建工具：Gradle 8.5.0 + Kotlin 2.0.21
- 构建脚本：`mobile-android/build.gradle.kts`（根级）+ `app/build.gradle.kts`（应用级）
- 插件：Android Application、Kotlin Android、Compose、KSP、Serialization

**构建配置：**
- 命名空间：`com.dip.material`
- 编译 SDK：35，最小 SDK：21，目标 SDK：35
- Java 版本：17（source/target compatibility）
- Compose BOM：2024.12.01

**构建命令：**
```bash
cd mobile-android
./gradlew :app:assembleDebug      # Debug APK
./gradlew :app:assembleRelease    # Release APK
./gradlew :app:compileDebugKotlin # 仅编译 Kotlin
```

**APK 输出：**
- Debug：`app/build/outputs/apk/debug/app-debug.apk`
- Release：`app/build/outputs/apk/release/app-release.apk`

## 容器化部署

**Docker Compose 编排：**
- MySQL 8.0：端口 3306，数据库 `dip_material`，utf8mb4 字符集
- Redis 7 Alpine：端口 6379，数据持久化
- RabbitMQ 3.12 Management：端口 5672/15672，默认 guest/guest
- 数据卷：mysql_data、redis_data、rabbitmq_data

**环境变量配置：**
```yaml
MYSQL_ROOT_PASSWORD: 172308687
MYSQL_DATABASE: dip_material
MYSQL_CHARSET: utf8mb4
MYSQL_COLLATION: utf8mb4_unicode_ci
RABBITMQ_DEFAULT_USER: guest
RABBITMQ_DEFAULT_PASS: guest
```

## 数据库初始化

**初始化脚本：**`scripts/init-db.sql`
- 预置角色：admin、warehouse_manager、operator、viewer
- 默认管理员：用户名 admin，密码 Admin@123（BCrypt 哈希）
- 示例生产线：SMT-01 生产线及 5 个工位
- 初始库位：A/B 区各 4 个仓库位置

## 构建约束与约定

**版本管理：**
- 前端版本：`package.json` 中 version "2.0.0"
- Android 版本：`versionCode = 3`，`versionName = "2.1"`
- 后端无显式版本号，通过 NuGet 包版本控制

**开发工作流：**
- 后端：`dotnet run` 热重载开发
- 前端：`npm run dev` 热更新 + API 代理
- Android：`gradlew assembleDebug` 快速迭代
- 数据库：EF Core `EnsureCreated()` 自动建表

**跨平台发布：**
- 后端支持 `--self-contained` 发布 Windows x64 可执行文件
- Android 支持 Debug/Release 双通道构建
- 前端构建为静态 HTML/CSS/JS 资源

**IDE 集成：**
- Claude Code 配置了常用构建命令快捷方式
- 支持 dotnet build/run、npm scripts、gradle tasks
- 进程管理：taskkill 清理 dotnet.exe 进程