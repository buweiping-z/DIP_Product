package com.dip.material.ui.online

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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OnlineScreen(onBack: () -> Unit, viewModel: OnlineViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var inputBarcode by remember { mutableStateOf("") }
    var showScanner by remember { mutableStateOf(false) }

    // 全部完成后自动关闭扫码窗口
    LaunchedEffect(state.allDone) {
        if (state.allDone) {
            showScanner = false
            kotlinx.coroutines.delay(1000)
            viewModel.clearSelection()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (state.selectedOrder != null) "上线确认" else "待上线订单") },
                navigationIcon = {
                    IconButton(onClick = {
                        if (state.selectedOrder != null) viewModel.clearSelection() else onBack()
                    }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            if (state.selectedOrder != null) {
                // === 上线扫描界面 ===
                if (showScanner) {
                    Box(Modifier.fillMaxWidth().fillMaxHeight(0.35f)) {
                        QrCodeScanner(onBarcodeScanned = { viewModel.scanOnline(it.trim()) }, isActive = true)
                        Row(Modifier.align(Alignment.TopEnd).padding(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = { showScanner = false }, colors = ButtonDefaults.buttonColors(containerColor = Color.Red)) { Text("关闭扫码") }
                        }
                    }
                }

                // 手动输入 + 扫码切换
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(value = inputBarcode, onValueChange = { inputBarcode = it },
                        label = { Text("手动输入料号") }, modifier = Modifier.weight(1f), singleLine = true)
                    Button(onClick = { if (inputBarcode.isNotBlank()) { viewModel.scanOnline(inputBarcode.trim()); inputBarcode = "" } },
                        enabled = inputBarcode.isNotBlank()) { Text("确认") }
                    IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, if (showScanner) "关闭" else "扫码") }
                }

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let {
                    val isOk = it.contains("成功") || it.contains("完成")
                    Text(it, color = if (isOk) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(horizontal = 16.dp), fontSize = 14.sp)
                }

                // 订单信息
                state.selectedOrder?.let { order ->
                    Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("${order.orderNo} | ${order.productName}", style = MaterialTheme.typography.titleSmall)
                            Text("计划数量: ${order.planQty.toInt()} | 料号: ${state.details.size} 项", style = MaterialTheme.typography.bodySmall)
                        }
                    }
                }

                // 料号核对列表
                LazyColumn(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(state.details) { d ->
                        val consumed = d.onlineConsumedQty
                        val required = d.totalRequiredQty
                        val isDone = consumed >= required
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isDone) MaterialTheme.colorScheme.primaryContainer
                                else MaterialTheme.colorScheme.surface
                            )) {
                            Row(Modifier.padding(10.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                    Text("已上线: ${consumed.toInt()} / ${required.toInt()}", style = MaterialTheme.typography.bodySmall)
                                }
                                Text(if (isDone) "✓" else "待确认",
                                    color = if (isDone) Color(0xFF4CAF50) else MaterialTheme.colorScheme.error,
                                    fontSize = 14.sp)
                            }
                        }
                    }
                }
            } else {
                // === 订单列表 ===
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待上线订单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待上线订单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order) }, Modifier.fillMaxWidth()) {
                            Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(order.orderNo, style = MaterialTheme.typography.titleMedium)
                                    Text("${order.productName} | 计划: ${order.planQty.toInt()}", style = MaterialTheme.typography.bodySmall)
                                }
                                Surface(shape = MaterialTheme.shapes.small, color = MaterialTheme.colorScheme.tertiaryContainer) {
                                    Text("待上线", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp))
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
