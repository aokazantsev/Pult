package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.material3.ColorScheme
import androidx.compose.ui.graphics.Color

object PultColorRoles {

    private val selectors: Map<String, (ColorScheme) -> Color> = linkedMapOf(
        "primary" to { it.primary },
        "on-primary" to { it.onPrimary },
        "primary-container" to { it.primaryContainer },
        "on-primary-container" to { it.onPrimaryContainer },
        "inverse-primary" to { it.inversePrimary },
        "secondary" to { it.secondary },
        "on-secondary" to { it.onSecondary },
        "secondary-container" to { it.secondaryContainer },
        "on-secondary-container" to { it.onSecondaryContainer },
        "tertiary" to { it.tertiary },
        "on-tertiary" to { it.onTertiary },
        "tertiary-container" to { it.tertiaryContainer },
        "on-tertiary-container" to { it.onTertiaryContainer },
        "background" to { it.background },
        "on-background" to { it.onBackground },
        "surface" to { it.surface },
        "on-surface" to { it.onSurface },
        "surface-variant" to { it.surfaceVariant },
        "on-surface-variant" to { it.onSurfaceVariant },
        "surface-tint" to { it.surfaceTint },
        "inverse-surface" to { it.inverseSurface },
        "inverse-on-surface" to { it.inverseOnSurface },
        "error" to { it.error },
        "on-error" to { it.onError },
        "error-container" to { it.errorContainer },
        "on-error-container" to { it.onErrorContainer },
        "outline" to { it.outline },
        "outline-variant" to { it.outlineVariant },
        "scrim" to { it.scrim },
        "surface-bright" to { it.surfaceBright },
        "surface-dim" to { it.surfaceDim },
        "surface-container" to { it.surfaceContainer },
        "surface-container-high" to { it.surfaceContainerHigh },
        "surface-container-highest" to { it.surfaceContainerHighest },
        "surface-container-low" to { it.surfaceContainerLow },
        "surface-container-lowest" to { it.surfaceContainerLowest },
        "primary-fixed" to { it.primaryFixed },
        "primary-fixed-dim" to { it.primaryFixedDim },
        "on-primary-fixed" to { it.onPrimaryFixed },
        "on-primary-fixed-variant" to { it.onPrimaryFixedVariant },
        "secondary-fixed" to { it.secondaryFixed },
        "secondary-fixed-dim" to { it.secondaryFixedDim },
        "on-secondary-fixed" to { it.onSecondaryFixed },
        "on-secondary-fixed-variant" to { it.onSecondaryFixedVariant },
        "tertiary-fixed" to { it.tertiaryFixed },
        "tertiary-fixed-dim" to { it.tertiaryFixedDim },
        "on-tertiary-fixed" to { it.onTertiaryFixed },
        "on-tertiary-fixed-variant" to { it.onTertiaryFixedVariant },
    )

    fun colors(scheme: ColorScheme): Map<String, Color> = selectors.mapValues { (_, select) -> select(scheme) }
}
