@file:OptIn(ExperimentalMaterial3Api::class)

package com.dip.material.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.dip.material.ui.viewmodels.ShelvingViewModel
import org.koin.androidx.compose.koinViewModel

@Composable
fun ShelvingScreen(viewModel: ShelvingViewModel = koinViewModel(), onBack: () -> Unit = {}) {
    val state by viewModel.state.collectAsState()
    var barcode by remember { mutableStateOf("") }

    LaunchedEffect(state.resultMsg) {
        if (state.resultMsg?.contains("成功") == true) barcode = ""
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("直接上架") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } }
            )
        }
    ) { padding ->
        Box(Modifier.padding(padding).fillMaxSize()) {
            LazyColumn(
                Modifier.fillMaxSize().padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                // Step indicator
                item {
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceEvenly
                    ) {
                        listOf("部品", "库位", "数量", "确认").forEachIndexed { i, label ->
                            Text(
                                label,
                                color = if (state.step > i) MaterialTheme.colorScheme.primary
                                        else if (state.step == i + 1) MaterialTheme.colorScheme.primary
                                        else MaterialTheme.colorScheme.onSurfaceVariant,
                                fontWeight = if (state.step == i + 1) FontWeight.Bold else FontWeight.Normal
                            )
                        }
                    }
                }

                // Step 1: Scan part
                if (state.step >= 1) {
                    item {
                        OutlinedTextField(
                            value = barcode,
                            onValueChange = { barcode = it },
                            label = { Text("扫部品条码") },
                            modifier = Modifier.fillMaxWidth(),
                            trailingIcon = {
                                Button(
                                    onClick = { viewModel.lookupPart(barcode) },
                                    enabled = barcode.isNotBlank() && !state.isLoading
                                ) { Text("查询") }
                            }
                        )
                    }
                }

                state.partLookupMsg?.let { msg ->
                    item { Text(msg, color = MaterialTheme.colorScheme.error) }
                }

                state.scannedPart?.let { part ->
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp)) {
                                Text("部品号: ${part.partNo}", fontWeight = FontWeight.Bold)
                                Text("名称: ${part.partName}")
                            }
                        }
                    }
                }

                if (state.partLocations.isNotEmpty()) {
                    item { Text("库存分布:", fontWeight = FontWeight.SemiBold) }
                    items(state.partLocations) { loc ->
                        Card(Modifier.fillMaxWidth()) {
                            Row(
                                Modifier.padding(12.dp).fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Text("库位: ${loc.locationCode}")
                                Text("数量: ${loc.availableQty}", fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }

                // Step 1 → Step 2 button
                if (state.step == 1 && state.scannedPart != null) {
                    item {
                        Button(
                            onClick = { viewModel.goToStep2() },
                            modifier = Modifier.fillMaxWidth()
                        ) { Text("下一步 → 扫库位") }
                    }
                }

                // Step 2: Scan location
                if (state.step >= 2) {
                    item {
                        OutlinedTextField(
                            value = barcode,
                            onValueChange = { barcode = it },
                            label = { Text("扫库位条码") },
                            modifier = Modifier.fillMaxWidth(),
                            trailingIcon = {
                                Button(
                                    onClick = { viewModel.lookupLocation(barcode) },
                                    enabled = barcode.isNotBlank() && !state.isLoading
                                ) { Text("查询") }
                            }
                        )
                    }
                }

                state.locationLookupMsg?.let { msg ->
                    item { Text(msg, color = MaterialTheme.colorScheme.error) }
                }

                state.scannedLocation?.let { loc ->
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp)) {
                                Text("库位: ${loc.locationCode}", fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }

                // Step 2 → Step 3 button
                if (state.step == 2 && state.scannedLocation != null) {
                    item {
                        Button(
                            onClick = { viewModel.goToStep3() },
                            modifier = Modifier.fillMaxWidth()
                        ) { Text("下一步 → 输数量") }
                    }
                }

                // Step 3: Enter quantity
                if (state.step >= 3) {
                    item {
                        OutlinedTextField(
                            value = state.quantity,
                            onValueChange = { viewModel.onQuantityInput(it) },
                            label = { Text("数量") },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }

                // Step 3 → Step 4 button
                if (state.step == 3) {
                    item {
                        Button(
                            onClick = { viewModel.gotoConfirm() },
                            modifier = Modifier.fillMaxWidth(),
                            enabled = state.quantity.toDoubleOrNull() != null &&
                                    (state.quantity.toDoubleOrNull() ?: 0.0) > 0
                        ) { Text("确认数量 → 提交上架") }
                    }
                }

                // Step 4: Confirm
                if (state.step == 4) {
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp)) {
                                Text("确认上架", fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
                                Spacer(Modifier.height(8.dp))
                                Text("部品: ${state.scannedPart?.partNo}")
                                Text("库位: ${state.scannedLocation?.locationCode}")
                                Text("数量: ${state.quantity}")
                            }
                        }
                    }
                    item {
                        Button(
                            onClick = { viewModel.confirmShelving() },
                            modifier = Modifier.fillMaxWidth(),
                            enabled = !state.isLoading
                        ) { Text("确认上架") }
                    }
                    item {
                        TextButton(onClick = { viewModel.resetToStep1() }, modifier = Modifier.fillMaxWidth()) {
                            Text("重新开始")
                        }
                    }
                }

                // Result message
                state.resultMsg?.let { msg ->
                    item {
                        Text(
                            msg,
                            color = if (msg.contains("成功")) MaterialTheme.colorScheme.primary
                                    else MaterialTheme.colorScheme.error,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }

                // Loading indicator
                if (state.isLoading) {
                    item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                }
            }
        }
    }
}
