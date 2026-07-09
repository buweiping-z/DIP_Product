package com.dip.material.ui.login

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(
    onLoginSuccess: () -> Unit,
    viewModel: LoginViewModel = viewModel()
) {
    val state by viewModel.state.collectAsState()
    var username by remember { mutableStateOf("admin") }
    var password by remember { mutableStateOf("admin123") }
    var showServerConfig by remember { mutableStateOf(false) }
    var serverUrl by remember { mutableStateOf("") }

    LaunchedEffect(Unit) { viewModel.loadServerUrl(); viewModel.loadCredentials() }
    LaunchedEffect(state.serverUrl) { serverUrl = state.serverUrl }
    LaunchedEffect(state.savedUsername) { if (state.savedUsername.isNotBlank()) username = state.savedUsername }
    LaunchedEffect(state.isLoggedIn) { if (state.isLoggedIn) onLoginSuccess() }

    Scaffold(
        topBar = { TopAppBar(title = { Text("DIP 物料管理") }, colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) }
    ) { padding ->
        Column(
            modifier = Modifier.fillMaxSize().padding(padding).padding(horizontal = 24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text("DIP 物料管理", fontSize = 28.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(4.dp))
            Text("线边仓管理 PDA", fontSize = 16.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(40.dp))

            OutlinedTextField(value = username, onValueChange = { username = it },
                label = { Text("用户名", fontSize = 16.sp) },
                leadingIcon = { Icon(Icons.Default.Person, null) },
                textStyle = androidx.compose.ui.text.TextStyle(fontSize = 18.sp),
                modifier = Modifier.fillMaxWidth(), singleLine = true)
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(value = password, onValueChange = { password = it },
                label = { Text("密码", fontSize = 16.sp) },
                leadingIcon = { Icon(Icons.Default.Lock, null) },
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                textStyle = androidx.compose.ui.text.TextStyle(fontSize = 18.sp),
                modifier = Modifier.fillMaxWidth(), singleLine = true)
            Spacer(Modifier.height(24.dp))

            Button(onClick = { viewModel.login(username, password) },
                enabled = !state.isLoading && username.isNotBlank() && password.isNotBlank(),
                modifier = Modifier.fillMaxWidth().height(52.dp)
            ) { Text(if (state.isLoading) "登录中..." else "登录", fontSize = 18.sp) }

            state.error?.let {
                Spacer(Modifier.height(12.dp))
                Text(it, color = MaterialTheme.colorScheme.error, fontSize = 16.sp)
            }

            Spacer(Modifier.height(24.dp))
            TextButton(onClick = { showServerConfig = !showServerConfig }) { Text("服务器设置") }

            if (showServerConfig) {
                OutlinedTextField(value = serverUrl, onValueChange = { serverUrl = it },
                    label = { Text("服务器地址") },
                    modifier = Modifier.fillMaxWidth(), singleLine = true)
                Spacer(Modifier.height(8.dp))
                Button(onClick = { viewModel.saveServerUrl(serverUrl); showServerConfig = false },
                    modifier = Modifier.fillMaxWidth()) { Text("保存") }
            }
        }
    }
}
