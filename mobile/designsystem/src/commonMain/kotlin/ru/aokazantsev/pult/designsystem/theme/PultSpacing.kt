package ru.aokazantsev.pult.designsystem.theme

import androidx.compose.runtime.Immutable
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

@Immutable
class PultSpacing(
    val xs: Dp,
    val s: Dp,
    val m: Dp,
    val l: Dp,
    val xl: Dp,
) {
    companion object {
        val Default = PultSpacing(
            xs = 4.dp,
            s = 8.dp,
            m = 12.dp,
            l = 16.dp,
            xl = 24.dp,
        )
    }
}
