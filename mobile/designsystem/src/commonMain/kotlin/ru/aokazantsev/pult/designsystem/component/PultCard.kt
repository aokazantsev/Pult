package ru.aokazantsev.pult.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun PultCard(
    modifier: Modifier = Modifier,
    dimmed: Boolean = false,
    content: @Composable () -> Unit,
) {
    Surface(
        modifier = modifier.fillMaxWidth().alpha(if (dimmed) DIMMED_ALPHA else 1f),
        shape = PultTheme.shapes.large,
        color = PultTheme.colors.surfaceContainerHigh,
    ) {
        Column(
            modifier = Modifier.padding(PultTheme.spacing.l),
            verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
        ) {
            content()
        }
    }
}

private const val DIMMED_ALPHA = 0.55f
