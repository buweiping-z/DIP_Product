package com.dip.material.ui.components

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.dip.material.utils.ScanBus
import kotlinx.coroutines.flow.collect

/**
 * 显示用料号解析（全局统一规则）：≤14位取全部，>14位去末尾4位。
 * 注意：仅用于文本框展示；回调仍传原始条码，长度判断由各 ViewModel 负责。
 */
private fun displayPartNo(code: String): String =
    if (code.length <= 14) code else code.substring(0, code.length - 4)

/**
 * PDA 扫码输入组件 —— 广播模式（替代原键盘楔/焦点模式）。
 *
 * 扫码来源：扫码枪广播 → [com.dip.material.utils.ScanBroadcastReceiver]
 *                       → [com.dip.material.utils.ScanBus]
 *                       → 本组件 collect → 回调 onBarcodeScanned
 *
 * 兜底输入：当 PDA 未配置广播输出（或现场调试）时，可展开手动输入框，
 *          输入后点“确认”触发与扫码完全相同的回调，保证可用性。
 *
 * 显示规则：接收框展示解析后的料号（≤14位取全部，>14位去末尾4位）；
 *          onBarcodeScanned 回调仍传原始条码，长度校验在各 ViewModel。
 *
 * Public API 与旧版兼容：onBarcodeScanned / label / modifier / enabled 含义不变。
 * 新增参数 manualFallback（默认 true）——是否显示手动输入框兜底。
 * 新增参数 clearKey（默认 null）——该值变化时清空接收框与手动输入框，
 *          步骤向导类界面传入 state.step，切换步骤时自动清空上一步残留。
 *
 * @param enabled        为 false 时忽略扫码与手动输入（与旧逻辑一致）
 * @param manualFallback 为 true 时展示“手动输入 + 确认”兜底行
 * @param clearKey       变化时清空文本框（如传 state.step）
 */
@Composable
fun BarcodeTextField(
    onBarcodeScanned: (String) -> Unit,
    label: String = "扫条码",
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    manualFallback: Boolean = true,
    clearKey: Any? = null
) {
    var lastCode by remember { mutableStateOf("") }
    var manualText by remember { mutableStateOf("") }
    // 收集协程常驻，用 rememberUpdatedState 保证回调/开关始终是最新组合的值
    val currentEnabled by rememberUpdatedState(enabled)
    val currentOnScanned by rememberUpdatedState(onBarcodeScanned)

    // 切换步骤/界面（clearKey 变化）时清空上一步残留内容
    LaunchedEffect(clearKey) {
        lastCode = ""
        manualText = ""
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

    Column(modifier = modifier.fillMaxWidth()) {
        // 最近一次扫码的解析结果（只读展示）
        OutlinedTextField(
            value = lastCode,
            onValueChange = { /* 广播模式：结果只读展示，不接受直接编辑 */ },
            label = { Text(label) },
            readOnly = true,
            singleLine = true,
            enabled = enabled,
            modifier = Modifier.fillMaxWidth()
        )

        // 手动输入兜底：广播未配置或调试时使用
        if (manualFallback && enabled) {
            Spacer(Modifier.height(8.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                OutlinedTextField(
                    value = manualText,
                    onValueChange = { manualText = it },
                    label = { Text("手动输入（未配广播时使用）") },
                    singleLine = true,
                    modifier = Modifier.weight(1f)
                )
                Spacer(Modifier.width(8.dp))
                Button(
                    onClick = {
                        val t = manualText.trim()
                        if (t.isNotBlank()) {
                            lastCode = displayPartNo(t)
                            currentOnScanned(t)
                            manualText = ""
                        }
                    }
                ) { Text("确认") }
            }
        }
    }
}
