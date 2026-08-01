---
name: frontend-instant-dialog-async-load
description: 弹框类交互应先显示再异步加载数据，用 loading 状态禁用依赖数据的控件
metadata:
  type: feedback
---

# 前端弹框先显示后加载模式

**Why:** 用户点击按钮后如果 await 数据加载完才弹框，感知延迟等于网络延迟。改为立刻弹框 + 局部 loading，用户感知延迟接近零（表单结构已可见，其他字段可先操作）。

**How to apply:**
- 点击处理函数改为非 async，先 `setShowDialog(true)` 再发请求
- 加 `productsLoading` 状态，请求期间禁用依赖数据的输入框并显示"加载中..."
- 请求完成后 `.then()` 更新数据，`.finally()` 关闭 loading
- 不依赖加载结果的字段（产线、优先级等）保持可操作
- 适用场景：新建/编辑弹框依赖下拉数据加载的情况

相关记忆：[[react-abortcontroller-mountedref-cleanup]]
