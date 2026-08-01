package com.dip.material.ui.changeover

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
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
fun ChangeoverScreen(onBack: () -> Unit, viewModel: ChangeoverViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    // 扫码音效
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    val isActive = state.step == 2

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (isActive) "途中切替" else "途中切替") },
                navigationIcon = {
                    IconButton(onClick = {
                        if (isActive) viewModel.backToBatches()
                        else onBack()
                    }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // PDA 扫码输入区
            BarcodeTextField(
                onBarcodeScanned = { barcode ->
                    val trimmed = barcode.trim()
                    if (isActive) viewModel.scanChangeover(trimmed)
                    else viewModel.scanOrder(trimmed)
                },
                label = if (isActive) "扫部品条码" else "扫订单号条码",
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
            )

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            state.scanMsg?.let { msg ->
                Surface(
                    color = if (state.msgOk) Color(0xFF388E3C) else Color(0xFFD32F2F),
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                ) {
                    Text(msg, color = Color.White,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                }
            }

            if (isActive) {
                // 产品信息
                Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                    Column(Modifier.padding(12.dp)) {
                        Text("产品: ${state.productName}", style = MaterialTheme.typography.titleSmall)
                        Text("扣动扫码枪扳机逐袋扫描", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                    }
                }

                // 进度计数器
                val totalParts = state.bomItems.size
                val doneParts = state.bomItems.count { (state.scannedCounts[it.partNo] ?: 0) > 0 }
                Surface(
                    color = MaterialTheme.colorScheme.surfaceVariant,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                    shape = MaterialTheme.shapes.small
                ) {
                    Text("已确认料号: $doneParts / $totalParts",
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                        style = MaterialTheme.typography.titleSmall,
                        color = if (doneParts >= totalParts) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)
                }

                // BOM 料号核对列表
                LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(state.bomItems) { item ->
                        val scannedQty = state.scannedCounts[item.partNo] ?: 0
                        val isDone = scannedQty > 0
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isDone) MaterialTheme.colorScheme.primaryContainer
                                else MaterialTheme.colorScheme.surface
                            )) {
                            Row(Modifier.padding(10.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(item.partNo, style = MaterialTheme.typography.titleSmall)
                                    if (item.partName.isNotEmpty())
                                        Text(item.partName, style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                    if (isDone)
                                        Text("已确认 $scannedQty", style = MaterialTheme.typography.bodySmall, color = Color(0xFF4CAF50))
                                }
                                Text(if (isDone) "✓" else "待确认",
                                    color = if (isDone) Color(0xFF4CAF50) else Color.Gray, fontSize = 14.sp)
                            }
                        }
                    }
                }

                // 全部完成按钮
                if (state.allDone) {
                    Button(
                        onClick = { viewModel.markComplete(); onBack() },
                        modifier = Modifier.fillMaxWidth().padding(16.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))
                    ) { Text("全部完成，结束切替", color = Color.White) }
                }
            } else {
                // 批次列表
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (state.batches.isNotEmpty()) {
                        item { Text("未完成批次 (${state.batches.size})", style = MaterialTheme.typography.titleMedium) }
                        items(state.batches) { b ->
                            val bn = b["batch_no"] as? String ?: ""
                            val pn = b["product_name"] as? String ?: ""
                            val done = ((b["scanned_count"] as? Double) ?: 0.0).toInt()
                            val total = ((b["bom_count"] as? Double) ?: 0.0).toInt()
                            Card(onClick = { viewModel.selectBatch(bn) }, Modifier.fillMaxWidth()) {
                                Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                    Column(Modifier.weight(1f)) {
                                        Text(pn, style = MaterialTheme.typography.titleMedium)
                                        Text("已确认: $done / $total", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                    }
                                    Surface(shape = MaterialTheme.shapes.small, color = Color(0xFFFFF3CD)) {
                                        Text("进行中", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp), fontSize = 12.sp)
                                    }
                                }
                            }
                        }
                    }
                    item {
                        Box(Modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
                            Text("扫订单号条码 开始新的途中切替", style = MaterialTheme.typography.bodyLarge, color = Color.Gray)
                        }
                    }
                }
            }
        }
    }
}
