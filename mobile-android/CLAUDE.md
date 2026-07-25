# DIP 物料管理 — Android 手机端

## 技术栈

| 层 | 技术 | 版本 |
|---|------|------|
| 语言 | Kotlin | 2.0.21 |
| UI | Jetpack Compose + Material 3 | BOM 2024.12.01 |
| 导航 | Navigation Compose | 2.8.0 |
| 网络 | Retrofit + OkHttp + Gson | 2.11.0 |
| 扫码 | PDA 广播模式（SEUIC Intent） | — |
| 存储 | DataStore Preferences | 1.1.0 |
| 图片 | Coil Compose | 2.6.0 |

## 构建

```bash
cd mobile-android
gradlew.bat :app:assembleDebug    # Debug APK
gradlew.bat :app:assembleRelease  # Release APK
```

APK 输出路径：`app/build/outputs/apk/debug/app-debug.apk`

## 项目结构（43 文件）

```
app/src/main/java/com/dip/material/
├── DIPApplication.kt            # Application + NetworkCallback WiFi 恢复
├── MainActivity.kt              # 单 Activity + Compose 入口 + 导航
├── data/
│   ├── models/Models.kt         # 所有 DTO 数据类（API 请求/响应）
│   ├── network/
│   │   ├── ApiService.kt        # Retrofit 接口（~35 个端点）
│   │   ├── AuthInterceptor.kt   # JWT 拦截器（纯内存读 Token）
│   │   ├── RetrofitClient.kt    # Retrofit 单例 + WiFi 绑定 + TokenAuthenticator
│   │   └── TokenHolder.kt       # 内存 Token 缓存（启动加载 + 写时同步 DataStore）
│   └── repository/
│       └── AppRepository.kt     # 统一数据访问层
├── ui/
│   ├── login/
│   │   ├── LoginScreen.kt       # 登录页（支持扫码枪扫工牌）
│   │   └── LoginViewModel.kt
│   ├── home/
│   │   ├── HomeScreen.kt        # 仪表盘 + 9 大功能入口
│   │   └── HomeViewModel.kt
│   ├── prep/
│   │   ├── PrepScreen.kt        # 备料：选单 → 扫物料 → 齐套检查
│   │   └── PrepViewModel.kt
│   ├── refill/
│   │   ├── RefillScreen.kt      # 补料：扫订单号 → 缺料清单 → 扫料确认
│   │   └── RefillViewModel.kt
│   ├── return_/
│   │   ├── ReturnScreen.kt      # 退料：扫物料 → 选库位 → 确认
│   │   └── ReturnViewModel.kt
│   ├── shelving/
│   │   ├── ShelvingScreen.kt    # 上架：扫部品 → 扫库位 → 确认
│   │   └── ShelvingViewModel.kt
│   ├── substitute/
│   │   ├── SubstituteScreen.kt  # 替代：扫替代料 → 扫目标库位 → 确认
│   │   └── SubstituteViewModel.kt
│   ├── online/
│   │   ├── OnlineScreen.kt      # 上线：扫备料部品 → 确认消耗
│   │   └── OnlineViewModel.kt
│   ├── outbound/
│   │   ├── OutboundScreen.kt    # 出库：扫物料 → 确认出库
│   │   └── OutboundViewModel.kt
│   ├── changeover/
│   │   ├── ChangeoverScreen.kt  # 途中切替：扫订单号 → 批次管理 → 逐袋确认
│   │   └── ChangeoverViewModel.kt
│   ├── components/
│   │   ├── BarcodeTextField.kt  # 扫码输入组件（PDA 广播模式）
│   │   ├── BarcodeAnalyzer.kt   # CameraX 分析器（备用）
│   │   ├── QrCodeScanner.kt     # 相机扫码（备用）
│   │   ├── ScannerOverlay.kt    # 扫码取景框
│   │   ├── ImageUtils.kt        # 图片处理
│   │   └── PcbTuneParams.kt     # PCB 调参
│   └── theme/
│       ├── Color.kt / Theme.kt / Type.kt  # Material 3 主题
└── utils/
    ├── PreferencesManager.kt    # DataStore：Token、用户名、服务器地址
    ├── ScanBroadcastReceiver.kt # SEUIC PDA 扫码广播接收器
    ├── ScanBus.kt               # 扫码事件 SharedFlow 总线
    ├── ScanConfig.kt            # 扫码配置（Extra Key 列表）
    ├── ScanSoundManager.kt      # 扫码成功/失败音效
    └── ImageUtils.kt            # 图片工具
```

## API 端点映射

| 模块 | 端点 | 用途 |
|------|------|------|
| Auth | POST `/auth/login` | 登录 |
| Auth | POST `/auth/refresh` | 刷新 Token |
| Auth | GET `/auth/me` | 当前用户信息 |
| Dashboard | GET `/dashboard/stats` | 仪表盘统计 |
| Parts | GET `/parts` | 部品列表 |
| Locations | GET `/locations` | 库位列表 |
| Inventory | GET `/inventory/available/{partId}` | 部品可用库存 |
| Prep | GET `/prep` | 备料单列表 |
| Prep | GET `/prep/{id}/details` | 备料单详情 |
| Prep | POST `/prep/{id}/scan` | 扫码备料 |
| Prep | POST `/prep/{id}/kit-check` | 齐套检查 |
| Refill | GET `/refill/pending` | 待补料清单 |
| Refill | POST `/refill/scan` | 补料扫码 |
| Return | POST `/return/scan` | 扫码退料 |
| Shelving | POST `/shelving/batch` | 创建上架批次 |
| Shelving | POST `/shelving/batch/{id}/scan` | 上架扫码 |
| Substitute | POST `/inventory/substitute` | 替代移库 |
| Online | POST `/online/confirm` | 上线确认 |
| Outbound | GET `/outbound` | 出库单列表 |
| Outbound | POST `/outbound/scan` | 出库扫码 |
| Changeover | GET `/changeover/bom` | 切替 BOM |
| Changeover | POST `/changeover/scan` | 切替扫码 |

## 9 大功能流程

### 1. 备料
选备料单 → 扫物料条码 → 自动匹配明细 +1 → 齐套检查

### 2. 补料
扫订单号 → 出缺料清单（部品+库位）→ 扫同款部品 → 自动匹配补上

### 3. 退料
扫物料条码 → 选回退库位 → 确认

### 4. 上架
扫部品条码 → 显示库位号 → 扫库位条码 → 自动匹配 → 录入数量 → 确认

### 5. 替代
扫替代料号(>14位) → 扫目标库位(严格匹配) → 自动确认

### 6. 上线
扫备料部品条码 → 确认消耗

### 7. 出库
扫物料条码 → 确认出库

### 8. 途中切替
扫订单号 → 创建批次 → 逐袋扫部品确认 → 全部完成（中途退出保留批次）

### 9. 仪表盘
显示订单/备料/补料/切替统计 + 库存预警

## 服务器配置

- 默认：`http://192.168.5.11:8800/`（可在登录页配置）
- 模拟器：`http://10.0.2.2:8800/`
- Token 存储在 DataStore，401 时 OkHttp Authenticator 自动刷新

## 注意事项

- 编译需要 Android SDK（local.properties: `sdk.dir=D\:\\Android\\Sdk`）
- 编译需要 JDK 17
- Debug 模式使用明文 HTTP（`cleartext` 已在 manifest 配置）
- 扫码主要使用 PDA 广播模式（SEUIC Intent），CameraX 为备用方案
- WiFi 绑定：RetrofitClient 通过 WifiSocketFactory 强制走 WiFi 网络（双网场景）

## 2026-07-22 更新

### WiFi 自动恢复
- `DIPApplication` 注册 `NetworkCallback` 监听 WiFi 恢复 → `RetrofitClient.reset()`
- `RetrofitClient.getApiService()` 加 `networkHandle` 兜底对比

### Token 自动刷新
- `TokenHolder` 内存缓存 Token（启动加载 + 写时同步 DataStore）
- `AuthInterceptor` 纯内存读，无 `runBlocking`
- `TokenAuthenticator`：拦截 401 → 刷新 → 重试

### 途中切替（新功能）
- `ChangeoverScreen` + `ChangeoverViewModel` 批次管理模式
- 扫产品名(前9位)→创建批次→逐袋扫部品确认→全部完成
- 中途退出保留批次，下次进入可续扫
- 首页任务栏显示"切替中"计数

### 扫描模块修复
- `extractBarcode` 只 `trim()` 首尾空白，不全局 `replace`
- 取所有候选 Extra Key 中最长值（保留空格）
- `scanProduct` 产品名取前 9 位（原 10 位）

### 其他修复
- onResume 预热连接（解决待机首次请求 5 秒延迟）
- 上架扫库位仅匹配已有库存库位，不允任意库位
- 补料核对不清 `boxPartNo`，允许连续扫多袋料
- 备料单列表显示产品名

### 2026-07-23/24 — 生连适配 + 扫码流程改造

**补料/切替入口改为订单号扫描：**
- `RefillViewModel.scanProduct` → `scanOrder`：去掉 9 字符截断，直接以订单号查 API
- `ChangeoverViewModel.scanProduct` → `scanOrder`：同上
- `ApiService` 新增 `getChangeoverBomByOrder(orderNo)` 和 `getRefillParts(orderNo=...)`
- BOM 加载改为直接从订单的 `bom_items` / `prep_details` 查，不绕 `product_boms`
- 切替批次产品名从后端返回（`order_products` 拼接），不再用"订单:WO2026..."

**替代移库流程改造：**
- 拆为两步扫描：扫替代料号(>14位) → 扫目标库位(严格匹配) → 自动确认
- 库位不匹配只能重扫，不能退回

**修复：**

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 替代移库提交后崩溃 | `state.selectedOrder!!` Compose 重组 NPE | 判空后提局部 val，去 `!!` |
| 2 | 补料扫 8 月订单返回 7+8 月料 | 后端未传 productionMonth | 直接查 prep_details |
| 3 | 切替扫订单号 BOM 为空 | ProductName 拼接名不匹配 | 直接查 bom_items |
- 删 BarcodeTextField 手动输入兜底 UI
