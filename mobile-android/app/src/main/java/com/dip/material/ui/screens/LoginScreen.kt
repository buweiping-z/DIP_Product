package com.dip.material.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Person
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.dip.material.ui.viewmodels.LoginViewModel
import org.koin.androidx.compose.koinViewModel

@Composable
fun LoginScreen(
    viewModel: LoginViewModel = koinViewModel(),
    onLoginSuccess: () -> Unit = {}
) {
    val state by viewModel.state.collectAsState()
    var username by remember { mutableStateOf("admin") }
    var password by remember { mutableStateOf("admin123") }

    LaunchedEffect(state.isLoggedIn) {
        if (state.isLoggedIn) onLoginSuccess()
    }

    Column(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text("DIP 物料管理", fontSize = 28.sp, fontWeight = FontWeight.Bold)
        Text("PDA System", fontSize = 18.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 48.dp))

        OutlinedTextField(value = username, onValueChange = { username = it },
            label = { Text("用户名", fontSize = 16.sp) },
            leadingIcon = { Icon(Icons.Default.Person, contentDescription = null) },
            textStyle = androidx.compose.ui.text.TextStyle(fontSize = 18.sp),
            modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp))

        OutlinedTextField(value = password, onValueChange = { password = it },
            label = { Text("密码", fontSize = 16.sp) },
            leadingIcon = { Icon(Icons.Default.Lock, contentDescription = null) },
            visualTransformation = PasswordVisualTransformation(),
            textStyle = androidx.compose.ui.text.TextStyle(fontSize = 18.sp),
            keyboardOptions = KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Password),
            modifier = Modifier.fillMaxWidth().padding(bottom = 32.dp))

        Button(onClick = { viewModel.login(username, password) },
            enabled = !state.isLoading && username.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth().height(52.dp)
        ) { Text(if (state.isLoading) "登录中..." else "登录", fontSize = 18.sp) }

        state.error?.let {
            Spacer(Modifier.height(16.dp))
            Text(it, color = MaterialTheme.colorScheme.error, fontSize = 16.sp)
        }
    }
}
