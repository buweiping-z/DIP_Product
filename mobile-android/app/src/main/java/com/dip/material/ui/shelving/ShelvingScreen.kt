package com.dip.material.ui.shelving

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShelvingScreen(onBack: () -> Unit, viewModel: ShelvingViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var input by remember { mutableStateOf("") }

    Scaffold(
        topBar = { TopAppBar(title = { Text("上架管理") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center) {
                listOf("部品", "库位", "数量", "确认").forEachIndexed { i, label ->
                    val active = state.step > i || state.step == 4
                    Surface(shape = MaterialTheme.shapes.small,
                        color = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceVariant,
                        modifier = Modifier.padding(horizontal = 2.dp)) {
                        Text("${i+1}.$label", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp), fontSize = 12.sp)
                    }
                }
            }
            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            when (state.step) {
                1 -> {
                    Text("第1步: 扫部品条码", style = MaterialTheme.typography.titleMedium)
                    OutlinedTextField(value = input, onValueChange = { input = it }, label = { Text("部品条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                    Button(onClick = { if (input.isNotBlank()) { viewModel.lookupPart(input.trim()); input = "" } }, enabled = input.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("查询部品") }
                }
                2 -> {
                    state.scannedPart?.let { part ->
                        Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                            Column(Modifier.padding(12.dp)) {
                                Text("已选部品: ${part.partNo}", style = MaterialTheme.typography.titleSmall)
                                Text("名称: ${part.partName}")
                                if (state.partLocations.isNotEmpty()) {
                                    Text("当前库存:", style = MaterialTheme.typography.labelSmall)
                                    state.partLocations.forEach { Text("  ${it.locationCode}: ${it.availableQty}") }
                                }
                            }
                        }
                    }
                    Text("第2步: 扫目标库位条码", style = MaterialTheme.typography.titleMedium)
                    OutlinedTextField(value = input, onValueChange = { input = it }, label = { Text("库位条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                    Button(onClick = { if (input.isNotBlank()) { viewModel.lookupLocation(input.trim()); input = "" } }, enabled = input.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("确认库位") }
                    TextButton(onClick = { viewModel.reset() }) { Text("重新开始") }
                }
                3 -> {
                    state.scannedPart?.let { Text("部品: ${it.partNo} / ${it.partName}") }
                    state.scannedLocation?.let { Text("目标库位: ${it.locationCode}") }
                    Text("第3步: 输入数量", style = MaterialTheme.typography.titleMedium)
                    OutlinedTextField(value = state.quantity, onValueChange = { viewModel.setQuantity(it) }, label = { Text("数量") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal), modifier = Modifier.fillMaxWidth(), singleLine = true)
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        TextButton(onClick = { viewModel.gotoStep2() }) { Text("返回") }
                        Button(onClick = { viewModel.confirm() }, enabled = state.quantity.toDoubleOrNull()?.let { it > 0 } == true) { Text("确认上架") }
                    }
                }
            }
            state.resultMsg?.let {
                Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(
                    containerColor = if (it.contains("成功")) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.errorContainer)) {
                    Text(it, modifier = Modifier.padding(12.dp))
                }
            }
        }
    }
}
