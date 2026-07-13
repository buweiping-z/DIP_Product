package com.dip.material.ui.prep

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.QrCodeScanner
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrepScreen(onBack: () -> Unit, viewModel: PrepViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var inputBarcode by remember { mutableStateOf("") }
    var showScanner by remember { mutableStateOf(false) }

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

    // 扫码回调
    fun onScanned(code: String) {
        viewModel.scanItem(code.trim())
        inputBarcode = code.trim()
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
                // 扫码区域
                if (showScanner) {
                    Box(Modifier.fillMaxWidth().fillMaxHeight(0.38f)) {
                        QrCodeScanner(onBarcodeScanned = { onScanned(it) })
                        Row(Modifier.align(Alignment.TopEnd).padding(8.dp)) {
                            Button(onClick = { showScanner = false },
                                colors = ButtonDefaults.buttonColors(containerColor = Color.Red)) { Text("关闭扫码") }
                        }
                    }
                }

                // 手动输入区
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(value = inputBarcode, onValueChange = { inputBarcode = it },
                        label = { Text("输入料号(>14位)") }, modifier = Modifier.weight(1f), singleLine = true)
                    Button(onClick = { if (inputBarcode.isNotBlank()) { viewModel.scanItem(inputBarcode.trim()); inputBarcode = "" } },
                        enabled = inputBarcode.isNotBlank()) { Text("确认") }
                    IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, if (showScanner) "关闭" else "扫码") }
                }

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let { msg ->
                    val isError = msg.contains("未匹配") || msg.contains("不足") || msg.contains("无效")
                    Surface(
                        color = if (isError) Color(0xFFD32F2F) else Color(0xFF388E3C),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) {
                        Text(msg, color = Color.White,
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                    }
                }

                // 备料明细
                state.selectedOrder?.let { order ->
                    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        item { Text("备料单: ${order.orderNo} | 产品: ${order.productName}", style = MaterialTheme.typography.titleSmall) }
                        item { Text("条码须>14位，逐袋扫码确认", style = MaterialTheme.typography.bodySmall, color = Color.Gray) }
                        // 进度计数器：已完成料号数 / 总料号数
                        val totalParts = order.details?.size ?: 0
                        val doneParts = order.details?.count { d ->
                            d.status == 2 || (state.scannedCounts[d.id] ?: 0) > 0
                        } ?: 0
                        item {
                            Text("已完成料号: $doneParts / $totalParts",
                                style = MaterialTheme.typography.bodyMedium,
                                color = if (doneParts >= totalParts) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)
                        }
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
                            Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Text(order.orderNo, style = MaterialTheme.typography.titleMedium)
                                Surface(shape = MaterialTheme.shapes.small, color = MaterialTheme.colorScheme.primaryContainer) {
                                    Text("待备料", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp))
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
