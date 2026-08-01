package com.dip.material.ui.online

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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OnlineScreen(onBack: () -> Unit, viewModel: OnlineViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    // PDA 扫码输入由 BarcodeTextField 自管理

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
                // PDA 扫码输入区
                BarcodeTextField(
                    onBarcodeScanned = { viewModel.scanOnline(it.trim()) },
                    label = "扫描料号条码",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let { msg ->
                    val isOk = msg.contains("已确认")
                    Surface(
                        color = if (isOk) Color(0xFF388E3C) else Color(0xFFD32F2F),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) {
                        Text(msg, color = Color.White,
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                    }
                }

                // 订单信息
                state.selectedOrder?.let { order ->
                    Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("${order.orderNo} | ${order.productName}", style = MaterialTheme.typography.titleSmall)
                            Text("扣动扫码枪扳机逐袋扫描", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                        }
                    }
                }

                // 进度计数器：已完成料号数 / 总料号数（固定在列表上方，始终可见）
                val totalParts = state.details.size
                val doneParts = state.details.count { (state.scannedCounts[it.id] ?: 0) > 0 }
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

                // 料号核对列表
                LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(state.details) { d ->
                        val scannedQty = state.scannedCounts[d.id] ?: 0
                        val isDone = scannedQty > 0
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isDone) MaterialTheme.colorScheme.primaryContainer
                                else MaterialTheme.colorScheme.surface
                            )) {
                            Row(Modifier.padding(10.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                    if (isDone) {
                                        Text("已确认 $scannedQty", style = MaterialTheme.typography.bodySmall, color = Color(0xFF4CAF50))
                                    }
                                }
                                Text(if (isDone) "✓" else "待确认",
                                    color = if (isDone) Color(0xFF4CAF50) else Color.Gray,
                                    fontSize = 14.sp)
                            }
                        }
                    }
                }
            } else {
                // 订单列表
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
