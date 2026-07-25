package com.dip.material.ui.shelving

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.BarcodeTextField
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShelvingScreen(onBack: () -> Unit, viewModel: ShelvingViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    // 扫码音效
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
                title = { Text("上架管理") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // ── PDA 扫码输入 ──
            BarcodeTextField(
                onBarcodeScanned = { onScanned(it) },
                label = "扫条码",
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                clearKey = state.step
            )

            // ── 步骤指示器 ──
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.Center
            ) {
                listOf("扫部品", "扫库位", "扫袋", "数量").forEachIndexed { i, label ->
                    val active = state.step > i || state.step == 4
                    Surface(
                        shape = MaterialTheme.shapes.small,
                        color = if (state.step == i + 1) MaterialTheme.colorScheme.primary
                        else if (active) MaterialTheme.colorScheme.primaryContainer
                        else MaterialTheme.colorScheme.surfaceVariant,
                        modifier = Modifier.padding(horizontal = 2.dp)
                    ) {
                        Text(
                            "${i + 1}.$label",
                            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                            fontSize = 12.sp
                        )
                    }
                }
            }

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            // ── 步骤内容 ──
            Column(
                Modifier.fillMaxSize().padding(horizontal = 16.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                when (state.step) {
                    1 -> {
                        Text("第1步：扫描部品料号", style = MaterialTheme.typography.titleMedium)
                        Text("对准部品条码，扣动 PDA 扫码枪扳机", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                    }

                    2 -> {
                        // 显示已扫到的部品信息
                        state.scannedPart?.let { part ->
                            Card(
                                Modifier.fillMaxWidth(),
                                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
                            ) {
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
                        Text("第2步：扫描目标库位", style = MaterialTheme.typography.titleMedium)
                        Text("对准库位条码，扣动 PDA 扫码枪扳机", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                        TextButton(onClick = { viewModel.reset() }) { Text("重新开始") }
                    }

                    3 -> {
                        // 部品 + 库位摘要
                        state.scannedPart?.let { part ->
                            state.scannedLocation?.let { loc ->
                                Card(
                                    Modifier.fillMaxWidth(),
                                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
                                ) {
                                    Column(Modifier.padding(12.dp)) {
                                        Text("部品: ${part.partNo} / ${part.partName}", style = MaterialTheme.typography.titleSmall)
                                        Text("目标库位: ${loc.locationCode}")
                                    }
                                }
                            }
                        }

                        Text("第3步：逐袋扫码确认", style = MaterialTheme.typography.titleMedium)

                        // 已扫袋数
                        Surface(
                            shape = MaterialTheme.shapes.medium,
                            color = MaterialTheme.colorScheme.secondaryContainer,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Text(
                                "已扫 ${state.bagCount} 袋",
                                modifier = Modifier.padding(horizontal = 20.dp, vertical = 14.dp),
                                style = MaterialTheme.typography.headlineSmall
                            )
                        }

                        Text("每袋扣动一次扫码枪扳机，可多次扫描", style = MaterialTheme.typography.bodySmall, color = Color.Gray)

                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            Button(
                                onClick = { viewModel.finishBags() },
                                enabled = state.bagCount > 0,
                                modifier = Modifier.weight(1f)
                            ) { Text("完成扫描") }

                            OutlinedButton(
                                onClick = { viewModel.reset() },
                                modifier = Modifier.weight(1f)
                            ) { Text("重新开始") }
                        }
                    }

                    4 -> {
                        // 汇总信息
                        state.scannedPart?.let { part ->
                            state.scannedLocation?.let { loc ->
                                Card(
                                    Modifier.fillMaxWidth(),
                                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
                                ) {
                                    Column(Modifier.padding(12.dp)) {
                                        Text("部品: ${part.partNo} / ${part.partName}", style = MaterialTheme.typography.titleSmall)
                                        Text("目标库位: ${loc.locationCode}")
                                        Text("已确认: ${state.bagCount} 袋")
                                    }
                                }
                            }
                        }

                        Text("第4步：输入总数量并上传", style = MaterialTheme.typography.titleMedium)

                        OutlinedTextField(
                            value = state.quantity,
                            onValueChange = { viewModel.setQuantity(it) },
                            label = { Text("总数量") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true
                        )

                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            OutlinedButton(onClick = { viewModel.reset() }, modifier = Modifier.weight(1f)) {
                                Text("取消")
                            }
                            Button(
                                onClick = { viewModel.confirm() },
                                enabled = state.quantity.toDoubleOrNull()?.let { it > 0 } == true && !state.isLoading,
                                modifier = Modifier.weight(1f)
                            ) { Text("确认上架") }
                        }
                    }
                }

                // 结果消息
                state.resultMsg?.let { msg ->
                    Surface(
                        color = if (msg.contains("成功")) Color(0xFF388E3C) else Color(0xFFD32F2F),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(
                            msg, color = Color.White,
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                            fontSize = 14.sp
                        )
                    }
                }
            }
        }
    }
}
