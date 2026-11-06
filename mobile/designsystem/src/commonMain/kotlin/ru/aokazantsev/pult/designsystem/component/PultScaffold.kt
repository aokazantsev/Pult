package ru.aokazantsev.pult.designsystem.component

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.theme.PultTheme

private val BOTTOM_BAR_SIDES = WindowInsetsSides.Bottom + WindowInsetsSides.Horizontal

@Composable
fun PultScaffold(
    modifier: Modifier = Modifier,
    topBar: @Composable () -> Unit = {},
    bottomBar: (@Composable () -> Unit)? = null,
    content: @Composable (PaddingValues) -> Unit,
) {
    Scaffold(
        modifier = modifier,
        topBar = topBar,
        bottomBar = {
            if (bottomBar != null) {
                Box(modifier = Modifier.windowInsetsPadding(WindowInsets.safeDrawing.only(BOTTOM_BAR_SIDES))) {
                    bottomBar()
                }
            }
        },
        containerColor = PultTheme.colors.surfaceContainer,
        contentWindowInsets = WindowInsets.safeDrawing,
        content = content,
    )
}
