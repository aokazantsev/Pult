package ru.aokazantsev.pult.designsystem.theme

import kotlin.test.Test
import kotlin.test.assertEquals

class PultColorRolesTest {

    @Test
    fun everyRoleNameMapsToItsSchemeColorInLightAndDark() {
        listOf(false, true).forEach { dark ->
            val scheme = PultSeedScheme.colorScheme(dark)
            val expected = mapOf(
            "primary" to scheme.primary,
            "on-primary" to scheme.onPrimary,
            "primary-container" to scheme.primaryContainer,
            "on-primary-container" to scheme.onPrimaryContainer,
            "inverse-primary" to scheme.inversePrimary,
            "secondary" to scheme.secondary,
            "on-secondary" to scheme.onSecondary,
            "secondary-container" to scheme.secondaryContainer,
            "on-secondary-container" to scheme.onSecondaryContainer,
            "tertiary" to scheme.tertiary,
            "on-tertiary" to scheme.onTertiary,
            "tertiary-container" to scheme.tertiaryContainer,
            "on-tertiary-container" to scheme.onTertiaryContainer,
            "background" to scheme.background,
            "on-background" to scheme.onBackground,
            "surface" to scheme.surface,
            "on-surface" to scheme.onSurface,
            "surface-variant" to scheme.surfaceVariant,
            "on-surface-variant" to scheme.onSurfaceVariant,
            "surface-tint" to scheme.surfaceTint,
            "inverse-surface" to scheme.inverseSurface,
            "inverse-on-surface" to scheme.inverseOnSurface,
            "error" to scheme.error,
            "on-error" to scheme.onError,
            "error-container" to scheme.errorContainer,
            "on-error-container" to scheme.onErrorContainer,
            "outline" to scheme.outline,
            "outline-variant" to scheme.outlineVariant,
            "scrim" to scheme.scrim,
            "surface-bright" to scheme.surfaceBright,
            "surface-dim" to scheme.surfaceDim,
            "surface-container" to scheme.surfaceContainer,
            "surface-container-high" to scheme.surfaceContainerHigh,
            "surface-container-highest" to scheme.surfaceContainerHighest,
            "surface-container-low" to scheme.surfaceContainerLow,
            "surface-container-lowest" to scheme.surfaceContainerLowest,
            "primary-fixed" to scheme.primaryFixed,
            "primary-fixed-dim" to scheme.primaryFixedDim,
            "on-primary-fixed" to scheme.onPrimaryFixed,
            "on-primary-fixed-variant" to scheme.onPrimaryFixedVariant,
            "secondary-fixed" to scheme.secondaryFixed,
            "secondary-fixed-dim" to scheme.secondaryFixedDim,
            "on-secondary-fixed" to scheme.onSecondaryFixed,
            "on-secondary-fixed-variant" to scheme.onSecondaryFixedVariant,
            "tertiary-fixed" to scheme.tertiaryFixed,
            "tertiary-fixed-dim" to scheme.tertiaryFixedDim,
            "on-tertiary-fixed" to scheme.onTertiaryFixed,
            "on-tertiary-fixed-variant" to scheme.onTertiaryFixedVariant,
            )

            assertEquals(expected, PultColorRoles.colors(scheme), "тёмная=$dark")
        }
    }
}
