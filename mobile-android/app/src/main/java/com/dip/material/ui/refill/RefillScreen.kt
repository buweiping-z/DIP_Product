package com.dip.material.ui.refill

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
fun RefillScreen(onBack: () -> Unit, viewModel: RefillViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var barcode by remember { mutableStateOf("") }

    Scaffold(topBar = { TopAppBar(title = { Text("补料管理") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        LazyColumn(Modifier.padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            item { OutlinedTextField(value = barcode, onValueChange = { barcode = it }, label = { Text("扫缺货部品条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true) }
            state.scanMsg?.let { item { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) } }
            if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
            item { Text("待补料清单 (${state.pendingItems.size}项)", style = MaterialTheme.typography.titleMedium) }
            items(state.pendingItems) { item ->
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(12.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text(item.partNo, style = MaterialTheme.typography.titleSmall)
                            Text("缺 ${item.remaining.toInt()}", color = MaterialTheme.colorScheme.error)
                        }
                        Text("备料单: ${item.prepOrderNo} | 产品: ${item.productName}", style = MaterialTheme.typography.bodySmall)
                        Button(onClick = { viewModel.scanRefill(barcode.trim().ifBlank { item.partNo }, item.prepOrderId, item.prepDetailId) }, Modifier.fillMaxWidth().padding(top = 4.dp)) { Text("补料 +1") }
                    }
                }
            }
        }
    }
}
