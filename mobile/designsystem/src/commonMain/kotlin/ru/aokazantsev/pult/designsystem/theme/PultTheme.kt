package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.material3.ColorScheme
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.MaterialExpressiveTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MotionScheme
import androidx.compose.material3.Typography
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf

private val LocalPultTypography = staticCompositionLocalOf { PultTypography.from(Typography()) }
private val LocalPultSpacing = staticCompositionLocalOf { PultSpacing.Default }
private val LocalPultSizes = staticCompositionLocalOf { PultSizes.Default }
private val LocalPultShapes = staticCompositionLocalOf<PultShapes> { error("PultShapes доступны только внутри PultTheme") }

object PultTheme {
    val colors: ColorScheme
        @Composable @ReadOnlyComposable
        get() = MaterialTheme.colorScheme

    val typography: PultTypography
        @Composable @ReadOnlyComposable
        get() = LocalPultTypography.current

    val spacing: PultSpacing
        @Composable @ReadOnlyComposable
        get() = LocalPultSpacing.current

    val sizes: PultSizes
        @Composable @ReadOnlyComposable
        get() = LocalPultSizes.current

    val shapes: PultShapes
        @Composable @ReadOnlyComposable
        get() = LocalPultShapes.current

    val wallpaperColorsAvailable: Boolean
        get() = isWallpaperColorSchemeAvailable()
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun PultTheme(
    darkTheme: Boolean,
    wallpaperColors: Boolean,
    content: @Composable () -> Unit,
) {
    val wallpaperScheme = if (wallpaperColors) rememberWallpaperColorScheme(darkTheme) else null
    val colorScheme = wallpaperScheme ?: PultSeedScheme.rememberColorScheme(darkTheme)
    val typography = remember { Typography() }
    val atlasTypography = remember(typography) { PultTypography.from(typography) }
    val atlasShapes = PultShapes.default()
    MaterialExpressiveTheme(
        colorScheme = colorScheme,
        motionScheme = MotionScheme.expressive(),
        typography = typography,
    ) {
        CompositionLocalProvider(
            LocalPultTypography provides atlasTypography,
            LocalPultShapes provides atlasShapes,
            content = content,
        )
    }
}
