package com.dip.material.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

private val DarkColorScheme = darkColorScheme(
    primary = Blue80, onPrimary = Grey900,
    primaryContainer = Blue700, onPrimaryContainer = Blue80,
    secondary = Grey300, onSecondary = Grey900,
    surface = Grey900, onSurface = Grey100,
    background = Grey900, onBackground = Grey100,
    error = Red600, onError = Grey50,
)

private val LightColorScheme = lightColorScheme(
    primary = Blue700, onPrimary = Grey50,
    primaryContainer = Blue80, onPrimaryContainer = Blue700,
    secondary = BlueGrey, onSecondary = Grey50,
    surface = Grey50, onSurface = Grey900,
    background = Grey100, onBackground = Grey900,
    error = Red600, onError = Grey50,
)

@Composable
fun DIPTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            window.statusBarColor = colorScheme.primary.toArgb()
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !darkTheme
        }
    }
    MaterialTheme(colorScheme = colorScheme, content = content)
}
