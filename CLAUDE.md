# CLAUDE.md

> **注意：2026-07-09 后端已从 Python FastAPI 迁移到 C# ASP.NET Core 8.0。**
> 新后端：`dip-system/api/` (C#)，前端：`dip-system/frontend-web/` (React + TypeScript)

本仓库根目录下的 `backend/`、`frontend-web/`、`mobile-android/` 为旧版系统代码，已删除。

## 当前系统

| 层 | 技术 |
|---|------|
| 后端 | C# ASP.NET Core 8.0 + EF Core 8 + Pomelo MySQL |
| 前端 | React 18 + TypeScript + Vite + Tailwind CSS |
| 数据库 | MySQL 8.0 (dip_material) |
| Excel | ClosedXML |

### 启动方式

```bash
# 后端 (端口 8800)
cd dip-system/api && dotnet run

# 前端 (端口 3000)
cd dip-system/frontend-web && npm run dev
```

访问：`http://localhost:3000` | Swagger：`http://localhost:8800/swagger`

### 免安装部署

```bash
cd dip-system/api
dotnet publish -c Release --self-contained -r win-x64
# 复制 publish/ 文件夹到目标电脑，双击 DIP.Api.exe
```

## 避坑经验

### EF Core NoTracking → 写操作静默失败
`AppDbContext` 中 **不能** 设置全局 `QueryTrackingBehavior.NoTracking`，否则所有实体不被追踪，`SaveChangesAsync()` 不生成 UPDATE。写操作若需要可用 `.AsTracking()` 单独指定。

### JSON 序列化必须 snake_case
全局配置 `JsonNamingPolicy.SnakeCaseLower`，因为前端全用 snake_case。

### [FromBody] Dictionary 中的 JsonElement
`Dictionary<string, object?>` 的 value 是 `JsonElement`，不是原生类型。用 `DictHelper` 中的扩展方法提取值。

## 修改履历

### 2026-07-09 — Python FastAPI → C# ASP.NET Core 8.0 迁移

**架构变更：**
- `dip-system/backend/` (Python) → 删除，替换为 `dip-system/api/` (C#)
- 根目录 `backend/` (旧 C# 代码) → 删除
- 前端不变，仅 Vite 代理端口 8400→8800

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 登录后菜单栏一闪白屏 | JSON camelCase/snake_case 不匹配，token 刷新失败 | Program.cs 全局 SnakeCaseLower |
| 2 | 编辑保存数据库不变 | EF Core 全局 NoTracking，ChangeTracker 不追踪 | 移除 AppDbContext 的 NoTracking |
| 3 | 编辑保存提示成功但数据未改 | `[FromBody] Dictionary` 的 value 是 JsonElement | 创建 DictHelper，先用 `is JsonElement` 判断再提取 |
| 4 | 启动报 Table already exists | Migrate() 试图重建旧表 | 改用 EnsureCreated() |

**新增文件：**
- `dip-system/api/` — 48 个源文件
- `Services/DictHelper.cs` — JsonElement 安全提取
- `memory/*.md` × 3 — EF Core / JsonElement / JSON 序列化 经验记忆
- `~/.claude/skills/bug-collect/` — bug 经验收集 Skill

### 2026-07-09 — 上架管理重构 + 补料/上线查询 + 用户管理

**上架管理（直接上架模式）：**
- 取消批次号管理，改为直接上架：扫部品→扫库位→输数量→确认
- 新增 `POST /shelving/direct`、`GET /shelving/records` 端点
- `MaterialShelving` 增加 `part_name` 字段
- 前端 LoadingList.tsx 重写：搜索栏（部品/库位/日期范围）
- 手机端 ShelvingScreen 重写：四步向导

**补料管理：**
- 去批次号列、去数量列
- 新增搜索栏（料号/库位/日期范围）
- 修复 `source_location_code` 始终为空

**上线确认：**
- 新增搜索栏（料号/工位/日期范围）

**用户管理（新增模块）：**
- `UserService` + `UserController`：CRUD + 密码重置 + 修改密码
- 前端 UserList 页面：搜索/新建/编辑/删除
- 侧边栏新增用户管理菜单项，显示当前用户姓名和角色
- Program.cs 启动时种子角色和 admin 账号

**修复的 Bug：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 5 | 新建用户提示成功但未写入 | API 业务错误 HTTP 200，前端不抛异常 | 所有 API 调用检查 `res.code === 0` |
| 6 | 新建用户 role_id 始终为 0 | React setState 异步，闭包读到旧值 | loadRoles 返回数据而非依赖 state |
| 7 | admin 报"仅管理员可操作" | 数据库 role_code 是 ADMIN(大写)，代码比小写 | `StringComparison.OrdinalIgnoreCase` |
| 8 | admin JWT 中 role 为空 | 旧 admin 的 RoleId 指向不存在的角色 | 种子数据幂等，修正已有 RoleId |

**新增避坑经验（memory）：**
- `api-response-code-check.md` — 前端必须检查业务 code
- `react-setstate-closure-trap.md` — React setState 异步闭包
- `seed-data-idempotency.md` — 种子数据幂等修正
