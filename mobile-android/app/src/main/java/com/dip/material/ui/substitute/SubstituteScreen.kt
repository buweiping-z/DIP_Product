package com.dip.material.ui.substitute

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
fun SubstituteScreen(onBack: () -> Unit, viewModel: SubstituteViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    // 扫码结果音效
    LaunchedEffect(state.scanEventId) {
        if (state.scanEventId > 0) {
            if (state.lastScanOk) ScanSoundManager.playSuccess()
            else ScanSoundManager.playError()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("替代料移库") },
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
            val order = state.selectedOrder
            if (order != null) {
                // ===== 扫码确认界面 =====

                // PDA 扫码输入区 — 标签和回调随步骤切换
                val scanLoc = state.scanningLocation
                val locTarget = state.matchedDetail?.targetLocationCode ?: ""
                BarcodeTextField(
                    onBarcodeScanned = { barcode ->
                        if (scanLoc) viewModel.scanLocation(barcode.trim())
                        else viewModel.scanBarcode(barcode.trim())
                    },
                    label = if (scanLoc) "扫目标库位: $locTarget"
                            else "扫替代料号条码(>14位)",
                    clearKey = scanLoc,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

                // 消息
                state.scanMsg?.let { msg ->
                    val isError = !state.lastScanOk
                    Surface(
                        color = if (isError) Color(0xFFD32F2F) else Color(0xFF388E3C),
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                    ) { Text(msg, color = Color.White, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp), fontSize = 14.sp) }
                }

                // 进度
                Spacer(Modifier.height(4.dp))
                Text("已完成: ${state.confirmedCount} / ${state.totalCount}",
                    modifier = Modifier.padding(horizontal = 16.dp),
                    style = MaterialTheme.typography.titleMedium,
                    color = if (state.allDone) Color(0xFF4CAF50) else MaterialTheme.colorScheme.primary)

                // 匹配结果显示
                if (state.showCandidates && state.matchCandidates.isNotEmpty()) {
                    // 多条候选列表（步骤1：选明细）
                    Text("请选择匹配的明细：", modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.bodyMedium)
                    LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp),
                        verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(state.matchCandidates) { c ->
                            Card(onClick = { viewModel.selectCandidate(c) }, Modifier.fillMaxWidth()) {
                                Column(Modifier.padding(12.dp)) {
                                    Text("替代料: ${c.substitutePartNo} → 缺料: ${c.originalPartNo}",
                                        style = MaterialTheme.typography.bodyMedium)
                                    Text("来源: ${c.sourceLocationCode} → 目标: ${c.targetLocationCode} | 数量: ${c.quantity.toInt()}",
                                        style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                                }
                            }
                        }
                    }
                } else {
                    val matched = state.matchedDetail
                    if (state.scanningLocation && matched != null) {
                        // 步骤2：料号已匹配，等待扫目标库位（仅展示，不显示确认/取消按钮）
                        Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                            colors = CardDefaults.cardColors(containerColor = Color(0xFFE3F2FD))) {
                            Column(Modifier.padding(12.dp)) {
                                Text("替代料: ${matched.substitutePartNo}", style = MaterialTheme.typography.titleSmall)
                                Text("来源库位: ${matched.sourceLocationCode}", style = MaterialTheme.typography.bodySmall)
                                Text("缺料: ${matched.originalPartNo}", style = MaterialTheme.typography.titleSmall)
                                Text("目标库位: ${matched.targetLocationCode}", style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.primary)
                                Text("数量: ${matched.quantity.toInt()}", style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.primary)
                            }
                        }
                        Spacer(Modifier.height(4.dp))
                        Text("↑ 请扫目标库位条码",
                            modifier = Modifier.padding(horizontal = 16.dp),
                            style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                    }
                }

                Spacer(Modifier.height(8.dp))

                // 明细列表（按来源库位排列）
                Text("明细列表", style = MaterialTheme.typography.titleSmall,
                    modifier = Modifier.padding(horizontal = 16.dp))
                LazyColumn(Modifier.weight(1f).padding(horizontal = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    items(order.details) { d ->
                        val isDone = d.status == 2
                        Card(Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isDone) MaterialTheme.colorScheme.primaryContainer
                                else MaterialTheme.colorScheme.surface)) {
                            Row(Modifier.padding(8.dp), horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text("${d.substitutePartNo} [${d.sourceLocationCode}] → ${d.originalPartNo} [${d.targetLocationCode}]",
                                        fontSize = 12.sp)
                                    Text("数量: ${d.quantity.toInt()}", fontSize = 11.sp, color = Color.Gray)
                                }
                                Text(if (isDone) "✓" else "待确认",
                                    color = if (isDone) Color(0xFF4CAF50) else Color.Gray, fontSize = 13.sp)
                            }
                        }
                    }
                }

                // 提交按钮（全部确认后显示）
                if (state.allDone) {
                    Button(onClick = { viewModel.confirmAll() },
                        modifier = Modifier.fillMaxWidth().padding(16.dp).height(48.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) {
                        Text("提交并完成移库", fontSize = 16.sp)
                    }
                }
            } else {
                // ===== 订单列表界面 =====
                LazyColumn(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { Text("待确认移库订单", style = MaterialTheme.typography.titleMedium) }
                    if (state.isLoading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    if (state.orders.isEmpty() && !state.isLoading) item { Text("无待确认订单") }
                    items(state.orders) { order ->
                        Card(onClick = { viewModel.selectOrder(order.id) }, Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(12.dp)) {
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically) {
                                    Text(order.orderNo, style = MaterialTheme.typography.titleMedium)
                                    Surface(shape = MaterialTheme.shapes.small,
                                        color = MaterialTheme.colorScheme.primaryContainer) {
                                        Text("待确认", modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                                            fontSize = 12.sp)
                                    }
                                }
                                Text("已确认: ${order.confirmedCount} / ${order.detailCount}",
                                    style = MaterialTheme.typography.bodySmall, color = Color.Gray)
                            }
                        }
                    }
                }
            }
        }
    }
}
