package com.dip.material.ui.callmaterial

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
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
fun CallMaterialScreen(onBack: () -> Unit, viewModel: CallMaterialViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("叫料 (${state.items.size}项)") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer
                )
            )
        }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // 扫码输入区
            BarcodeTextField(
                onBarcodeScanned = { barcode ->
                    viewModel.scanPart(barcode.trim(), onBack)
                },
                label = "扫部品条码",
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
            )

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            // 消息提示
            state.scanMsg?.let { msg ->
                val bg = if (state.msgOk) Color(0xFF388E3C) else Color(0xFFD32F2F)
                Surface(color = bg, modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
                    Text(msg, color = Color.White,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                        fontSize = 14.sp)
                }
            }

            // 已扫列表
            LazyColumn(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp).weight(1f),
                verticalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                itemsIndexed(state.items) { idx, item ->
                    Card(Modifier.fillMaxWidth()) {
                        Row(
                            Modifier.padding(12.dp).fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(Modifier.weight(1f)) {
                                Text(item.partNo, style = MaterialTheme.typography.titleSmall)
                                Text(item.locationCode, fontSize = 11.sp,
                                    color = MaterialTheme.colorScheme.primary)
                            }
                            TextButton(
                                onClick = { viewModel.removeItem(idx) },
                                colors = ButtonDefaults.textButtonColors(contentColor = Color(0xFFD32F2F))
                            ) {
                                Text("删除", fontSize = 12.sp)
                            }
                        }
                    }
                }
            }

            // 上传按钮
            Button(
                onClick = { viewModel.upload(onBack) },
                enabled = state.items.isNotEmpty() && !state.uploading,
                modifier = Modifier.fillMaxWidth().padding(16.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))
            ) {
                Text(
                    if (state.uploading) "提交中..." else "上传叫料 (${state.items.size}项)",
                    color = Color.White
                )
            }
        }
    }
}
