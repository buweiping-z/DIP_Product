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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.dip.material.ui.components.BarcodeTextField

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RefillScreen(onBack: () -> Unit, viewModel: RefillViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    val batches by viewModel.activeBatches.collectAsState()
    // PDA 扫码输入由 BarcodeTextField 自管理

    LaunchedEffect(Unit) { viewModel.loadBatches() }

    val title = when (state.step) {
        0 -> "补料管理"
        1 -> "选择料号"
        2 -> "取料 (${state.pickedIds.size}/${state.selectedIds.size})"
        3 -> "核对 (${state.verifiedIds.size}/${state.selectedIds.size})"
        else -> "补料管理"
    }

    Scaffold(
        topBar = {
            TopAppBar(title = { Text(title) },
                navigationIcon = { IconButton(onClick = {
                    if (state.step > 0) viewModel.clearAll() else onBack()
                }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer))
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // PDA 扫码输入区
            if (state.step >= 1) {
                val hint = when (state.step) {
                    1 -> "扫部品条码(>14位)"
                    2 -> "扫部品条码(>14位)"
                    3 -> if (state.boxPartNo.isEmpty()) "扫料盒(≤14位)" else "扫部品(>14位且含料盒号)"
                    else -> ""
                }
                BarcodeTextField(
                    onBarcodeScanned = { barcode ->
                        when (state.step) {
                            1 -> viewModel.togglePart(barcode.trim())
                            2 -> viewModel.scanPick(barcode.trim())
                            3 -> viewModel.scanVerify(barcode.trim())
                        }
                    },
                    label = hint,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                    clearKey = state.step
                )
            }

            // 核对时显示当前料盒信息
            if (state.step == 3 && state.boxPart != null) {
                Surface(color = Color(0xFFFFF3CD), modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
                    Row(Modifier.padding(12.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text("当前料盒: ${state.boxPart!!.partNo}", style = MaterialTheme.typography.titleSmall)
                        Text("扫部品条码确认(>14位)", fontSize = 12.sp, color = Color.Gray)
                    }
                }
            }

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())
            state.scanMsg?.let { msg ->
                val ok = state.msgOk
                Surface(color = if (ok) Color(0xFF388E3C) else Color(0xFFD32F2F), modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
                    Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp)
                }
            }

            when (state.step) {
                0 -> {
                    Column(Modifier.fillMaxWidth().padding(32.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                        if (batches.isNotEmpty()) {
                            Card(Modifier.fillMaxWidth().padding(bottom = 16.dp), colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF3CD))) {
                                Column(Modifier.padding(12.dp)) {
                                    Text("未完成批次 (${batches.size})：", style = MaterialTheme.typography.titleSmall)
                                    batches.forEach { b ->
                                        val bn = b["batch_no"] as? String ?: ""; val pn = b["product_name"] as? String ?: ""
                                        Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                                            Text("$pn ($bn)", fontSize = 13.sp, modifier = Modifier.weight(1f, true))
                                            TextButton(onClick = { viewModel.selectBatch(bn) }) { Text("恢复", fontSize = 12.sp) }
                                        }
                                    }
                                    TextButton(onClick = { viewModel.loadBatches() }) { Text("刷新", fontSize = 12.sp) }
                                }
                            }
                        }
                        Text("补料管理", style = MaterialTheme.typography.headlineMedium); Spacer(Modifier.height(16.dp))
                        Text("扫描或输入产品名称", style = MaterialTheme.typography.bodyLarge, color = Color.Gray); Spacer(Modifier.height(16.dp))
                        BarcodeTextField(
                            onBarcodeScanned = { viewModel.scanProduct(it.trim()) },
                            label = "产品名称/条码",
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }
                1 -> {
                    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp).weight(1f, fill = true), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(state.parts) { item ->
                            val checked = item.prepDetailId in state.selectedIds
                            Card(onClick = { viewModel.togglePart(item.partNo) }, Modifier.fillMaxWidth(),
                                colors = CardDefaults.cardColors(containerColor = if (checked) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface)) {
                                Row(Modifier.padding(12.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                    Column(Modifier.weight(1f, fill = true)) { Text(item.partNo, style = MaterialTheme.typography.titleSmall); if (item.locationCodes.isNotEmpty()) Text(item.locationCodes.joinToString(", "), fontSize = 11.sp, color = MaterialTheme.colorScheme.primary) }
                                    if (checked) Text("✓", color = Color(0xFF4CAF50), fontSize = 18.sp)
                                }
                            }
                        }
                    }
                    Button(onClick = { viewModel.startRefill() }, enabled = state.selectedIds.isNotEmpty(), modifier = Modifier.fillMaxWidth().padding(16.dp), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) {
                        Text("开始补料 (${state.selectedIds.size}项)", color = Color.White)
                    }
                }
                2 -> {
                    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp).weight(1f, fill = true), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(state.parts.filter { it.prepDetailId in state.selectedIds }) { item ->
                            val isDone = item.prepDetailId in state.pickedIds
                            Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = if (isDone) Color(0xFFC8E6C9) else MaterialTheme.colorScheme.surface)) {
                                Row(Modifier.padding(12.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                    Column {
                                        Text(item.partNo, style = MaterialTheme.typography.titleSmall)
                                        if (item.locationCodes.isNotEmpty()) Text(item.locationCodes.joinToString(", "), fontSize = 11.sp, color = MaterialTheme.colorScheme.primary)
                                    }
                                    if (isDone) Text("✓", color = Color(0xFF4CAF50), fontSize = 18.sp)
                                }
                            }
                        }
                    }
                    Button(onClick = { viewModel.goPickDone() }, modifier = Modifier.fillMaxWidth().padding(16.dp), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2196F3))) {
                        Text("取料完成，去核对", color = Color.White)
                    }
                }
                3 -> {
                    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp).weight(1f, fill = true), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(state.parts.filter { it.prepDetailId in state.selectedIds }) { item ->
                            val isDone = item.prepDetailId in state.verifiedIds
                            Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = if (isDone) Color(0xFFC8E6C9) else MaterialTheme.colorScheme.surface)) {
                                Row(Modifier.padding(12.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                    Column {
                                        Text(item.partNo, style = MaterialTheme.typography.titleSmall)
                                        if (item.locationCodes.isNotEmpty()) Text(item.locationCodes.joinToString(", "), fontSize = 11.sp, color = MaterialTheme.colorScheme.primary)
                                    }
                                    if (isDone) Text("✓", color = Color(0xFF4CAF50), fontSize = 18.sp)
                                }
                            }
                        }
                    }
                    Button(onClick = { viewModel.goDone() }, modifier = Modifier.fillMaxWidth().padding(16.dp), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) {
                        Text("核对完成，结束补料", color = Color.White)
                    }
                }
            }
        }
    }
}
