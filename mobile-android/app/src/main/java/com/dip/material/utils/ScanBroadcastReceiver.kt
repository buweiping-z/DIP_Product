package com.dip.material.utils

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * 扫码枪广播接收器（SEUIC 东大集成 Cruise 广播模式）。
 *
 * 在 [com.dip.material.MainActivity] 中动态注册，监听 [ScanConfig.CANDIDATE_ACTIONS]
 * 中的任一 action。收到广播后从 Intent 的 Extra 中提取条码，通过 [ScanBus.emit] 发射。
 *
 * 去重由 [ScanBus.emit] 统一处理（单例，状态跨 BroadcastReceiver 实例保留）。
 */
class ScanBroadcastReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context?, intent: Intent?) {
        if (intent == null || intent.action !in ScanConfig.CANDIDATE_ACTIONS) return
        val code = extractBarcode(intent) ?: return
        ScanBus.emit(code)
    }

    private fun extractBarcode(intent: Intent): String? {
        var best: String? = null
        for (key in ScanConfig.EXTRA_KEYS) {
            val value = intent.getStringExtra(key)
            if (!value.isNullOrBlank()) {
                val cleaned = value.trim()
                if (best == null || cleaned.length > best.length) best = cleaned
            }
        }
        return best
    }
}
