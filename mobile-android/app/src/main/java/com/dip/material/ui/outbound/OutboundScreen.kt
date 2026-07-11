package com.dip.material.ui.outbound

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
fun OutboundScreen(onBack: () -> Unit, viewModel: OutboundViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var inputBarcode by remember { mutableStateOf("") }
    var showScanner by remember { mutableStateOf(false) }

    // 全部完成后自动关闭扫码窗口并返回列表
    LaunchedEffect(state.allDone) {
        if (state.allDone) {
            showScanner = false
            kotlinx.coroutines.delay(800)
            viewModel.clearSelection()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (state.selectedOrder != null) "出库核销" else "出库管理") },
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
                // 扫描界面
                if (showScanner) {
                    Box(Modifier.fillMaxWidth().fillMaxHeight(0.4f)) {
                        QrCodeScanner(onBarcodeScanned = { viewModel.scanOutbound(it.trim()) }, isActive = true)
                        Row(Modifier.align(Alignment.TopEnd).padding(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = { showScanner = false }, colors = ButtonDefaults.buttonColors(containerColor = Color.Red)) { Text("关闭扫码") }
                        }
                    }
                }

                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(value = inputBarcode, onValueChange = { inputBarcode = it },
                        label = { Text("手动输入料号") }, modifier = Modifier.weight(1f), singleLine = true)
                    Button(onClick = { if (inputBarcode.isNotBlank()) { viewModel.scanOutbound(inputBarcode.trim()); inputBarcode = "" } },
                        enabled = inputBarcode.isNotBlank()) { Text("确认") }
                    IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, if (showScanner) "关闭" else "扫码") }
                }

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let {
                    val isOk = it.contains("成功")
                    Text(it, color = if (isOk) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(horizontal = 16.dp), fontSize = 14.sp)
                }

                // 出库单详情
                state.selectedOrder?.let { order ->
                    Card(Modifier.fillMaxWidth().padding(16.dp),
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                        Column(Modifier.padding(16.dp)) {
                            Text("订单号: ${order.orderNo}", style = MaterialTheme.typography.titleSmall)
                            Text("料号: ${order.partNo}", style = MaterialTheme.typography.bodyMedium)
                            Text("物料名称: ${order.partName}", style = MaterialTheme.typography.bodyMedium)
                            Text("库位: ${order.locationCode}", style = MaterialTheme.typography.bodyMedium)
                            Text("出库数量: ${order.quantity.toInt()}", style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.primary)
                        }
                    }
                }
            } else {
                // 待出库订单列表
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待出库订单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待出库订单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order) }, Modifier.fillMaxWidth()) {
                            Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(order.partNo, style = MaterialTheme.typography.titleMedium)
                                    Text("${order.partName} | ${order.locationCode} × ${order.quantity.toInt()}", style = MaterialTheme.typography.bodySmall)
                                }
                                Surface(shape = MaterialTheme.shapes.small, color = MaterialTheme.colorScheme.tertiaryContainer) {
                                    Text("待出库", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp), fontSize = 12.sp)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
