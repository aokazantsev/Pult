package ru.aokazantsev.pult.designsystem.component

import androidx.compose.material3.HorizontalDivider
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun PultDivider(modifier: Modifier = Modifier) {
    HorizontalDivider(modifier = modifier, color = PultTheme.colors.outlineVariant)
}
