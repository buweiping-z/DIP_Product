package com.dip.material.ui.outbound

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
fun OutboundScreen(onBack: () -> Unit, viewModel: OutboundViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
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
                val order = state.selectedOrder!!

                // PDA 扫码输入
                BarcodeTextField(
                    onBarcodeScanned = { viewModel.scanOutbound(it.trim()) },
                    label = "扫部品条码(>14位) 逐种核销",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                state.scanMsg?.let { msg ->
                    val isOk = msg.contains("成功") || msg.contains("已确认")
                    Surface(
                        color = if (isOk) Color(0xFF388E3C) else Color(0xFFD32F2F),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) { Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp) }
                }

                // 进度
                Text("已完成料号: ${state.doneParts} / ${state.totalParts}",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                    style = MaterialTheme.typography.titleMedium,
                    color = if (state.allDone) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)

                // 订单信息
                Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                    Column(Modifier.padding(12.dp)) {
                        Text("订单号: ${order.orderNo}", style = MaterialTheme.typography.titleSmall)
                        Text("条码须>14位，扣动扫码枪扳机逐袋扫描", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                    }
                }

                // 明细列表
                LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(order.details) { d ->
                        val scanned = state.scannedCounts[d.id] ?: 0
                        val isDone = scanned > 0
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = when {
                                    isDone -> MaterialTheme.colorScheme.primaryContainer
                                    else -> MaterialTheme.colorScheme.surface
                                })) {
                            Row(Modifier.padding(10.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                    Text("${d.partName}  |  ${d.locationCode}  × ${d.quantity.toInt()}",
                                        style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                }
                                Text(
                                    if (isDone) "已扫${scanned}袋" else "待核销",
                                    color = if (isDone) Color(0xFF4CAF50) else Color.Gray,
                                    fontSize = 14.sp)
                            }
                        }
                    }
                }

                // 全部完成按钮
                if (state.allDone) {
                    Button(onClick = { viewModel.confirmAll() },
                        modifier = Modifier.fillMaxWidth().padding(16.dp).height(48.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) {
                        Text("提交并完成出库", fontSize = 16.sp)
                    }
                }
            } else {
                // 待出库订单列表
                LazyColumn(Modifier.weight(1f).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待出库订单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待出库订单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order) }, Modifier.fillMaxWidth()) {
                            Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(order.orderNo, style = MaterialTheme.typography.titleMedium)
                                    Text("${order.detailCount} 种部品", style = MaterialTheme.typography.bodySmall, color = Color.Gray)
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
