---
kind: frontend_style
name: Tailwind CSS + React 前端样式体系
category: frontend_style
scope:
    - '**'
source_files:
    - dip-system/frontend-web/tailwind.config.js
    - dip-system/frontend-web/postcss.config.js
    - dip-system/frontend-web/package.json
    - dip-system/frontend-web/src/index.css
    - dip-system/frontend-web/src/App.tsx
    - dip-system/frontend-web/src/pages/Layout.tsx
    - dip-system/frontend-web/src/lib/toast.ts
    - dip-system/frontend-web/src/lib/Pagination.tsx
    - dip-system/frontend-web/src/lib/HelpButton.tsx
---

## 1. 系统与方法论
- 样式框架：Tailwind CSS 3.4（原子化 CSS），通过 PostCSS + Autoprefixer 在 Vite 构建管线中处理。
- 构建工具：Vite 5，React 18 + TypeScript，路由使用 react-router-dom 6，图标库使用 lucide-react。
- 无传统 CSS 文件组织，所有样式以 className 字符串内联于 JSX/TSX 中，遵循 Tailwind 原子类写法。
- 全局样式入口为 src/index.css，仅引入 Tailwind 的 base/components/utilities 三个层级，并设置 body 基础字体栈。

## 2. 关键文件与包
- 配置与构建
  - tailwind.config.js：仅声明 content 扫描路径与空 extend/plugins，未自定义主题色、字号、断点等设计令牌。
  - postcss.config.js：启用 tailwindcss 与 autoprefixer 两个插件。
  - vite.config.ts / package.json：定义 dev/build/preview 脚本，依赖 tailwindcss、autoprefixer、postcss、@vitejs/plugin-react、typescript。
- 全局样式
  - src/index.css：@tailwind 指令 + 全局 body 字体设置。
- 布局与页面
  - src/App.tsx：基于 react-router 的路由表，配合 Suspense + lazy 实现页面级懒加载。
  - src/pages/Layout.tsx：侧边栏+主内容区的双栏布局，使用 Tailwind flex/grid 组合完成响应式结构。
- 通用 UI 片段
  - src/lib/toast.ts：纯 DOM 注入的轻量 Toast 组件，使用 Tailwind 原子类控制颜色、定位、动画。
  - src/lib/Pagination.tsx、src/lib/HelpButton.tsx、src/lib/Barcode.tsx：小型可复用组件，全部以 Tailwind 原子类驱动样式。

## 3. 架构与约定
- 组件粒度：页面级组件位于 src/pages/*，每个 .tsx 文件即一个功能页；公共小部件放在 src/lib/*。
- 样式组织：不拆分独立 .css/.scss 文件，样式完全由 className 字符串表达，依赖 Tailwind 原子类组合。
- 主题策略：未扩展 tailwind.config 的主题（extend 为空），直接使用 Tailwind 默认色板（gray/slate/blue/green/red 等）。
- 图标与视觉：统一使用 lucide-react 提供的 SVG 图标，尺寸通过 size prop 控制。
- 交互反馈：通过 src/lib/toast.ts 提供统一的 showToast(type) 提示，错误/成功/信息分别对应红/绿/蓝背景。
- 路由与加载：App 层集中管理路由，页面按需懒加载，Suspense fallback 统一显示“加载中...”占位。

## 4. 约定与约束
- 样式必须通过 Tailwind 原子类编写，不使用自定义 CSS 类名（除 toast 动态注入的 @keyframes 外）。
- 全局样式仅允许在 index.css 中以 @tailwind 指令引入，禁止新增全局 CSS 规则。
- 主题色、字号、间距等设计令牌未做集中定义，需直接沿用 Tailwind 默认值。
- 组件样式风格：按钮、表格、卡片等均采用 rounded、shadow、border、hover:bg-* 等原子类组合，保持视觉一致性。
- 移动端适配：当前未配置断点或响应式前缀，布局主要依赖 flex 自适应宽度。
- 动画：Toast 动画通过运行时注入 <style> 标签实现，避免额外 CSS 文件。

## 5. 适用边界
- 该样式体系仅覆盖 Web 前端（dip-system/frontend-web），Android PDA 客户端使用独立的 Material3/Compose 样式体系，不在本范畴内。
- 后端 ASP.NET Core 项目不包含前端样式代码，仅提供静态资源托管。