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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrepScreen(onBack: () -> Unit, viewModel: PrepViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var barcode by remember { mutableStateOf("") }
    var showScanner by remember { mutableStateOf(false) }

    Scaffold(
        topBar = { TopAppBar(title = { Text(if (state.selectedOrder != null) "备料扫描" else "备料管理") }, navigationIcon = { IconButton(onClick = {
            if (state.selectedOrder != null) viewModel.clearSelection() else onBack()
        }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // Half-screen scanner
            if (showScanner) {
                Box(Modifier.fillMaxWidth().fillMaxHeight(0.35f)) {
                    QrCodeScanner(onBarcodeScanned = { code ->
                        viewModel.scanItem(code)
                        barcode = code
                        showScanner = false
                    }, isActive = showScanner)
                    IconButton(onClick = { showScanner = false }, modifier = Modifier.align(Alignment.TopEnd).padding(8.dp)) {
                        Text("✕", color = Color.White, fontSize = 20.sp)
                    }
                }
            }

            if (state.selectedOrder != null) {
                val order = state.selectedOrder!!
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(12.dp)) {
                        Text("备料单: ${order.orderNo}", style = MaterialTheme.typography.titleMedium)
                        Text("产品: ${order.productName}")
                    } } }
                    state.scanMsg?.let { item { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) } }
                    item {
                        OutlinedTextField(value = barcode, onValueChange = { barcode = it },
                            label = { Text("扫物料条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true,
                            trailingIcon = { IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, "扫码") } })
                        Button(onClick = { if (barcode.isNotBlank()) { viewModel.scanItem(barcode.trim()); barcode = "" } },
                            enabled = barcode.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("扫描") }
                    }
                    order.details?.let { details ->
                        items(details) { d ->
                            Card(Modifier.fillMaxWidth()) {
                                Column(Modifier.padding(12.dp)) {
                                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                        Text(d.partNo, style = MaterialTheme.typography.titleSmall)
                                        Surface(shape = MaterialTheme.shapes.small,
                                            color = if (d.status == 2) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant) {
                                            Text(if (d.status == 2) "完成" else "待备", modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                                                fontSize = MaterialTheme.typography.labelSmall.fontSize)
                                        }
                                    }
                                    Text("备料: ${d.actualQty.toInt()} / ${d.totalRequiredQty.toInt()}",
                                        style = MaterialTheme.typography.bodyMedium,
                                        color = if (d.actualQty >= d.totalRequiredQty) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error)
                                    // 库存库位
                                    d.stocks?.takeIf { it.isNotEmpty() }?.let { stocks ->
                                        Text("库存库位:", style = MaterialTheme.typography.labelSmall)
                                        stocks.forEach { s ->
                                            Text("  ${s.locationCode}: 可用 ${s.availableQty.toInt()}",
                                                style = MaterialTheme.typography.bodySmall)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            } else {
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
