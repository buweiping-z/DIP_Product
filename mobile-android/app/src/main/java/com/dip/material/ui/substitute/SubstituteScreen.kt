package com.dip.material.ui.substitute

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
fun SubstituteScreen(onBack: () -> Unit, viewModel: SubstituteViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    var originalPartId by remember { mutableStateOf("") }
    var substitutePartId by remember { mutableStateOf("") }
    var sourceLocationId by remember { mutableStateOf("") }
    var targetLocationId by remember { mutableStateOf("") }
    var quantity by remember { mutableStateOf("") }

    Scaffold(topBar = { TopAppBar(title = { Text("替代料移库") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(value = originalPartId, onValueChange = { originalPartId = it }, label = { Text("原部品ID") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = substitutePartId, onValueChange = { substitutePartId = it }, label = { Text("替代部品ID") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = sourceLocationId, onValueChange = { sourceLocationId = it }, label = { Text("来源库位ID") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = targetLocationId, onValueChange = { targetLocationId = it }, label = { Text("目标库位ID") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            OutlinedTextField(value = quantity, onValueChange = { quantity = it }, label = { Text("数量") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            Button(onClick = {
                viewModel.createSubstitute(originalPartId.toIntOrNull() ?: 0, substitutePartId.toIntOrNull() ?: 0,
                    sourceLocationId.toIntOrNull() ?: 0, targetLocationId.toIntOrNull() ?: 0, quantity.toDoubleOrNull() ?: 0.0)
            }, modifier = Modifier.fillMaxWidth()) { Text("创建移库") }
            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())
            state.scanMsg?.let { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) }
        }
    }
}
