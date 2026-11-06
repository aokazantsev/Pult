package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.runtime.Immutable
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

@Immutable
class PultSizes(
    val icon: Dp,
    val iconLarge: Dp,
    val loader: Dp,
    val swatch: Dp,
    val shapeSample: Dp,
    val tableColumnMax: Dp,
) {
    companion object {
        val Default = PultSizes(
            icon = 24.dp,
            iconLarge = 48.dp,
            loader = 48.dp,
            swatch = 56.dp,
            shapeSample = 64.dp,
            tableColumnMax = 240.dp,
        )
    }
}
