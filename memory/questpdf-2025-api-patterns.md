---
name: questpdf-2025-api-patterns
description: QuestPDF 2025.x 版本的正确 API 用法——Table Cell、字体注册、License 设置
metadata:
  type: feedback
---

# QuestPDF 2025.x API 正确用法

**Why:** 2025.x 版本 API 与网上大量旧版教程不同。`TableDescriptor` 和 `TableCellDescriptor` 都不是 `IContainer`，直接传给辅助方法会编译失败。字体 API 也从 `.Font()` 改为 `.FontFamily()`，注册从全局 `FontManager` 改为 `QuestPDF.Drawing.FontManager`。

**How to apply:**
- 表格单元格：`table.Cell().Element(c => Helper(c, text))`，不要 `Helper(table, text)`
- 表头：`table.Header(h => { h.Cell().Element(c => ...); })`
- 字体注册：`QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(path))`
- 字体引用：`.FontFamily("SimHei")` 不是 `.Font("SimHei")`
- License：生成前必须 `QuestPDF.Settings.License = LicenseType.Community;`
- 用 static bool 防止重复注册字体

相关记忆：[[aspnet-imemorycache-registration]]
