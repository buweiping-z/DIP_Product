package com.dip.material.ui.substitute

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SubstituteScreen(onBack: () -> Unit, viewModel: SubstituteViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    Scaffold(topBar = {
        TopAppBar(
            title = {
                Text(if (state.viewMode == SubstituteUiState.ViewMode.ORDER_DETAIL)
                    "替代单明细" else "替代移库")
            },
            navigationIcon = {
                IconButton(onClick = {
                    if (state.viewMode == SubstituteUiState.ViewMode.ORDER_DETAIL)
                        viewModel.backToList()
                    else onBack
                }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") }
            },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
        )
    }) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            when (state.viewMode) {
                SubstituteUiState.ViewMode.ORDER_LIST -> {
                    if (state.orders.isEmpty() && !state.isLoading) {
                        Text("暂无替代移库单", modifier = Modifier.fillMaxWidth())
                    } else {
                        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            items(state.orders) { order ->
                                Card(modifier = Modifier.fillMaxWidth().clickable { viewModel.selectOrder(order) }) {
                                    Column(Modifier.padding(12.dp)) {
                                        Text("单号: ${order.orderNo}", style = MaterialTheme.typography.titleSmall)
                                        Text("状态: ${if (order.status == 1) "待确认" else "已完成"}", style = MaterialTheme.typography.bodySmall)
                                        Text("已确认: ${order.confirmedCount}/${order.detailCount}", style = MaterialTheme.typography.bodySmall)
                                    }
                                }
                            }
                        }
                    }
                }

                SubstituteUiState.ViewMode.ORDER_DETAIL -> {
                    state.selectedOrder?.let { selOrder ->
                        Text("单号: ${selOrder.orderNo}", style = MaterialTheme.typography.titleSmall)
                        Text("${state.details.size} 项明细 — 已确认 ${selOrder.confirmedCount}/${selOrder.detailCount}", style = MaterialTheme.typography.bodySmall)

                        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.weight(1f)) {
                            items(state.details) { detail ->
                                Card(modifier = Modifier.fillMaxWidth()) {
                                    Column(Modifier.padding(12.dp)) {
                                        Text("原料号: ${detail.originalPartNo}", style = MaterialTheme.typography.titleSmall)
                                        Text("替代料号: ${detail.substitutePartNo}", style = MaterialTheme.typography.bodySmall)
                                        Text("来源库位: ${detail.sourceLocationCode} → 目标库位: ${detail.targetLocationCode}", style = MaterialTheme.typography.bodySmall)
                                        Text("数量: ${detail.quantity} | 状态: ${if (detail.status == 2) "已确认" else "待确认"}", style = MaterialTheme.typography.bodySmall)
                                        if (detail.status == 1) {
                                            Button(onClick = { viewModel.confirmDetail(selOrder.id, detail.id) },
                                                modifier = Modifier.fillMaxWidth().padding(top = 4.dp)) { Text("确认此项") }
                                        }
                                    }
                                }
                            }
                        }

                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = { viewModel.backToList() }, modifier = Modifier.weight(1f)) { Text("返回列表") }
                            Button(onClick = { viewModel.confirmAll(selOrder.id) }, modifier = Modifier.weight(1f)) { Text("全部确认") }
                        }
                    }
                }
            }

            state.scanMsg?.let {
                Text(it, color = if (it.contains("完成") || it.contains("成功"))
                    MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error)
            }
        }
    }
}
