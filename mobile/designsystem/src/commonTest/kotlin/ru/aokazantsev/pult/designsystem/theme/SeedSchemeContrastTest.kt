package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import kotlin.test.Test
import kotlin.test.assertTrue

class SeedSchemeContrastTest {

    @Test
    fun codeTextRolesAreReadableOnCodeBlockBackground() {
        listOf(false, true).forEach { dark ->
            val scheme = PultSeedScheme.colorScheme(dark)
            val roles = mapOf(
                "onSurface" to scheme.onSurface,
                "onSurfaceVariant" to scheme.onSurfaceVariant,
                "primary" to scheme.primary,
                "secondary" to scheme.secondary,
                "tertiary" to scheme.tertiary,
                "error" to scheme.error,
            )
            roles.forEach { (name, color) ->
                val ratio = contrast(color, scheme.surfaceContainerHigh)
                assertTrue(ratio >= MIN_TEXT_CONTRAST, "$name на surfaceContainerHigh, тёмная=$dark: $ratio")
            }
        }
    }

    private fun contrast(first: Color, second: Color): Double {
        val lighter = maxOf(first.luminance(), second.luminance()) + LUMINANCE_OFFSET
        val darker = minOf(first.luminance(), second.luminance()) + LUMINANCE_OFFSET
        return (lighter / darker).toDouble()
    }

    private companion object {
        const val MIN_TEXT_CONTRAST = 4.5
        const val LUMINANCE_OFFSET = 0.05f
    }
}
