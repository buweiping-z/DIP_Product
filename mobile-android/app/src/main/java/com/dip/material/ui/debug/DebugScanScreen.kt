package com.dip.material.ui.debug

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import com.dip.material.utils.ScanConfig

/**
 * 扫码调试页：监听几乎所有主流 PDA 的广播 Action，扣动扳机后实时显示
 * 设备真实发出的 action 与所有 extras，用于精确定位广播约定（action / extra key）。
 */
@Composable
fun DebugScanScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    var lastAction by remember { mutableStateOf<String?>(null) }
    var lastExtras by remember { mutableStateOf<List<Pair<String, String>>>(emptyList()) }
    var receivedCount by remember { mutableIntStateOf(0) }

    // 宽候选 action：覆盖主流 PDA 各自的默认/常见广播 action
    val candidateActions = remember {
        listOf(
            ScanConfig.ACTION,
            "com.android.server.scannerservice.broadcast",
            "com.seuic.scanner.action.SCAN_RESULT",
            "com.seuic.scanner.broadcast",
            "nlscan.action.SCANNER_RESULT",
            "com.honeywell.decodeintent.action",
            "com.honeywell.scan.broadcast",
            "com.symbol.datawedge.api.RESULT_ACTION",
            "android.intent.action.BARCODE_SCAN_RESULT",
            "com.ubx.scanner.scan",
            "scan.rcv.message",
            "com.urovo.scan.action"
        )
    }

    DisposableEffect(Unit) {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(ctx: Context?, intent: Intent?) {
                if (intent == null) return
                lastAction = intent.action
                val list = mutableListOf<Pair<String, String>>()
                intent.extras?.keySet()?.forEach { k ->
                    val v = intent.extras?.get(k)
                    list.add(k to (v?.toString() ?: "null"))
                }
                lastExtras = list
                receivedCount++
            }
        }
        val filter = IntentFilter().apply {
            candidateActions.forEach { addAction(it) }
            priority = IntentFilter.SYSTEM_HIGH_PRIORITY
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            context.registerReceiver(receiver, filter)
        }
        onDispose { context.unregisterReceiver(receiver) }
    }

    Column(Modifier.fillMaxSize().padding(16.dp).systemBarsPadding()) {
        Text("扫码调试", style = MaterialTheme.typography.titleLarge)
        Spacer(Modifier.height(8.dp))
        Button(onClick = onBack) { Text("返回首页") }

        Spacer(Modifier.height(16.dp))
        Text("收到次数：$receivedCount", style = MaterialTheme.typography.titleMedium)
        Text(
            "最近 Action：${lastAction ?: "（尚未收到，请扣动扫码枪扳机）"}",
            style = MaterialTheme.typography.bodyMedium,
            fontFamily = FontFamily.Monospace
        )

        Spacer(Modifier.height(12.dp))
        Text("最近 Extras：", style = MaterialTheme.typography.titleMedium)
        if (lastExtras.isEmpty()) {
            Text("（空 —— 说明设备没用以上任一 action 广播）", color = MaterialTheme.colorScheme.error)
        } else {
            LazyColumn(Modifier.fillMaxHeight(0.4f)) {
                items(lastExtras) { (k, v) ->
                    Text("  $k = $v", style = MaterialTheme.typography.bodySmall, fontFamily = FontFamily.Monospace)
                }
            }
        }

        Spacer(Modifier.height(16.dp))
        Button(onClick = {
            val cfg = Intent(ScanConfig.SERVICE_SETTINGS_ACTION).apply {
                putExtra(ScanConfig.CONFIG_ACTION_KEY, ScanConfig.ACTION)
            }
            context.sendBroadcast(cfg)
        }) { Text("重新下发配置广播") }

        Spacer(Modifier.height(16.dp))
        Text(
            "用法：进入本页后扣动扫码枪扳机。若上方出现 action 与 extras，把“最近 Action”和 extras 里的 key=value 发我即可精确定位。若始终空白，说明设备未以广播方式输出（检查 PDA 设置是否真的开了 Intent Output 且关闭了键盘楔）。",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.outline
        )
    }
}
