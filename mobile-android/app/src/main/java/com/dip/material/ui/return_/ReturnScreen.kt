package com.dip.material.ui.return_

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.BarcodeTextField
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReturnScreen(onBack: () -> Unit, viewModel: ReturnViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    fun onScanned(code: String) {
        val trimmed = code.trim()
        when (state.step) {
            1 -> viewModel.scanPart(trimmed)
            2 -> viewModel.scanLocation(trimmed)
            3 -> viewModel.scanBag(trimmed)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("退料管理") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState())) {
            // PDA 扫码输入
            if (state.step <= 3) {
                val hint = when (state.step) {
                    1 -> "第1步：扫部品料号(>14位)"
                    2 -> "第2步：扫目标库位"
                    3 -> "第3步：逐件扫料号(>14位)"
                    else -> ""
                }
                BarcodeTextField(
                    onBarcodeScanned = { onScanned(it) },
                    label = hint,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                    clearKey = state.step
                )
            }

            // 步骤指示器
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.Center
            ) {
                listOf("扫料号", "扫库位", "扫退料", "完成").forEachIndexed { i, label ->
                    Surface(
                        shape = MaterialTheme.shapes.small,
                        color = when {
                            state.step == i + 1 -> MaterialTheme.colorScheme.primary
                            state.step > i + 1 -> MaterialTheme.colorScheme.primaryContainer
                            else -> MaterialTheme.colorScheme.surfaceVariant
                        },
                        modifier = Modifier.padding(horizontal = 2.dp)
                    ) {
                        Text("${i + 1}.$label", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp), fontSize = 12.sp)
                    }
                }
            }

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            // 消息
            state.scanMsg?.let { msg ->
                val isOk = msg.contains("已退") || msg.contains("成功") || msg.contains("完成")
                Surface(
                    color = if (isOk) Color(0xFF388E3C) else Color(0xFFD32F2F),
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                ) { Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp) }
            }

            when (state.step) {
                1 -> {
                    Text("第1步：扣动扫码枪扳机扫描部品料号获取库位信息", modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                        style = MaterialTheme.typography.bodyMedium, color = Color.Gray)
                }

                2 -> {
                    // 显示已扫到的部品信息
                    state.scannedPart?.let { part ->
                        Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                            Column(Modifier.padding(12.dp)) {
                                Text("部品: ${part.partNo}", style = MaterialTheme.typography.titleSmall)
                                Text("名称: ${part.partName}")
                                if (state.partLocations.isNotEmpty()) {
                                    Text("当前库存:", style = MaterialTheme.typography.labelSmall)
                                    state.partLocations.forEach { loc ->
                                        Text("  ${loc.locationCode}: 可用${loc.availableQty}  冻结${loc.frozenQty}")
                                    }
                                }
                            }
                        }
                    }
                    Text("第2步：扣动扫码枪扳机扫描目标退料库位", modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                        style = MaterialTheme.typography.bodyMedium, color = Color.Gray)
                    TextButton(onClick = { viewModel.reset() }, modifier = Modifier.padding(horizontal = 16.dp)) { Text("重新开始") }
                }

                3 -> {
                    // 部品 + 库位摘要
                    state.scannedPart?.let { part ->
                        state.scannedLocation?.let { loc ->
                            Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                                Column(Modifier.padding(12.dp)) {
                                    Text("部品: ${part.partNo} / ${part.partName}", style = MaterialTheme.typography.titleSmall)
                                    Text("退料库位: ${loc.locationCode}")
                                }
                            }
                        }
                    }

                    Text("第3步：逐件扫退料料号", modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.bodyMedium)

                    // 已扫件数
                    val cnt = state.scannedCounts[state.scannedPart?.id ?: -1] ?: 0
                    Surface(
                        shape = MaterialTheme.shapes.medium,
                        color = MaterialTheme.colorScheme.secondaryContainer,
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) {
                        Text("已退 $cnt 件", modifier = Modifier.padding(horizontal = 20.dp, vertical = 14.dp),
                            style = MaterialTheme.typography.headlineSmall)
                    }

                    Spacer(Modifier.height(12.dp))
                    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Button(onClick = { viewModel.finish() }, enabled = cnt > 0, modifier = Modifier.weight(1f)) {
                            Text("退料完成")
                        }
                        OutlinedButton(onClick = { viewModel.reset() }, modifier = Modifier.weight(1f)) {
                            Text("重新开始")
                        }
                    }
                }

                4 -> {
                    // 已完成确认
                    Text("退料完成！", modifier = Modifier.padding(16.dp),
                        style = MaterialTheme.typography.headlineMedium, color = Color(0xFF4CAF50))
                    Button(onClick = { viewModel.reset() }, modifier = Modifier.padding(horizontal = 16.dp)) {
                        Text("继续退料")
                    }
                }
            }
        }
    }
}
