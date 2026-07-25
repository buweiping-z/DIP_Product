package com.dip.material.ui.prep

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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.BarcodeTextField
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrepScreen(onBack: () -> Unit, viewModel: PrepViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    // PDA 扫码输入由 BarcodeTextField 自管理

    // 全部完成后不自动关闭，等用户手动退出时再跑完成流程
    LaunchedEffect(state.allDone) {
        if (state.allDone) {
            // 不做任何自动操作，用户自己关闭
        }
    }

    // 扫码结果音效
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text(if (state.selectedOrder != null) "备料扫描" else "备料管理") },
            navigationIcon = { IconButton(onClick = {
                if (state.selectedOrder != null) viewModel.clearSelection() else onBack()
            }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            if (state.selectedOrder != null) {
                // PDA 扫码输入区
                BarcodeTextField(
                    onBarcodeScanned = { viewModel.scanItem(it.trim()) },
                    label = "输入料号(>14位)",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let { msg ->
                    val isError = !state.lastScanOk
                    Surface(
                        color = if (isError) Color(0xFFD32F2F) else Color(0xFF388E3C),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) {
                        Text(msg, color = Color.White,
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                    }
                }

                // 备料明细 - 进度计数器固定在列表上方，始终可见
                state.selectedOrder?.let { order ->
                    val totalParts = order.details?.size ?: 0
                    val doneParts = order.details?.count { d ->
                        d.status == 2 || (state.scannedCounts[d.id] ?: 0) > 0
                    } ?: 0
                    Surface(
                        color = MaterialTheme.colorScheme.surfaceVariant,
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                        shape = MaterialTheme.shapes.small
                    ) {
                        Text("已完成料号: $doneParts / $totalParts",
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                            style = MaterialTheme.typography.titleSmall,
                            color = if (doneParts >= totalParts) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)
                    }
                    LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        item { Text("备料单: ${order.orderNo} | 产品: ${order.productName}", style = MaterialTheme.typography.titleSmall) }
                        item { Text("条码须>14位，扣动扫码枪扳机逐袋扫描", style = MaterialTheme.typography.bodySmall, color = Color.Gray) }
                        order.details?.let { details ->
                            items(details) { d ->
                                val isDone = d.status == 2
                                val isShort = d.status == 3
                                val scannedQty = state.scannedCounts[d.id] ?: 0
                                val hasProgress = scannedQty > 0 && !isDone
                                Card(Modifier.fillMaxWidth(),
                                    colors = CardDefaults.cardColors(
                                        containerColor = when {
                                            isDone -> MaterialTheme.colorScheme.primaryContainer
                                            isShort -> MaterialTheme.colorScheme.errorContainer
                                            hasProgress -> MaterialTheme.colorScheme.secondaryContainer
                                            else -> MaterialTheme.colorScheme.surface
                                        })) {
                                    Column(Modifier.padding(10.dp)) {
                                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                            Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                            Text(
                                                when {
                                                    isDone -> "✓"
                                                    isShort -> "缺货"
                                                    hasProgress -> "已备$scannedQty"
                                                    else -> "待确认"
                                                },
                                                color = when {
                                                    isDone -> Color(0xFF4CAF50)
                                                    isShort -> MaterialTheme.colorScheme.error
                                                    hasProgress -> Color(0xFF2196F3)
                                                    else -> Color.Gray
                                                },
                                                fontSize = 13.sp
                                            )
                                        }
                                        Text("需求: ${d.requiredQty.toInt()}", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                        d.stocks?.takeIf { it.isNotEmpty() }?.let { stocks ->
                                            Text(stocks.joinToString(" | ") { "${it.locationCode}:${it.availableQty.toInt()}" },
                                                style = MaterialTheme.typography.bodySmall, fontSize = 11.sp)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            } else {
                // 备料单列表
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待备料单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待备料单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order.id) }, Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp)) {
                                if (order.productName.isNotEmpty())
                                    Text(order.productName, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                    Text(order.orderNo, style = MaterialTheme.typography.titleMedium, color = Color.Gray)
                                    Surface(shape = MaterialTheme.shapes.small, color = MaterialTheme.colorScheme.primaryContainer) {
                                        Text("待备料", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp))
                                    }
                                }
                                Text("需求总数: ${order.totalRequiredQty.toInt()}", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                            }
                        }
                    }
                }
            }
        }
    }
}
