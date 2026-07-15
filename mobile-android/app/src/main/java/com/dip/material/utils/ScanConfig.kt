package com.dip.material.utils

/**
 * 扫码枪广播模式配置 —— SEUIC 东大集成 Cruise 系列 PDA
 *
 * ⚠️ 设备端前置要求（务必在 PDA 系统设置里开启，否则 App 收不到广播）：
 *   1. 扫描设置 → 输出模式 选 “广播 / Intent Output”
 *   2. 关闭“键盘输出 / 禁止将扫描按键的值传递给应用 (Disable keyboard wedge)”
 *
 * 适配要点（来自东大集成 CRUISE 1 官方二次开发教程）：
 *   - 部分固件不会主动用固定 action 广播，需要 App 启动时发一条“配置广播”
 *     [SERVICE_SETTINGS_ACTION] 把结果 action 设成我们自己的 [ACTION]。
 *   - 不同固件默认 action 不一样，所以这里同时监听多个候选 action 兜底。
 *   - 条码 Extra Key 也按多个候选依次尝试。
 */
object ScanConfig {
    /** 我们自定义的广播 Action（通过 service_settings 配置扫码服务使用它） */
    const val ACTION = "com.dip.material.SCAN_RESULT"

    /** 候选监听 Action：自定义 action + 各常见固件/默认 action */
    val CANDIDATE_ACTIONS = setOf(
        ACTION,
        "com.android.server.scannerservice.broadcast",
        "com.seuic.scanner.action.SCAN_RESULT",
        "com.seuic.scanner.broadcast"
    )

    /** 承载条码的 Extra Key 候选（按顺序取第一个非空值） */
    val EXTRA_KEYS = listOf(
        "scannerdata",   // CRUISE 官方教程默认 key
        "scan_data",
        "SCAN_CODE", "scan_code",
        "SCAN_BARCODE1",
        "barcode_string", "barcode", "data",
        "scanResult", "scanner_data",
        "com.symbol.datawedge.data_string"
    )

    /** SEUIC 扫码服务配置 Action：发送此广播可设定扫描结果广播的 action 名 */
    const val SERVICE_SETTINGS_ACTION = "com.android.scanner.service_settings"

    /** 配置广播里用于指定 action 名的 Extra Key */
    const val CONFIG_ACTION_KEY = "action_barcode_broadcast"
}
