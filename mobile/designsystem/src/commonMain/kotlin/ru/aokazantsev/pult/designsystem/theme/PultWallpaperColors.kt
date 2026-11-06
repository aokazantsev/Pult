package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.material3.ColorScheme
import androidx.compose.runtime.Composable

internal expect fun isWallpaperColorSchemeAvailable(): Boolean

@Composable
internal expect fun rememberWallpaperColorScheme(darkTheme: Boolean): ColorScheme?
