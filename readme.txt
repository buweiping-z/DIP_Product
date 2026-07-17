DIP 重架构设计文档审查报告
我逐节仔细审阅了全文档，发现 若干矛盾、遗漏和技术细节问题，按严重程度分类如下。

🔴 P0 — 文档自相矛盾（必须修正）
1. Dashboard API 路径修复方向自相矛盾
位置
内容
§3.3 规则	备料路径统一为 /api/v1/prep（单数），"前端从 /preps 改为 /prep"
§6.8 修复清单	Dashboard.vue 修复为 /prep → /preps（改成复数了！）

问题： §3.3 说标准是单数 /prep，§6.8 却说修复方向是改成复数 /preps，完全相反。

建议修正 §6.8 为：

Dashboard.vue 路径修正：当前调用路径与后端不一致，统一改为 /api/v1/prep，使用 getPrepList() 方法调用。

2. API 路径"单数规则"自身不一致
§3.3 明确规则："路径用单数名词，不加 s"

模块
文档路径
是否符合规则
备料	/api/v1/prep	✅ 单数
工单	/api/v1/orders	❌ 复数，应为 /order
库存	/api/v1/inventory	✅ 不可数名词
部品	/api/v1/parts	❌ 复数，应为 /part
上料	/api/v1/loading	✅
上线	/api/v1/online	✅
退料	/api/v1/return	⚠️ 见下方第3点
盘点	/api/v1/count	⚠️ 见下方第3点
调拨	/api/v1/transfer	✅

建议： 将 orders → /order、parts → /part，或者在规则中注明"已有复数路径保持不变"，二选一，不能矛盾。

3. return 和 count 作为 API 路径段有保留字风险
return 是 C#、Kotlin、JavaScript 的保留关键字
count 是 SQL 聚合函数名
在路由属性中可以用字符串绕过，但在自动代码生成（Swagger → Kotlin data class / TS types）时可能产生命名冲突。

建议： 考虑使用 /api/v1/material-return 和 /api/v1/stock-count，或在文档中明确说明风险已评估。

🟠 P1 — 技术方案有遗漏或不完整
4. MySQL 乐观锁修复不完整
§4.2 的修复方案：

csharp

builder.Entity<Inventory>()
    .Property(i => i.Version)
    .IsConcurrencyToken();
问题： IsConcurrencyToken() 只会在 UPDATE 时加 WHERE Version = @originalVersion，但 不会自动递增 Version。IsRowVersion() 才会自动递增。

文档注释写的 Version = Version + 1 是误导——EF Core 用 IsConcurrencyToken() 时 不会 生成这个 SET 子句。

必须补充： 每次 SaveChangesAsync 前手动递增：

csharp

entity.Version++;
await _db.SaveChangesAsync();
或者在 SaveChangesInterceptor 中统一拦截处理。

5. 多租户写入操作"零改动"的说法不成立
§5.6 改动影响范围表：

层
改动内容
工作量
各业务 Service	零改动	—

问题： HasQueryFilter 只解决了 读取隔离。新建实体时 TenantId 不会自动赋值，默认为 0，会导致数据写入错误的租户（或被查询过滤器过滤掉后查不出来）。

必须补充以下方案之一：

SaveChangesInterceptor — 在 SavingChanges 事件中，对新增的 BaseEntity 自动设置 TenantId = _tenantProvider.CurrentTenantId
或在 BaseEntity 构造时注入（不推荐，实体不应依赖 DI）
文档应新增此拦截器到改动清单中。

6. FreezeAsync 去除 SaveChanges 的方案未明确
§4.3 代码示例：

csharp

await _inventory.FreezeAsync(partId, locationId, qty);  // 不 SaveChanges
问题： 当前 FreezeAsync 内部一定调用了 SaveChangesAsync。说"不 SaveChanges"但没有说明如何实现：

是新增一个 FreezeCoreAsync（不保存）+ 保留 FreezeAsync（保存）？
还是给 FreezeAsync 加 bool autoSave = true 参数？
还是直接改 FreezeAsync 不保存，所有调用方各自处理？
建议： 明确采用方法拆分模式（FreezeAsync 内部调用 FreezeCoreAsync + SaveChanges），编排方法直接调 FreezeCoreAsync。

7. JavaScript vs TypeScript 的根本性矛盾
§2 核心设计决策：

决策
选择
理由
前端语言	JavaScript + .d.ts	项目当前是全 .js，暂不全量迁 TS

但全文所有代码示例和目录结构都是 .ts 文件 + TypeScript 语法：

usePagination.ts、useCrudDialog.ts、useWebSocket.ts
ref<T[]>、reactive<T>、import('@/api/types')
export function usePagination<T>(options: {...})
这不是"JavaScript + .d.ts"，这就是 TypeScript。

建议： 二选一并统一全文：

方案 A： 承认迁移到 TypeScript（composables 和 api 层用 .ts，views 渐进迁移），修改设计决策表述
方案 B： 保持 JavaScript，所有代码示例改为 .js + JSDoc 类型注解
🟡 P2 — 实现细节问题
8. useWebSocket 中 Notification 类型名冲突
typescript

const notifications = ref<Notification[]>([])
Notification 是浏览器全局 API（Web Notifications），直接使用会引用到全局类型而非业务通知类型。

建议： 定义 interface AppNotification { ... } 或从 types.d.ts 导入。

9. useWebSocket 的 SignalR 连接创建位置
typescript

export function useWebSocket() {
  // 在函数体顶层创建连接，不在 onMounted 内
  const connection = new HubConnectionBuilder()...build()
  
  onMounted(async () => { await connection.start() })
}
问题： 连接对象在组合函数调用时立即创建（而非 onMounted 时），如果该组合函数在 setup 之外被调用（如 SSR 或测试环境），会因缺少 window 对象而报错。

建议： 将 connection 创建移入 onMounted 内部。

10. useCrudDialog 的 Object.assign 浅拷贝风险
typescript

const open = (row?: T & { id?: number }) => {
  if (row?.id) {
    Object.assign(formData, row)  // 浅拷贝
  }
}
问题： 如果 row 含嵌套对象（如 { part: { id: 1, name: '...' } }），Object.assign 只复制引用。编辑表单时修改嵌套属性会 直接污染源数据。

建议： 使用深拷贝 structuredClone(row) 或 JSON.parse(JSON.stringify(row))。

11. usePagination 的 onMounted(fetchData) 无法关闭
typescript

onMounted(fetchData)  // 每次挂载自动请求
问题： 某些页面需要等待路由参数或前置数据就绪后才请求（如先加载产线列表再按产线筛选工单），强制 onMounted 请求会导致首次请求浪费或参数缺失。

建议： 增加 options.immediate?: boolean = true，允许关闭自动请求。

12. ApiResponse.Fail<T> 的泛型冗余
csharp

public static ApiResponse<T> Fail<T>(int code, string message) => new(code, default, message);
调用时必须显式指定类型：ApiResult.Fail<PartDto>(1, "error")，对错误响应来说很冗余。

建议： 增加非泛型重载：

csharp

public static ApiResponse<object?> Fail(int code, string message) => new(code, null, message);
13. §3.4 统一错误处理中 HTTP 200 + code≠0 的处理未明确
文档说 "code ≠ 0 → ElMessage.error(message)"，但这发生在 Axios 的 onFulfilled 回调中（HTTP 200），而非 onRejected（HTTP 4xx/5xx）。应明确区分两个拦截器分支：

onFulfilled（HTTP 2xx）：检查 response.data.code，≠0 时弹错误 + reject
onRejected（HTTP 4xx/5xx）：按状态码处理（401 刷新 token，其他弹网络错误）
📋 审查结论
类别
数量
建议
P0 文档矛盾	3	必须修正后才能进入实施
P1 技术遗漏	4	必须在对应 Phase 实施前补全方案
P2 实现细节	6	实施时注意即可，不阻塞

最关键的两个问题：

§3.3 vs §6.8 的 prep 路径方向相反 — 如果开发人员按 §6.8 改成 /preps，Phase 1 完成后 Phase 3 又要改回 /prep，白做一遍
多租户写入拦截器缺失 — 不补这个，新数据全部落到 TenantId = 0，比不做多租户还危险（给人数据隔离的假象）
建议修正上述 P0 和 P1 问题后，文档即可作为实施依据。