package com.dip.material.ui.return_

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
fun ReturnScreen(onBack: () -> Unit, viewModel: ReturnViewModel = viewModel()) {
    val state by viewModel.state.collectAsState()
    LaunchedEffect(Unit) { viewModel.loadLocations() }

    Scaffold(topBar = { TopAppBar(title = { Text("退料管理") }, navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "返回") } },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(value = state.scannedBarcode, onValueChange = { viewModel.setBarcode(it) }, label = { Text("扫物料条码") }, modifier = Modifier.fillMaxWidth(), singleLine = true,
                trailingIcon = { Button(onClick = { viewModel.scanReturn(state.scannedBarcode) }, enabled = state.scannedBarcode.isNotBlank()) { Text("退料") } })

            Text("选择退料库位:", style = MaterialTheme.typography.titleSmall)
            LazyColumn(Modifier.weight(1f)) {
                items(state.locations) { loc ->
                    Card(onClick = { viewModel.setLocationId(loc.id) }, Modifier.fillMaxWidth(),
                        colors = CardDefaults.cardColors(containerColor = if (state.selectedLocationId == loc.id) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface)) {
                        Text(loc.locationCode, modifier = Modifier.padding(12.dp))
                    }
                }
            }
            state.scanMsg?.let { Text(it, color = if (it.contains("成功")) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error) }
        }
    }
}
