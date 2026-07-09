# DIP 物料管理 — Android 手机端

## 技术栈

| 层 | 技术 | 版本 |
|---|------|------|
| 语言 | Kotlin | 2.0.21 |
| UI | Jetpack Compose | BOM 2024.06.00 |
| 导航 | Navigation Compose | 2.8.0 |
| 网络 | Retrofit + OkHttp + Gson | 2.11.0 |
| DI | Koin | 3.5.6 |
| 扫码 | CameraX + ML Kit Barcode Scanning | 1.3.4 / 17.3.0 |
| 存储 | DataStore Preferences | 1.1.0 |
| 图片 | Coil Compose | 2.6.0 |
| 串行化 | Kotlinx Serialization | 1.7.0 |

## 构建

```bash
cd mobile-android
gradlew.bat :app:assembleDebug    # Debug APK
gradlew.bat :app:assembleRelease  # Release APK
```

APK 输出路径：`app/build/outputs/apk/debug/app-debug.apk`

## 项目结构（22 文件）

```
app/src/main/java/com/dip/material/
├── DIPApplication.kt            # Koin 初始化
├── MainActivity.kt              # 单 Activity + Compose 入口
├── di/
│   └── AppModule.kt             # Koin 模块（注入 Repository + 8 ViewModel）
├── data/
│   ├── models/Models.kt         # 所有 DTO 数据类（API 请求/响应）
│   ├── network/
│   │   ├── ApiService.kt        # Retrofit 接口（18 个端点）
│   │   ├── AuthInterceptor.kt   # JWT 拦截器 + 自动刷新 Token
│   │   └── RetrofitClient.kt    # Retrofit 单例，Base URL: http://10.0.2.2:8400
│   └── repository/
│       └── AppRepository.kt     # 统一数据访问层
├── navigation/
│   └── AppNavigation.kt         # NavHost：Login → Home → 6 功能页
├── ui/
│   ├── screens/
│   │   ├── LoginScreen.kt       # 登录页
│   │   ├── HomeScreen.kt        # 仪表盘 + 6 大快捷入口
│   │   ├── PrepScreen.kt        # 备料：选单 → 扫物料 → 齐套检查
│   │   ├── RefillScreen.kt      # 补料：待补清单 + 补料记录
│   │   ├── ReturnScreen.kt      # 退料：扫物料 → 选库位 → 确认
│   │   ├── ShelvingScreen.kt    # 上架：扫部品 → 扫库位 → 确认
│   │   ├── SubstituteScreen.kt  # 替代：扫替代料 → 扫缺料 → 选库位
│   │   └── OnlineScreen.kt      # 上线：扫备料部品 → 确认消耗
│   ├── viewmodels/
│   │   └── ViewModels.kt        # 8 个 ViewModel（Login/Home/Prep/Refill/Return/Shelving/Substitute/Online）
│   └── theme/
│       ├── Color.kt / Theme.kt / Type.kt  # Material 3 主题（浅色/深色）
└── utils/
    └── PreferencesManager.kt    # DataStore 持久化：Token、用户名、服务器地址
```

## API 端点映射

| 模块 | 端点 | 用途 |
|------|------|------|
| Auth | POST `/auth/login` | 登录 |
| Auth | POST `/auth/refresh` | 刷新 Token |
| Dashboard | GET `/dashboard/stats` | 仪表盘统计 |
| Parts | GET `/parts` | 部品列表 |
| Locations | GET `/locations` | 库位列表 |
| Inventory | GET `/inventory/available/{partId}` | 部品可用库存 |
| Prep | GET `/prep` | 备料单列表 |
| Prep | GET `/prep/{id}/details` | 备料单详情 |
| Prep | POST `/prep/{id}/scan` | 扫码备料 |
| Prep | POST `/prep/{id}/kit-check` | 齐套检查 |
| Prep | GET `/prep/pending` | 待补料清单 |
| Prep | GET `/prep/refills` | 补料记录 |
| Return | POST `/return/scan` | 扫码退料 |
| Return | GET `/return` | 退料记录 |
| Shelving | GET `/shelving/batch` | 上架批次列表 |
| Shelving | POST `/shelving/batch/{id}/confirm` | 确认上架 |
| Substitute | GET/POST `/inventory/substitute` | 替代移库 |
| Online | POST `/online/confirm` | 上线确认 |

## 6 大功能流程

### 1. 备料
选备料单 → 扫物料条码 → 自动匹配明细 +1 → 齐套检查

### 2. 补料
扫缺货部品条码 → 出缺料清单（部品+库位）→ 扫同款部品 → 自动匹配补上
→ 产线上线时扫新+老条码校验

### 3. 退料
扫物料条码 → 选回退库位 → 确认

### 4. 上架
扫部品条码 → 显示库位号 → 扫库位条码 → 自动匹配 → 录入数量 → 确认

### 5. 替代
扫替代料条码 → 扫缺料条码 → 自动匹配 → 扫目标库位 → 确认

### 6. 上线
扫备料部品条码 → 确认消耗

## 服务器配置

- 模拟器默认：`http://10.0.2.2:8400`
- 真机：修改 `RetrofitClient.kt` 中的 `BASE_URL` 为 PC 的局域网 IP
- Token 存储在 DataStore，登录时自动保存，401 时自动刷新

## 注意事项

- 编译需要 Android SDK（local.properties: `sdk.dir=D\:\\Android\\Sdk`）
- 编译需要 JDK 17
- Debug 模式使用明文 HTTP（`cleartext` 已在 manifest 配置）
- 扫码功能需要真机或带摄像头的模拟器
- 替代料移库的部品 ID 暂用条码 hash 映射，后续需对接 `/parts/search` 接口
