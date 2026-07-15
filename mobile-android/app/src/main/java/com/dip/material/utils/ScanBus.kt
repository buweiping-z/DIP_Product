package com.dip.material.utils

import kotlinx.coroutines.flow.MutableSharedFlow

/**
 * 扫码广播总线。
 *
 * [ScanBroadcastReceiver] 收到扫码枪广播后，把条码发射到这里；
 * 当前页面中唯一挂载的 [com.dip.material.ui.components.BarcodeTextField]
 * collect 该流后回调 onBarcodeScanned。
 *
 * 用 SharedFlow + extraBufferCapacity，确保没有页面在 collect 时扫码结果不丢失，
 * 且同一时刻通常只有一个页面在收集（Compose NavHost 同屏只存在一个 Screen）。
 */
object ScanBus {
    val scans = MutableSharedFlow<String>(extraBufferCapacity = 64)
}
