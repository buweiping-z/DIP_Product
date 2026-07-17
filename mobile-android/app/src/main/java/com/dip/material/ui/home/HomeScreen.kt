package com.dip.material.ui.home

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
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

    LaunchedEffect(Unit) { viewModel.loadPendingTasks() }

    Scaffold(
        topBar = { TopAppBar(title = { Text("DIP 物料管理") }, actions = {
            IconButton(onClick = { viewModel.loadPendingTasks() }) { Icon(Icons.Default.Refresh, "刷新") }
            IconButton(onClick = onLogout) { Icon(Icons.AutoMirrored.Filled.Logout, "退出") }
        },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            // 未完成任务栏
            val hasPending = state.pendingPrep > 0 || state.pendingRefill > 0 || state.pendingSubstitute > 0
                || state.pendingOnline > 0 || state.pendingOutbound > 0
            if (hasPending) {
                Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF3CD))) {
                    Column(Modifier.padding(12.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                            if (state.pendingPrep > 0)
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text("${state.pendingPrep}", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE65100))
                                    Text("待备料", fontSize = 11.sp, color = Color.Gray)
                                }
                            if (state.pendingOnline > 0)
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text("${state.pendingOnline}", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE65100))
                                    Text("待上线", fontSize = 11.sp, color = Color.Gray)
                                }
                            if (state.pendingOutbound > 0)
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text("${state.pendingOutbound}", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE65100))
                                    Text("待出库", fontSize = 11.sp, color = Color.Gray)
                                }
                        }
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                            if (state.pendingRefill > 0)
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text("${state.pendingRefill}", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE65100))
                                    Text("补料中", fontSize = 11.sp, color = Color.Gray)
                                }
                            if (state.pendingSubstitute > 0)
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text("${state.pendingSubstitute}", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE65100))
                                    Text("待移库", fontSize = 11.sp, color = Color.Gray)
                                }
                        }
                    }
                }
            } else {
                Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFE8F5E9))) {
                    Text("无未完成任务", modifier = Modifier.padding(12.dp), color = Color(0xFF2E7D32), fontSize = 14.sp)
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
fun FuncCard(label: String, icon: androidx.compose.ui.graphics.vector.ImageVector, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = modifier) {
        Column(Modifier.padding(16.dp).fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            Icon(icon, null, modifier = Modifier.size(36.dp), tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.height(4.dp))
            Text(label, fontSize = 14.sp)
        }
    }
}
