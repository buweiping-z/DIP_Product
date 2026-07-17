package com.dip.material

import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.dip.material.ui.theme.DIPTheme
import com.dip.material.ui.login.LoginScreen
import com.dip.material.ui.home.HomeScreen
import com.dip.material.ui.shelving.ShelvingScreen
import com.dip.material.ui.prep.PrepScreen
import com.dip.material.ui.refill.RefillScreen
import com.dip.material.ui.return_.ReturnScreen
import com.dip.material.ui.online.OnlineScreen
import com.dip.material.ui.substitute.SubstituteScreen
import com.dip.material.ui.outbound.OutboundScreen
import com.dip.material.data.network.RetrofitClient
import com.dip.material.utils.PreferencesManager
import com.dip.material.utils.ScanBroadcastReceiver
import com.dip.material.utils.ScanConfig
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    // 扫码枪广播接收器（SEUIC 东大集成 Cruise 广播模式）
    private val scanReceiver = ScanBroadcastReceiver()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        // 全局禁止软键盘自动弹出（PDA 使用硬件扫码枪，不需要屏幕键盘）
        window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN)

        // 下发 SEUIC 扫码服务配置（设定扫描结果的广播 action），并注册接收器
        applyScanConfig()
        val filter = IntentFilter().apply {
            ScanConfig.CANDIDATE_ACTIONS.forEach { addAction(it) }
            priority = IntentFilter.SYSTEM_HIGH_PRIORITY
        }
        // SEUIC 扫码服务是独立系统应用，跨 UID 广播，必须用 RECEIVER_EXPORTED；
        // API 33+ 强制要求显式指定 EXPORTED/NOT_EXPORTED 标志，否则 registerReceiver 抛 SecurityException。
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(scanReceiver, filter, Context.RECEIVER_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(scanReceiver, filter)
        }

        setContent { DIPTheme { AppNavHost() } }
    }

    override fun onDestroy() {
        super.onDestroy()
        try {
            unregisterReceiver(scanReceiver)
        } catch (_: Exception) {
            // 未注册或已注销时忽略
        }
    }

    override fun onResume() {
        super.onResume()
        // 回到前台时再下发一次扫码服务配置，确保 action 始终生效
        // （配置可能被系统/扫码服务重置，CRUISE 1 需要主动维持）
        applyScanConfig()
    }

    /**
     * 下发 SEUIC 扫码服务配置广播，把扫描结果输出 action 设为我们监听的 [ScanConfig.ACTION]。
     * CRUISE 1 系列需要 App 主动下发此配置，否则默认 action 可能不一致而收不到扫码。
     */
    private fun applyScanConfig() {
        val configIntent = Intent(ScanConfig.SERVICE_SETTINGS_ACTION).apply {
            putExtra(ScanConfig.CONFIG_ACTION_KEY, ScanConfig.ACTION)
        }
        sendBroadcast(configIntent)
    }
}

@Composable
fun AppNavHost() {
    val navController = rememberNavController()
    val scope = rememberCoroutineScope()
    val prefs = PreferencesManager(navController.context.applicationContext)

    NavHost(navController = navController, startDestination = "login") {
        composable("login") { LoginScreen(onLoginSuccess = { navController.navigate("home") { popUpTo("login") { inclusive = true } } }) }
        composable("home") { HomeScreen(
            onNavigateToShelving = { navController.navigate("shelving") },
            onNavigateToPrep = { navController.navigate("prep") },
            onNavigateToRefill = { navController.navigate("refill") },
            onNavigateToReturn = { navController.navigate("return") },
            onNavigateToOnline = { navController.navigate("online") },
            onNavigateToSubstitute = { navController.navigate("substitute") },
            onNavigateToOutbound = { navController.navigate("outbound") },
            onLogout = {
                scope.launch {
                    prefs.clearTokens()
                    RetrofitClient.reset()
                    navController.navigate("login") { popUpTo("home") { inclusive = true } }
                }
            }
        )}
        composable("shelving") { ShelvingScreen(onBack = { navController.popBackStack() }) }
        composable("prep") { PrepScreen(onBack = { navController.popBackStack() }) }
        composable("refill") { RefillScreen(onBack = { navController.popBackStack() }) }
        composable("return") { ReturnScreen(onBack = { navController.popBackStack() }) }
        composable("online") { OnlineScreen(onBack = { navController.popBackStack() }) }
        composable("substitute") { SubstituteScreen(onBack = { navController.popBackStack() }) }
        composable("outbound") { OutboundScreen(onBack = { navController.popBackStack() }) }
    }
}
