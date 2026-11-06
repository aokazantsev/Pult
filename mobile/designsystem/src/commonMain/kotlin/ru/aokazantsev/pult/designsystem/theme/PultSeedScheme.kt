package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.material3.ColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Color
import com.materialkolor.PaletteStyle
import com.materialkolor.dynamicColorScheme
import com.materialkolor.dynamiccolor.ColorSpec

internal object PultSeedScheme {

    private val Seed = Color(0xFF2F6FEB)

    fun colorScheme(darkTheme: Boolean): ColorScheme =
        dynamicColorScheme(
            seedColor = Seed,
            isDark = darkTheme,
            style = PaletteStyle.TonalSpot,
            specVersion = ColorSpec.SpecVersion.SPEC_2025,
        )

    @Composable
    fun rememberColorScheme(darkTheme: Boolean): ColorScheme = remember(darkTheme) { colorScheme(darkTheme) }
}
