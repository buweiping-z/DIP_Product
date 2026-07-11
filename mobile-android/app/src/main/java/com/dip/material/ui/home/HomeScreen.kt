package com.dip.material.ui.home

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    onNavigateToShelving: () -> Unit,
    onNavigateToPrep: () -> Unit,
    onNavigateToRefill: () -> Unit,
    onNavigateToReturn: () -> Unit,
    onNavigateToOnline: () -> Unit,
    onNavigateToSubstitute: () -> Unit,
    onNavigateToOutbound: () -> Unit,
    onLogout: () -> Unit,
    viewModel: HomeViewModel = viewModel()
) {
    val state by viewModel.state.collectAsState()
    val stats = state.stats

    Scaffold(
        topBar = { TopAppBar(title = { Text("DIP 物料管理") }, actions = { IconButton(onClick = onLogout) { Icon(Icons.AutoMirrored.Filled.Logout, "退出") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            // Stats cards
            if (stats != null) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    StatCard("今日上架", "${stats.todayOps?.shelving ?: 0}", Modifier.weight(1f))
                    StatCard("今日退料", "${stats.todayOps?.returns ?: 0}", Modifier.weight(1f))
                    StatCard("备料扫描", "${stats.todayOps?.prepScans ?: 0}", Modifier.weight(1f))
                }
            }

            if (state.isLoading) LinearProgressIndicator(Modifier.fillMaxWidth())

            Spacer(Modifier.height(8.dp))
            Text("功能菜单", fontSize = 20.sp, fontWeight = FontWeight.Bold)

            // 6 function cards
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FuncCard("上架", Icons.Default.Upload, Modifier.weight(1f), onClick = onNavigateToShelving)
                FuncCard("备料", Icons.Default.Inventory, Modifier.weight(1f), onClick = onNavigateToPrep)
                FuncCard("补料", Icons.Default.AddCircle, Modifier.weight(1f), onClick = onNavigateToRefill)
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FuncCard("退料", Icons.Default.Archive, Modifier.weight(1f), onClick = onNavigateToReturn)
                FuncCard("上线", Icons.Default.CheckCircle, Modifier.weight(1f), onClick = onNavigateToOnline)
                FuncCard("替代", Icons.Default.SwapHoriz, Modifier.weight(1f), onClick = onNavigateToSubstitute)
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FuncCard("出库", Icons.Default.ExitToApp, Modifier.weight(1f), onClick = onNavigateToOutbound)
            }
        }
    }
}

@Composable
fun StatCard(label: String, value: String, modifier: Modifier = Modifier) {
    Card(modifier, colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
        Column(Modifier.padding(12.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Text(value, fontSize = 24.sp, fontWeight = FontWeight.Bold)
            Text(label, fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer)
        }
    }
}

@Composable
fun FuncCard(label: String, icon: androidx.compose.ui.graphics.vector.ImageVector, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = modifier) {
        Column(Modifier.padding(16.dp).fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            Icon(icon, null, modifier = Modifier.size(36.dp), tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.height(4.dp))
            Text(label, fontSize = 14.sp)
        }
    }
}
