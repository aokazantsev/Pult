package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.Typography
import androidx.compose.runtime.Immutable
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily

@Immutable
class PultTypography(
    val displayLarge: TextStyle,
    val displayMedium: TextStyle,
    val displaySmall: TextStyle,
    val headlineLarge: TextStyle,
    val headlineMedium: TextStyle,
    val headlineSmall: TextStyle,
    val titleLarge: TextStyle,
    val titleMedium: TextStyle,
    val titleSmall: TextStyle,
    val bodyLarge: TextStyle,
    val bodyMedium: TextStyle,
    val bodySmall: TextStyle,
    val labelLarge: TextStyle,
    val labelMedium: TextStyle,
    val labelSmall: TextStyle,
    val emphasized: PultEmphasizedTypography,
    val code: TextStyle,
) {
    companion object {
        @OptIn(ExperimentalMaterial3ExpressiveApi::class)
        internal fun from(typography: Typography) = PultTypography(
            displayLarge = typography.displayLarge,
            displayMedium = typography.displayMedium,
            displaySmall = typography.displaySmall,
            headlineLarge = typography.headlineLarge,
            headlineMedium = typography.headlineMedium,
            headlineSmall = typography.headlineSmall,
            titleLarge = typography.titleLarge,
            titleMedium = typography.titleMedium,
            titleSmall = typography.titleSmall,
            bodyLarge = typography.bodyLarge,
            bodyMedium = typography.bodyMedium,
            bodySmall = typography.bodySmall,
            labelLarge = typography.labelLarge,
            labelMedium = typography.labelMedium,
            labelSmall = typography.labelSmall,
            emphasized = PultEmphasizedTypography(
                displayLarge = typography.displayLargeEmphasized,
                displayMedium = typography.displayMediumEmphasized,
                displaySmall = typography.displaySmallEmphasized,
                headlineLarge = typography.headlineLargeEmphasized,
                headlineMedium = typography.headlineMediumEmphasized,
                headlineSmall = typography.headlineSmallEmphasized,
                titleLarge = typography.titleLargeEmphasized,
                titleMedium = typography.titleMediumEmphasized,
                titleSmall = typography.titleSmallEmphasized,
                bodyLarge = typography.bodyLargeEmphasized,
                bodyMedium = typography.bodyMediumEmphasized,
                bodySmall = typography.bodySmallEmphasized,
                labelLarge = typography.labelLargeEmphasized,
                labelMedium = typography.labelMediumEmphasized,
                labelSmall = typography.labelSmallEmphasized,
            ),
            code = typography.bodyMedium.copy(fontFamily = FontFamily.Monospace),
        )
    }
}
