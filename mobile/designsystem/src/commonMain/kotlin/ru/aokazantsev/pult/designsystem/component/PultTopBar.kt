package ru.aokazantsev.pult.designsystem.component

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import ru.aokazantsev.pult.designsystem.icon.PultIcons
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PultTopBar(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    titleOverflow: TextOverflow = TextOverflow.Ellipsis,
    subtitleOverflow: TextOverflow = TextOverflow.Ellipsis,
    onBack: (() -> Unit)? = null,
    actions: @Composable RowScope.() -> Unit = {},
    windowInsets: WindowInsets = WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal),
) {
    TopAppBar(
        title = {
            Column {
                PultText(text = title, maxLines = 1, overflow = titleOverflow)
                if (subtitle != null) {
                    PultText(
                        text = subtitle,
                        style = PultTheme.typography.bodySmall,
                        color = PultTheme.colors.onSurfaceVariant,
                        maxLines = 1,
                        overflow = subtitleOverflow,
                    )
                }
            }
        },
        modifier = modifier,
        navigationIcon = {
            if (onBack != null) {
                PultIconButton(icon = PultIcons.ArrowBack, contentDescription = "Назад", onClick = onBack)
            }
        },
        actions = actions,
        windowInsets = windowInsets,
        colors = TopAppBarDefaults.topAppBarColors(
            containerColor = PultTheme.colors.surfaceContainer,
            scrolledContainerColor = PultTheme.colors.surfaceContainer,
        ),
    )
}
