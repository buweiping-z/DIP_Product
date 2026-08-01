package com.dip.material.utils

import kotlinx.coroutines.flow.MutableSharedFlow

/**
 * 扫码广播总线（单例）。
 *
 * [ScanBroadcastReceiver] 收到扫码枪广播后，把条码通过 [emit] 发射到这里；
 * 当前页面中唯一挂载的 [com.dip.material.ui.components.BarcodeTextField]
 * collect 该流后回调 onBarcodeScanned。
 *
 * 去重：同一条码 300ms 内重复发射会被丢弃。
 * 去重逻辑放在单例而非 BroadcastReceiver 中，因为 Android 每次广播都创建新的
 * BroadcastReceiver 实例，实例字段无法跨调用保留。
 */
object ScanBus {
    val scans = MutableSharedFlow<String>(extraBufferCapacity = 64)

    private var lastCode: String? = null
    private var lastTime: Long = 0L

    fun emit(code: String) {
        val now = System.currentTimeMillis()
        if (code == lastCode && (now - lastTime) < 300) return
        lastCode = code
        lastTime = now
        scans.tryEmit(code)
    }
}
