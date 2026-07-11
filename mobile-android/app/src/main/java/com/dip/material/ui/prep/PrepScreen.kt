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

    // 全部完成后自动关闭扫码窗口并返回
    LaunchedEffect(state.allDone) {
        if (state.allDone) {
            showScanner = false
            kotlinx.coroutines.delay(800)
            viewModel.clearSelection()
        }
    }

    // 扫码结果音效（key 为递增计数器，确保每次扫描都触发）
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    // 扫码回调
    fun onScanned(code: String) {
        val trimmed = code.trim()
        viewModel.scanItem(trimmed)
        inputBarcode = trimmed
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
                // Half-screen scanner (持续显示)
                if (showScanner) {
                    Box(Modifier.fillMaxWidth().fillMaxHeight(0.35f)) {
                        QrCodeScanner(onBarcodeScanned = { onScanned(it) }, isActive = showScanner)
                        Row(Modifier.align(Alignment.TopEnd).padding(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            // 手动退出扫描
                            Button(onClick = { showScanner = false }, colors = ButtonDefaults.buttonColors(containerColor = Color.Red)) { Text("关闭扫码") }
                        }
                    }
                }

                // 手动输入区
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(value = inputBarcode, onValueChange = { inputBarcode = it },
                        label = { Text("手动输入料号") }, modifier = Modifier.weight(1f), singleLine = true)
                    Button(onClick = { if (inputBarcode.isNotBlank()) { viewModel.scanItem(inputBarcode.trim()); inputBarcode = "" } },
                        enabled = inputBarcode.isNotBlank()) { Text("确认") }
                    IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, if (showScanner) "关闭" else "扫码") }
                }

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let {
                    val isError = it.contains("未匹配") || it.contains("不足")
                    Text(it, color = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(horizontal = 16.dp), fontSize = 14.sp)
                }

                // 备料明细
                state.selectedOrder?.let { order ->
                    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        item { Text("备料单: ${order.orderNo} | 产品: ${order.productName}", style = MaterialTheme.typography.titleSmall) }
                        order.details?.let { details ->
                            items(details) { d ->
                                Card(Modifier.fillMaxWidth(),
                                    colors = CardDefaults.cardColors(
                                        containerColor = if (d.status == 2) MaterialTheme.colorScheme.primaryContainer
                                        else MaterialTheme.colorScheme.surface)) {
                                    Column(Modifier.padding(10.dp)) {
                                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                            Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                            Text(if (d.status == 2) "✓" else "${d.actualQty.toInt()}/${d.totalRequiredQty.toInt()}",
                                                color = if (d.status == 2) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error)
                                        }
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
