package com.dip.material.ui.online

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OnlineScreen(onBack: () -> Unit, viewModel: OnlineViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var prepOrderId by remember { mutableStateOf("") }
    var partNo by remember { mutableStateOf("") }
    var barcode by remember { mutableStateOf("") }

    Scaffold(topBar = { TopAppBar(title = { Text("上线确认") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(value = prepOrderId, onValueChange = { prepOrderId = it }, label = { Text("备料单ID") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = partNo, onValueChange = { partNo = it }, label = { Text("料号") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = barcode, onValueChange = { barcode = it }, label = { Text("物料条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            Button(onClick = { viewModel.confirmOnline(prepOrderId.toIntOrNull() ?: 0, partNo, barcode) },
                enabled = !state.isLoading, modifier = Modifier.fillMaxWidth()) { Text("确认上线") }
            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())
            state.scanMsg?.let { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) }
        }
    }
}
