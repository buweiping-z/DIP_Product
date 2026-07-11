package com.dip.material.ui.shelving

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.QrCodeScanner
import com.dip.material.utils.ScanSoundManager

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShelvingScreen(onBack: () -> Unit, viewModel: ShelvingViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var input by remember { mutableStateOf("") }
    var showScanner by remember { mutableStateOf(false) }

    // 切换步骤时清空输入框；到达步骤3时关闭扫描窗口
    LaunchedEffect(state.step) {
        input = ""
        if (state.step == 3) showScanner = false
    }

    // 扫码结果音效（key 为递增计数器，确保每次扫描都触发）
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    fun onScanned(code: String) {
        val trimmed = code.trim()
        when (state.step) {
            1 -> viewModel.lookupPart(trimmed)
            2 -> viewModel.lookupLocation(trimmed)
        }
        // 扫描窗口不关闭，直到第2步库位匹配成功进入第3步时由 LaunchedEffect 关闭
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("上架管理") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // Half-screen scanner
            if (showScanner) {
                Box(Modifier.fillMaxWidth().fillMaxHeight(0.4f)) {
                    QrCodeScanner(onBarcodeScanned = { onScanned(it) }, isActive = showScanner)
                    IconButton(onClick = { showScanner = false }, modifier = Modifier.align(Alignment.TopEnd).padding(8.dp)) {
                        Text("✕", color = androidx.compose.ui.graphics.Color.White, fontSize = 20.sp)
                    }
                }
            }

            // Step content
            Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
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
                        OutlinedTextField(value = input, onValueChange = { input = it }, label = { Text("部品条码") },
                            modifier = Modifier.fillMaxWidth(), singleLine = true,
                            trailingIcon = { IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, "扫码") } })
                        Button(onClick = { if (input.isNotBlank()) { viewModel.lookupPart(input.trim()); input = "" } },
                            enabled = input.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("查询部品") }
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
                        OutlinedTextField(value = input, onValueChange = { input = it }, label = { Text("库位条码") },
                            modifier = Modifier.fillMaxWidth(), singleLine = true,
                            trailingIcon = { IconButton(onClick = { showScanner = !showScanner }) { Icon(Icons.Default.QrCodeScanner, "扫码") } })
                        Button(onClick = { if (input.isNotBlank()) { viewModel.lookupLocation(input.trim()); input = "" } },
                            enabled = input.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("确认库位") }
                        TextButton(onClick = { viewModel.reset(); input = "" }) { Text("重新开始") }
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
                state.resultMsg?.let { msg ->
                    Surface(
                        color = if (msg.contains("成功")) Color(0xFF388E3C) else Color(0xFFD32F2F),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                    }
                }
            }
        }
    }
}
