package com.dip.material.ui.components

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import com.dip.material.utils.ScanBus
import kotlinx.coroutines.flow.collect

/**
 * 显示用料号解析（全局统一规则）：≤14位取全部，>14位去末尾4位。
 * 注意：仅用于文本框展示；回调仍传原始条码，长度判断由各 ViewModel 负责。
 */
private fun displayPartNo(code: String): String =
    if (code.length <= 14) code else code.substring(0, code.length - 4)

/**
 * PDA 扫码输入组件 —— 广播模式。
 *
 * 扫码来源：扫码枪广播 → [ScanBroadcastReceiver] → [ScanBus]
 *                       → 本组件 collect → 回调 onBarcodeScanned
 *
 * 显示规则：接收框展示解析后的料号（≤14位取全部，>14位去末尾4位）；
 *          onBarcodeScanned 回调仍传原始条码，长度校验在各 ViewModel。
 *
 * @param enabled  为 false 时忽略扫码（与旧逻辑一致）
 * @param clearKey 变化时清空文本框（如传 state.step）
 */
@Composable
fun BarcodeTextField(
    onBarcodeScanned: (String) -> Unit,
    label: String = "扫条码",
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    clearKey: Any? = null
) {
    var lastCode by remember { mutableStateOf("") }
    // 收集协程常驻，用 rememberUpdatedState 保证回调/开关始终是最新组合的值
    val currentEnabled by rememberUpdatedState(enabled)
    val currentOnScanned by rememberUpdatedState(onBarcodeScanned)

    // 切换步骤/界面（clearKey 变化）时清空上一步残留内容
    LaunchedEffect(clearKey) {
        lastCode = ""
    }

    // 消费扫码总线（广播模式主通道）
    LaunchedEffect(Unit) {
        ScanBus.scans.collect { barcode ->
            if (!currentEnabled) return@collect
            val trimmed = barcode.trim()
            if (trimmed.isNotBlank()) {
                lastCode = displayPartNo(trimmed)
                currentOnScanned(trimmed)
            }
        }
    }

    // 最近一次扫码的解析结果（只读展示）
    OutlinedTextField(
        value = lastCode,
        onValueChange = { /* 结果只读展示，不接受直接编辑 */ },
        label = { Text(label) },
        readOnly = true,
        singleLine = true,
        enabled = enabled,
        modifier = modifier.fillMaxWidth()
    )
}
