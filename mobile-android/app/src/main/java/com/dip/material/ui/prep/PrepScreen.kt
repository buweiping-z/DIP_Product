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
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PrepScreen(onBack: () -> Unit, viewModel: PrepViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var barcode by remember { mutableStateOf("") }

    Scaffold(
        topBar = { TopAppBar(title = { Text(if (state.selectedOrder != null) "备料扫描" else "备料管理") }, navigationIcon = { IconButton(onClick = {
            if (state.selectedOrder != null) viewModel.clearSelection() else onBack()
        }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        if (state.selectedOrder != null) {
            val order = state.selectedOrder!!
            LazyColumn(Modifier.padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                item { Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(12.dp)) { Text("备料单: ${order.orderNo}", style = MaterialTheme.typography.titleMedium); Text("产品: ${order.productName}") } } }
                state.scanMsg?.let { item { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) } }
                item { OutlinedTextField(value = barcode, onValueChange = { barcode = it }, label = { Text("扫物料条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true,
                    trailingIcon = { Button(onClick = { if (barcode.isNotBlank()) { viewModel.scanItem(barcode.trim()); barcode = "" } }) { Text("扫描") } }) }
                state.selectedOrder?.details?.let { details ->
                    items(details) { d ->
                        Card(Modifier.fillMaxWidth()) { Row(Modifier.padding(12.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            Column { Text(d.partNo, style = MaterialTheme.typography.titleSmall); Text("${d.actualQty}/${d.requiredQty}", style = MaterialTheme.typography.bodySmall) }
                            Surface(shape = MaterialTheme.shapes.small, color = if (d.status == 2) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant) {
                                Text(if (d.status == 2) "完成" else "待备", modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp), fontSize = MaterialTheme.typography.labelSmall.fontSize)
                            }
                        } }
                    }
                }
            }
        } else {
            LazyColumn(Modifier.padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
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
