package com.dip.material

import android.os.Bundle
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
import com.dip.material.utils.PreferencesManager
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { DIPTheme { AppNavHost() } }
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
                    prefs.clear()
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
