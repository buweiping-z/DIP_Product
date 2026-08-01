---
name: aspnet-imemorycache-registration
description: ASP.NET Core IMemoryCache 需要显式 AddMemoryCache() 注册，配合 ConcurrentDictionary 跟踪 key 实现批量失效
metadata:
  type: feedback
---

# ASP.NET Core IMemoryCache 注册与批量失效模式

**Why:** IMemoryCache 不是 ASP.NET Core 默认注册的服务，注入时 DI 会报错。另外 IMemoryCache 没有"按前缀清除"的能力，需要自己维护 key 列表才能实现批量失效。

**How to apply:**
- `Program.cs` 加 `builder.Services.AddMemoryCache();`
- 用 `static ConcurrentDictionary<string, byte>` 跟踪所有活跃的 cache key
- 写入缓存时 `_cache.Set(key, value, expiry)` + `_keys.TryAdd(key, 0)`
- 失效时遍历 `_keys` 逐个 `_cache.Remove(key)` + `_keys.TryRemove(key, out _)`
- 适用于数据少变但查询重的场景（如 BOM 产品列表按月份缓存）

相关记忆：[[questpdf-2025-api-patterns]]
